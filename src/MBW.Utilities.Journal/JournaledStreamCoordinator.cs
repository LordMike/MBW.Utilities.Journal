using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;

namespace MBW.Utilities.Journal;

/// <summary>
/// Coordinates two-phase commits and recovery for a fixed set of journaled streams.
/// </summary>
public sealed class JournaledStreamCoordinator
{
    private readonly JournaledStream[] _participants;
    private readonly IJournalWitnessStore _witnessStore;
    private readonly JournalCoordinatorRecoveryMode _recoveryMode;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private JournaledStreamCoordinator(JournaledStream[] participants,
        IJournalWitnessStore witnessStore, JournalCoordinatorRecoveryMode recoveryMode)
    {
        _participants = participants;
        _witnessStore = witnessStore;
        _recoveryMode = recoveryMode;
    }

    public static async Task<JournaledStreamCoordinator> CreateAsync(
        IEnumerable<JournaledStream> participants,
        IJournalWitnessStore witnessStore,
        JournalCoordinatorRecoveryMode recoveryMode = JournalCoordinatorRecoveryMode.Default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(witnessStore);

        JournaledStream[] materialized = participants.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one participant is required", nameof(participants));
        if (materialized.Any(static stream => stream is null))
            throw new ArgumentException("Participants must not contain null streams", nameof(participants));
        if (materialized.Distinct(ReferenceEqualityComparer.Instance).Count() != materialized.Length)
            throw new ArgumentException("A stream may participate only once", nameof(participants));

        JournaledStreamCoordinator coordinator = new(materialized, witnessStore, recoveryMode);
        await coordinator.RecoverAsync(cancellationToken);
        if (materialized.Any(static participant => participant.State != JournaledStreamState.Ready))
            throw new JournalRecoveryRequiredException("Not every participant could be recovered to Ready.");

        return coordinator;
    }

    public async Task CommitAsync(
        JournalCommitFailureMode onFailure = JournalCommitFailureMode.PreserveForRecovery,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await CommitCore(onFailure, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await RecoverCore(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            Guid? witness = await _witnessStore.ReadAsync(cancellationToken);
            if (witness.HasValue)
                throw new JournalInInvalidStateException("Rollback is forbidden while a durable witness exists");
            if (_participants.Any(static participant =>
                    participant.State == JournaledStreamState.CommittedButNotApplied))
                throw new JournalInInvalidStateException("Rollback is forbidden after a durable commit marker exists");

            EnsureSinglePreparationKey();
            await RollbackParticipants(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task CommitCore(JournalCommitFailureMode onFailure, CancellationToken cancellationToken)
    {
        EnsureOpenParticipants();
        Guid? witness = await _witnessStore.ReadAsync(cancellationToken);

        if (witness.HasValue || _participants.Any(static participant =>
                participant.State == JournaledStreamState.CommittedButNotApplied))
        {
            await RecoverDecidedTransaction(witness, cancellationToken);
            return;
        }

        if (_participants.All(static participant => participant.State == JournaledStreamState.Ready))
            return;

        Guid preparationKey = EnsureSinglePreparationKey() ?? Guid.NewGuid();
        try
        {
            foreach (JournaledStream participant in _participants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (participant.State is JournaledStreamState.Ready or JournaledStreamState.Dirty)
                    await participant.Prepare(preparationKey);
                else if (participant.State != JournaledStreamState.Prepared ||
                         participant.PreparationKey != preparationKey)
                    throw new JournalInInvalidStateException("Participants are not in one preparable transaction");
            }
        }
        catch (Exception prepareException)
        {
            if (onFailure == JournalCommitFailureMode.RollbackIfSafe)
            {
                Guid? observedWitness;
                try
                {
                    observedWitness = await _witnessStore.ReadAsync(CancellationToken.None);
                }
                catch (Exception readException)
                {
                    throw new AggregateException(
                        "Preparation failed and witness absence could not be confirmed.",
                        prepareException, readException);
                }

                if (!observedWitness.HasValue)
                {
                    try
                    {
                        await RollbackParticipants(CancellationToken.None);
                    }
                    catch (Exception rollbackException)
                    {
                        throw new AggregateException("Preparation and safe rollback both failed.",
                            prepareException, rollbackException);
                    }
                }
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(prepareException).Throw();
            throw;
        }

        try
        {
            await _witnessStore.StoreAsync(preparationKey, cancellationToken);
        }
        catch (Exception storeException)
        {
            Guid? observed;
            try
            {
                observed = await _witnessStore.ReadAsync(CancellationToken.None);
            }
            catch (Exception readException)
            {
                throw new AggregateException("Witness storage failed and its durable state is unknown.",
                    storeException, readException);
            }

            if (observed == preparationKey)
            {
                // The durable install succeeded and only its acknowledgement failed.
            }
            else if (!observed.HasValue)
            {
                if (onFailure == JournalCommitFailureMode.RollbackIfSafe)
                {
                    try
                    {
                        await RollbackParticipants(CancellationToken.None);
                    }
                    catch (Exception rollbackException)
                    {
                        throw new AggregateException("Witness storage and safe rollback both failed.",
                            storeException, rollbackException);
                    }
                }

                throw new InvalidOperationException("The witness could not be stored.", storeException);
            }
            else
            {
                throw new InvalidOperationException("The witness belongs to another transaction.", storeException);
            }
        }

        await CommitPreparedAndApply(preparationKey, cancellationToken);
    }

    private async Task RecoverCore(CancellationToken cancellationToken)
    {
        EnsureOpenParticipants();
        Guid? witness = await _witnessStore.ReadAsync(cancellationToken);
        Guid? participantKey = EnsureSinglePreparationKey();
        bool hasDirty = _participants.Any(static participant => participant.State == JournaledStreamState.Dirty);
        bool hasPrepared = _participants.Any(static participant => participant.State == JournaledStreamState.Prepared);
        bool hasCommitted = _participants.Any(static participant =>
            participant.State == JournaledStreamState.CommittedButNotApplied);

        if (witness.HasValue && participantKey.HasValue && witness.Value != participantKey.Value)
            throw new InvalidOperationException("The witness and participant preparation keys do not match.");

        if (hasDirty && (witness.HasValue || hasCommitted))
            throw new InvalidOperationException("Dirty participants cannot coexist with a durable commit decision.");

        if (hasCommitted)
        {
            if (!participantKey.HasValue)
                throw new InvalidOperationException("A committed participant has no preparation key.");
            await CommitPreparedAndApply(participantKey.Value, cancellationToken);
            return;
        }

        if (witness.HasValue)
        {
            if (hasPrepared)
            {
                if ((_recoveryMode & JournalCoordinatorRecoveryMode.CommitWitnessed) == 0)
                    throw new JournalRecoveryRequiredException("A witnessed prepared transaction requires commit recovery.");

                await CommitPreparedAndApply(witness.Value, cancellationToken);
                return;
            }

            if (_participants.All(static participant => participant.State == JournaledStreamState.Ready))
            {
                await _witnessStore.ClearAsync(witness.Value, cancellationToken);
                return;
            }
        }

        if (hasDirty || hasPrepared)
        {
            if ((_recoveryMode & JournalCoordinatorRecoveryMode.RollbackUnwitnessed) == 0)
                throw new JournalRecoveryRequiredException("An unwitnessed transaction requires rollback recovery.");

            await RollbackParticipants(cancellationToken);
        }
    }

    private async Task RecoverDecidedTransaction(Guid? witness, CancellationToken cancellationToken)
    {
        Guid? participantKey = EnsureSinglePreparationKey();
        if (_participants.Any(static participant => participant.State == JournaledStreamState.Dirty))
            throw new InvalidOperationException("A dirty participant cannot coexist with a durable commit decision.");
        if (witness.HasValue && participantKey.HasValue && witness.Value != participantKey.Value)
            throw new InvalidOperationException("The witness and participant preparation keys do not match.");

        Guid key = witness ?? participantKey ??
            throw new InvalidOperationException("The durable transaction has no preparation key.");
        await CommitPreparedAndApply(key, cancellationToken);
    }

    private async Task CommitPreparedAndApply(Guid preparationKey, CancellationToken cancellationToken)
    {
        List<Exception> failures = [];

        foreach (JournaledStream participant in _participants.Where(static participant =>
                     participant.State == JournaledStreamState.Prepared))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (participant.PreparationKey != preparationKey)
                    throw new InvalidOperationException("A prepared participant has a different key.");
                await participant.Commit(preparationKey);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (_participants.Any(participant => participant.State is not (JournaledStreamState.Ready or
                JournaledStreamState.CommittedButNotApplied) ||
            participant.State == JournaledStreamState.CommittedButNotApplied &&
            participant.PreparationKey != preparationKey))
        {
            if (failures.Count == 1)
                throw failures[0];
            throw new AggregateException("Not every participant could be marked committed.", failures);
        }

        foreach (JournaledStream participant in _participants.Where(static participant =>
                     participant.State == JournaledStreamState.CommittedButNotApplied))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await participant.Apply();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (_participants.All(static participant => participant.State == JournaledStreamState.Ready))
            await _witnessStore.ClearAsync(preparationKey, cancellationToken);

        if (failures.Count == 1)
            throw failures[0];
        if (failures.Count > 1)
            throw new AggregateException("The coordinated transaction did not complete cleanly.", failures);
    }

    private async Task RollbackParticipants(CancellationToken cancellationToken)
    {
        List<Exception> failures = [];
        foreach (JournaledStream participant in _participants.Where(static participant =>
                     participant.State is JournaledStreamState.Dirty or JournaledStreamState.Prepared))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await participant.Rollback();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 1)
            throw failures[0];
        if (failures.Count > 1)
            throw new AggregateException("One or more participants could not be rolled back.", failures);
    }

    private Guid? EnsureSinglePreparationKey()
    {
        Guid[] keys = _participants
            .Where(static participant => participant.PreparationKey.HasValue)
            .Select(static participant => participant.PreparationKey!.Value)
            .Distinct()
            .ToArray();
        if (keys.Length > 1)
            throw new InvalidOperationException("Participants contain multiple preparation keys.");

        return keys.Length == 0 ? null : keys[0];
    }

    private void EnsureOpenParticipants()
    {
        if (_participants.Any(static participant => participant.State == JournaledStreamState.Closed))
            throw new JournalInInvalidStateException("A closed stream cannot participate in coordination.");
    }
}

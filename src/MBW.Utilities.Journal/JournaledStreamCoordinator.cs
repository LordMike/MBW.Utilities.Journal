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

    /// <summary>
    /// Creates a coordinator for a fixed participant set and completes the configured initial recovery before
    /// returning.
    /// </summary>
    /// <param name="participants">The complete set of journaled streams that always participate together.</param>
    /// <param name="witnessStore">The durable witness store dedicated to this participant set.</param>
    /// <param name="recoveryMode">The automatic actions allowed for pending journals found during initialization.</param>
    /// <param name="cancellationToken">Token used to cancel recovery.</param>
    /// <returns>A coordinator whose participants are all in the <see cref="JournaledStreamState.Ready"/> state.</returns>
    /// <remarks>
    /// Open every participant with <see cref="JournalOpenMode.Coordinated"/> and do not expose or use the streams
    /// until this method returns successfully. Reuse the same complete participant set and witness store after a
    /// restart.
    /// </remarks>
    /// <exception cref="ArgumentException">The participant set is empty, contains nulls, or contains duplicates.</exception>
    /// <exception cref="JournalRecoveryRequiredException">The configured mode does not allow required recovery, or a witnessed participant is missing.</exception>
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

    /// <summary>
    /// Prepares every participant, stores the durable witness, commits every marker, clears the witness, and then
    /// applies every journal.
    /// </summary>
    /// <param name="onFailure">The policy used only while rollback can still be proven safe.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <remarks>
    /// The witness records the exact journal nonce set and remains present until every commit marker is durable.
    /// After it is cleared, each committed journal can be applied independently. Call <see cref="RecoverAsync"/> to
    /// retry an interrupted operation.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="onFailure"/> is unset or undefined.</exception>
    public async Task CommitAsync(
        JournalCommitFailureMode onFailure = JournalCommitFailureMode.PreserveForRecovery,
        CancellationToken cancellationToken = default)
    {
        if (onFailure == JournalCommitFailureMode.Unset || !Enum.IsDefined(onFailure))
            throw new ArgumentOutOfRangeException(nameof(onFailure), onFailure,
                "A defined commit failure policy must be selected.");

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

    /// <summary>
    /// Retries recovery for the coordinator's participants using the recovery policy selected at creation.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel recovery.</param>
    /// <remarks>Use this after a commit or prior recovery attempt failed while leaving retryable journals intact.</remarks>
    /// <exception cref="JournalRecoveryRequiredException">The configured mode does not allow required recovery, or a witnessed participant is missing.</exception>
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

    /// <summary>
    /// Rolls back every dirty or prepared participant when no durable commit decision exists.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel rollback.</param>
    /// <remarks>This operation is rejected if a witness or committed participant makes rollback unsafe.</remarks>
    /// <exception cref="JournalInInvalidStateException">A witness or durable commit marker exists.</exception>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            JournalWitness? witness = await _witnessStore.ReadAsync(cancellationToken);
            if (witness is not null)
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
        JournalWitness? existingWitness = await _witnessStore.ReadAsync(cancellationToken);
        if (existingWitness is not null)
        {
            await CompleteWitnessedCommit(existingWitness, cancellationToken);
            return;
        }

        bool hasDirty = HasState(JournaledStreamState.Dirty);
        bool hasPrepared = HasState(JournaledStreamState.Prepared);
        bool hasCommitted = HasState(JournaledStreamState.CommittedButNotApplied);
        if (hasCommitted)
        {
            if (hasDirty || hasPrepared)
                throw LostWitnessException();

            await ApplyCommittedParticipants(cancellationToken);
            return;
        }

        if (_participants.All(static participant => participant.State == JournaledStreamState.Ready))
            return;

        Guid preparationKey = EnsureSinglePreparationKey() ?? Guid.NewGuid();
        JournalWitness witness;
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

            witness = CreateWitness(preparationKey);
        }
        catch (Exception prepareException)
        {
            await HandlePreWitnessFailure(prepareException, onFailure);
            throw;
        }

        try
        {
            await _witnessStore.StoreAsync(witness, cancellationToken);
        }
        catch (Exception storeException)
        {
            JournalWitness? observed;
            try
            {
                observed = await _witnessStore.ReadAsync(CancellationToken.None);
            }
            catch (Exception readException)
            {
                throw new AggregateException("Witness storage failed and its durable state is unknown.",
                    storeException, readException);
            }

            if (observed is not null && observed.ValueEquals(witness))
            {
                // The durable install succeeded and only its acknowledgement failed.
            }
            else if (observed is null)
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
                throw new InvalidOperationException("A different journal witness already exists.", storeException);
            }
        }

        await CompleteWitnessedCommit(witness, cancellationToken);
    }

    private async Task RecoverCore(CancellationToken cancellationToken)
    {
        EnsureOpenParticipants();
        JournalWitness? witness = await _witnessStore.ReadAsync(cancellationToken);
        bool hasDirty = HasState(JournaledStreamState.Dirty);
        bool hasPrepared = HasState(JournaledStreamState.Prepared);
        bool hasCommitted = HasState(JournaledStreamState.CommittedButNotApplied);

        if (witness is not null)
        {
            ValidateWitnessParticipants(witness);
            if ((_recoveryMode & JournalCoordinatorRecoveryMode.CommitWitnessed) == 0)
                throw new JournalRecoveryRequiredException(
                    "A witnessed transaction requires commit recovery.");

            await CompleteWitnessedCommit(witness, cancellationToken);
            return;
        }

        if (hasCommitted)
        {
            if (hasDirty || hasPrepared)
                throw LostWitnessException();

            await ApplyCommittedParticipants(cancellationToken);
            return;
        }

        if (hasDirty || hasPrepared)
        {
            EnsureSinglePreparationKey();
            if ((_recoveryMode & JournalCoordinatorRecoveryMode.RollbackUnwitnessed) == 0)
                throw new JournalRecoveryRequiredException(
                    "An unwitnessed transaction requires rollback recovery.");

            await RollbackParticipants(cancellationToken);
        }
    }

    private async Task CompleteWitnessedCommit(JournalWitness witness, CancellationToken cancellationToken)
    {
        ValidateWitnessParticipants(witness);
        List<Exception> failures = [];

        foreach (JournaledStream participant in _participants.Where(static participant =>
                     participant.State == JournaledStreamState.Prepared))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await participant.Commit(witness.PreparationKey);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (_participants.Any(static participant =>
                participant.State != JournaledStreamState.CommittedButNotApplied))
        {
            if (failures.Count == 1)
                throw failures[0];
            throw new AggregateException("Not every witnessed participant could be marked committed.", failures);
        }

        await ClearCompletedWitness(witness, cancellationToken);
        await ApplyCommittedParticipants(cancellationToken);
    }

    private async Task ClearCompletedWitness(JournalWitness witness, CancellationToken cancellationToken)
    {
        try
        {
            await _witnessStore.ClearAsync(witness, cancellationToken);
        }
        catch (Exception clearException)
        {
            JournalWitness? observed;
            try
            {
                observed = await _witnessStore.ReadAsync(CancellationToken.None);
            }
            catch (Exception readException)
            {
                throw new AggregateException("Witness clearing failed and its durable state is unknown.",
                    clearException, readException);
            }

            if (observed is null)
                return;
            if (observed.ValueEquals(witness))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(clearException).Throw();

            throw new InvalidOperationException(
                "Witness clearing failed and the store contains a different witness.", clearException);
        }
    }

    private async Task ApplyCommittedParticipants(CancellationToken cancellationToken)
    {
        List<Exception> failures = [];
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

        if (failures.Count == 1)
            throw failures[0];
        if (failures.Count > 1)
            throw new AggregateException("One or more committed journals could not be applied.", failures);
    }

    private async Task HandlePreWitnessFailure(Exception prepareException, JournalCommitFailureMode onFailure)
    {
        if (onFailure != JournalCommitFailureMode.RollbackIfSafe)
            return;

        JournalWitness? observedWitness;
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

        if (observedWitness is not null)
            return;

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

    private JournalWitness CreateWitness(Guid preparationKey)
    {
        ulong[] nonces = _participants.Select(participant => participant.JournalNonce ??
            throw new InvalidOperationException("A prepared participant has no journal nonce.")).ToArray();
        return new JournalWitness(preparationKey, nonces);
    }

    private void ValidateWitnessParticipants(JournalWitness witness)
    {
        if (_participants.Length != witness.ParticipantNonces.Count ||
            _participants.Any(participant =>
                participant.State is not (JournaledStreamState.Prepared or
                    JournaledStreamState.CommittedButNotApplied) ||
                participant.PreparationKey != witness.PreparationKey ||
                !participant.JournalNonce.HasValue))
        {
            throw new JournalRecoveryRequiredException(
                "The supplied participants do not exactly match the durable journal witness.");
        }

        ulong[] actualNonces = _participants.Select(static participant => participant.JournalNonce!.Value).ToArray();
        Array.Sort(actualNonces);
        if (!actualNonces.AsSpan().SequenceEqual(witness.ParticipantNonceSpan))
            throw new JournalRecoveryRequiredException(
                "The supplied participants do not exactly match the durable journal witness.");
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

    private bool HasState(JournaledStreamState state) =>
        _participants.Any(participant => participant.State == state);

    private static InvalidOperationException LostWitnessException() => new(
        "Prepared and committed participants cannot coexist without their durable witness.");

    private void EnsureOpenParticipants()
    {
        if (_participants.Any(static participant => participant.State == JournaledStreamState.Closed))
            throw new JournalInInvalidStateException("A closed stream cannot participate in coordination.");
    }
}

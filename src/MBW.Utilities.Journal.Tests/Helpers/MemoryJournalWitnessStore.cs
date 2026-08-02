using MBW.Utilities.Journal.Abstracts;
namespace MBW.Utilities.Journal.Tests.Helpers;

internal sealed class MemoryJournalWitnessStore : IJournalWitnessStore
{
    public JournalWitness? Value { get; private set; }

    public ValueTask<JournalWitness?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Value);
    }

    public ValueTask StoreAsync(JournalWitness witness, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(witness);

        JournalWitness? existing = Value;
        if (existing is not null)
        {
            if (existing.ValueEquals(witness))
                return ValueTask.CompletedTask;

            throw new InvalidOperationException("A different journal witness already exists.");
        }

        Value = witness;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(JournalWitness witness, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(witness);

        JournalWitness? existing = Value;
        if (existing is not null && !existing.ValueEquals(witness))
            throw new InvalidOperationException("The stored journal witness does not match.");

        Value = null;
        return ValueTask.CompletedTask;
    }
}

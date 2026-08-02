using MBW.Utilities.Journal.Abstracts;
namespace MBW.Utilities.Journal.Tests.Helpers;

internal sealed class MemoryJournalWitnessStore : IJournalWitnessStore
{
    public Guid? Value { get; private set; }

    public ValueTask<Guid?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Value);
    }

    public ValueTask StoreAsync(Guid preparationKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("The preparation key must not be empty", nameof(preparationKey));

        Guid? existing = Value;
        if (existing.HasValue)
        {
            if (existing.Value == preparationKey)
                return ValueTask.CompletedTask;

            throw new InvalidOperationException("The witness belongs to another prepared transaction.");
        }

        Value = preparationKey;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(Guid preparationKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("The preparation key must not be empty", nameof(preparationKey));

        Guid? existing = Value;
        if (existing.HasValue && existing.Value != preparationKey)
            throw new InvalidOperationException("The witness belongs to another prepared transaction.");

        Value = null;
        return ValueTask.CompletedTask;
    }
}

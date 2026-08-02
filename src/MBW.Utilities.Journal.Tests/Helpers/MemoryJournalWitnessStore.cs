using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.Tests.Helpers;

internal sealed class MemoryJournalWitnessStore : IJournalWitnessStore
{
    public Guid? Value { get; private set; }

    public ValueTask<Guid?> ReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Value);

    public ValueTask StoreAsync(Guid preparationKey, CancellationToken cancellationToken = default)
    {
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("Key must not be empty", nameof(preparationKey));
        if (Value.HasValue && Value.Value != preparationKey)
            throw new InvalidOperationException("A different key is already stored");

        Value = preparationKey;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(Guid preparationKey, CancellationToken cancellationToken = default)
    {
        if (Value.HasValue && Value.Value != preparationKey)
            throw new InvalidOperationException("A different key is stored");

        Value = null;
        return ValueTask.CompletedTask;
    }
}

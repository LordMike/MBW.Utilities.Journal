using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.Tests.Helpers;

internal sealed class MemoryJournalWitnessStore : IJournalWitnessStore
{
    private byte[]? _fileContents;

    public Guid? Value => _fileContents is null ? null : JournalWitnessRecord.Read(_fileContents);

    public byte[]? FileContents
    {
        get => _fileContents?.ToArray();
        set => _fileContents = value?.ToArray();
    }

    public ValueTask<Guid?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Value);
    }

    public ValueTask StoreAsync(Guid preparationKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid? existing = Value;
        if (existing.HasValue)
        {
            if (existing.Value == preparationKey)
                return ValueTask.CompletedTask;

            throw new InvalidOperationException("The witness belongs to another prepared transaction.");
        }

        _fileContents = JournalWitnessRecord.Create(preparationKey);
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

        _fileContents = null;
        return ValueTask.CompletedTask;
    }
}

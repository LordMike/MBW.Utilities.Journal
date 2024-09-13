namespace MBW.Utilities.Journal.Exceptions;

public sealed class JournalCorruptedException(string message, bool originalFileHasBeenAltered) : Exception(message)
{
    public bool OriginalFileHasBeenAltered { get; } = originalFileHasBeenAltered;
}
using System.Diagnostics.CodeAnalysis;

namespace MBW.Utilities.Journal.Exceptions;

[SuppressMessage("Roslynator", "RCS1194:Implement exception constructors")]
public sealed class JournalCorruptedException(string message, bool originalFileHasBeenAltered) : Exception(message)
{
    public bool OriginalFileHasBeenAltered { get; } = originalFileHasBeenAltered;
}
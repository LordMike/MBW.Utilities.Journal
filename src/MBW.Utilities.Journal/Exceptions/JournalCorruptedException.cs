using System.Diagnostics.CodeAnalysis;

namespace MBW.Utilities.Journal.Exceptions;

[SuppressMessage("Roslynator", "RCS1194:Implement exception constructors")]
public sealed class JournalCorruptedException : Exception
{
    internal JournalCorruptedException(string message, bool originalFileHasBeenAltered) : base(message)
    {
        OriginalFileHasBeenAltered = originalFileHasBeenAltered;
    }

    public bool OriginalFileHasBeenAltered { get; }
}
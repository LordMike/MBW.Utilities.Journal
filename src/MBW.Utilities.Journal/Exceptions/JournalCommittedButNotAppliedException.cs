using System.Diagnostics.CodeAnalysis;

namespace MBW.Utilities.Journal.Exceptions;

[SuppressMessage("Roslynator", "RCS1194:Implement exception constructors")]
public sealed class JournalCommittedButNotAppliedException : Exception
{
    internal JournalCommittedButNotAppliedException(string message) : base(message)
    {
    }
}
using System.Diagnostics.CodeAnalysis;

namespace MBW.Utilities.Journal.Exceptions;

[SuppressMessage("Roslynator", "RCS1194:Implement exception constructors")]
public sealed class JournalInInvalidStateException : Exception
{
    public JournalInInvalidStateException(string message) : base(message)
    {
        // Kept in play, to allow third parties to throw this
    }

    internal JournalInInvalidStateException(JournaledStreamState currentState,
        JournaledStreamState[] expectedStates) : this("This journal is not in a valid state. Current state: " +
                                                      currentState + ", expected one of: " +
                                                      string.Join(", ", expectedStates))
    {
    }
}
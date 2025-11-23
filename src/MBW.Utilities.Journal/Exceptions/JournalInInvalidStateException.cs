using System.Diagnostics.CodeAnalysis;

namespace MBW.Utilities.Journal.Exceptions;

[SuppressMessage("Roslynator", "RCS1194:Implement exception constructors")]
public sealed class JournalInInvalidStateException : Exception
{
    internal JournalInInvalidStateException(JournaledStreamState currentState, JournaledStreamState[] expectedStates) :
        base("This journal is not in a valid state. Current state: " + currentState + ", expected one of: " +
             string.Join(", ", expectedStates))
    {
    }
}
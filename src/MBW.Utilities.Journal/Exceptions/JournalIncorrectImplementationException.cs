using System.Diagnostics.CodeAnalysis;

namespace MBW.Utilities.Journal.Exceptions;

[SuppressMessage("Roslynator", "RCS1194:Implement exception constructors")]
public sealed class JournalIncorrectImplementationException(byte journalImplementationId, byte programImplementationId)
    : Exception(GetMessage(journalImplementationId, programImplementationId))
{
    private static string GetMessage(byte journalImplementationId, byte programImplementationId)
    {
        // Produce user-friendly names for the used journal, 
        string expectedName = Enum.IsDefined((JournalImplementation)journalImplementationId)
            ? ((JournalImplementation)journalImplementationId).ToString()
            : "id " + journalImplementationId;
        string usedName = Enum.IsDefined((JournalImplementation)programImplementationId)
            ? ((JournalImplementation)programImplementationId).ToString()
            : "id " + programImplementationId;

        return
            $"The wrong Journal implementation was used for this existing journal. The existing journal used {expectedName}, and this program tried to use {usedName}";
    }
}
namespace MBW.Utilities.Journal.Structures;

[Flags]
public enum JournalHeaderFlags : byte
{
    None,

    /// <summary>
    /// The journal footer, preparation key, and final length are durable.
    /// </summary>
    Prepared = 1,

    /// <summary>
    /// A durable commit decision exists. This flag is valid only together with <see cref="Prepared"/>.
    /// </summary>
    Committed = 2,
}

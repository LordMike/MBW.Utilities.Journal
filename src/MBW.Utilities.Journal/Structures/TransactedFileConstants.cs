namespace MBW.Utilities.Journal.Structures;

internal static class TransactedFileConstants
{
    /// <summary>
    /// "JRNLVER1"
    /// </summary>
    public const ulong HeaderMagic = 0x315245564C4E524A;

    /// <summary>
    /// "JRNL_END"
    /// </summary>
    public const ulong FooterMagic = 0x444E455F4C4E524A;

    /// <summary>
    /// "SGMT"
    /// </summary>
    public const uint LocalMagic = 0x544D4753;
}
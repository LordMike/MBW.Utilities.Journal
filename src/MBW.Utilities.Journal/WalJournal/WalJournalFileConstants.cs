namespace MBW.Utilities.Journal.WalJournal;

internal static class WalJournalFileConstants
{
    /// <summary>
    /// "JRNL_END"
    /// </summary>
    public const ulong WalJournalFooterMagic = 0x4A524E4C5F454E44;

    /// <summary>
    /// "SGMT"
    /// </summary>
    public const uint WalJournalLocalMagic = 0x53474D54;
}
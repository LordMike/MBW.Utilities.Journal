using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.WalJournal;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WalJournalFooter : IStructWithMagic<ulong>
{
    /// <summary>
    /// "JRNL_END"
    /// </summary>
    public const ulong ExpectedMagic = 0x4A524E4C5F454E44;

    public static int StructSize { get; } = Marshal.SizeOf(typeof(WalJournalFooter));

    public required ulong Magic;
    public required ulong HeaderNonce;
    public required uint Entries;
    public required long FinalLength;
    public required ushort MaxEntryDataLength;
    
    ulong IStructWithMagic<ulong>.Magic => Magic;
}
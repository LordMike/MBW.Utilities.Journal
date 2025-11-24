using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.WalJournal;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WalJournalFooter : IStructWithMagic<ulong>
{
    /// <summary>
    /// "JRNL_END"
    /// </summary>
    public static ulong ExpectedMagic => 0x4A524E4C5F454E44;

    public static int StructSize { get; } = Marshal.SizeOf(typeof(WalJournalFooter));

    internal required ulong Magic;
    internal required ulong HeaderNonce;
    internal required uint Entries;
    internal required long FinalLength;
    internal required ushort MaxEntryDataLength;
    
    ulong IStructWithMagic<ulong>.Magic => Magic;
}
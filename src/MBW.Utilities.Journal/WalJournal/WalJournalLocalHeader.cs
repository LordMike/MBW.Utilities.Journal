using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.WalJournal;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WalJournalLocalHeader : IStructWithMagic<uint>
{
    /// <summary>
    /// "SGMT"
    /// </summary>
    internal const uint ExpectedMagic = 0x53474D54;

    public static int StructSize { get; } = Marshal.SizeOf(typeof(WalJournalLocalHeader));

    internal required uint Magic;
    internal required long InnerOffset;
    internal required ushort Length;
    internal required ulong XxHashChecksum;

    uint IStructWithMagic<uint>.Magic => Magic;
}
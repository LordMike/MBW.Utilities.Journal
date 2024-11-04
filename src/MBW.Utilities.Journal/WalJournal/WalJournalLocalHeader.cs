using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WalJournalLocalHeader : IStructWithMagic<uint>
{
    public static int StructSize { get; } = Marshal.SizeOf(typeof(WalJournalLocalHeader));

    public required uint Magic;
    public required long InnerOffset;
    public required ushort Length;
    public required ulong XxHashChecksum;

    uint IStructWithMagic<uint>.Magic => Magic;
}
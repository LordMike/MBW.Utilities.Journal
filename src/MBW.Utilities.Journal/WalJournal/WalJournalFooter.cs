using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WalJournalFooter : IStructWithMagic<ulong>
{
    public static int StructSize { get; } = Marshal.SizeOf(typeof(WalJournalFooter));

    public required ulong Magic;
    public required ulong HeaderNonce;
    public required uint Entries;
    public required long FinalLength;
    public required ushort MaxEntryDataLength;
    
    ulong IStructWithMagic<ulong>.Magic => Magic;
}
using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.SampleJournal.Implementation;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FullCopyJournalFooter : IStructWithMagic<ulong>
{
    public static ulong ExpectedMagic => 0x46434A4C464E4C31; // "FCJLFNL1"
    public static int StructSize => Marshal.SizeOf<FullCopyJournalFooter>();

    public ulong Magic;
    public long FinalLength;
    
    // Explicit implementation, to let us control the backing field
    ulong IStructWithMagic<ulong>.Magic => Magic;
}
using System.Runtime.InteropServices;

namespace MBW.Utilities.Journal.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct JournalFileHeader : IStructWithMagic<ulong>
{
    public static int StructSize { get; } = Marshal.SizeOf(typeof(JournalFileHeader));

    public required ulong Magic;
    public required JournalStrategy Strategy;
    public required ulong Nonce;
    public required JournalHeaderFlags Flags;

    ulong IStructWithMagic<ulong>.Magic => Magic;
}
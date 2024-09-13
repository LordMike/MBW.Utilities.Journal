using System.Runtime.InteropServices;

namespace MBW.Utilities.Journal.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct TransactLocalHeader
{
    public static readonly int StructSize = Marshal.SizeOf(typeof(TransactLocalHeader));

    public required uint Magic;
    public required long InnerOffset;
    public required ushort Length;
    public required ulong XxHashChecksum;
}
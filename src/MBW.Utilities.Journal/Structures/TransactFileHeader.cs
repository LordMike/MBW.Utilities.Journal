using System.Runtime.InteropServices;

namespace MBW.Utilities.Journal.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct TransactFileHeader
{
    public static readonly int StructSize = Marshal.SizeOf(typeof(TransactFileHeader));

    public required ulong Magic;
    public required ulong Nonce;
}
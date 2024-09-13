using System.Runtime.InteropServices;

namespace MBW.Utilities.Journal.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct TransactFileFooter
{
    public static readonly int StructSize = Marshal.SizeOf(typeof(TransactFileFooter));

    public required ulong Magic;
    public required ulong HeaderNonce;
    public required uint Entries;
    public required long FinalLength;
    public required ushort MaxEntryDataLength;
}
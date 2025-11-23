using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct JournalFileHeader : IStructWithMagic<ulong>
{ 
    /// <summary>
    /// "JRNLVER1"
    /// </summary>
    internal const ulong ExpectedMagic = 0x4A524E4C56455231;
    
    public static int StructSize { get; } = Marshal.SizeOf(typeof(JournalFileHeader));

    internal required ulong Magic;
    internal required JournalStrategy Strategy;
    internal required ulong Nonce;
    internal required JournalHeaderFlags Flags;

    ulong IStructWithMagic<ulong>.Magic => Magic;
}
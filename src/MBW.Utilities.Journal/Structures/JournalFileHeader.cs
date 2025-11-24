using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct JournalFileHeader : IStructWithMagic<ulong>
{ 
    /// <summary>
    /// "JRNLVER1"
    /// </summary>
    public static ulong ExpectedMagic => 0x315245564C4E524A;
    
    public static int StructSize { get; } = Marshal.SizeOf(typeof(JournalFileHeader));

    public required ulong Magic;
    
    /// <summary>
    /// The implementation Id, to help detect if we're using the proper Journal implementation.
    /// This is a byte, to allow for extensions w/o extending this library.
    /// </summary>
    public required byte ImplementationId;
    public required ulong Nonce;
    public required JournalHeaderFlags Flags;

    ulong IStructWithMagic<ulong>.Magic => Magic;
}
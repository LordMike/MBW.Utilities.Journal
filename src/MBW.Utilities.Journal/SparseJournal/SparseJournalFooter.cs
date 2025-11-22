using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.SparseJournal;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparseJournalFooter : IStructWithMagic<ulong>
{
    /// <summary>
    /// "SPRS_END"
    /// </summary>
    public const ulong ExpectedMagic = 0x535052535F454E44;
    
    public static int StructSize { get; } = Marshal.SizeOf(typeof(SparseJournalFooter));

    public required ulong Magic;
    public required ulong HeaderNonce;
    public required long FinalLength;
    /// <summary>
    /// Size of individual chunks, in a power of 2. The size is `pow(BlockSize, 2)`
    /// </summary>
    public required byte BlockSize;
    public required uint BitmapLengthUlongs;
    public required ulong StartOfBitmap;
    
    ulong IStructWithMagic<ulong>.Magic => Magic;
}
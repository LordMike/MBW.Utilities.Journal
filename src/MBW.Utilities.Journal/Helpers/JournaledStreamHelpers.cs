using System.Numerics;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.Helpers;

internal static class JournaledStreamHelpers
{
    public static bool TryRead<TStruct, TMagic>(Stream stream, TMagic expectedMagic, out TStruct header) where TStruct : unmanaged, IStructWithMagic<TMagic> where TMagic : INumber<TMagic>
    {
        header = stream.ReadOneIfEnough<TStruct>(stackalloc byte[TStruct.StructSize], out bool success);
        if (!success)
            return false;

        return header.Magic == expectedMagic;
    }

    public static void CheckOriginStreamRequirements(Stream origin)
    {
        if (origin is { CanWrite: false, CanRead: false })
            throw new ArgumentException("Must be able to write or read from origin", nameof(origin));
        if (origin is { CanSeek: false })
            throw new ArgumentException("Must be able to seek from origin", nameof(origin));
    }
}
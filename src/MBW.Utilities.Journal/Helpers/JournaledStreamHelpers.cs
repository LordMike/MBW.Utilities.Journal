using System.Numerics;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Extensions;

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
}
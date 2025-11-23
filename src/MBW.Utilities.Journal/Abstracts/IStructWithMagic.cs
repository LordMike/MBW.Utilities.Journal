using System.Numerics;

namespace MBW.Utilities.Journal.Abstracts;

internal interface IStructWithMagic<out TMagic> : IStructWithMagic where TMagic : INumber<TMagic>
{
    internal TMagic Magic { get; }
}

internal interface IStructWithMagic
{
    internal static abstract int StructSize { get; }
}
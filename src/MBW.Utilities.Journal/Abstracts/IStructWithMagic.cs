using System.Numerics;

namespace MBW.Utilities.Journal.Abstracts;

internal interface IStructWithMagic<out TMagic> : IStructWithMagic where TMagic : INumber<TMagic>
{
    public TMagic Magic { get; }
}

internal interface IStructWithMagic
{
    public static abstract int StructSize { get; }
}
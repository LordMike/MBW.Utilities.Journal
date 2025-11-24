using System.Numerics;

namespace MBW.Utilities.Journal.Abstracts;

public interface IStructWithMagic<out TMagic> : IStructWithMagic where TMagic : INumber<TMagic>
{
    TMagic Magic { get; }

    static abstract TMagic ExpectedMagic { get; }
}

public interface IStructWithMagic
{
    static abstract int StructSize { get; }
}
using System.Numerics;

namespace MBW.Utilities.Journal.Abstracts;

/// <summary>
/// Adds info the struct, which helps readers know its size and a magic string that can be used to verify the struct read is of the correct type
/// </summary>
/// <typeparam name="TMagic">The magic to use. Ensure this is set when writing the struct.</typeparam>
public interface IStructWithMagic<out TMagic> where TMagic : INumber<TMagic>
{
    static abstract int StructSize { get; }
    
    TMagic Magic { get; }

    static abstract TMagic ExpectedMagic { get; }
}
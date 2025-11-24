using System.Diagnostics;
using System.Numerics;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.Abstracts;

public abstract class JournalFactoryBase(byte implementationId) : IJournalFactory
{
    /// <summary>
    /// Called when a new Journal is to be made. When this method is called, the Journal stream is positioned just after a Journal header has been added.
    /// </summary>
    /// <remarks>Do note the position of the header if you ever need to seek back to the original location in the Journal. Overwriting the header will corrupt the journal</remarks>
    protected abstract IJournal Create(Stream origin, Stream journal, JournalFileHeader header);

    /// <summary>
    /// Called when an existing journal is to be opened. When this method is called, the Journal stream is positioned just after a Journal header has been read.
    /// </summary>
    /// <remarks>Do note the position of the header if you ever need to seek back to the original location in the Journal. Overwriting the header will corrupt the journal</remarks>
    protected abstract IJournal Open(Stream origin, Stream journal, JournalFileHeader header);

    public IJournal Create(Stream origin, Stream journal)
    {
        Debug.Assert(journal.Length == 0);

        JournalFileHeader header = new JournalFileHeader
        {
            Magic = JournalFileHeader.ExpectedMagic,
            Nonce = unchecked((ulong)Random.Shared.NextInt64()),
            ImplementationId = implementationId,
            Flags = JournalHeaderFlags.None
        };
        journal.Write(header.AsSpan());

        return Create(origin, journal, header);
    }

    public IJournal Open(Stream origin, Stream journal)
    {
        if (!JournaledStreamHelpers.TryRead(journal, JournalFileHeader.ExpectedMagic, out JournalFileHeader header))
            throw new JournalCorruptedException("The existing journal was not identified as a valid journal", false);

        if (header.Magic != JournalFileHeader.ExpectedMagic)
            throw new JournalCorruptedException("Journal header was corrupted", false);

        if ((header.Flags & JournalHeaderFlags.Committed) == 0)
            throw new JournalCorruptedException("Journal header indicates the journal was not committed", false);

        if (header.ImplementationId != implementationId)
            throw new JournalIncorrectImplementationException(header.ImplementationId, implementationId);

        return Open(origin, journal, header);
    }

    /// <summary>
    /// Reads the designated footer from the stream. This method seeks to the end, minus the size of the footer struct.
    /// </summary>
    /// <remarks>The structs Magic is validated, an exception is thrown if the struct cannot be read</remarks>
    /// <exception cref="InvalidOperationException">The struct could not be read from the end of the stream</exception>
    protected TStruct ReadFooterStruct<TStruct, TMagic>(Stream stream) where TStruct : unmanaged, IStructWithMagic<TMagic>
        where TMagic : INumber<TMagic>
    {
        stream.Seek(-TStruct.StructSize, SeekOrigin.End);
        return ReadStruct<TStruct, TMagic>(stream);
    }

    /// <summary>
    /// Reads the designated struct from the stream. Use <see cref="ReadFooterStruct"/> if you want to read a footer.
    /// </summary>
    /// <remarks>The structs Magic is validated, an exception is thrown if the struct cannot be read</remarks>
    /// <exception cref="InvalidOperationException">The struct could not be read from the stream</exception>
    protected TStruct ReadStruct<TStruct, TMagic>(Stream stream) where TStruct : unmanaged, IStructWithMagic<TMagic>
        where TMagic : INumber<TMagic>
    {
        if (!JournaledStreamHelpers.TryRead(stream, TStruct.ExpectedMagic, out TStruct header))
            throw new InvalidOperationException($"Unable to read the {typeof(TStruct).Name} struct");

        return header;
    }
}
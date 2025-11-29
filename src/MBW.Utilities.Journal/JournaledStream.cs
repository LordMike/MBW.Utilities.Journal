using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal;

public sealed class JournaledStream : Stream
{
    private JournaledStreamState _state;
    private readonly IJournalStreamFactory _journalStreamFactory;
    private readonly IJournalFactory _journalFactory;

    private readonly Stream _origin;
    private Stream? _journalStream;
    private IJournal? _journal;

    private long _virtualOffset;
    private long _virtualLength;

    /// <summary>
    /// Creates a journal-enabled stream wrapper over an origin stream using the supplied journal storage and strategy.
    /// </summary>
    /// <param name="origin">Underlying stream to be journaled.</param>
    /// <param name="journalStreamFactory">Factory that provides the journal backing stream.</param>
    /// <param name="journalFactory">Factory creating the journal strategy implementation.</param>
    /// <exception cref="ArgumentException">Thrown when the origin stream does not support required capabilities.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a pre-existing journal is detected.</exception>
    internal JournaledStream(Stream origin, IJournalStreamFactory journalStreamFactory, IJournalFactory journalFactory)
    {
        if (origin is { CanWrite: false, CanRead: false })
            throw new ArgumentException("Must be able to write or read from origin", nameof(origin));
        if (origin is { CanSeek: false })
            throw new ArgumentException("Must be able to seek from origin", nameof(origin));

        _origin = origin;
        _journalStreamFactory = journalStreamFactory;
        _journalFactory = journalFactory;

        _virtualLength = _origin.Length;
        _virtualOffset = 0;

        // Determine initial state
        if (_journalStreamFactory.Exists(string.Empty))
            throw new InvalidOperationException("Cannot open a journal stream on a stream with a pre-existing Journal");

        _state = JournaledStreamState.Clean;

        Invariant();
    }

    /// <summary>
    /// Finalizes the journal (if needed) and optionally applies it to the origin.
    /// </summary>
    /// <param name="applyImmediately">When true, applies and deletes the journal; when false, leaves a finalized journal on disk.</param>
    /// <exception cref="JournalInInvalidStateException">Thrown when no journal is open/finalized.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the journal cannot be opened or finalized.</exception>
    public async Task Commit(bool applyImmediately = true)
    {
        if (_state == JournaledStreamState.Clean)
            return;

        RequireState(JournaledStreamState.JournalOpened, JournaledStreamState.JournalFinalized);
        Debug.Assert(IsJournalOpened());

        if (_state == JournaledStreamState.JournalOpened)
        {
            await _journal.FinalizeJournal(_virtualLength);

            // Update header
            _journalStream.Seek(0, SeekOrigin.Begin);
            if (!JournaledStreamHelpers.TryRead(_journalStream, JournalFileHeader.ExpectedMagic,
                    out JournalFileHeader header))
                throw new JournalCorruptedException("Updating the header on the journal was not possible", false);

            header.Flags |= JournalHeaderFlags.Committed;
            _journalStream.Seek(0, SeekOrigin.Begin);
            _journalStream.Write(header.AsSpan());
            await _journalStream.FlushAsync();

            _state = JournaledStreamState.JournalFinalized;
        }

        if (applyImmediately)
        {
            await _journal.ApplyJournal();
            _state = JournaledStreamState.Clean;

            CloseJournal(true);
        }

        Contracts.Ensures((applyImmediately && _state == JournaledStreamState.Clean) ||
                          (!applyImmediately && _state == JournaledStreamState.JournalFinalized));
        Invariant();
    }

    /// <summary>
    /// Discards any uncommitted journal and restores the virtual view to the origin length.
    /// </summary>
    /// <exception cref="JournalInInvalidStateException">Thrown when no journal has been opened.</exception>
    public async Task Rollback()
    {
        if (_state == JournaledStreamState.Clean)
            return;

        RequireState(JournaledStreamState.JournalOpened);
        Debug.Assert(IsJournalOpened());

        _virtualOffset = Math.Clamp(_virtualOffset, 0, _origin.Length);
        _virtualLength = _origin.Length;

        _state = JournaledStreamState.Clean;

        CloseJournal(true);

        Invariant();
    }

    /// <summary>
    /// Reads from the virtual stream view, overlaying journaled data on top of the origin content when applicable.
    /// </summary>
    /// <exception cref="JournalInInvalidStateException">Thrown when the stream is not in a readable state.</exception>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened,
            JournaledStreamState.JournalFinalized);

        // Trim down the read to match the length of the stream, at most
        int maxToRead = (int)Math.Min(_virtualLength - _virtualOffset, buffer.Length);
        buffer = buffer.Slice(0, maxToRead);

        if (_state == JournaledStreamState.Clean)
        {
            _origin.Seek(_virtualOffset, SeekOrigin.Begin);
            int read = await _origin.ReadAsync(buffer, cancellationToken);
            _virtualOffset += read;

            Invariant();
            return read;
        }

        Debug.Assert(IsJournalOpened());

        {
            int read = await _journal.ReadAsync(_virtualOffset, buffer, cancellationToken);
            Debug.Assert(read >= 0 && read <= buffer.Length);

            _virtualOffset += read;

            Invariant();
            return read;
        }
    }

    /// <summary>
    /// Writes to the journal at the current virtual position, extending the virtual length as needed.
    /// </summary>
    /// <exception cref="JournalInInvalidStateException">Thrown when the stream is not writable.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the journal cannot be opened.</exception>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened);

        if (buffer.Length == 0)
            return;

        OpenJournal();

        await _journal.WriteAsync(_virtualOffset, buffer, cancellationToken);

        _virtualOffset += buffer.Length;
        _virtualLength = Math.Max(_virtualOffset, _virtualLength);
        Invariant();
    }

    /// <summary>
    /// Flushes any buffered journal data. Does not flush the origin stream.
    /// </summary>
    public override void Flush() => _journal?.Flush();

    /// <summary>
    /// Adjusts the virtual length of the stream. Extending beyond the origin forces the journal to open.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    /// <exception cref="JournalInInvalidStateException">Thrown when the stream is not in a writable state.</exception>
    public override void SetLength(long value)
    {
        RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened);
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        OpenJournal();

        _virtualLength = value;
        _virtualOffset = Math.Clamp(_virtualOffset, 0, _virtualLength);
        Invariant();
    }

    /// <summary>
    /// Moves the virtual position within the journaled stream. Seeking beyond the origin length will open the journal.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the resulting position is negative.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the stream is finalized and the seek exceeds its virtual length.</exception>
    /// <exception cref="JournalCommittedButNotAppliedException">A write was attempted on a journal which is not yet applied to the origin.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened,
            JournaledStreamState.JournalFinalized);

        long newOffset = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _virtualOffset + offset,
            SeekOrigin.End => _virtualLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };

        if (newOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset),
                $"Desired offset, {offset} from {origin} placed the offset at {newOffset} which was out of range");
        if (newOffset > _virtualLength && _state == JournaledStreamState.JournalFinalized)
            throw new JournalCommittedButNotAppliedException(
                "Cannot write to a committed but not yet applied journal. Call Commit() first to complete the journal, before writing again");

        // If we're outside the origin, we're in Write-territory
        if (newOffset > _origin.Length)
            OpenJournal();

        _virtualOffset = newOffset;
        _virtualLength = Math.Max(_virtualLength, _virtualOffset);

        Invariant();
        return _virtualOffset;
    }

    [MemberNotNullWhen(true, nameof(_journalStream), nameof(_journal))]
    private bool IsJournalOpened()
    {
        if (_journal != null)
        {
            Debug.Assert(_journalStream != null);
            return true;
        }

        return false;
    }

    [MemberNotNull(nameof(_journalStream), nameof(_journal))]
    private void OpenJournal()
    {
        if (_state is JournaledStreamState.JournalOpened)
        {
            Debug.Assert(_journal != null && _journalStream != null);
            return;
        }

        RequireState(JournaledStreamState.Clean);

        // Open a journal
        if (!_journalStreamFactory.TryOpen(string.Empty, true, out _journalStream))
            throw new InvalidOperationException("Unable to open a journal stream");

        _journal = _journalFactory.Create(_origin, _journalStream);
        _state = JournaledStreamState.JournalOpened;
    }

    private void CloseJournal(bool discard)
    {
        _journal = null;

        if (_journalStream != null)
        {
            _journalStream.Dispose();
            _journalStream = null;

            if (discard)
                _journalStreamFactory.Delete(string.Empty);
        }
    }

    protected override void Dispose(bool disposing) => Close();

    /// <summary>
    /// Closes the journaled stream and its journal (if any) without applying pending changes.
    /// </summary>
    public override void Close()
    {
        CloseJournal(false);

        _state = JournaledStreamState.Closed;
        Invariant();
    }

    public override bool CanRead => _origin.CanRead && _state is JournaledStreamState.Clean
        or JournaledStreamState.JournalOpened or JournaledStreamState.JournalFinalized;

    public override bool CanSeek => _origin.CanSeek && _state is JournaledStreamState.Clean
        or JournaledStreamState.JournalOpened or JournaledStreamState.JournalFinalized;

    public override bool CanWrite =>
        _origin.CanWrite && _state is JournaledStreamState.Clean or JournaledStreamState.JournalOpened;

    public override long Length => _virtualLength;

    public override long Position
    {
        get
        {
            RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened,
                JournaledStreamState.JournalFinalized);
            return _virtualOffset;
        }
        set
        {
            RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened,
                JournaledStreamState.JournalFinalized);
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Position must be non-negative");

            if (value > _virtualLength)
                RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened);

            _virtualOffset = value;
            _virtualLength = Math.Max(_virtualLength, _virtualOffset);

            Invariant();
        }
    }

    private void RequireState(params JournaledStreamState[] allowedStats)
    {
        if (!allowedStats.Contains(_state))
            throw new JournalInInvalidStateException(_state, allowedStats);
    }

    private void Invariant()
    {
        Contracts.Invariant(_state != JournaledStreamState.Unset, "State must be initialized");
        Contracts.Invariant(_virtualOffset >= 0, "Virtual offset must be non-negative");
        Contracts.Invariant(_virtualLength >= 0, "Virtual length must be non-negative");
        Contracts.Invariant(_virtualOffset <= _virtualLength);
        Contracts.Invariant((_journal == null) == (_journalStream == null));

        if (_state == JournaledStreamState.Clean)
        {
            Contracts.Invariant(_virtualLength == _origin.Length);

            Contracts.Invariant(_journal == null);
        }
        else if (_state == JournaledStreamState.Closed)
        {
            Contracts.Invariant(_journal == null);
        }
        else if (_state is JournaledStreamState.JournalOpened or JournaledStreamState.JournalFinalized)
        {
            Contracts.Invariant(_journal != null);
        }
    }

    #region Stream Overloads

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

    public override int Read(Span<byte> buffer)
    {
        // Bridge sync call into async core
        byte[] tmp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            // Avoid double-implementing read, by bridging. We just hope the caller used ConfigureAwait(false) if they needed to.
            int read = ReadAsync(tmp.AsMemory(0, buffer.Length)).GetAwaiter().GetResult();
            tmp.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // Bridge sync call into async core
        byte[] tmp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            // Avoid double-implementing read, by bridging. We just hope the caller used ConfigureAwait(false) if they needed to.
            buffer.CopyTo(tmp);
            WriteAsync(tmp.AsMemory(0, buffer.Length)).GetAwaiter().GetResult();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }

    #endregion
}
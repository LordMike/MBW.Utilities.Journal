using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MBW.Utilities.Journal.Abstracts;
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

    public JournaledStream(Stream origin, IJournalStreamFactory journalStreamFactory, IJournalFactory journalFactory)
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

    public async Task Commit(bool applyImmediately = true)
    {
        if (_state == JournaledStreamState.Clean)
            return;

        RequireState(JournaledStreamState.JournalOpened, JournaledStreamState.JournalFinalized);
        Contracts.Requires(IsJournalOpened(false));

        if (_state == JournaledStreamState.JournalOpened)
        {
            await _journal.FinalizeJournal(_virtualLength);

            // Update header
            _journalStream.Seek(0, SeekOrigin.Begin);
            if (!JournaledStreamHelpers.TryRead(_journalStream, JournalFileConstants.HeaderMagic,
                    out JournalFileHeader header))
                throw new InvalidOperationException();

            header.Flags |= JournalHeaderFlags.Committed;
            _journalStream.Seek(0, SeekOrigin.Begin);
            _journalStream.Write(header.AsSpan());

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

    public async Task Rollback()
    {
        if (_state == JournaledStreamState.Clean)
            return;

        RequireState(JournaledStreamState.JournalOpened);
        Contracts.Requires(IsJournalOpened(false));

        _virtualOffset = Math.Clamp(_virtualOffset, 0, _origin.Length);
        _virtualLength = _origin.Length;

        _state = JournaledStreamState.Clean;

        CloseJournal(true);

        Contracts.Ensures(_virtualOffset <= _virtualLength, "Rollback keeps offsets within bounds");
        Contracts.Ensures(_state == JournaledStreamState.Clean, "Rollback transitions to clean");
        Invariant();
    }

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

        Contracts.Requires(IsJournalOpened(false));

        {
            int read = await _journal.ReadAsync(_virtualOffset, buffer, cancellationToken);
            Debug.Assert(read >= 0 && read <= buffer.Length);

            _virtualOffset += read;

            Invariant();
            return read;
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened);
        Contracts.Requires(IsJournalOpened(true));

        if (buffer.Length == 0)
            return;

        await _journal.WriteAsync(_virtualOffset, buffer, cancellationToken);

        _virtualOffset += buffer.Length;
        _virtualLength = Math.Max(_virtualOffset, _virtualLength);
        Invariant();
    }

    public override void Flush()
    {
        _journal?.Flush();
    }

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

    public override void SetLength(long value)
    {
        RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened);
        Contracts.Requires(value >= 0, nameof(value));

        Contracts.Requires(IsJournalOpened(true));

        _virtualLength = value;
        _virtualOffset = Math.Clamp(_virtualOffset, 0, _virtualLength);
        Invariant();
    }

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

        Contracts.Requires(newOffset >= 0,
            $"Desired offset, {offset} from {origin} placed the offset at {newOffset} which was out of range");
        Contracts.Requires(newOffset <= _virtualLength || _state != JournaledStreamState.JournalFinalized,
            $"Desired offset, {offset} is outside the stream, while it is read-only");

        if (newOffset > _origin.Length)
            Contracts.Requires(IsJournalOpened(true));

        _virtualOffset = newOffset;
        _virtualLength = Math.Max(_virtualLength, _virtualOffset);

        Invariant();
        return _virtualOffset;
    }

    [MemberNotNullWhen(true, nameof(_journalStream), nameof(_journal))]
    private bool IsJournalOpened(bool openIfClosed)
    {
        if (_journal != null)
        {
            Debug.Assert(_journalStream != null);
            return true;
        }

        if (!openIfClosed)
            return false;

        // Open a journal
        if (!_journalStreamFactory.TryOpen(string.Empty, true, out _journalStream))
            throw new InvalidOperationException("Unable to open a journal stream");

        _journal = _journalFactory.Create(_origin, _journalStream);
        _state = JournaledStreamState.JournalOpened;
        return true;
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
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened,
                JournaledStreamState.JournalFinalized);
            Contracts.Requires(value >= 0, nameof(value));

            if (value > _virtualLength)
                RequireState(JournaledStreamState.Clean, JournaledStreamState.JournalOpened);

            _virtualOffset = value;
            _virtualLength = Math.Max(_virtualLength, _virtualOffset);

            Invariant();
        }
    }

    private void RequireState(params JournaledStreamState[] allowedStats)
    {
        Contracts.Requires(allowedStats.Contains(_state), "Journaled stream is in an invalid state: " + _state);
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
}
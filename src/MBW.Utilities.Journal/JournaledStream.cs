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
    private Guid? _preparationKey;
    private bool _journalFinalized;
    private bool _recoveryOnlyDirty;
    private bool _allowUnkeyedCommit;

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

        _state = JournaledStreamState.Ready;

        Invariant();
    }

    internal JournaledStream(Stream origin, IJournalStreamFactory journalStreamFactory,
        IJournalFactory journalFactory, Stream journalStream, JournalFileHeader header, IJournal? journal)
    {
        _origin = origin;
        _journalStreamFactory = journalStreamFactory;
        _journalFactory = journalFactory;
        _journalStream = journalStream;
        _journal = journal;
        _virtualOffset = 0;

        if ((header.Flags & JournalHeaderFlags.Prepared) == 0)
        {
            _state = JournaledStreamState.Dirty;
            _virtualLength = origin.Length;
            _recoveryOnlyDirty = true;
        }
        else
        {
            _state = (header.Flags & JournalHeaderFlags.Committed) != 0
                ? JournaledStreamState.CommittedButNotApplied
                : JournaledStreamState.Prepared;
            _virtualLength = header.FinalLength;
            _preparationKey = header.PreparationKey;
            _journalFinalized = true;
        }

        Invariant();
    }

    /// <summary>
    /// Gets the current lifecycle state of this journaled stream.
    /// </summary>
    public JournaledStreamState State => _state;

    /// <summary>
    /// Gets the durable preparation key for a prepared or committed journal.
    /// </summary>
    public Guid? PreparationKey => _preparationKey;

    /// <summary>
    /// Finalizes the journal under a coordination key without making the commit decision durable.
    /// </summary>
    public async Task Prepare(Guid preparationKey)
    {
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("The preparation key must not be empty", nameof(preparationKey));

        if (_state == JournaledStreamState.Prepared)
        {
            if (_preparationKey == preparationKey)
                return;

            throw new JournalInInvalidStateException("The journal is already prepared with a different key");
        }

        RequireState(JournaledStreamState.Ready, JournaledStreamState.Dirty);
        OpenJournal();
        Debug.Assert(IsJournalOpened());

        if (!_journalFinalized)
        {
            await _journal.FinalizeJournal(_virtualLength);
            _journalFinalized = true;
        }

        await _journalStream.FlushDurablyAsync();

        JournalFileHeader header = ReadHeaderForUpdate();
        header.PreparationKey = preparationKey;
        header.FinalLength = _virtualLength;
        header.Flags = JournalHeaderFlags.None;
        await WriteHeaderAndFlush(header);

        header.Flags = JournalHeaderFlags.Prepared;
        await WriteHeaderAndFlush(header);

        _preparationKey = preparationKey;
        _state = JournaledStreamState.Prepared;
        Invariant();
    }

    /// <summary>
    /// Persists a commit marker without applying the journal to the origin.
    /// </summary>
    public async Task Commit()
    {
        if (_state is JournaledStreamState.Ready or JournaledStreamState.CommittedButNotApplied)
            return;

        if (_state == JournaledStreamState.Prepared)
        {
            if (_allowUnkeyedCommit && _preparationKey.HasValue)
            {
                await Commit(_preparationKey.Value);
                return;
            }

            throw new JournalInInvalidStateException(
                "A prepared journal must be committed with its matching preparation key");
        }

        RequireState(JournaledStreamState.Dirty);
        Guid preparationKey = Guid.NewGuid();
        await Prepare(preparationKey);
        _allowUnkeyedCommit = true;
        await Commit(preparationKey);
    }

    /// <summary>
    /// Persists a commit marker for a journal prepared with the matching key.
    /// </summary>
    public async Task Commit(Guid preparationKey)
    {
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("The preparation key must not be empty", nameof(preparationKey));

        if (_state == JournaledStreamState.CommittedButNotApplied)
        {
            if (_preparationKey == preparationKey)
                return;

            throw new JournalInInvalidStateException("The committed journal uses a different preparation key");
        }

        RequireState(JournaledStreamState.Prepared);
        Debug.Assert(IsJournalOpened());

        if (_preparationKey != preparationKey)
            throw new JournalInInvalidStateException("The supplied preparation key does not match the journal");

        JournalFileHeader header = ReadHeaderForUpdate();
        if (header.PreparationKey != preparationKey ||
            (header.Flags & JournalHeaderFlags.Prepared) == 0)
            throw new JournalCorruptedException("The prepared journal header does not match its in-memory state", false);

        header.Flags |= JournalHeaderFlags.Committed;
        await WriteHeaderAndFlush(header);

        _state = JournaledStreamState.CommittedButNotApplied;
        _allowUnkeyedCommit = false;
        Invariant();
    }

    /// <summary>
    /// Applies a committed journal to the origin and removes the journal after a successful flush.
    /// </summary>
    public async Task Apply()
    {
        if (_state == JournaledStreamState.Ready)
            return;

        RequireState(JournaledStreamState.CommittedButNotApplied);
        EnsureFinalizedJournalOpen();

        await _journal.ApplyJournal();
        await _origin.FlushDurablyAsync();
        try
        {
            CloseJournal(true);
        }
        catch
        {
            if (_journalStreamFactory.TryOpen(string.Empty, false, out Stream? reopenedStream))
            {
                try
                {
                    _journalStream = reopenedStream;
                    _journal = _journalFactory.Open(_origin, reopenedStream);
                }
                catch
                {
                    reopenedStream.Dispose();
                    _journalStream = null;
                    _journal = null;
                }
            }
            else
            {
                ResetToReady();
            }

            throw;
        }

        ResetToReady();
        Invariant();
    }

    /// <summary>
    /// Discards any uncommitted journal and restores the virtual view to the origin length.
    /// </summary>
    /// <exception cref="JournalInInvalidStateException">Thrown when no journal has been opened.</exception>
    public async Task Rollback()
    {
        if (_state == JournaledStreamState.Ready)
            return;

        RequireState(JournaledStreamState.Dirty, JournaledStreamState.Prepared);

        _virtualOffset = Math.Clamp(_virtualOffset, 0, _origin.Length);
        _virtualLength = _origin.Length;
        CloseJournal(true);

        _preparationKey = null;
        _journalFinalized = false;
        _recoveryOnlyDirty = false;
        _allowUnkeyedCommit = false;
        _state = JournaledStreamState.Ready;

        Invariant();
    }

    /// <summary>
    /// Reads from the virtual stream view, overlaying journaled data on top of the origin content when applicable.
    /// </summary>
    /// <exception cref="JournalInInvalidStateException">Thrown when the stream is not in a readable state.</exception>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        RequireUsableJournal();
        RequireState(JournaledStreamState.Ready, JournaledStreamState.Dirty,
            JournaledStreamState.Prepared, JournaledStreamState.CommittedButNotApplied);

        // Trim down the read to match the length of the stream, at most
        int maxToRead = (int)Math.Min(_virtualLength - _virtualOffset, buffer.Length);
        buffer = buffer.Slice(0, maxToRead);

        if (_state == JournaledStreamState.Ready)
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
        RequireUsableJournal();
        RequireState(JournaledStreamState.Ready, JournaledStreamState.Dirty);

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
        RequireUsableJournal();
        RequireState(JournaledStreamState.Ready, JournaledStreamState.Dirty);
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
        RequireUsableJournal();
        RequireState(JournaledStreamState.Ready, JournaledStreamState.Dirty,
            JournaledStreamState.Prepared, JournaledStreamState.CommittedButNotApplied);

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
        if (newOffset > _virtualLength && _state is JournaledStreamState.Prepared or JournaledStreamState.CommittedButNotApplied)
            throw new JournalCommittedButNotAppliedException(
                "Cannot extend a prepared or committed journal. Apply or roll it back before writing again");

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
        if (_state is JournaledStreamState.Dirty)
        {
            Debug.Assert(_journal != null && _journalStream != null);
            return;
        }

        RequireState(JournaledStreamState.Ready);

        // Open a journal
        if (!_journalStreamFactory.TryOpen(string.Empty, true, out _journalStream))
            throw new InvalidOperationException("Unable to open a journal stream");

        _journal = _journalFactory.Create(_origin, _journalStream);
        _state = JournaledStreamState.Dirty;
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

    public override bool CanRead => !_recoveryOnlyDirty && _origin.CanRead && _state is JournaledStreamState.Ready
        or JournaledStreamState.Dirty or JournaledStreamState.Prepared or JournaledStreamState.CommittedButNotApplied;

    public override bool CanSeek => !_recoveryOnlyDirty && _origin.CanSeek && _state is JournaledStreamState.Ready
        or JournaledStreamState.Dirty or JournaledStreamState.Prepared or JournaledStreamState.CommittedButNotApplied;

    public override bool CanWrite =>
        !_recoveryOnlyDirty && _origin.CanWrite && _state is JournaledStreamState.Ready or JournaledStreamState.Dirty;

    public override long Length => _virtualLength;

    public override long Position
    {
        get
        {
            RequireUsableJournal();
            RequireState(JournaledStreamState.Ready, JournaledStreamState.Dirty,
                JournaledStreamState.Prepared, JournaledStreamState.CommittedButNotApplied);
            return _virtualOffset;
        }
        set
        {
            RequireUsableJournal();
            RequireState(JournaledStreamState.Ready, JournaledStreamState.Dirty,
                JournaledStreamState.Prepared, JournaledStreamState.CommittedButNotApplied);
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Position must be non-negative");

            if (value > _virtualLength)
                RequireState(JournaledStreamState.Ready, JournaledStreamState.Dirty);

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

    private void RequireUsableJournal()
    {
        if (_recoveryOnlyDirty)
            throw new JournalInInvalidStateException(
                "An unfinalized journal reopened for coordinated recovery can only be rolled back");
    }

    private JournalFileHeader ReadHeaderForUpdate()
    {
        Debug.Assert(_journalStream != null);
        _journalStream.Seek(0, SeekOrigin.Begin);
        if (!JournaledStreamHelpers.TryRead(_journalStream, JournalFileHeader.ExpectedMagic,
                out JournalFileHeader header))
            throw new JournalCorruptedException("Updating the journal header was not possible", false);

        return header;
    }

    private async Task WriteHeaderAndFlush(JournalFileHeader header)
    {
        Debug.Assert(_journalStream != null);
        _journalStream.Seek(0, SeekOrigin.Begin);
        _journalStream.Write(header.AsSpan());
        await _journalStream.FlushDurablyAsync();
    }

    [MemberNotNull(nameof(_journalStream), nameof(_journal))]
    private void EnsureFinalizedJournalOpen()
    {
        if (IsJournalOpened())
            return;

        if (!_journalStreamFactory.TryOpen(string.Empty, false, out Stream? reopenedStream))
            throw new JournalCorruptedException("The finalized journal is no longer available", true);

        try
        {
            _journal = _journalFactory.Open(_origin, reopenedStream);
            _journalStream = reopenedStream;
        }
        catch
        {
            reopenedStream.Dispose();
            throw;
        }
    }

    private void ResetToReady()
    {
        _virtualLength = _origin.Length;
        _virtualOffset = Math.Clamp(_virtualOffset, 0, _virtualLength);
        _preparationKey = null;
        _journalFinalized = false;
        _recoveryOnlyDirty = false;
        _allowUnkeyedCommit = false;
        _state = JournaledStreamState.Ready;
    }

    private void Invariant()
    {
        Contracts.Invariant(_virtualOffset >= 0, "Virtual offset must be non-negative");
        Contracts.Invariant(_virtualLength >= 0, "Virtual length must be non-negative");
        Contracts.Invariant(_virtualOffset <= _virtualLength);
        Contracts.Invariant(_recoveryOnlyDirty || ((_journal == null) == (_journalStream == null)));

        if (_state == JournaledStreamState.Ready)
        {
            Contracts.Invariant(_virtualLength == _origin.Length);

            Contracts.Invariant(_journal == null);
        }
        else if (_state == JournaledStreamState.Closed)
        {
            Contracts.Invariant(_journal == null);
        }
        else if (_state is JournaledStreamState.Dirty or JournaledStreamState.Prepared or
                 JournaledStreamState.CommittedButNotApplied)
        {
            Contracts.Invariant(_journal != null || _recoveryOnlyDirty);
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

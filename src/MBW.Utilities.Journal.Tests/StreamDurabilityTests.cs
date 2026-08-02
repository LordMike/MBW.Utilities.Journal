using MBW.Utilities.Journal.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class StreamDurabilityTests
{
    [Fact]
    public async Task FileStreamsFlushIntermediateBuffers()
    {
        string path = Path.Combine(Path.GetTempPath(), $"journal-flush-{Guid.NewGuid():N}.tmp");
        try
        {
            await using RecordingFileStream stream = new(path);
            await stream.FlushDurablyAsync();

            Assert.True(stream.LastFlushToDisk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OtherStreamsUseTheirFlushContract()
    {
        await using RecordingMemoryStream stream = new();
        await stream.FlushDurablyAsync();

        Assert.Equal(1, stream.FlushCount);
    }

    private sealed class RecordingFileStream(string path)
        : FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
    {
        internal bool? LastFlushToDisk { get; private set; }

        public override void Flush(bool flushToDisk)
        {
            LastFlushToDisk = flushToDisk;
            base.Flush(flushToDisk);
        }
    }

    private sealed class RecordingMemoryStream : MemoryStream
    {
        internal int FlushCount { get; private set; }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return base.FlushAsync(cancellationToken);
        }
    }
}

namespace MBW.Utilities.Journal.Helpers;

internal static class StreamDurability
{
    internal static async ValueTask FlushDurablyAsync(this Stream stream,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (stream is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
            return;
        }

        await stream.FlushAsync(cancellationToken);
    }
}

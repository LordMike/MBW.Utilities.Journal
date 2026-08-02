namespace MBW.Utilities.Journal;

/// <summary>
/// Convenience operations for committing and optionally applying journaled streams.
/// </summary>
public static class JournaledStreamExtensions
{
    /// <summary>
    /// Persists the commit marker and optionally applies the journal immediately.
    /// </summary>
    public static async Task Commit(this JournaledStream stream, bool applyImmediately)
    {
        ArgumentNullException.ThrowIfNull(stream);

        await stream.Commit();
        if (applyImmediately)
            await stream.Apply();
    }
}

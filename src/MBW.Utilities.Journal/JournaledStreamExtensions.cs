namespace MBW.Utilities.Journal;

/// <summary>
/// Convenience operations for committing and applying journaled streams.
/// </summary>
public static class JournaledStreamExtensions
{
    /// <summary>
    /// Persists the commit marker and applies the journal to the origin.
    /// </summary>
    public static async Task CommitAndApply(this JournaledStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        await stream.Commit();
        await stream.Apply();
    }
}

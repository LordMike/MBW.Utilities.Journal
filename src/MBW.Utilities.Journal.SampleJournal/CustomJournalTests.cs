using MBW.Utilities.Journal.SampleJournal.Implementation;
using Xunit;

namespace MBW.Utilities.Journal.SampleJournal;

public class CustomJournalTests
{
    [Fact]
    public async Task UseFullCopyJournalTest()
    {
        string filePath = Path.GetTempFileName();
        string journalPath = filePath + ".journal";

        try
        {
            await using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite))
            {
                // Build a >1 MB payload up front so the "full copy" journal has something chunky to duplicate.
                foreach (var bytes in GenerateLoremIpsums(1024 * 1024))
                    fileStream.Write(bytes);
            }

            // Snapshot the original for future comparisons
            byte[] original = await File.ReadAllBytesAsync(filePath);

            // With existing data, we can open up the file with our new journal, and make edits
            // We'll confirm that the edits do not make it into the original file
            byte[] expectedFinal;
            await using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
            await using (JournaledStream journalStream = await JournaledStreamFactory.CreateJournal(fileStream,
                             new FileBasedJournalStreamFactory(journalPath), new FullCopyJournalFactory()))
            {
                // Make an edit
                journalStream.Write("Edited 1234567890"u8);

                // Read the full edited file out, so that we can verify it later
                journalStream.Seek(0, SeekOrigin.Begin);

                expectedFinal = new byte[journalStream.Length];
                await journalStream.ReadExactlyAsync(expectedFinal);

                // Commit the journal, but do not apply it. We want to verify that the original file stays unedited
                // Usually, a user will call Commit(), which both commits and applies immediately, but for this example, we'll postpone the applying
                await journalStream.Commit(false);
            }

            // As we passed false to Commit, the journal will be committed, but not applied to the original
            // We can now verify that the original is intact
            {
                var actual = await File.ReadAllBytesAsync(filePath);
                Assert.Equal(original, actual);
            }

            // Opening the file again, with a journal, applies the already committed data
            await using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
            await using (JournaledStream journalStream = await JournaledStreamFactory.CreateJournal(fileStream,
                             new FileBasedJournalStreamFactory(journalPath), new FullCopyJournalFactory()))
            {
                // We do not need to do any further work
            }

            // The journal should now have been applied, so we can verify that our file now holds the edited data
            {
                var actual = await File.ReadAllBytesAsync(filePath);
                Assert.Equal(expectedFinal, actual);
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(filePath))
                File.Delete(filePath);
            if (File.Exists(journalPath))
                File.Delete(journalPath);
        }
    }

    private static IEnumerable<byte[]> GenerateLoremIpsums(int limitBytes)
    {
        const string paragraph =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";
        long totalBytes = 0;
        int counter = 0;

        while (totalBytes <= limitBytes)
        {
            string line = $"{counter:D5}: {paragraph}\n";
            byte[] data = System.Text.Encoding.UTF8.GetBytes(line);

            yield return data;

            totalBytes += data.Length;
            counter++;
        }
    }
}
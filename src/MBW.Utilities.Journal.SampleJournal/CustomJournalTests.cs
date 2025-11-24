using MBW.Utilities.Journal;
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
                await journalStream.Commit(false);
            }

            // Prove the original has no changes
            {
                var actual = await File.ReadAllBytesAsync(filePath);
                Assert.Equal(original, actual);
            }

            // Reopen the journal, allow commit to be applied
            await using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
            await using (JournaledStream journalStream = await JournaledStreamFactory.CreateJournal(fileStream,
                             new FileBasedJournalStreamFactory(journalPath), new FullCopyJournalFactory()))
            {
                // At this point, the committed journal will be applied to the orignal
                
                // Confirm the journaled view has our edit
                journalStream.Seek(0, SeekOrigin.Begin);

                var actual = new byte[journalStream.Length];
                await journalStream.ReadExactlyAsync(actual);

                Assert.Equal(expectedFinal, actual);
            }

            // After the journal was applied, confirm our file actually also has the expected content
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
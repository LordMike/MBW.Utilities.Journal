namespace MBW.Utilities.Journal.Tests;

public class JournaledStreamExamples
{
    [Fact]
    public async Task DemonstrateJournaledStreamUsage()
    {
        string filePath = Path.GetTempFileName();
        string journalPath = filePath + ".journal";

        try
        {
            // Create a file stream to work with
            using (FileStream fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                // Wrap the file stream in a JournaledStream. This allows us to read and write to the stream without affecting the underlying file.
                using (JournaledStream journalStream = await JournaledStreamFactory.CreateWalJournal(fileStream, journalPath))
                {
                    // Write data to the JournaledStream
                    // Note! Do not use the original FileStream directly, as this bypasses the Journal
                    string data = "Hello, Journaled World!";
                    journalStream.Write(System.Text.Encoding.UTF8.GetBytes(data));

                    // Commit the transaction, persisting the data to the file
                    // Alternatively, you can also call RollBack() to delete the journal and discard any changes
                    await journalStream.Commit();
                }
            }

            // At this point, the data has been written to the file and committed.

            // At a later point, we can reopen the file and read the data using JournaledStream
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                // Even though we will not write to the file, we still need to use the JournaledStream. This ensures
                // that if our write was partial, we will replay the journal again to ensure it has been fully written.
                using (JournaledStream journalStream = await JournaledStreamFactory.CreateWalJournal(fileStream, journalPath))
                {
                    // Read the data back from the JournaledStream
                    byte[] readBuffer = new byte[1024];
                    int bytesRead = journalStream.Read(readBuffer, 0, readBuffer.Length);
                    string readData = System.Text.Encoding.UTF8.GetString(readBuffer, 0, bytesRead);

                    Assert.Equal("Hello, Journaled World!", readData);
                }
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
}

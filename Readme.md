## MBW.Utilities.TransactedStream [![Generic Build](https://github.com/LordMike/MBW.Utilities.TransactedStream/actions/workflows/dotnet.yml/badge.svg)](https://github.com/LordMike/MBW.Utilities.TransactedStream/actions/workflows/dotnet.yml) [![NuGet](https://img.shields.io/nuget/v/MBW.Utilities.TransactedStream.svg)](https://www.nuget.org/packages/MBW.Utilities.TransactedStream)

An implementation of a generic transactional stream for .NET, with support for writing changes to a journal file. The journal can then be committed, rolled back or replayed to ensure consistent writes. 

## Packages

| Package |                                                                        Nuget                                                                         | Alpha |
| ------------- |:----------------------------------------------------------------------------------------------------------------------------------------------------:|:-------------:|
| MBW.Utilities.TransactedStream |     [![NuGet](https://img.shields.io/nuget/v/MBW.Utilities.TransactedStream.svg)](https://www.nuget.org/packages/MBW.Utilities.TransactedStream)     | [Alpha](https://github.com/LordMike/MBW.Utilities.TransactedStream/packages/692005) |

## How to use

```csharp
using (var fs = File.Open("myfile", FileMode.OpenOrCreate))
{
    using (var transaction = new TransactedStream.TransactedStream(fs, "myfile.jrnl"))
    {
        // Write changes to transaction here. They will not be written to 'fs' yet
        using var sw = new StreamWriter(transaction);
        sw.WriteLine("Transacted change!");
        
        // At this point, if the app closes or computer crashes, "myfile" has not been altered yet
        
        // Commit the changes, this writes it to the original stream
        transaction.Commit();
    }
}

using (var fs = File.Open("myfile", FileMode.OpenOrCreate))
{
    // At a later point, we can open a transaction to "myfile" again
    // If there was an incomplete transaction (committed, but not yet written to "myfile"), it will be applied here
    using (var transaction = new TransactedStream.TransactedStream(fs, "myfile.jrnl"))
    {
        // Read the file. Transaction here will never see changes that weren't fully committed and written to "myfile"
        using var sr = new StreamReader(transaction);
        var str = sr.ReadToEnd();
    }
}
```

### Features

* Turn any read/write stream into a journaled stream which guarantees that writes complete fully, or can be retried
* Easy codepath for using a file as the journal

## Notes

* If you have a database file or similar you want to protect, it is important to _always_ wrap streams for that in TransactedStreams. The reason being that if a write happens that isn't fully written to the file, it will not be noticed by future readers, if they don't also use TransactedStreams
  * It is recommended to create a producer in your app, that produces the stream (wrapped in TransactedStream) for your datafile centrally, so all users get the same behavior
  * TransactedStream does not create the journal, until a write happens, so the cost of creating a TransactedStream is a file exists check (to see if a past journal existed)
* This is not thread safe in any manner, like all other streams
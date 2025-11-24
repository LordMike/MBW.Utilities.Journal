## MBW.Utilities.Journal [![Generic Build](https://github.com/LordMike/MBW.Utilities.Journal/actions/workflows/dotnet.yml/badge.svg)](https://github.com/LordMike/MBW.Utilities.Journal/actions/workflows/dotnet.yml) [![NuGet](https://img.shields.io/nuget/v/MBW.Utilities.Journal.svg)](https://www.nuget.org/packages/MBW.Utilities.Journal)

An implementation of a generic transactional stream for .NET, with support for writing changes to a journal file. The journal can then be committed, rolled back or replayed to ensure consistent writes. 

## Packages

| Package |                                                                        Nuget                                                                         | Alpha |
| ------------- |:----------------------------------------------------------------------------------------------------------------------------------------------------:|:-------------:|
| MBW.Utilities.Journal |     [![NuGet](https://img.shields.io/nuget/v/MBW.Utilities.Journal.svg)](https://www.nuget.org/packages/MBW.Utilities.Journal)     | [Alpha](https://github.com/LordMike/MBW.Utilities.Journal/packages/692005) |

## How to use

```
var myStream = File.OpenWrite("DemoFile.txt");
var myJournal = await JournaledStreamFactory.CreateSparseJournal(myStream, "DemoFile.txt.jrnl");

// Always use the Journal to write to the file
myJournal.Write("Hello world"u8);

// Remember to call Commit() to apply changes
await myJournal.Commit();
```

More extended examples:
* Refer to this example in the tests: [JournaledStreamExamples.cs](src/MBW.Utilities.Journal.Tests/JournaledStreamExamples.cs).
* Implement your own Journal implementation: refer to the [sample project](src/MBW.Utilities.Journal.SampleJournal).
* The remaining tests may also be useful for references

### Features

* Turn any read/write stream into a journaled stream which guarantees that writes complete fully, or can be retried, assuming your streams are backed by persistent storage
* Easy codepath for using a file as the journal
* Exchangeable journal file implementation, so the journal can be written other places than in files
* Fully async API
* Ability to Commit and Apply journals in separate operations
* Recovers unapplied journals on startup, if a previous program run did not fully apply a journal (configurable behaviour)

## Notes

* If you have a database file or similar you want to protect, it is important to _always_ wrap streams for that in JournalStreams. The reason being that if a write happens that isn't fully written to the file, it will not be noticed by future readers, if they don't also use JournalStreams
  * It is recommended to create a producer in your app, that produces the stream (wrapped in JournalStreams) for your datafile centrally, so all users get the same behavior
  * JournalStreams does not create the journal, until a write happens, so the cost of creating a JournalStreams is an exists check (to see if a past journal exists)
* This is not thread safe in any manner, like all other streams
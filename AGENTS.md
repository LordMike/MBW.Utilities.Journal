# Project description
This repository provides a journaling layer over streams so reads and writes can be staged, rolled forward, or discarded safely. It supports multiple journaling strategies, handles recovery/apply/discard flows for pending journals, and exposes a single journaled stream façade that enforces state transitions and invariants around committing or rolling back changes.

Supported strategies:
- Write-ahead log: append-only journal that replays tracked segments onto the origin.
- Sparse journal: block-based overlay that tracks dirty blocks via bitmap and applies only touched blocks.

# Development
- Journaled stream façade (`JournaledStream`, `JournaledStreamFactory`) coordinates origin streams with journal factories/streams, tracks virtual position/length, and enforces contracts for stateful operations (commit, rollback, seek, set length, read/write).
- Journal implementations (`WalJournal`, `SparseJournal`, their factories) encapsulate strategy-specific IO, metadata (headers/footers/bitmaps), and apply/finalize behaviors.
- Contract helpers and invariants guard pre/postconditions and state; exceptions convey invalid states, corruption, or committed-but-not-applied scenarios.
- Unit tests cover WAL and sparse scenarios, corruption handling, recovery modes, open modes, and general journaling behaviors via in-memory test streams and journal factories.

# MergerNotes

MergerNotes is a cross-platform note merger/importer for `*.jwlibrary` backups.

Current focus:
- Parse and verify `.jwlibrary` packages
- Inspect notes, highlights, tags, bookmarks, and media
- Prepare a Windows-first UI with a shared core for future macOS/Linux support

## Status

This repository currently contains the first project skeleton:
- Avalonia desktop app shell
- Core data models
- Backup inspection/import service contracts
- Architecture notes

## Build prerequisites

To build the app locally you will need:
- .NET 8 SDK or newer
- Avalonia UI packages restored from NuGet

## Suggested next step

Open `docs/architecture.md` for the proposed project structure and data flow.

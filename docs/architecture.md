# Architecture

## Goals

- Cross-platform desktop app
- Windows-first delivery
- Safe import of `.jwlibrary` backup packages
- Keep business logic separate from UI

## Proposed solution layout

```text
src/
  MergerNotes.App/          # Avalonia desktop shell
  MergerNotes.Core/         # Shared domain models and import contracts
  MergerNotes.Infrastructure/ # SQLite/ZIP parsing and backup adapters
```

## Import pipeline

1. Open `.jwlibrary` as a ZIP archive
2. Read `manifest.json`
3. Verify `userData.db` SHA-256 against the manifest
4. Load `userData.db` from SQLite
5. Map `Location`, `Note`, `UserMark`, `TagMap`, `Bookmark`, `IndependentMedia`
6. Present parsed data in the UI for preview and merge actions

## Domain model

- `BackupPackage`
- `DocumentLocation`
- `Annotation`
- `Note`
- `Bookmark`
- `Tag`
- `MediaAsset`

## Windows packaging

For Windows delivery, the app can later be published with:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## Notes

- `Location` is the central anchor for most records
- `Note` may exist without content
- `IndependentMedia` should stay as a separate asset model
- `TagMap` is polymorphic and can reference notes, locations, or playlist items

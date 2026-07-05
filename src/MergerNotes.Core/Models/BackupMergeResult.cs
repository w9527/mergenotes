namespace MergerNotes.Core.Models;

public sealed record BackupMergeResult(
    string OutputPath,
    BackupSnapshot Snapshot,
    MergeReport Report);

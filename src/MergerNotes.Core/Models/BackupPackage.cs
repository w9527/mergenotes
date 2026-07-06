namespace MergerNotes.Core.Models;

public sealed record BackupPackage(
    string Name,
    DateTimeOffset CreationDate,
    string DatabaseName,
    string DatabaseSha256,
    int SchemaVersion)
{
    public bool DatabaseHashMatchesManifest { get; init; } = true;
}

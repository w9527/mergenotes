using MergerNotes.Core.Models;

namespace MergerNotes.Core.Abstractions;

public interface IBackupImporter
{
    Task<BackupSnapshot> ReadSnapshotAsync(string backupPath, CancellationToken cancellationToken = default);
}

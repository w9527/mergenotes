using MergerNotes.Core.Models;

namespace MergerNotes.Core.Abstractions;

public interface IBackupMerger
{
    Task<BackupMergeResult> MergeAsync(
        string baseBackupPath,
        string incomingBackupPath,
        string outputBackupPath,
        CancellationToken cancellationToken = default);
}

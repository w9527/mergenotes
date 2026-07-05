using System.IO.Compression;
using MergerNotes.Core.Abstractions;
using MergerNotes.Core.Models;

namespace MergerNotes.Infrastructure.Import;

public sealed class JwlibraryBackupImporter : IBackupImporter
{
    public async Task<BackupSnapshot> ReadSnapshotAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            throw new ArgumentException("Backup path cannot be empty.", nameof(backupPath));
        }

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup file was not found.", backupPath);
        }

        var extractDir = Path.Combine(Path.GetTempPath(), "MergerNotes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);

        try
        {
            await JwlibrarySnapshotReader.ExtractAsync(backupPath, extractDir, cancellationToken).ConfigureAwait(false);

            var manifest = await JwlibrarySnapshotReader.ReadManifestAsync(extractDir, cancellationToken).ConfigureAwait(false);
            var dbPath = Path.Combine(extractDir, manifest.DatabaseName);

            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException($"Database file '{manifest.DatabaseName}' was not found in the backup.", dbPath);
            }

            var dbHash = await JwlibrarySnapshotReader.ComputeSha256Async(dbPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(dbHash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Database SHA-256 did not match the manifest.");
            }

            return await JwlibrarySnapshotReader.ReadSnapshotAsync(manifest, dbPath, extractDir, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(extractDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}

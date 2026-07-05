using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MergerNotes.Core.Abstractions;
using MergerNotes.Core.Models;

namespace MergerNotes.Infrastructure.Import;

public sealed class JwlibraryBackupMerger : IBackupMerger
{
    public async Task<BackupMergeResult> MergeAsync(
        string baseBackupPath,
        string incomingBackupPath,
        string outputBackupPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseBackupPath))
        {
            throw new ArgumentException("Base backup path cannot be empty.", nameof(baseBackupPath));
        }

        if (string.IsNullOrWhiteSpace(incomingBackupPath))
        {
            throw new ArgumentException("Incoming backup path cannot be empty.", nameof(incomingBackupPath));
        }

        if (string.IsNullOrWhiteSpace(outputBackupPath))
        {
            throw new ArgumentException("Output backup path cannot be empty.", nameof(outputBackupPath));
        }

        var root = Path.Combine(Path.GetTempPath(), "MergerNotesMerge", Guid.NewGuid().ToString("N"));
        var baseDir = Path.Combine(root, "base");
        var incomingDir = Path.Combine(root, "incoming");
        var outputDir = Path.Combine(root, "output");
        Directory.CreateDirectory(root);

        try
        {
            await JwlibrarySnapshotReader.ExtractAsync(baseBackupPath, outputDir, cancellationToken).ConfigureAwait(false);
            await JwlibrarySnapshotReader.ExtractAsync(incomingBackupPath, incomingDir, cancellationToken).ConfigureAwait(false);

            var baseManifest = await JwlibrarySnapshotReader.ReadManifestAsync(outputDir, cancellationToken).ConfigureAwait(false);
            var incomingManifest = await JwlibrarySnapshotReader.ReadManifestAsync(incomingDir, cancellationToken).ConfigureAwait(false);
            var outputDbPath = Path.Combine(outputDir, baseManifest.DatabaseName);
            var incomingDbPath = Path.Combine(incomingDir, incomingManifest.DatabaseName);

            var baseSnapshot = await JwlibrarySnapshotReader.ReadSnapshotAsync(baseManifest, outputDbPath, outputDir, cancellationToken).ConfigureAwait(false);
            var incomingSnapshot = await JwlibrarySnapshotReader.ReadSnapshotAsync(incomingManifest, incomingDbPath, incomingDir, cancellationToken).ConfigureAwait(false);

            var report = await MergeSnapshotsAsync(
                outputDbPath,
                outputDir,
                incomingSnapshot,
                baseSnapshot,
                incomingDir,
                cancellationToken).ConfigureAwait(false);

            await WriteManifestAsync(
                outputDir,
                outputBackupPath,
                baseManifest,
                outputDbPath,
                cancellationToken).ConfigureAwait(false);

            if (File.Exists(outputBackupPath))
            {
                File.Delete(outputBackupPath);
            }

            ZipFile.CreateFromDirectory(outputDir, outputBackupPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            var finalSnapshot = await new JwlibraryBackupImporter().ReadSnapshotAsync(outputBackupPath, cancellationToken).ConfigureAwait(false);
            return new BackupMergeResult(outputBackupPath, finalSnapshot, report);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static async Task<MergeReport> MergeSnapshotsAsync(
        string outputDbPath,
        string outputDir,
        BackupSnapshot incoming,
        BackupSnapshot baseSnapshot,
        string incomingDir,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={outputDbPath};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var nextIds = await LoadNextIdsAsync(connection, cancellationToken).ConfigureAwait(false);
        var locationMap = BuildLocationMap(baseSnapshot.Locations);
        var userMarkMap = BuildUserMarkMap(baseSnapshot.UserMarks);
        var noteMap = BuildNoteMap(baseSnapshot.Notes);
        var tagKeyMap = BuildTagMap(baseSnapshot.Tags);
        var tagIdMap = new Dictionary<long, long>();
        var bookmarkMap = BuildBookmarkMap(baseSnapshot.Bookmarks);
        var mediaMap = BuildMediaMap(baseSnapshot.MediaAssets);
        var inputFieldMap = BuildInputFieldMap(baseSnapshot.InputFields);
        var blockRangeMap = BuildBlockRangeMap(baseSnapshot.BlockRanges);
        var nextTagMapPositions = BuildNextTagMapPositions(baseSnapshot.TagMaps);

        var addedLocations = 0;
        var addedUserMarks = 0;
        var addedNotes = 0;
        var updatedNotes = 0;
        var addedBlockRanges = 0;
        var addedTags = 0;
        var addedTagMaps = 0;
        var addedBookmarks = 0;
        var addedMediaAssets = 0;
        var addedInputFields = 0;
        var skippedPlaylistTagMaps = 0;

        foreach (var location in incoming.Locations)
        {
            var key = LocationKey.From(location);
            if (!locationMap.TryGetValue(key, out var mappedId))
            {
                mappedId = nextIds.LocationId++;
                await InsertLocationAsync(connection, transaction, location with { LocationId = mappedId }, cancellationToken).ConfigureAwait(false);
                locationMap[key] = mappedId;
                addedLocations++;
            }

            locationMap[key] = mappedId;
        }

        foreach (var userMark in incoming.UserMarks)
        {
            var mappedLocationId = locationMap[LocationKey.From(incoming.Locations.First(l => l.LocationId == userMark.LocationId))];
            if (!userMarkMap.TryGetValue(userMark.UserMarkGuid, out var mappedId))
            {
                mappedId = nextIds.UserMarkId++;
                await InsertUserMarkAsync(connection, transaction, userMark with { UserMarkId = mappedId, LocationId = mappedLocationId }, cancellationToken).ConfigureAwait(false);
                userMarkMap[userMark.UserMarkGuid] = mappedId;
                addedUserMarks++;
            }
            else
            {
                await UpdateUserMarkAsync(connection, transaction, mappedId, userMark with { LocationId = mappedLocationId }, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var note in incoming.Notes)
        {
            var mappedLocationId = note.LocationId is long noteLocationId && incoming.Locations.Any(l => l.LocationId == noteLocationId)
                ? locationMap[LocationKey.From(incoming.Locations.First(l => l.LocationId == noteLocationId))]
                : note.LocationId;
            var mappedUserMarkId = note.UserMarkId is long noteUserMarkId && incoming.UserMarks.Any(u => u.UserMarkId == noteUserMarkId)
                ? userMarkMap[incoming.UserMarks.First(u => u.UserMarkId == noteUserMarkId).UserMarkGuid]
                : note.UserMarkId;

            if (!noteMap.TryGetValue(note.Guid, out var existing))
            {
                var mapped = note with { NoteId = nextIds.NoteId++, LocationId = mappedLocationId, UserMarkId = mappedUserMarkId };
                await InsertNoteAsync(connection, transaction, mapped, cancellationToken).ConfigureAwait(false);
                noteMap[note.Guid] = mapped.NoteId;
                addedNotes++;
            }
            else
            {
                var existingNote = baseSnapshot.Notes.First(n => n.NoteId == existing);
                if (note.LastModified > existingNote.LastModified || note.Content != existingNote.Content || note.Title != existingNote.Title)
                {
                    await UpdateNoteAsync(connection, transaction, existing, note with { LocationId = mappedLocationId, UserMarkId = mappedUserMarkId }, cancellationToken).ConfigureAwait(false);
                    updatedNotes++;
                }
            }
        }

        var incomingUserMarksById = incoming.UserMarks.ToDictionary(x => x.UserMarkId);

        foreach (var range in incoming.BlockRanges)
        {
            if (!incomingUserMarksById.TryGetValue(range.UserMarkId, out var incomingUserMark))
            {
                continue;
            }

            var mappedUserMarkId = userMarkMap[incomingUserMark.UserMarkGuid];
            var key = new BlockRangeKey(range.BlockType, range.Identifier, range.StartToken, range.EndToken, mappedUserMarkId);
            if (blockRangeMap.Add(key))
            {
                var mapped = range with { BlockRangeId = nextIds.BlockRangeId++, UserMarkId = mappedUserMarkId };
                await InsertBlockRangeAsync(connection, transaction, mapped, cancellationToken).ConfigureAwait(false);
                addedBlockRanges++;
            }
        }

        foreach (var tag in incoming.Tags)
        {
            var key = TagKey.From(tag);
            if (!tagKeyMap.ContainsKey(key))
            {
                var mapped = tag with { TagId = nextIds.TagId++ };
                await InsertTagAsync(connection, transaction, mapped, cancellationToken).ConfigureAwait(false);
                tagKeyMap[key] = mapped.TagId;
                tagIdMap[tag.TagId] = mapped.TagId;
                addedTags++;
            }
            else
            {
                tagIdMap[tag.TagId] = tagKeyMap[key];
            }
        }

        foreach (var bookmark in incoming.Bookmarks)
        {
            var mappedLocationId = locationMap[LocationKey.From(incoming.Locations.First(l => l.LocationId == bookmark.LocationId))];
            var mappedPublicationLocationId = locationMap[LocationKey.From(incoming.Locations.First(l => l.LocationId == bookmark.PublicationLocationId))];
            var key = BookmarkKey.From(bookmark, mappedLocationId, mappedPublicationLocationId);
            if (!bookmarkMap.ContainsKey(key))
            {
                var mapped = bookmark with { BookmarkId = nextIds.BookmarkId++, LocationId = mappedLocationId, PublicationLocationId = mappedPublicationLocationId };
                await InsertBookmarkAsync(connection, transaction, mapped, cancellationToken).ConfigureAwait(false);
                bookmarkMap[key] = mapped.BookmarkId;
                addedBookmarks++;
            }
        }

        foreach (var media in incoming.MediaAssets)
        {
            if (mediaMap.ContainsKey(media.Sha256))
            {
                continue;
            }

            var sourceAbs = Path.Combine(incomingDir, media.FilePath);
            if (!File.Exists(sourceAbs))
            {
                continue;
            }

            var targetFilePath = media.FilePath;
            var targetAbs = Path.Combine(outputDir, targetFilePath);
            if (File.Exists(targetAbs))
            {
                targetFilePath = BuildUniqueMediaPath(media, media.Sha256);
                targetAbs = Path.Combine(outputDir, targetFilePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetAbs) ?? outputDir);
            File.Copy(sourceAbs, targetAbs, overwrite: true);

            var mapped = media with { IndependentMediaId = nextIds.IndependentMediaId++, FilePath = targetFilePath };
            await InsertMediaAsync(connection, transaction, mapped, cancellationToken).ConfigureAwait(false);
            mediaMap[media.Sha256] = mapped.IndependentMediaId;
            addedMediaAssets++;
        }

        foreach (var field in incoming.InputFields)
        {
            var mappedLocationId = locationMap[LocationKey.From(incoming.Locations.First(l => l.LocationId == field.LocationId))];
            var key = new InputFieldKey(mappedLocationId, field.TextTag);
            if (!inputFieldMap.Contains(key))
            {
                var mapped = field with { LocationId = mappedLocationId };
                await InsertInputFieldAsync(connection, transaction, mapped, cancellationToken).ConfigureAwait(false);
                inputFieldMap.Add(key);
                addedInputFields++;
            }
        }

        foreach (var tagMapRow in incoming.TagMaps)
        {
            if (tagMapRow.PlaylistItemId is not null)
            {
                skippedPlaylistTagMaps++;
                continue;
            }

            if (tagMapRow.NoteId is long noteIdValue && incoming.Notes.Any(n => n.NoteId == noteIdValue))
            {
                var incomingNote = incoming.Notes.First(n => n.NoteId == noteIdValue);
                if (!noteMap.TryGetValue(incomingNote.Guid, out var mappedNoteId))
                {
                    continue;
                }

                if (!tagIdMap.TryGetValue(tagMapRow.TagId, out var mappedTagId))
                {
                    continue;
                }

                var key = new TagMapKey(mappedNoteId, null, null, mappedTagId, tagMapRow.Position);
                if (await TagMapExistsAsync(connection, cancellationToken, key).ConfigureAwait(false))
                {
                    continue;
                }

                await InsertTagMapAsync(connection, transaction, tagMapRow with { TagMapId = nextIds.TagMapId++, NoteId = mappedNoteId, PlaylistItemId = null, LocationId = null, TagId = mappedTagId, Position = GetNextTagMapPosition(nextTagMapPositions, mappedTagId) }, cancellationToken).ConfigureAwait(false);
                addedTagMaps++;
                continue;
            }

            if (tagMapRow.LocationId is long locationIdValue && incoming.Locations.Any(l => l.LocationId == locationIdValue))
            {
                var incomingLocation = incoming.Locations.First(l => l.LocationId == locationIdValue);
                var mappedLocationId = locationMap[LocationKey.From(incomingLocation)];
                if (!tagIdMap.TryGetValue(tagMapRow.TagId, out var mappedTagId))
                {
                    continue;
                }

                var key = new TagMapKey(null, mappedLocationId, null, mappedTagId, tagMapRow.Position);
                if (await TagMapExistsAsync(connection, cancellationToken, key).ConfigureAwait(false))
                {
                    continue;
                }

                await InsertTagMapAsync(connection, transaction, tagMapRow with { TagMapId = nextIds.TagMapId++, NoteId = null, PlaylistItemId = null, LocationId = mappedLocationId, TagId = mappedTagId, Position = GetNextTagMapPosition(nextTagMapPositions, mappedTagId) }, cancellationToken).ConfigureAwait(false);
                addedTagMaps++;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new MergeReport(
            baseSnapshot.Notes.Count,
            incoming.Notes.Count,
            baseSnapshot.Notes.Count + addedNotes,
            addedLocations,
            addedUserMarks,
            addedNotes,
            updatedNotes,
            addedBlockRanges,
            addedTags,
            addedTagMaps,
            addedBookmarks,
            addedMediaAssets,
            addedInputFields,
            skippedPlaylistTagMaps);
    }

    private static async Task WriteManifestAsync(
        string outputDir,
        string outputBackupPath,
        BackupPackage baseManifest,
        string dbPath,
        CancellationToken cancellationToken)
    {
        var hash = await JwlibrarySnapshotReader.ComputeSha256Async(dbPath, cancellationToken).ConfigureAwait(false);
        var manifest = new
        {
            name = Path.GetFileName(outputBackupPath),
            creationDate = DateTimeOffset.UtcNow.ToString("O"),
            version = 1,
            type = 0,
            userDataBackup = new
            {
                lastModifiedDate = DateTimeOffset.UtcNow.ToString("O"),
                deviceName = "MergerNotes",
                databaseName = baseManifest.DatabaseName,
                hash,
                schemaVersion = baseManifest.SchemaVersion,
            },
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = false });
        await File.WriteAllTextAsync(Path.Combine(outputDir, "manifest.json"), json, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(long LocationId, long UserMarkId, long NoteId, long BlockRangeId, long TagId, long TagMapId, long BookmarkId, long IndependentMediaId)> LoadNextIdsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        return (
            await GetNextIdAsync(connection, "Location", "LocationId", cancellationToken).ConfigureAwait(false),
            await GetNextIdAsync(connection, "UserMark", "UserMarkId", cancellationToken).ConfigureAwait(false),
            await GetNextIdAsync(connection, "Note", "NoteId", cancellationToken).ConfigureAwait(false),
            await GetNextIdAsync(connection, "BlockRange", "BlockRangeId", cancellationToken).ConfigureAwait(false),
            await GetNextIdAsync(connection, "Tag", "TagId", cancellationToken).ConfigureAwait(false),
            await GetNextIdAsync(connection, "TagMap", "TagMapId", cancellationToken).ConfigureAwait(false),
            await GetNextIdAsync(connection, "Bookmark", "BookmarkId", cancellationToken).ConfigureAwait(false),
            await GetNextIdAsync(connection, "IndependentMedia", "IndependentMediaId", cancellationToken).ConfigureAwait(false));
    }

    private static async Task<long> GetNextIdAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COALESCE(MAX({column}), 0) + 1 FROM {table}";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    private static Dictionary<LocationKey, long> BuildLocationMap(IEnumerable<DocumentLocation> rows)
        => rows.ToDictionary(LocationKey.From, x => x.LocationId);

    private static Dictionary<Guid, long> BuildUserMarkMap(IEnumerable<UserMark> rows)
        => rows.ToDictionary(x => x.UserMarkGuid, x => x.UserMarkId);

    private static Dictionary<Guid, long> BuildNoteMap(IEnumerable<Note> rows)
        => rows.ToDictionary(x => x.Guid, x => x.NoteId);

    private static Dictionary<TagKey, long> BuildTagMap(IEnumerable<Tag> rows)
        => rows.ToDictionary(TagKey.From, x => x.TagId);

    private static Dictionary<BookmarkKey, long> BuildBookmarkMap(IEnumerable<Bookmark> rows)
        => rows.ToDictionary(x => BookmarkKey.From(x, x.LocationId, x.PublicationLocationId), x => x.BookmarkId);

    private static Dictionary<string, long> BuildMediaMap(IEnumerable<MediaAsset> rows)
        => rows.ToDictionary(x => x.Sha256, x => x.IndependentMediaId);

    private static HashSet<InputFieldKey> BuildInputFieldMap(IEnumerable<InputField> rows)
        => rows.Select(x => new InputFieldKey(x.LocationId, x.TextTag)).ToHashSet();

    private static HashSet<BlockRangeKey> BuildBlockRangeMap(IEnumerable<BlockRange> rows)
        => rows.Select(x => new BlockRangeKey(x.BlockType, x.Identifier, x.StartToken, x.EndToken, x.UserMarkId)).ToHashSet();

    private static Dictionary<long, int> BuildNextTagMapPositions(IEnumerable<TagMap> rows)
        => rows.GroupBy(x => x.TagId).ToDictionary(x => x.Key, x => x.Max(row => row.Position) + 1);

    private static int GetNextTagMapPosition(Dictionary<long, int> positions, long tagId)
    {
        if (!positions.TryGetValue(tagId, out var position))
        {
            position = 0;
        }

        positions[tagId] = position + 1;
        return position;
    }

    private static string BuildUniqueMediaPath(MediaAsset media, string sha256)
    {
        var directory = Path.GetDirectoryName(media.FilePath);
        var extension = Path.GetExtension(media.FilePath);
        var baseName = Path.GetFileNameWithoutExtension(media.FilePath);
        var unique = $"{baseName}_{sha256[..8]}{extension}";
        return string.IsNullOrWhiteSpace(directory) ? unique : Path.Combine(directory, unique);
    }

    private static async Task InsertLocationAsync(SqliteConnection connection, SqliteTransaction transaction, DocumentLocation row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Location (LocationId, BookNumber, ChapterNumber, DocumentId, Track, IssueTagNumber, KeySymbol, MepsLanguage, Type, Title, Specialty, Edition)
            VALUES ($LocationId, $BookNumber, $ChapterNumber, $DocumentId, $Track, $IssueTagNumber, $KeySymbol, $MepsLanguage, $Type, $Title, $Specialty, $Edition)
            """;
        AddLocationParameters(command, row);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddLocationParameters(SqliteCommand command, DocumentLocation row)
    {
        command.Parameters.AddWithValue("$LocationId", row.LocationId);
        command.Parameters.AddWithValue("$BookNumber", (object?)row.BookNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$ChapterNumber", (object?)row.ChapterNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$DocumentId", (object?)row.DocumentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$Track", (object?)row.Track ?? DBNull.Value);
        command.Parameters.AddWithValue("$IssueTagNumber", row.IssueTagNumber);
        command.Parameters.AddWithValue("$KeySymbol", (object?)row.KeySymbol ?? DBNull.Value);
        command.Parameters.AddWithValue("$MepsLanguage", (object?)row.MepsLanguage ?? DBNull.Value);
        command.Parameters.AddWithValue("$Type", row.Type);
        command.Parameters.AddWithValue("$Title", (object?)row.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$Specialty", (object?)row.Specialty ?? DBNull.Value);
        command.Parameters.AddWithValue("$Edition", (object?)row.Edition ?? DBNull.Value);
    }

    private static async Task InsertUserMarkAsync(SqliteConnection connection, SqliteTransaction transaction, UserMark row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO UserMark (UserMarkId, ColorIndex, LocationId, StyleIndex, UserMarkGuid, Version)
            VALUES ($UserMarkId, $ColorIndex, $LocationId, $StyleIndex, $UserMarkGuid, $Version)
            """;
        command.Parameters.AddWithValue("$UserMarkId", row.UserMarkId);
        command.Parameters.AddWithValue("$ColorIndex", row.ColorIndex);
        command.Parameters.AddWithValue("$LocationId", row.LocationId);
        command.Parameters.AddWithValue("$StyleIndex", row.StyleIndex);
        command.Parameters.AddWithValue("$UserMarkGuid", row.UserMarkGuid.ToString());
        command.Parameters.AddWithValue("$Version", row.Version);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateUserMarkAsync(SqliteConnection connection, SqliteTransaction transaction, long userMarkId, UserMark row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE UserMark
            SET ColorIndex = $ColorIndex,
                LocationId = $LocationId,
                StyleIndex = $StyleIndex,
                Version = $Version
            WHERE UserMarkId = $UserMarkId
            """;
        command.Parameters.AddWithValue("$UserMarkId", userMarkId);
        command.Parameters.AddWithValue("$ColorIndex", row.ColorIndex);
        command.Parameters.AddWithValue("$LocationId", row.LocationId);
        command.Parameters.AddWithValue("$StyleIndex", row.StyleIndex);
        command.Parameters.AddWithValue("$Version", row.Version);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertNoteAsync(SqliteConnection connection, SqliteTransaction transaction, Note row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Note (NoteId, Guid, UserMarkId, LocationId, Title, Content, LastModified, Created, BlockType, BlockIdentifier)
            VALUES ($NoteId, $Guid, $UserMarkId, $LocationId, $Title, $Content, $LastModified, $Created, $BlockType, $BlockIdentifier)
            """;
        command.Parameters.AddWithValue("$NoteId", row.NoteId);
        AddNoteParameters(command, row);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateNoteAsync(SqliteConnection connection, SqliteTransaction transaction, long noteId, Note row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE Note
            SET UserMarkId = $UserMarkId,
                LocationId = $LocationId,
                Title = $Title,
                Content = $Content,
                LastModified = $LastModified,
                Created = $Created,
                BlockType = $BlockType,
                BlockIdentifier = $BlockIdentifier
            WHERE NoteId = $NoteId
            """;
        command.Parameters.AddWithValue("$NoteId", noteId);
        AddNoteParameters(command, row);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddNoteParameters(SqliteCommand command, Note row)
    {
        command.Parameters.AddWithValue("$Guid", row.Guid.ToString());
        command.Parameters.AddWithValue("$UserMarkId", (object?)row.UserMarkId ?? DBNull.Value);
        command.Parameters.AddWithValue("$LocationId", (object?)row.LocationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$Title", (object?)row.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$Content", (object?)row.Content ?? DBNull.Value);
        command.Parameters.AddWithValue("$LastModified", row.LastModified.ToString("O"));
        command.Parameters.AddWithValue("$Created", row.Created.ToString("O"));
        command.Parameters.AddWithValue("$BlockType", row.BlockType);
        command.Parameters.AddWithValue("$BlockIdentifier", (object?)row.BlockIdentifier ?? DBNull.Value);
    }

    private static async Task InsertBlockRangeAsync(SqliteConnection connection, SqliteTransaction transaction, BlockRange row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO BlockRange (BlockRangeId, BlockType, Identifier, StartToken, EndToken, UserMarkId)
            VALUES ($BlockRangeId, $BlockType, $Identifier, $StartToken, $EndToken, $UserMarkId)
            """;
        command.Parameters.AddWithValue("$BlockRangeId", row.BlockRangeId);
        command.Parameters.AddWithValue("$BlockType", row.BlockType);
        command.Parameters.AddWithValue("$Identifier", row.Identifier);
        command.Parameters.AddWithValue("$StartToken", (object?)row.StartToken ?? DBNull.Value);
        command.Parameters.AddWithValue("$EndToken", (object?)row.EndToken ?? DBNull.Value);
        command.Parameters.AddWithValue("$UserMarkId", row.UserMarkId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertTagAsync(SqliteConnection connection, SqliteTransaction transaction, Tag row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Tag (TagId, Type, Name) VALUES ($TagId, $Type, $Name)";
        command.Parameters.AddWithValue("$TagId", row.TagId);
        command.Parameters.AddWithValue("$Type", row.Type);
        command.Parameters.AddWithValue("$Name", row.Name);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertBookmarkAsync(SqliteConnection connection, SqliteTransaction transaction, Bookmark row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Bookmark (BookmarkId, LocationId, PublicationLocationId, Slot, Title, Snippet, BlockType, BlockIdentifier)
            VALUES ($BookmarkId, $LocationId, $PublicationLocationId, $Slot, $Title, $Snippet, $BlockType, $BlockIdentifier)
            """;
        command.Parameters.AddWithValue("$BookmarkId", row.BookmarkId);
        command.Parameters.AddWithValue("$LocationId", row.LocationId);
        command.Parameters.AddWithValue("$PublicationLocationId", row.PublicationLocationId);
        command.Parameters.AddWithValue("$Slot", row.Slot);
        command.Parameters.AddWithValue("$Title", row.Title);
        command.Parameters.AddWithValue("$Snippet", (object?)row.Snippet ?? DBNull.Value);
        command.Parameters.AddWithValue("$BlockType", row.BlockType);
        command.Parameters.AddWithValue("$BlockIdentifier", (object?)row.BlockIdentifier ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertMediaAsync(SqliteConnection connection, SqliteTransaction transaction, MediaAsset row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO IndependentMedia (IndependentMediaId, OriginalFilename, FilePath, MimeType, Hash)
            VALUES ($IndependentMediaId, $OriginalFilename, $FilePath, $MimeType, $Hash)
            """;
        command.Parameters.AddWithValue("$IndependentMediaId", row.IndependentMediaId);
        command.Parameters.AddWithValue("$OriginalFilename", row.OriginalFilename);
        command.Parameters.AddWithValue("$FilePath", row.FilePath);
        command.Parameters.AddWithValue("$MimeType", row.MimeType);
        command.Parameters.AddWithValue("$Hash", row.Sha256);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertInputFieldAsync(SqliteConnection connection, SqliteTransaction transaction, InputField row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO InputField (LocationId, TextTag, Value) VALUES ($LocationId, $TextTag, $Value)";
        command.Parameters.AddWithValue("$LocationId", row.LocationId);
        command.Parameters.AddWithValue("$TextTag", row.TextTag);
        command.Parameters.AddWithValue("$Value", row.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TagMapExistsAsync(SqliteConnection connection, CancellationToken cancellationToken, TagMapKey key)
    {
        if (key.NoteId is null && key.LocationId is null && key.PlaylistItemId is null)
        {
            return false;
        }

        await using var command = connection.CreateCommand();
        if (key.NoteId is not null)
        {
            command.CommandText =
                """
                SELECT 1
                FROM TagMap
                WHERE NoteId = $NoteId
                  AND TagId = $TagId
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$NoteId", key.NoteId.Value);
        }
        else if (key.LocationId is not null)
        {
            command.CommandText =
                """
                SELECT 1
                FROM TagMap
                WHERE LocationId = $LocationId
                  AND TagId = $TagId
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$LocationId", key.LocationId.Value);
        }
        else
        {
            command.CommandText =
                """
                SELECT 1
                FROM TagMap
                WHERE PlaylistItemId = $PlaylistItemId
                  AND TagId = $TagId
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$PlaylistItemId", key.PlaylistItemId!.Value);
        }

        command.Parameters.AddWithValue("$TagId", key.TagId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static async Task InsertTagMapAsync(SqliteConnection connection, SqliteTransaction transaction, TagMap row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO TagMap (TagMapId, PlaylistItemId, LocationId, NoteId, TagId, Position)
            VALUES ($TagMapId, $PlaylistItemId, $LocationId, $NoteId, $TagId, $Position)
            """;
        command.Parameters.AddWithValue("$TagMapId", row.TagMapId);
        command.Parameters.AddWithValue("$PlaylistItemId", (object?)row.PlaylistItemId ?? DBNull.Value);
        command.Parameters.AddWithValue("$LocationId", (object?)row.LocationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$NoteId", (object?)row.NoteId ?? DBNull.Value);
        command.Parameters.AddWithValue("$TagId", row.TagId);
        command.Parameters.AddWithValue("$Position", row.Position);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        }
    }

    private readonly record struct LocationKey(
        string KeySymbol,
        int IssueTagNumber,
        int? MepsLanguage,
        long? DocumentId,
        int? Track,
        int Type,
        string Specialty,
        string Edition,
        int? BookNumber,
        int? ChapterNumber)
    {
        public static LocationKey From(DocumentLocation row) => new(
            row.KeySymbol ?? string.Empty,
            row.IssueTagNumber,
            row.MepsLanguage,
            row.DocumentId,
            row.Track,
            row.Type,
            row.Specialty ?? string.Empty,
            row.Edition ?? string.Empty,
            row.BookNumber,
            row.ChapterNumber);
    }

    private readonly record struct TagKey(int Type, string Name)
    {
        public static TagKey From(Tag row) => new(row.Type, row.Name);
    }

    private readonly record struct BookmarkKey(long PublicationLocationId, int Slot)
    {
        public static BookmarkKey From(Bookmark row, long locationId, long publicationLocationId)
            => new(publicationLocationId, row.Slot);
    }

    private readonly record struct InputFieldKey(long LocationId, string TextTag);

    private readonly record struct BlockRangeKey(int BlockType, long Identifier, long? StartToken, long? EndToken, long UserMarkId);

    private readonly record struct TagMapKey(long? NoteId, long? LocationId, long? PlaylistItemId, long TagId, int Position);
}

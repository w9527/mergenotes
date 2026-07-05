using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MergerNotes.Core.Models;

namespace MergerNotes.Infrastructure.Import;

internal static class JwlibrarySnapshotReader
{
    public static async Task<BackupPackage> ReadManifestAsync(string rootDir, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(rootDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("manifest.json was not found in the backup.", manifestPath);
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var backup = root.GetProperty("userDataBackup");

        return new BackupPackage(
            root.GetProperty("name").GetString() ?? string.Empty,
            ParseDateTimeOffset(root.GetProperty("creationDate").GetString()),
            backup.GetProperty("databaseName").GetString() ?? "userData.db",
            backup.GetProperty("hash").GetString() ?? string.Empty,
            backup.GetProperty("schemaVersion").GetInt32());
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<BackupSnapshot> ReadSnapshotAsync(
        BackupPackage package,
        string dbPath,
        string rootDir,
        CancellationToken cancellationToken)
    {
        var locations = new List<DocumentLocation>();
        var userMarks = new List<UserMark>();
        var notes = new List<Note>();
        var blockRanges = new List<BlockRange>();
        var tags = new List<Tag>();
        var tagMaps = new List<TagMap>();
        var bookmarks = new List<Bookmark>();
        var mediaAssets = new List<MediaAsset>();
        var inputFields = new List<InputField>();

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        locations.AddRange(await ReadLocationsAsync(connection, cancellationToken).ConfigureAwait(false));
        userMarks.AddRange(await ReadUserMarksAsync(connection, cancellationToken).ConfigureAwait(false));
        notes.AddRange(await ReadNotesAsync(connection, cancellationToken).ConfigureAwait(false));
        blockRanges.AddRange(await ReadBlockRangesAsync(connection, cancellationToken).ConfigureAwait(false));
        tags.AddRange(await ReadTagsAsync(connection, cancellationToken).ConfigureAwait(false));
        tagMaps.AddRange(await ReadTagMapsAsync(connection, cancellationToken).ConfigureAwait(false));
        bookmarks.AddRange(await ReadBookmarksAsync(connection, cancellationToken).ConfigureAwait(false));
        mediaAssets.AddRange(await ReadMediaAssetsAsync(connection, rootDir, cancellationToken).ConfigureAwait(false));
        inputFields.AddRange(await ReadInputFieldsAsync(connection, cancellationToken).ConfigureAwait(false));

        return new BackupSnapshot(
            package,
            locations,
            userMarks,
            notes,
            blockRanges,
            tags,
            tagMaps,
            bookmarks,
            mediaAssets,
            inputFields);
    }

    public static async Task<string> ExtractAsync(string backupPath, string extractDir, CancellationToken cancellationToken)
    {
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        Directory.CreateDirectory(extractDir);
        await Task.Run(() => ZipFile.ExtractToDirectory(backupPath, extractDir), cancellationToken).ConfigureAwait(false);
        return extractDir;
    }

    private static async Task<IReadOnlyList<DocumentLocation>> ReadLocationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var list = new List<DocumentLocation>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT LocationId, BookNumber, ChapterNumber, DocumentId, Track, IssueTagNumber, KeySymbol, MepsLanguage, Type, Title, Specialty, Edition
            FROM Location
            ORDER BY LocationId
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new DocumentLocation(
                reader.GetInt64(0),
                GetNullableInt32(reader, 1),
                GetNullableInt32(reader, 2),
                GetNullableInt64(reader, 3),
                GetNullableInt32(reader, 4),
                reader.GetInt32(5),
                GetNullableString(reader, 6),
                GetNullableInt32(reader, 7),
                reader.GetInt32(8),
                GetNullableString(reader, 9),
                GetNullableString(reader, 10),
                GetNullableString(reader, 11)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<UserMark>> ReadUserMarksAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var list = new List<UserMark>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT UserMarkId, ColorIndex, LocationId, StyleIndex, UserMarkGuid, Version
            FROM UserMark
            ORDER BY UserMarkId
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new UserMark(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt64(2),
                reader.GetInt32(3),
                Guid.Parse(reader.GetString(4)),
                reader.GetInt32(5)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<Note>> ReadNotesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var list = new List<Note>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT NoteId, Guid, UserMarkId, LocationId, Title, Content, LastModified, Created, BlockType, BlockIdentifier
            FROM Note
            ORDER BY NoteId
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new Note(
                reader.GetInt64(0),
                Guid.Parse(reader.GetString(1)),
                GetNullableInt64(reader, 2),
                GetNullableInt64(reader, 3),
                GetNullableString(reader, 4),
                GetNullableString(reader, 5),
                ParseDateTimeOffset(reader.GetString(6)),
                ParseDateTimeOffset(reader.GetString(7)),
                reader.GetInt32(8),
                GetNullableInt64(reader, 9)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<BlockRange>> ReadBlockRangesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var list = new List<BlockRange>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT BlockRangeId, BlockType, Identifier, StartToken, EndToken, UserMarkId
            FROM BlockRange
            ORDER BY BlockRangeId
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new BlockRange(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt64(2),
                GetNullableInt64(reader, 3),
                GetNullableInt64(reader, 4),
                reader.GetInt64(5)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<Tag>> ReadTagsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var list = new List<Tag>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TagId, Type, Name
            FROM Tag
            ORDER BY TagId
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new Tag(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<TagMap>> ReadTagMapsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var list = new List<TagMap>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TagMapId, PlaylistItemId, LocationId, NoteId, TagId, Position
            FROM TagMap
            ORDER BY TagMapId
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new TagMap(
                reader.GetInt64(0),
                GetNullableInt64(reader, 1),
                GetNullableInt64(reader, 2),
                GetNullableInt64(reader, 3),
                reader.GetInt64(4),
                reader.GetInt32(5)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<Bookmark>> ReadBookmarksAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var list = new List<Bookmark>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT BookmarkId, LocationId, PublicationLocationId, Slot, Title, Snippet, BlockType, BlockIdentifier
            FROM Bookmark
            ORDER BY BookmarkId
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new Bookmark(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt32(3),
                reader.GetString(4),
                GetNullableString(reader, 5),
                reader.GetInt32(6),
                GetNullableInt64(reader, 7)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<MediaAsset>> ReadMediaAssetsAsync(SqliteConnection connection, string rootDir, CancellationToken cancellationToken)
    {
        var list = new List<MediaAsset>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT IndependentMediaId, OriginalFilename, FilePath, MimeType, Hash
            FROM IndependentMedia
            ORDER BY IndependentMediaId
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var filePath = reader.GetString(2);
            var absolute = Path.Combine(rootDir, filePath);
            if (File.Exists(absolute))
            {
                var hash = await ComputeSha256Async(absolute, cancellationToken).ConfigureAwait(false);
                list.Add(new MediaAsset(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    filePath,
                    reader.GetString(3),
                    hash));
            }
            else
            {
                list.Add(new MediaAsset(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    filePath,
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        return list;
    }

    private static async Task<IReadOnlyList<InputField>> ReadInputFieldsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var list = new List<InputField>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT LocationId, TextTag, Value
            FROM InputField
            ORDER BY LocationId, TextTag
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new InputField(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return list;
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value)
        => DateTimeOffset.Parse(value ?? "1970-01-01T00:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static long? GetNullableInt64(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}

namespace MergerNotes.Core.Models;

public sealed record BackupSnapshot(
    BackupPackage Package,
    IReadOnlyList<DocumentLocation> Locations,
    IReadOnlyList<UserMark> UserMarks,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<BlockRange> BlockRanges,
    IReadOnlyList<Tag> Tags,
    IReadOnlyList<TagMap> TagMaps,
    IReadOnlyList<Bookmark> Bookmarks,
    IReadOnlyList<MediaAsset> MediaAssets,
    IReadOnlyList<InputField> InputFields);

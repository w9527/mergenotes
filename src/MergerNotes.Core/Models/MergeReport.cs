namespace MergerNotes.Core.Models;

public sealed record MergeReport(
    int BaseNotes,
    int IncomingNotes,
    int MergedNotes,
    int AddedLocations,
    int AddedUserMarks,
    int AddedNotes,
    int UpdatedNotes,
    int AddedBlockRanges,
    int AddedTags,
    int AddedTagMaps,
    int AddedBookmarks,
    int AddedMediaAssets,
    int AddedInputFields,
    int SkippedPlaylistTagMaps);

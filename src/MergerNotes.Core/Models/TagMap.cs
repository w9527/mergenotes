namespace MergerNotes.Core.Models;

public sealed record TagMap(
    long TagMapId,
    long? PlaylistItemId,
    long? LocationId,
    long? NoteId,
    long TagId,
    int Position);

namespace MergerNotes.Core.Models;

public sealed record Bookmark(
    long BookmarkId,
    long LocationId,
    long PublicationLocationId,
    int Slot,
    string Title,
    string? Snippet,
    int BlockType,
    long? BlockIdentifier);

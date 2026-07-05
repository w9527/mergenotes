namespace MergerNotes.Core.Models;

public sealed record Note(
    long NoteId,
    Guid Guid,
    long? UserMarkId,
    long? LocationId,
    string? Title,
    string? Content,
    DateTimeOffset LastModified,
    DateTimeOffset Created,
    int BlockType,
    long? BlockIdentifier);

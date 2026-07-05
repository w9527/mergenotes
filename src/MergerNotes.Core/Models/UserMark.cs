namespace MergerNotes.Core.Models;

public sealed record UserMark(
    long UserMarkId,
    int ColorIndex,
    long LocationId,
    int StyleIndex,
    Guid UserMarkGuid,
    int Version);

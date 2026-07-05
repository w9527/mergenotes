namespace MergerNotes.Core.Models;

public sealed record BlockRange(
    long BlockRangeId,
    int BlockType,
    long Identifier,
    long? StartToken,
    long? EndToken,
    long UserMarkId);

namespace MergerNotes.Core.Models;

public sealed record MediaAsset(
    long IndependentMediaId,
    string OriginalFilename,
    string FilePath,
    string MimeType,
    string Sha256);

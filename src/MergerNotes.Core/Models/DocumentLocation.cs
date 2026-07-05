namespace MergerNotes.Core.Models;

public sealed record DocumentLocation(
    long LocationId,
    int? BookNumber,
    int? ChapterNumber,
    long? DocumentId,
    int? Track,
    int IssueTagNumber,
    string? KeySymbol,
    int? MepsLanguage,
    int Type,
    string? Title,
    string? Specialty,
    string? Edition);

namespace MergerNotes.Core.Models;

public sealed record InputField(
    long LocationId,
    string TextTag,
    string Value);

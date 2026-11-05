namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>One reader's outcome for one file: its layout, or the reason there is none.</summary>
public sealed record ReaderResult(string Reader, object? Layout, string? Failure);

using System.Collections.Generic;
using System.IO;

namespace MSFSBlindAssist.Utils.Logging;

public sealed record RotationPlan(string DeleteFirst, IReadOnlyList<(string From, string To)> Moves);

/// <summary>Pure rotation policy. No file I/O — the LogWriter applies the plan, skipping any From that doesn't exist.</summary>
public static class LogRotator
{
    public static bool ShouldRotate(long sizeBytes, long capBytes) => sizeBytes >= capBytes;

    public static string RotatedName(string basePath, int index)
    {
        string dir  = Path.GetDirectoryName(basePath) ?? "";
        string name = Path.GetFileNameWithoutExtension(basePath);
        string ext  = Path.GetExtension(basePath); // includes leading dot
        return Path.Combine(dir, $"{name}.{index}{ext}");
    }

    public static RotationPlan Plan(string basePath, int retention)
    {
        var moves = new List<(string, string)>();
        for (int i = retention - 1; i >= 1; i--)
            moves.Add((RotatedName(basePath, i), RotatedName(basePath, i + 1)));
        moves.Add((basePath, RotatedName(basePath, 1)));
        return new RotationPlan(RotatedName(basePath, retention), moves);
    }
}

using System.Globalization;

namespace MSFSBlindAssist.Services.Volanta;

/// <summary>
/// What the web map (<c>VolantaWebMap</c>) downloaded in the last minute, by kind, as the bytes that
/// came over the network (compressed), so the pilot can hear what running Volanta's map a second time costs.
/// Thread-safe; pure apart from its own list.
/// </summary>
public sealed class VolantaWebMapStats
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly object _gate = new();
    private readonly List<(DateTimeOffset At, string Kind, long Bytes)> _recent = new();

    public const string VolantaUsers = "Volanta users";
    public const string NetworkPilots = "network pilots";
    public const string Atc = "ATC";
    public const string Friends = "friends list";
    public const string Other = "everything else";

    /// <summary>The kind a downloaded file counts under, from what the link makes of its address.</summary>
    internal static string KindOf(VolantaLink.FeedKind? feed) => feed switch
    {
        VolantaLink.FeedKind.VolantaFlights => VolantaUsers,
        VolantaLink.FeedKind.NetworkFlights => NetworkPilots,
        VolantaLink.FeedKind.Controllers => Atc,
        VolantaLink.FeedKind.Friends => Friends,
        _ => Other,
    };

    public void Add(DateTimeOffset at, string kind, long bytes)
    {
        lock (_gate)
        {
            _recent.Add((at, kind, Math.Max(0, bytes)));
            _recent.RemoveAll(r => at - r.At > Window);
        }
    }

    /// <summary>"In the last minute: 10 Volanta users files, 6.6 MB; …; in all 7.6 MB, about 456 MB an hour." Files
    /// served from the browser's own cache count as files with no bytes.</summary>
    public string Summary(DateTimeOffset now)
    {
        List<(DateTimeOffset At, string Kind, long Bytes)> recent;
        lock (_gate) recent = _recent.Where(r => now - r.At <= Window).ToList();
        if (recent.Count == 0) return "Nothing downloaded in the last minute.";
        var parts = new List<string>();
        foreach (string kind in new[] { VolantaUsers, NetworkPilots, Atc, Friends })
        {
            var files = recent.Where(r => r.Kind == kind).ToList();
            if (files.Count == 0) continue;
            parts.Add($"{files.Count.ToString(Inv)} {kind} {(files.Count == 1 ? "file" : "files")}, {Megabytes(files.Sum(f => f.Bytes))}");
        }
        long other = recent.Where(r => r.Kind == Other).Sum(r => r.Bytes);
        if (other > 0) parts.Add($"{Other} {Megabytes(other)}");
        // Only other files with no bytes (served from the browser's cache): nothing to name.
        if (parts.Count == 0) return "Nothing downloaded in the last minute.";
        long total = recent.Sum(r => r.Bytes);
        double perHour = total * (TimeSpan.FromHours(1) / Window) / 1048576.0;
        return $"In the last minute: {string.Join("; ", parts)}. In all {Megabytes(total)}, about "
            + $"{Math.Round(perHour).ToString("N0", Inv)} MB an hour.";
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0", Inv) + " MB";
}

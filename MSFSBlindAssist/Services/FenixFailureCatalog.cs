// The Fenix v3 manual-failures catalog (GET /fenix/failures/manual): chapters of groups of failures,
// each armed (has a failureCondition), failed, or normal. Pure parsing and search, no I/O.

using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

public enum FenixFailureState { Normal, Armed, Failed }

public enum FenixFailureFilter { All, Armed, Failed, ArmedOrFailed }

public enum FenixFailureAction { FailNow, Arm, Clear }

public sealed record FenixFailureCondition(int? Ias, int? AltAbove, int? AltBelow, string? AfterEvent, int? AfterEventSeconds, string? Time)
{
    public bool IsEmpty => Ias == null && AltAbove == null && AltBelow == null
        && AfterEvent == null && AfterEventSeconds == null && Time == null;
}

public sealed record FenixFailure(string Id, string Title, bool Failed, FenixFailureCondition? Condition, JObject Raw)
{
    // Failed wins over armed, as in the EFB.
    public FenixFailureState State => Failed ? FenixFailureState.Failed
        : Condition != null ? FenixFailureState.Armed : FenixFailureState.Normal;
}

public sealed record FenixFailureGroup(string Name, IReadOnlyList<FenixFailure> Failures)
{
    public bool ShowsHeader => Failures.Count > 1;
}

public sealed record FenixFailureChapter(int Ata, string Title, string ShortTitle, IReadOnlyList<FenixFailureGroup> Groups)
{
    public IEnumerable<FenixFailure> AllFailures => Groups.SelectMany(g => g.Failures);
}

public sealed class FenixFailureCatalog
{
    public IReadOnlyList<FenixFailureChapter> Chapters { get; }

    public int FailureCount { get; }

    private FenixFailureCatalog(IReadOnlyList<FenixFailureChapter> chapters)
    {
        Chapters = chapters;
        FailureCount = chapters.Sum(c => c.AllFailures.Count());
    }

    /// <summary>Parses the gateway's reply; null when it is not the catalog (not JSON, no "atas" array).</summary>
    public static FenixFailureCatalog? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JToken root;
        try
        {
            // DateParseHandling.None: an ISO "time" string must stay the text the gateway sent.
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            root = JToken.ReadFrom(reader);
        }
        catch (JsonException) { return null; }

        if (root is not JObject obj || obj["atas"] is not JArray atas) return null;

        var chapters = new List<FenixFailureChapter>();
        foreach (JToken ataToken in atas)
        {
            if (ataToken is not JObject ata) continue;
            int? id = ToInt(ata["id"]);
            if (id == null) continue;

            var groups = new List<FenixFailureGroup>();
            if (ata["groups"] is JArray groupArray)
            {
                foreach (JToken groupToken in groupArray)
                {
                    if (groupToken is not JObject group) continue;
                    var failures = new List<FenixFailure>();
                    if (group["failures"] is JArray failureArray)
                    {
                        foreach (JToken f in failureArray)
                        {
                            FenixFailure? failure = ParseFailure(f);
                            if (failure != null) failures.Add(failure);
                        }
                    }
                    if (failures.Count > 0)
                        groups.Add(new FenixFailureGroup(Text(group["groupName"]), failures));
                }
            }
            chapters.Add(new FenixFailureChapter(id.Value, Text(ata["title"]), Text(ata["shortTitle"]), groups));
        }
        return new FenixFailureCatalog(chapters);
    }

    /// <summary>Reads one failure object (also the server's reply to a save); null without an id.</summary>
    public static FenixFailure? ParseFailure(JToken? token)
    {
        if (token is not JObject obj) return null;
        string id = Text(obj["id"]);
        if (id.Length == 0) return null;

        bool failed = obj["failed"] is { Type: JTokenType.Boolean } b && b.Value<bool>();

        FenixFailureCondition? condition = null;
        JToken? c = obj["failureCondition"];
        if (c != null && c.Type != JTokenType.Null && c.Type != JTokenType.Undefined)
        {
            // Any non-null value arms the failure (the EFB tests truthiness); only an object carries fields.
            condition = c is JObject co
                ? new FenixFailureCondition(
                    ToInt(co["ias"]), ToInt(co["alt"]), ToInt(co["altb"]),
                    TextOrNull(co["afterEvent"]), ToInt(co["afterEventSeconds"]), TimeText(co["time"]))
                : new FenixFailureCondition(null, null, null, null, null, null);
        }

        return new FenixFailure(id, Text(obj["title"]), failed, condition, obj);
    }

    public FenixFailure? Find(string id)
        => Chapters.SelectMany(c => c.AllFailures).FirstOrDefault(f => f.Id == id);

    /// <summary>A copy with the failure of the same id replaced; order is kept.</summary>
    public FenixFailureCatalog With(FenixFailure replacement)
    {
        var chapters = Chapters.Select(c => c with
        {
            Groups = c.Groups.Select(g => g with
            {
                Failures = g.Failures.Select(f => f.Id == replacement.Id ? replacement : f).ToList()
            }).ToList()
        }).ToList();
        return new FenixFailureCatalog(chapters);
    }

    public (int Armed, int Failed) Counts()
    {
        int armed = 0, failed = 0;
        foreach (FenixFailure f in Chapters.SelectMany(c => c.AllFailures))
        {
            if (f.State == FenixFailureState.Armed) armed++;
            else if (f.State == FenixFailureState.Failed) failed++;
        }
        return (armed, failed);
    }

    /// <summary>The EFB's search: a case-insensitive substring of "ATA n - title", the group name or the failure title.</summary>
    public IReadOnlyList<FenixFailureChapter> Filter(string? search, FenixFailureFilter show)
    {
        bool hasSearch = !string.IsNullOrWhiteSpace(search);
        var result = new List<FenixFailureChapter>();
        foreach (FenixFailureChapter chapter in Chapters)
        {
            string heading = $"ATA {chapter.Ata} - {chapter.Title}";
            bool headingHit = !hasSearch || heading.Contains(search!, StringComparison.OrdinalIgnoreCase);
            var groups = new List<FenixFailureGroup>();
            foreach (FenixFailureGroup group in chapter.Groups)
            {
                bool groupHit = headingHit || group.Name.Contains(search!, StringComparison.OrdinalIgnoreCase);
                var kept = group.Failures
                    .Where(f => (groupHit || f.Title.Contains(search!, StringComparison.OrdinalIgnoreCase)) && Passes(f.State, show))
                    .ToList();
                if (kept.Count > 0) groups.Add(group with { Failures = kept });
            }
            if (groups.Count > 0) result.Add(chapter with { Groups = groups });
        }
        return result;
    }

    private static bool Passes(FenixFailureState state, FenixFailureFilter show) => show switch
    {
        FenixFailureFilter.Armed => state == FenixFailureState.Armed,
        FenixFailureFilter.Failed => state == FenixFailureState.Failed,
        FenixFailureFilter.ArmedOrFailed => state != FenixFailureState.Normal,
        _ => true,
    };

    private static string Text(JToken? t) => t?.Type switch
    {
        JTokenType.String => t.Value<string>() ?? "",
        JTokenType.Integer => t.Value<long>().ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    private static string? TextOrNull(JToken? t) => t is { Type: JTokenType.String } ? t.Value<string>() : null;

    // JSON integer -> int, integral float -> int, anything else -> null.
    private static int? ToInt(JToken? t)
    {
        if (t == null) return null;
        if (t.Type == JTokenType.Integer)
        {
            long v = t.Value<long>();
            return v is >= int.MinValue and <= int.MaxValue ? (int)v : null;
        }
        if (t.Type == JTokenType.Float)
        {
            double d = t.Value<double>();
            return d == Math.Floor(d) && Math.Abs(d) <= int.MaxValue ? (int)d : null;
        }
        return null;
    }

    private static string? TimeText(JToken? t)
    {
        if (t == null) return null;
        if (t.Type == JTokenType.String) return t.Value<string>();
        if (t.Type == JTokenType.Integer) return t.Value<long>().ToString(CultureInfo.InvariantCulture);
        if (t.Type == JTokenType.Float) return t.Value<double>().ToString("R", CultureInfo.InvariantCulture);
        return null;
    }
}

// What the Fenix v3 Failures window says: node text, status lines, confirmations and the
// background-change announcement. Pure formatting; numbers and times are always invariant.

using System.Globalization;

namespace MSFSBlindAssist.Services;

public static class FenixFailureText
{
    public const string Unavailable = "Fenix failures are not available. Load the Fenix, then press F5.";
    public const string SelectFailureFirst = "Select a failure first.";

    private const string ArmedAtPrefix = "armed at ";

    public static string ChapterNode(FenixFailureChapter chapter)
        => $"{chapter.Title}, ATA {Num(chapter.Ata)}{CountSuffix(chapter.AllFailures)}";

    public static string GroupNode(FenixFailureGroup group)
        => group.Name + CountSuffix(group.Failures);

    public static string FailureNode(FenixFailure failure, TimeZoneInfo zone) => failure.State switch
    {
        FenixFailureState.Failed => failure.Title + ", failed",
        FenixFailureState.Armed => failure.Title + ", " + ArmedPhrase(failure.Condition, zone),
        _ => failure.Title,
    };

    public static string Condition(FenixFailureCondition condition, TimeZoneInfo zone)
    {
        var parts = new List<string>();
        if (condition.Ias != null) parts.Add("above IAS " + Num(condition.Ias.Value));
        if (condition.AltAbove != null) parts.Add("above altitude " + Num(condition.AltAbove.Value) + " AMSL");
        if (condition.AltBelow != null) parts.Add("below altitude " + Num(condition.AltBelow.Value) + " AMSL");

        if (condition.AfterEvent != null)
        {
            string e = EventName(condition.AfterEvent);
            parts.Add(condition.AfterEventSeconds is { } s
                ? $"{Seconds(s)} after {e}"
                : "at " + e);
        }
        else if (condition.AfterEventSeconds is { } secs && condition.Time == null)
            parts.Add("after " + Seconds(secs));
        else if (condition.Time != null)
            parts.Add(TimePart(condition.Time, zone));

        return string.Join(", ", parts);
    }

    public static string Summary(FenixFailureCatalog catalog)
    {
        int chapters = catalog.Chapters.Count(c => c.AllFailures.Any());
        var (armed, failed) = catalog.Counts();
        return $"{Plural(catalog.FailureCount, "failure", "failures")} in {Plural(chapters, "chapter", "chapters")}. {armed} armed, {failed} failed.";
    }

    public static string Refreshed(FenixFailureCatalog catalog)
    {
        var (armed, failed) = catalog.Counts();
        return $"Failures refreshed. {armed} armed, {failed} failed.";
    }

    public static string Matches(int count, string search) => count switch
    {
        0 => $"No failures match {search}.",
        1 => $"1 failure matches {search}.",
        _ => $"{Num(count)} failures match {search}.",
    };

    public static string Shown(int count) => count switch
    {
        0 => "No failures shown.",
        1 => "1 failure shown.",
        _ => $"{Num(count)} failures shown.",
    };

    public static string AutomaticFailures(IReadOnlyList<FenixSetting> settings)
    {
        var parts = new List<string>();
        string? mode = settings.FirstOrDefault(s => s.Key == "auto_failure_mode")?.Value;
        string? rate = settings.FirstOrDefault(s => s.Key == "failure_rate")?.Value;
        if (!string.IsNullOrEmpty(mode)) parts.Add($"Automatic failure severity: {mode}.");
        if (!string.IsNullOrEmpty(rate)) parts.Add($"Automatic failure rate: {rate}.");
        return string.Join(" ", parts);
    }

    public static string SavedLine(FenixFailure failure, TimeZoneInfo zone) => failure.State switch
    {
        FenixFailureState.Failed => failure.Title + " failed.",
        FenixFailureState.Armed => $"{failure.Title} {ArmedPhrase(failure.Condition, zone)}.",
        _ => failure.Title + " cleared.",
    };

    public static string Confirm(FenixFailure failure, FenixFailureAction action, FenixFailureCondition? condition, TimeZoneInfo zone)
    {
        const string Tail = " This changes your flight.";
        switch (action)
        {
            case FenixFailureAction.FailNow:
                return $"Fail {failure.Title} now?{Tail}";
            case FenixFailureAction.Arm:
                string text = condition == null ? "" : Condition(condition, zone);
                return text.Length == 0 ? $"Arm {failure.Title}?{Tail}" : $"Arm {failure.Title}: {text}?{Tail}";
            default:
                return $"Clear {failure.Title}?{Tail}";
        }
    }

    public static string NotConfirmed(string title) => $"The Fenix did not confirm the change to {title}.";

    /// <summary>
    /// What changed between two reads, for ids in both reads only (a different variant may have loaded);
    /// at most three named. Null when nothing changed or the only change is exceptId (this window's own).
    /// </summary>
    public static string? Changes(FenixFailureCatalog before, FenixFailureCatalog after, string? exceptId, TimeZoneInfo zone)
    {
        var earlier = new Dictionary<string, FenixFailure>();
        foreach (FenixFailure f in before.Chapters.SelectMany(c => c.AllFailures))
            earlier.TryAdd(f.Id, f);

        var lines = new List<string>();
        foreach (FenixFailure now in after.Chapters.SelectMany(c => c.AllFailures))
        {
            if (now.Id == exceptId || !earlier.TryGetValue(now.Id, out FenixFailure? was)) continue;
            if (was.State == now.State
                && (now.State != FenixFailureState.Armed || ConditionKey(was, zone) == ConditionKey(now, zone))) continue;

            lines.Add(was.State == FenixFailureState.Armed && now.State == FenixFailureState.Failed
                ? $"Failure triggered: {now.Title}."
                : SavedLine(now, zone));
        }

        if (lines.Count == 0) return null;
        if (lines.Count <= 3) return string.Join(" ", lines);
        int extra = lines.Count - 3;
        return string.Join(" ", lines.Take(3)) + (extra == 1 ? " And 1 more change." : $" And {Num(extra)} more changes.");
    }

    private static string? ConditionKey(FenixFailure f, TimeZoneInfo zone)
        => f.Condition == null ? null : Condition(f.Condition with { Time = null }, zone); // a ticking countdown is not a change

    private static string ArmedPhrase(FenixFailureCondition? condition, TimeZoneInfo zone)
    {
        string text = condition == null ? "" : Condition(condition, zone);
        if (text.Length == 0) return "armed";
        return text.StartsWith(ArmedAtPrefix, StringComparison.Ordinal) && !text.Contains(',')
            ? text
            : "armed: " + text;
    }

    private static string CountSuffix(IEnumerable<FenixFailure> failures)
    {
        int armed = 0, failed = 0;
        foreach (FenixFailure f in failures)
        {
            if (f.State == FenixFailureState.Armed) armed++;
            else if (f.State == FenixFailureState.Failed) failed++;
        }
        string s = "";
        if (armed > 0) s += $", {Num(armed)} armed";
        if (failed > 0) s += $", {Num(failed)} failed";
        return s;
    }

    private static string EventName(string name) => name switch
    {
        "V1" => "V1",
        "Takeoff" => "take-off",
        "Landing" => "landing",
        _ => name,
    };

    private static string Seconds(int n) => n == 1 ? "1 second" : Num(n) + " seconds";

    // Numeric text is the seconds remaining until the failure triggers (it counts down); other text is an
    // ISO date, read as UTC when it carries no zone. Unparseable text is kept as is.
    private static string TimePart(string text, TimeZoneInfo zone)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double remaining)
            && double.IsFinite(remaining))
        {
            int n = (int)Math.Min(int.MaxValue, Math.Max(1, Math.Ceiling(remaining)));
            return "fails in " + Seconds(n);
        }
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset when))
            return ArmedAtPrefix + text;
        return ArmedAtPrefix + TimeZoneInfo.ConvertTime(when, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string Plural(int n, string one, string many)
        => n == 1 ? "1 " + one : Num(n) + " " + many;

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
}

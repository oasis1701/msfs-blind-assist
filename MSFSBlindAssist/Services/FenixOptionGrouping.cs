using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>One group of the Fenix options window: its name and the options under it, in the order shown.</summary>
public sealed record FenixOptionGroup(string Name, IReadOnlyList<FenixSetting> Options)
{
    /// <summary>The group's node in the tree: its name and how many options it holds.</summary>
    public string NodeText => $"{Name} ({Options.Count})";
}

/// <summary>
/// How the Fenix options window orders the options into groups, and the texts it speaks for its status
/// box and for F5. First the options that touch what the app does, then one group per Fenix category in
/// the EFB's own order. Pure logic: no network, no state, no logging.
/// </summary>
public static class FenixOptionGrouping
{
    public const string AffectsAppGroup = "Options that affect MSFS Blind Assist";
    public const string OtherGroup = "Other, not in the Fenix EFB";
    public const string LoadingText = "Loading Fenix options...";
    public const string NoAnswerText = "The Fenix did not answer. Load the Fenix in the simulator, then press F5.";

    // The EFB's parseGpwsSortKey: the altitude callouts (gpws2500, gpws500_glide_deviation, ...). The digits
    // are ASCII only, as the EFB's \d is.
    private static readonly Regex Callout = new(@"^gpws([0-9]+)([a-z|_]*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The options in the order the window shows them: the affecting group (when any is present), the
    /// categories by name, then the options that belong to no category. An affecting option is listed in
    /// that group and again in its own category.
    /// </summary>
    public static IReadOnlyList<FenixOptionGroup> Group(IReadOnlyList<FenixSetting> settings)
    {
        var groups = new List<FenixOptionGroup>();

        var affecting = new List<FenixSetting>();
        foreach (string key in FenixOptionKeys.AffectsApp)
        {
            var present = settings.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));
            if (present is not null) affecting.Add(present);
        }
        if (affecting.Count > 0) groups.Add(new FenixOptionGroup(AffectsAppGroup, affecting));

        var other = new List<FenixSetting>();
        var named = new Dictionary<string, List<FenixSetting>>(StringComparer.Ordinal);
        foreach (var s in settings)
        {
            string? name = CategoryName(s.Category);
            if (name is null) { other.Add(s); continue; }
            if (!named.TryGetValue(name, out var list)) named[name] = list = new List<FenixSetting>();
            list.Add(s);
        }

        foreach (var entry in named.OrderBy(e => e.Key, StringComparer.InvariantCultureIgnoreCase))
            groups.Add(new FenixOptionGroup(entry.Key, Sorted(entry.Value)));
        if (other.Count > 0) groups.Add(new FenixOptionGroup(OtherGroup, Sorted(other)));
        return groups;
    }

    /// <summary>How many options changed between two reads: a different value, or present in only one of them.</summary>
    public static int CountChanged(IReadOnlyList<FenixSetting> before, IReadOnlyList<FenixSetting> after)
    {
        var was = ValuesByKey(before);
        var now = ValuesByKey(after);
        int changed = now.Count(o => !was.TryGetValue(o.Key, out var old) || !string.Equals(old, o.Value, StringComparison.Ordinal));
        return changed + was.Count(o => !now.ContainsKey(o.Key));
    }

    /// <summary>
    /// The affecting options the app was tested with a value for and that read as something else, each as
    /// its row, in the order the window lists them. An option with no tested value is never listed.
    /// </summary>
    public static IReadOnlyList<string> Untested(IReadOnlyList<FenixSetting> settings)
    {
        var rows = new List<string>();
        foreach (string key in FenixOptionKeys.AffectsApp)
        {
            string? tested = FenixOptionNotes.TestedValue(key);
            if (tested is null) continue;
            var present = settings.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));
            if (present is not null && !FenixOptionCatalog.SameValue(present.Value, tested)) rows.Add(FenixOptionCatalog.RowText(present));
        }
        return rows;
    }

    /// <summary>
    /// The status box: the Fenix version, how many options, what F2, Shift+F2 and Ctrl+F2 do, and what is not
    /// as tested.
    /// </summary>
    public static string StatusText(string? version, IReadOnlyList<FenixSetting> settings, bool changesAllowed)
    {
        string text = $"Fenix {(string.IsNullOrWhiteSpace(version) ? "version unknown" : version)}: {settings.Count} options. "
            + (changesAllowed
                ? "F2 changes an on/off or choice option for this simulator session. Shift+F2 makes an option permanent. "
                  + "Ctrl+F2 resets your change to the Fenix default."
                : "Read only: change options in the Fenix EFB.");
        var untested = Untested(settings);
        return untested.Count > 0
            ? text + $" Not what MSFS Blind Assist is tested with: {string.Join("; ", untested)}."
            : text;
    }

    /// <summary>What F5 speaks once it has read the options again.</summary>
    public static string RefreshText(int changed) => changed switch
    {
        0 => "Refreshed, no changes.",
        1 => "Refreshed, 1 option changed.",
        _ => $"Refreshed, {changed} options changed."
    };

    /// <summary>The group name for a Fenix category, or null for "None" and no category.</summary>
    private static string? CategoryName(string? category)
    {
        if (string.IsNullOrEmpty(category) || string.Equals(category, "None", StringComparison.Ordinal)) return null;
        string name = FenixOptionCatalog.Humanize(category);
        return name.Length == 0 ? null : name;
    }

    private static Dictionary<string, string?> ValuesByKey(IReadOnlyList<FenixSetting> settings)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var s in settings) values[s.Key] = s.Value;
        return values;
    }

    // OrderBy is stable, so options the comparison ties keep the order the Fenix gave them.
    private static List<FenixSetting> Sorted(List<FenixSetting> options)
        => options.OrderBy(o => o, Comparer<FenixSetting>.Create(CompareOptions)).ToList();

    /// <summary>
    /// The EFB's compareSettings: two altitude callouts by altitude, highest first, then by suffix; any other
    /// pair, and callouts that tie, by label.
    /// </summary>
    private static int CompareOptions(FenixSetting a, FenixSetting b)
    {
        var ca = Callout.Match(a.Key);
        var cb = Callout.Match(b.Key);
        if (ca.Success && cb.Success)
        {
            double altitudeA = double.Parse(ca.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            double altitudeB = double.Parse(cb.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            if (altitudeA != altitudeB) return altitudeB.CompareTo(altitudeA);
            int suffix = string.CompareOrdinal(ca.Groups[2].Value.ToLowerInvariant(), cb.Groups[2].Value.ToLowerInvariant());
            if (suffix != 0) return suffix;
        }
        return StringComparer.InvariantCultureIgnoreCase.Compare(FenixOptionCatalog.Label(a.Key), FenixOptionCatalog.Label(b.Key));
    }
}

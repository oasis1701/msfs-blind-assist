using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services;

/// <summary>
/// How the Fenix options window words an option: its label, how its value reads, and the detail lines.
/// Labels and value labels are the Fenix EFB's own (<see cref="FenixOptionLabels"/>); a key or value the
/// snapshot lacks reads the way the EFB itself shows it. A secret's value is never read, so it cannot
/// reach a row, the details, speech or a log. Pure text: no network, no state, no logging.
/// </summary>
public static class FenixOptionCatalog
{
    private static readonly Regex Separators = new("[_-]+", RegexOptions.CultureInvariant);
    private static readonly Regex AcronymEnd = new("([A-Z]+)([A-Z][a-z])", RegexOptions.CultureInvariant);
    private static readonly Regex WordStart = new("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant);

    /// <summary>True for the five credential and account options, matched by the whole key.</summary>
    public static bool IsSecret(string key) => FenixOptionKeys.Secrets.Contains(key, StringComparer.Ordinal);

    /// <summary>
    /// The EFB's humanizeKey pipe for a key it has no label for: separators to spaces, a space inside a
    /// run of capitals and before each capital that follows a lowercase letter or digit, then each word
    /// capitalised. "IaeV2500" reads "Iae V2500". Culture-invariant.
    /// </summary>
    public static string Humanize(string key)
    {
        string spaced = Separators.Replace(key, " ");
        spaced = AcronymEnd.Replace(spaced, "$1 $2");
        spaced = WordStart.Replace(spaced, "$1 $2");
        var words = spaced.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>The Fenix EFB's label for the option, else <see cref="Humanize"/> of its key.</summary>
    public static string Label(string key)
        => FenixOptionLabels.TryGet(key, out var text) ? text.Label : Humanize(key);

    /// <summary>How the option's value reads: On or Off, the EFB's label for a choice, a plain number, or the text.</summary>
    public static string FormatValue(FenixSetting s)
    {
        if (IsSecret(s.Key)) return string.IsNullOrEmpty(s.Value) ? "not set" : "set, hidden";
        if (s.Value is null) return "unknown";
        if (s.Type == "System.Boolean")
            return bool.TryParse(s.Value, out bool on) ? (on ? "On" : "Off") : s.Value;
        if (s.PossibleValues.Count > 0) return ChoiceLabel(s.Key, s.Value);
        if (s.Type is "System.Int32" or "System.Single")
            return NumberParsing.TryParseExternal(s.Value, out double number)
                ? number.ToString("0.###", CultureInfo.InvariantCulture)
                : s.Value;
        return s.Value.Length == 0 ? "not set" : s.Value;
    }

    /// <summary>The window's row for the option: "Label: value".</summary>
    public static string RowText(FenixSetting s) => $"{Label(s.Key)}: {FormatValue(s)}";

    /// <summary>
    /// The lines Enter reads for the option, each present only when it applies: what the option means for
    /// the app, the Fenix's description, the choices, where the value comes from, whether the EFB lets you
    /// change it, and that a secret is never shown.
    /// </summary>
    public static IReadOnlyList<string> DetailLines(FenixSetting s)
    {
        var lines = new List<string>();

        string? note = FenixOptionNotes.AppNote(s.Key);
        if (note is not null) lines.Add($"Effect on MSFS Blind Assist: {note}");

        lines.Add(FenixOptionLabels.TryGet(s.Key, out var text) && text.Description.Length > 0
            ? text.Description
            : "Fenix gives no description for this option.");

        if (s.PossibleValues.Count > 0)
            lines.Add($"Choices: {string.Join(", ", s.PossibleValues.Select(v => ChoiceLabel(s.Key, v)))}.");

        lines.Add(SourceText(s.Source));
        if (!s.Writable) lines.Add("The Fenix EFB does not let you change this option.");
        if (IsSecret(s.Key)) lines.Add("MSFS Blind Assist never shows or logs this value.");
        return lines;
    }

    /// <summary>
    /// The Fenix's own tooltip for where a value comes from, by the EFB's rule: lower case, "userfile" is
    /// "user", and anything but livery, session or user is the default.
    /// </summary>
    public static string SourceText(string? source)
        => SourceKind(source) switch
        {
            "livery" => "This setting has been overridden by the current livery.",
            "session" => "This setting is overridden for the current simulator session and will reset when you close MSFS or change aircraft/livery.",
            "user" => "This setting has been overridden by you and will apply regardless of livery or Fenix defaults.",
            _ => "This setting is the default Fenix value."
        };

    /// <summary>
    /// Where an option's value comes from, by the EFB's own normalisation (the one place it lives): lower case
    /// (culture-invariant), "userfile" is "user", and anything but livery, session or user is "default".
    /// Returns exactly "default", "livery", "session" or "user".
    /// </summary>
    internal static string SourceKind(string? source)
    {
        string kind = (source ?? "").ToLowerInvariant();
        return kind switch
        {
            "livery" => "livery",
            "session" => "session",
            "user" or "userfile" => "user",
            _ => "default"
        };
    }

    /// <summary>
    /// Whether two option values are the same one, the one comparison every part of the options window uses:
    /// two on/off values match whatever their spelling ("True" is "true"), any other pair must match exactly.
    /// A missing value matches nothing.
    /// </summary>
    internal static bool SameValue(string? a, string? b)
    {
        if (a is null || b is null) return false;
        return bool.TryParse(a, out bool x) && bool.TryParse(b, out bool y)
            ? x == y
            : string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>
    /// A choice as the EFB shows it: "" is "(none)", else the option's value label (exact, then lower case
    /// as the EFB's F() looks it up), else the raw value. An empty label counts as none: the EFB builds a
    /// choice with the raw value as its label and a label-less entry (acp_type's images) keeps it.
    /// </summary>
    internal static string ChoiceLabel(string key, string value)
    {
        if (value.Length == 0) return "(none)";
        if (FenixOptionLabels.TryGet(key, out var text))
        {
            if (text.ValueLabels.TryGetValue(value, out var label) && !string.IsNullOrEmpty(label)) return label;
            if (text.ValueLabels.TryGetValue(value.ToLowerInvariant(), out label) && !string.IsNullOrEmpty(label)) return label;
        }
        return value;
    }
}

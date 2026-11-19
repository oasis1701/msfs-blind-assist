using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>The kind of JSON value BlueBird keeps a setting in.</summary>
public enum Bbs757ValueType { Bool, Integer, Float, Text }

/// <summary>
/// One value BlueBird stores in a settings file: a switch, a whole number, a number with a fraction or text.
/// Values compare by what they hold, so every factory sets the fields its type does not use the same way
/// (false, 0, 0 and "", never null): <c>Of(2L)</c> equals <c>Of(2L)</c> and nothing else, <c>OfFloat(2.0)</c> included.
/// </summary>
public readonly record struct Bbs757Value(Bbs757ValueType Type, bool Bool, long Integer, double Float, string Text)
{
    private static readonly JsonSerializerOptions TextOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static Bbs757Value Of(bool v) => new(Bbs757ValueType.Bool, v, 0, 0, "");

    public static Bbs757Value Of(long v) => new(Bbs757ValueType.Integer, false, v, 0, "");

    /// <summary>A float. Named apart from <c>Of</c> so that a whole number such as <c>Of(2)</c> is never taken for one.</summary>
    public static Bbs757Value OfFloat(double v) => new(Bbs757ValueType.Float, false, 0, v, "");

    public static Bbs757Value Of(string v) => new(Bbs757ValueType.Text, false, 0, 0, v);

    /// <summary>
    /// The value as the JSON text BlueBird's files hold: <c>true</c>, <c>12</c>, <c>1.0</c>, <c>"abc"</c>. Always invariant:
    /// a float has a decimal point (never a comma), an integer the plain minus sign (sv-SE's own is U+2212). Text is
    /// escaped as little as JSON allows, so a code with an accent stays readable in the file. Throws
    /// <see cref="ArgumentException"/> for a float that is NaN or infinity: JSON has no such number.
    /// </summary>
    public string ToJson() => Type switch
    {
        Bbs757ValueType.Bool => Bool ? "true" : "false",
        Bbs757ValueType.Integer => Integer.ToString(CultureInfo.InvariantCulture),
        Bbs757ValueType.Float => FloatToJson(Float),
        Bbs757ValueType.Text => JsonSerializer.Serialize(Text, TextOptions),
        _ => throw new InvalidOperationException($"Unknown value type {Type}."),
    };

    // "R" is the shortest text that reads back as the same double. A whole number gets ".0" so the file keeps
    // the float it held; an exponent already marks a decimal number. NaN and infinity are refused: "NaN.0" or
    // "Infinity.0" is not JSON, and the check sits here, where the text is made, so it holds however the value was built.
    private static string FloatToJson(double v)
    {
        if (!double.IsFinite(v))
            throw new ArgumentException("A float that is not finite (NaN or infinity) cannot be written as JSON.", nameof(v));

        string text = v.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('e') ? text : text + ".0";
    }

    /// <summary>
    /// The value of a JSON element as <paramref name="type"/>, or null when it does not fit: Bool from true or false;
    /// Integer from a number written as a whole number that fits a long; Float from any finite number (a stored 1 is a
    /// valid Float); Text from a string that can be read. Null, an object, an array, an undefined element and a string
    /// with an unpaired surrogate escape (<c>"\uD800x"</c>: it parses, but no string can hold it) fit no type.
    /// </summary>
    public static Bbs757Value? From(JsonElement e, Bbs757ValueType type) => type switch
    {
        Bbs757ValueType.Bool when e.ValueKind is JsonValueKind.True or JsonValueKind.False =>
            Of(e.ValueKind == JsonValueKind.True),
        Bbs757ValueType.Integer when e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long whole) => Of(whole),
        Bbs757ValueType.Float when e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double number)
            && double.IsFinite(number) => OfFloat(number),
        Bbs757ValueType.Text when e.ValueKind == JsonValueKind.String && TryGetText(e, out string? text) => Of(text),
        _ => null,
    };

    // GetString throws InvalidOperationException for a string whose escapes are not valid UTF-16 (an unpaired
    // surrogate such as \uD800), which JsonDocument parses all the same: such a string reads as no text.
    private static bool TryGetText(JsonElement e, [NotNullWhen(true)] out string? text)
    {
        try
        {
            text = e.GetString();
            return text != null;
        }
        catch (InvalidOperationException)
        {
            text = null;
            return false;
        }
    }
}

/// <summary>Which of BlueBird's files holds a key.</summary>
public enum Bbs757SettingFile
{
    /// <summary><c>efb_settings.json</c>: one file for the whole aircraft.</summary>
    Global,

    /// <summary><c>liveries\&lt;registration&gt;-opt.json</c>: one file for each registration flown.</summary>
    Registration,
}

/// <summary>
/// The sections the catalog groups its settings by, in the order it lists them: the tablet's Settings pages, the
/// AIRCRAFT app's Failures page, and ACARS, which is no tablet page: it gathers the ACARS rows of the Settings >
/// Aircraft and AIRAC data pages.
/// </summary>
public enum Bbs757SettingSection { General, Aircraft, AiracData, Acars, Failures, LiveryOptions, Callouts, CabinAudio }

/// <summary>How the tablet lets a setting be changed.</summary>
public enum Bbs757SettingKind
{
    /// <summary>A switch: exactly two choices, Off then On.</summary>
    OnOff,

    /// <summary>One of the tablet's words, each storing its own value.</summary>
    Choice,

    /// <summary>A number the pilot types.</summary>
    Number,

    /// <summary>Text the pilot types.</summary>
    Text,

    /// <summary>
    /// A number the tablet sets along a slider: the pilot moves it between the catalog's steps
    /// (<see cref="Bbs757Setting.Steps"/>), and only a step is written.
    /// </summary>
    Slider,
}

/// <summary>What a Number box takes.</summary>
public enum Bbs757NumberEntry
{
    /// <summary>ASCII digits only, as a SimBrief ID (<see cref="Bbs757EfbLayout.IsSimBriefId"/>).</summary>
    Digits,

    /// <summary>A whole number.</summary>
    Whole,

    /// <summary>A whole percent, stored as a fraction from 0 to 1.</summary>
    Percent,
}

/// <summary>What a Number box takes and how far: <paramref name="Min"/> to <paramref name="Max"/>, both allowed.</summary>
public sealed record Bbs757NumberRange(Bbs757NumberEntry Entry, double Min, double Max);

/// <summary>A second key the tablet writes together with a setting's own (its UNITS switch also writes <c>units</c>).</summary>
public sealed record Bbs757LinkedKey(string Path, Bbs757SettingFile File);

/// <summary>
/// One of the tablet's words for a setting and the value BlueBird stores for it. <paramref name="AlsoStored"/> is
/// what the setting's <see cref="Bbs757Setting.Also"/> key gets with this choice, when it has one.
/// </summary>
public sealed record Bbs757SettingChoice(string Word, Bbs757Value Stored, Bbs757Value? AlsoStored = null);

/// <summary>
/// A setting the tablet shows, and what BlueBird stores for it (docs/b757.md, "The settings window").
/// </summary>
/// <param name="Path">The key, dotted through the file's objects: <c>loading_speed</c>, <c>failures.random_enabled</c>,
/// <c>options.egpws_callouts.2500</c>.</param>
/// <param name="File">The file that holds it.</param>
/// <param name="Section">The section it is in: the tablet page it is on, or ACARS.</param>
/// <param name="Label">The tablet's label, in sentence case with its abbreviations as the tablet writes them.</param>
/// <param name="Kind">How the tablet changes it.</param>
/// <param name="Type">The JSON type BlueBird keeps it in.</param>
/// <param name="Choices">For OnOff and Choice, the tablet's words in its order; none for Number, Text and Slider.</param>
/// <param name="EmptyStored">What a cleared Number or Text box stores; null when it cannot be cleared.</param>
/// <param name="MayLog">False for the pilot's identifiers: their values never reach a log.</param>
/// <param name="Range">For Number, what the box takes.</param>
/// <param name="Also">A second key a choice writes with this one.</param>
/// <param name="ExclusiveGroup">A callout in a group of which only one may be on: switching one on switches the rest off.</param>
/// <param name="EnabledBy">The path of the setting that must be On for this one to change (the announcement categories).</param>
/// <param name="Steps">For Slider, the values it moves between, lowest first: the only values it writes.</param>
public sealed record Bbs757Setting(string Path, Bbs757SettingFile File, Bbs757SettingSection Section, string Label,
    Bbs757SettingKind Kind, Bbs757ValueType Type, IReadOnlyList<Bbs757SettingChoice> Choices,
    Bbs757Value? EmptyStored = null, bool MayLog = true, Bbs757NumberRange? Range = null,
    Bbs757LinkedKey? Also = null, string? ExclusiveGroup = null, string? EnabledBy = null,
    IReadOnlyList<double>? Steps = null);

/// <summary>
/// One key to write in one file. <paramref name="Setting"/> is the setting the write is for: the one it changes,
/// or, for a linked key (UNITS and <c>units</c>), the setting the pilot changed.
/// </summary>
public sealed record Bbs757SettingWrite(Bbs757Setting Setting, string Path, Bbs757SettingFile File, Bbs757Value Value);

/// <summary>
/// The catalog of BlueBird's tablet settings: for each one its key, file, tablet label, the tablet's words and the
/// value BlueBird stores for each. The words and stored values were measured on the tablet
/// (2026-10-27, each changed on the tablet and both files read after it), with three exceptions. Hoppie ACARS's On is taken by type: it
/// was not tapped when measuring, as switching it on could log on to Hoppie. The "not set" values of the SimBrief ID
/// and the two ACARS codes (<c>simbrief_id</c> -1, the codes "") come from the spec and earlier notes, not from this
/// measurement: section 8 was not tapped. The TOD pause distance range of 1 to 999 is a ruling: the tablet's keypad
/// limits were not measured. The random failure frequency's slider steps are the app's, along the tablet's own scale.
/// The app writes only values BlueBird itself writes. The keys the tablet does not show (<c>units</c> on its own,
/// <c>options.model_visibility.*</c>, the charts and map state) are not here, so nothing can change them.
/// Pure: no files, no simulator.
/// </summary>
public static class Bbs757EfbSettings
{
    private const string Egpws = "options.egpws_callouts.";
    private const string Cabin = "options.cabin_audio.";

    /// <summary>
    /// Every setting, the sections in the order General, Aircraft, AIRAC data, ACARS, Failures, Livery options, Callouts
    /// and Cabin audio, and each section's settings in the tablet's row order.
    /// </summary>
    public static IReadOnlyList<Bbs757Setting> All { get; } = Build();

    private static readonly Dictionary<string, Bbs757Setting> PathIndex =
        All.ToDictionary(s => s.Path, StringComparer.Ordinal);

    /// <summary>The setting with this key, or null when the tablet shows none (or this version does not know it).</summary>
    public static Bbs757Setting? ByPath(string path) => PathIndex.GetValueOrDefault(path);

    /// <summary>The settings of one section, in the tablet's row order.</summary>
    public static IReadOnlyList<Bbs757Setting> InSection(Bbs757SettingSection section) =>
        All.Where(s => s.Section == section).ToArray();

    /// <summary>The page's name as the tablet gives it, in sentence case, and ACARS, which is no page.</summary>
    public static string SectionName(Bbs757SettingSection section) => section switch
    {
        Bbs757SettingSection.General => "General",
        Bbs757SettingSection.Aircraft => "Aircraft",
        Bbs757SettingSection.AiracData => "AIRAC data",
        Bbs757SettingSection.Acars => "ACARS",
        Bbs757SettingSection.Failures => "Failures",
        Bbs757SettingSection.LiveryOptions => "Livery options",
        Bbs757SettingSection.Callouts => "Callouts",
        Bbs757SettingSection.CabinAudio => "Cabin audio",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, "No such section."),
    };

    // ---- the EFB colors theme -----------------------------------------------------------------------

    /// <summary>The EFB colors theme's key. Neither 757 EFB window offers it as a choice, only as a note
    /// (<see cref="ThemeNote"/>, the owner's ruling of 2026-10-30, B757-21); the settings window writes it only as
    /// <see cref="DarkBlueTheme"/>, through its one way back to that theme.</summary>
    public const string ThemePath = "ui_theme";

    // The theme's label, as the tablet and the catalog give it.
    private const string ThemeLabel = "EFB colors theme";

    /// <summary>Why the EFB colors theme is a note: the tablet reader reads only the default theme, Dark blue (B757-21).</summary>
    public const string ThemeNotSupportedText = "Non-default color schemes are not supported.";

    /// <summary>
    /// The default EFB colors theme, Dark blue (stored 0, the catalog's first choice), the one theme the tablet reader reads: the
    /// only value the app writes to <see cref="ThemePath"/>, through the 757 EFB settings window's Set to Dark blue button, which
    /// shows while BlueBird's file holds another theme (B757-21, the owner's ruling of 2026-10-30).
    /// </summary>
    public static Bbs757Value DarkBlueTheme => Bbs757Value.Of(0L);

    /// <summary>
    /// What the 757 tablet window shows in the EFB colors theme's place, as text, since neither 757 EFB window offers the theme
    /// as a choice (B757-21): "EFB colors theme: Dark blue. Non-default color schemes are not supported." with the theme's word
    /// (<paramref name="choice"/>, one of the catalog's), or "EFB colors theme. Non-default color schemes are not supported."
    /// when none is given. The words are joined as they are: no Windows culture changes them.
    /// </summary>
    public static string ThemeNote(string? choice) =>
        ThemeLabel + (string.IsNullOrEmpty(choice) ? ". " : ": " + choice + ". ") + ThemeNotSupportedText;

    /// <summary>
    /// The same note after the theme's label, as the 757 EFB settings window shows it in a read-only box named by that label:
    /// "Dark blue. Non-default color schemes are not supported.", or the sentence alone when no <paramref name="choice"/> is
    /// given. The screen reader says the box's name first, so the two windows say the same words.
    /// </summary>
    public static string ThemeNoteValue(string? choice) =>
        (string.IsNullOrEmpty(choice) ? "" : choice + ". ") + ThemeNotSupportedText;

    // ---- what one change writes ---------------------------------------------------------------------

    /// <summary>
    /// The writes a change of <paramref name="setting"/> to <paramref name="value"/> makes, in the order to make them
    /// (the tablet's own rules, evidence section 9). A callout of an exclusive group switched on first writes false for
    /// each other member of its group, in <see cref="All"/> order, then its own true: a write that fails halfway leaves
    /// a state the tablet can produce. Anything else writes the setting's own value, then, when the setting has an
    /// <see cref="Bbs757Setting.Also"/> key and the choice storing the value has an
    /// <see cref="Bbs757SettingChoice.AlsoStored"/>, that key too (UNITS writes <c>units</c>), carrying the same setting.
    /// The app writes only values BlueBird itself stores, so a switch or a choice takes only the value one of its choices
    /// stores, and a slider only one of its steps: anything else throws <see cref="ArgumentException"/>, before anything
    /// is returned. A number or text box's value comes from <see cref="TryParseEntry"/> and is written as given.
    /// </summary>
    public static IReadOnlyList<Bbs757SettingWrite> WritesFor(Bbs757Setting setting, Bbs757Value value)
    {
        var choice = setting.Choices.FirstOrDefault(c => c.Stored == value);
        if (choice is null && setting.Kind is Bbs757SettingKind.OnOff or Bbs757SettingKind.Choice)
            throw new ArgumentException($"BlueBird has no choice of '{setting.Label}' that stores {value}.", nameof(value));
        if (setting.Kind == Bbs757SettingKind.Slider
            && (value.Type != setting.Type || setting.Steps?.Contains(value.Float) != true))
            throw new ArgumentException($"'{setting.Label}' has no step that stores {value}.", nameof(value));

        var writes = new List<Bbs757SettingWrite>();

        if (setting.ExclusiveGroup is { } groupName && value.Type == Bbs757ValueType.Bool && value.Bool)
        {
            foreach (var other in All)
            {
                if (other.ExclusiveGroup == groupName && other.Path != setting.Path)
                    writes.Add(new Bbs757SettingWrite(other, other.Path, other.File, Bbs757Value.Of(false)));
            }
        }

        writes.Add(new Bbs757SettingWrite(setting, setting.Path, setting.File, value));

        if (setting.Also is { } also && choice?.AlsoStored is { } alsoValue)
        {
            writes.Add(new Bbs757SettingWrite(setting, also.Path, also.File, alsoValue));
        }

        return writes;
    }

    // ---- number and text boxes ----------------------------------------------------------------------

    // The one box whose empty value is the tablet's switch turned off; for the others that can be cleared it is "not set".
    private const string TodPauseDistancePath = "tod_pause_distance";

    /// <summary>
    /// What a Number or Text box shows for the stored value, as the tablet shows it. Digits and Whole: the integer, or
    /// an empty box for <see cref="Bbs757Setting.EmptyStored"/> (off for the TOD pause distance, "not set" for the
    /// SimBrief ID: <see cref="EmptyHint"/>). Percent: <c>floor(v * 100)</c> clamped to the range (the tablet shows
    /// 0.99901 as 99%). Text: the text. Always invariant. Throws <see cref="ArgumentException"/> for any other kind.
    /// </summary>
    public static string NumberText(Bbs757Setting setting, Bbs757Value stored)
    {
        RequireBox(setting, nameof(setting));
        if (setting.Kind == Bbs757SettingKind.Text) return stored.Text;

        var range = RangeOf(setting);
        return range.Entry switch
        {
            Bbs757NumberEntry.Digits or Bbs757NumberEntry.Whole =>
                setting.EmptyStored is { } empty && stored == empty
                    ? ""
                    : stored.Integer.ToString(CultureInfo.InvariantCulture),
            Bbs757NumberEntry.Percent =>
                Math.Clamp(Math.Floor(stored.Float * 100), range.Min, range.Max).ToString("0", CultureInfo.InvariantCulture),
            _ => throw new ArgumentException($"Unknown number entry {range.Entry}.", nameof(setting)),
        };
    }

    /// <summary>
    /// Reads what the pilot typed into a Number or Text box, after trimming it. Empty stores
    /// <see cref="Bbs757Setting.EmptyStored"/>, or is refused when the setting has none. Digits must pass
    /// <see cref="Bbs757EfbLayout.IsSimBriefId"/>; Whole and Percent are read by
    /// <see cref="NumberParsing.TryParseTyped"/> (as typed on any Windows culture), need a whole number and must lie in
    /// the range. A percent p is stored as
    /// <c>(p + 0.4) / 100</c> (1.0 for 100), so the tablet shows p whether it floors or rounds. Text is stored trimmed.
    /// On a refusal <paramref name="reason"/> says what to type: for the TOD pause distance also that an empty box
    /// switches it off, and for the SimBrief ID that it is digits, not 0 ("0" is digits, and refused). Throws
    /// <see cref="ArgumentException"/> for any other kind.
    /// </summary>
    public static bool TryParseEntry(Bbs757Setting setting, string typed, out Bbs757Value value, out string? reason)
    {
        RequireBox(setting, nameof(setting));
        value = default;
        reason = null;
        string text = typed.Trim();

        if (text.Length == 0)
        {
            if (setting.EmptyStored is { } empty)
            {
                value = empty;
                return true;
            }
            reason = RefusalReason(setting);
            return false;
        }

        if (setting.Kind == Bbs757SettingKind.Text)
        {
            value = Bbs757Value.Of(text);
            return true;
        }

        var range = RangeOf(setting);
        switch (range.Entry)
        {
            case Bbs757NumberEntry.Digits when Bbs757EfbLayout.IsSimBriefId(text):
                value = Bbs757Value.Of(Bbs757EfbLayout.SimBriefIdValue(text));
                return true;
            case Bbs757NumberEntry.Whole when TryWholeNumber(text, range, out long whole):
                value = Bbs757Value.Of(whole);
                return true;
            case Bbs757NumberEntry.Percent when TryWholeNumber(text, range, out long percent):
                value = Bbs757Value.OfFloat(StoredPercent(percent));
                return true;
            default:
                reason = RefusalReason(setting);
                return false;
        }
    }

    /// <summary>
    /// What an empty box means, for the box's description: "Leave empty to switch it off." for the TOD pause distance,
    /// whose empty box is the tablet's switch turned off, and "Leave empty for not set." for every other Number or Text
    /// setting that can be cleared (<see cref="Bbs757Setting.EmptyStored"/>: the SimBrief ID and the two codes). Null for
    /// a setting that cannot be cleared or is no box.
    /// </summary>
    public static string? EmptyHint(Bbs757Setting setting)
    {
        if (setting.Kind is not (Bbs757SettingKind.Number or Bbs757SettingKind.Text) || setting.EmptyStored is null)
            return null;
        return EmptyMeansOff(setting) ? "Leave empty to switch it off." : "Leave empty for not set.";
    }

    // Whether the setting's empty box is the tablet's switch turned off, not "not set".
    private static bool EmptyMeansOff(Bbs757Setting setting) =>
        setting.EmptyStored is not null && setting.Path == TodPauseDistancePath;

    private static bool TryWholeNumber(string text, Bbs757NumberRange range, out long whole)
    {
        whole = 0;
        if (!NumberParsing.TryParseTyped(text, out double number)
            || number != Math.Floor(number) || number < range.Min || number > range.Max)
            return false;
        whole = (long)number;
        return true;
    }

    // The tablet shows floor(v * 100), and BlueBird keeps the value as a 32-bit float, which can land just under p / 100
    // (0.29 becomes 0.28999999 and shows 28; 0.294 came back 0.29399999, measured 2026-10-28). A typed percent is
    // stored 0.4 above its own, so it shows p after that loss, and under a tablet that rounds too. 100 is 1.0: the
    // tablet's brightness goes no higher.
    private static double StoredPercent(long percent) => percent >= 100 ? 1.0 : (percent + 0.4) / 100;

    // What the pilot is told to type when an entry is refused, and for the TOD pause distance how to switch it off. The
    // numbers are invariant and have no decimals.
    private static string RefusalReason(Bbs757Setting setting)
    {
        if (setting.Kind == Bbs757SettingKind.Text) return "type some text";

        var range = RangeOf(setting);
        return range.Entry switch
        {
            // Digits is the SimBrief ID's entry (Bbs757NumberEntry.Digits): "0" is digits and refused too.
            Bbs757NumberEntry.Digits => "type a SimBrief ID number (digits, not 0)",
            Bbs757NumberEntry.Whole or Bbs757NumberEntry.Percent =>
                string.Create(CultureInfo.InvariantCulture, $"type a whole number from {range.Min:0} to {range.Max:0}")
                + (EmptyMeansOff(setting) ? ", or leave the box empty to switch it off" : ""),
            _ => throw new ArgumentException($"Unknown number entry {range.Entry}.", nameof(setting)),
        };
    }

    private static void RequireBox(Bbs757Setting setting, string paramName)
    {
        if (setting.Kind is not (Bbs757SettingKind.Number or Bbs757SettingKind.Text))
            throw new ArgumentException($"'{setting.Label}' is not a number or text setting.", paramName);
    }

    private static Bbs757NumberRange RangeOf(Bbs757Setting setting) =>
        setting.Range ?? throw new ArgumentException($"'{setting.Label}' has no number range.", nameof(setting));

    // ---- sliders ------------------------------------------------------------------------------------

    /// <summary>
    /// What a Slider reads for the stored value, as the tablet shows the random failure frequency ("FREQUENCY: 2.0x
    /// (1x-1000x)"): one decimal and an x, <c>2.5x</c>, <c>1000.0x</c>; 1.9952623149688795 reads <c>2.0x</c>. Always
    /// invariant. Throws <see cref="ArgumentException"/> for a setting that is no slider.
    /// </summary>
    public static string SliderText(Bbs757Setting setting, Bbs757Value stored)
    {
        RequireSlider(setting);
        return stored.Float.ToString("0.0", CultureInfo.InvariantCulture) + "x";
    }

    /// <summary>
    /// The index of the step nearest <paramref name="value"/> on the tablet's scale, where each step is a factor apart
    /// (the nearer by the logarithm): where a value set on the tablet between steps sits. A value at or below the first
    /// step is the first, at or above the last the last; one that is no number is the first. Throws
    /// <see cref="ArgumentException"/> for a setting that is no slider.
    /// </summary>
    public static int NearestStep(Bbs757Setting setting, double value)
    {
        IReadOnlyList<double> steps = RequireSlider(setting);
        if (!double.IsFinite(value) || value <= steps[0]) return 0;
        if (value >= steps[^1]) return steps.Count - 1;

        double target = Math.Log10(value);
        int nearest = 0;
        for (int index = 1; index < steps.Count; index++)
        {
            if (Math.Abs(Math.Log10(steps[index]) - target) < Math.Abs(Math.Log10(steps[nearest]) - target))
                nearest = index;
        }
        return nearest;
    }

    private static IReadOnlyList<double> RequireSlider(Bbs757Setting setting) =>
        setting is { Kind: Bbs757SettingKind.Slider, Steps: { Count: > 0 } steps }
            ? steps
            : throw new ArgumentException($"'{setting.Label}' is not a slider.", nameof(setting));

    // ---- the catalog --------------------------------------------------------------------------------

    private static IReadOnlyList<Bbs757Setting> Build() =>
        GeneralSettings()
            .Concat(AircraftSettings())
            .Concat(AiracDataSettings())
            .Concat(AcarsSettings())
            .Concat(FailureSettings())
            .Concat(LiveryOptionSettings())
            .Concat(CalloutSettings())
            .Concat(CabinAudioSettings())
            .ToArray();

    // The tablet keeps the Livery options, Callouts and Cabin audio pages' selections for the aircraft's registration:
    // they are in each registration's file. Everything else is in efb_settings.json.
    private static Bbs757SettingFile FileOf(Bbs757SettingSection section) => section switch
    {
        Bbs757SettingSection.LiveryOptions or Bbs757SettingSection.Callouts or Bbs757SettingSection.CabinAudio
            => Bbs757SettingFile.Registration,
        _ => Bbs757SettingFile.Global,
    };

    /// <summary>A switch: Off stores false, On stores true.</summary>
    private static Bbs757Setting Switch(Bbs757SettingSection section, string path, string label,
        string? exclusiveGroup = null, string? enabledBy = null) =>
        new(path, FileOf(section), section, label, Bbs757SettingKind.OnOff, Bbs757ValueType.Bool,
            new[] { Choice("Off", false), Choice("On", true) },
            ExclusiveGroup: exclusiveGroup, EnabledBy: enabledBy);

    /// <summary>Two words for a true or false value, the one stored as true first (the tablet's order).</summary>
    private static Bbs757Setting TwoWords(Bbs757SettingSection section, string path, string label,
        string whenTrue, string whenFalse) =>
        Pick(section, path, label, Bbs757ValueType.Bool, Choice(whenTrue, true), Choice(whenFalse, false));

    /// <summary>A choice of the tablet's words, in the tablet's order.</summary>
    private static Bbs757Setting Pick(Bbs757SettingSection section, string path, string label, Bbs757ValueType type,
        params Bbs757SettingChoice[] choices) =>
        new(path, FileOf(section), section, label, Bbs757SettingKind.Choice, type, choices);

    /// <summary>A number set along a slider, written only at <paramref name="steps"/>; it has no choices.</summary>
    private static Bbs757Setting Slider(Bbs757SettingSection section, string path, string label, double[] steps) =>
        new(path, FileOf(section), section, label, Bbs757SettingKind.Slider, Bbs757ValueType.Float,
            Array.Empty<Bbs757SettingChoice>(), Steps: steps);

    /// <summary>A number or text box, which has no choices.</summary>
    private static Bbs757Setting Box(Bbs757SettingSection section, string path, string label, Bbs757SettingKind kind,
        Bbs757ValueType type, Bbs757Value? emptyStored = null, bool mayLog = true, Bbs757NumberRange? range = null,
        string? enabledBy = null) =>
        new(path, FileOf(section), section, label, kind, type, Array.Empty<Bbs757SettingChoice>(),
            EnabledBy: enabledBy, EmptyStored: emptyStored, MayLog: mayLog, Range: range);

    private static Bbs757SettingChoice Choice(string word, bool stored) => new(word, Bbs757Value.Of(stored));

    private static Bbs757SettingChoice Choice(string word, long stored) => new(word, Bbs757Value.Of(stored));

    private static Bbs757SettingChoice Choice(string word, string stored) => new(word, Bbs757Value.Of(stored));

    // General (efb_settings.json), evidence section 2.
    private static Bbs757Setting[] GeneralSettings()
    {
        const Bbs757SettingSection section = Bbs757SettingSection.General;
        return new[]
        {
            Switch(section, "force_keyboard_capture", "Lock keyboard to EFB"),
            Switch(section, "efb_power_on_during_init", "EFB powered on during initialization"),
            // MANUAL has no word on the tablet: it shows the percent below instead.
            TwoWords(section, "auto_brightness", "EFB brightness mode", "Auto", "Manual"),
            Box(section, "display_brightness", "EFB brightness", Bbs757SettingKind.Number, Bbs757ValueType.Float,
                range: new Bbs757NumberRange(Bbs757NumberEntry.Percent, 0, 100)),
            // DEFAULT is 0, then the digit itself.
            Pick(section, "background_num", "EFB background", Bbs757ValueType.Integer,
                Enumerable.Range(0, 10)
                    .Select(n => Choice(n == 0 ? "Default" : n.ToString(CultureInfo.InvariantCulture), n)).ToArray()),
            Switch(section, "autobrake_notifications", "Live autobrake info"),
            // 3 SEC to 30 SEC along the slider, one whole second at a time, then NEVER (0) at its right end.
            Pick(section, "notification_popback_seconds", "Notifications fade-out timer", Bbs757ValueType.Integer,
                Enumerable.Range(3, 28)
                    .Select(n => Choice(string.Create(CultureInfo.InvariantCulture, $"{n} sec"), n))
                    .Append(Choice("Never", 0L)).ToArray()),
            // The slider's order, which is not the stored numbers': LIGHT GREY is 3 and DARK GREY is 2. Neither 757 EFB window
            // offers it as a choice (B757-21): the catalog keeps it to name the stored choice in their notes, and its first, Dark
            // blue, is what the settings window's way back writes (DarkBlueTheme).
            Pick(section, ThemePath, ThemeLabel, Bbs757ValueType.Integer,
                Choice("Dark blue", 0L), Choice("Light blue", 1L), Choice("Light grey", 3L), Choice("Dark grey", 2L)),
        };
    }

    // Aircraft (efb_settings.json), evidence sections 3 and 4: the Settings > Aircraft rows but the two ACARS ones (the
    // ACARS section has them), then GND SRVC mode from the AIRCRAFT app's Ground services page.
    private static Bbs757Setting[] AircraftSettings()
    {
        const Bbs757SettingSection section = Bbs757SettingSection.Aircraft;
        return new[]
        {
            // BlueBird's 2026-10-31 update added Adaptive handling assist, the automatic step climbs and Throttle lockout
            // (measured 2026-11-01, each switched on and back off on the tablet).
            Switch(section, "easy_flight_enabled", "Adaptive handling assist"),
            // Shown under it only while it is on: a slider storing 0 to 100, which the tablet says as GENTLE at 0, BALANCED at
            // 50, RESPONSIVE at 100 and "N/100" between.
            Box(section, "easy_flight_authority", "Control response", Bbs757SettingKind.Number, Bbs757ValueType.Integer,
                range: new Bbs757NumberRange(Bbs757NumberEntry.Whole, 0, 100), enabledBy: "easy_flight_enabled"),
            TwoWords(section, "automatic_cockpit_lighting", "Adaptive cockpit lighting", "Automatic", "Manual"),
            // Off is -1 (an empty box); switched on, the tablet stores 40 and then what its keypad types. The range 1 to
            // 999 is a ruling: the keypad's limits were not measured.
            Box(section, TodPauseDistancePath, "TOD pause distance (NM)", Bbs757SettingKind.Number, Bbs757ValueType.Integer,
                emptyStored: Bbs757Value.Of(-1L), range: new Bbs757NumberRange(Bbs757NumberEntry.Whole, 1, 999)),
            // The tablet's own label; its status line under it (OFF, DISARMED) is no setting.
            TwoWords(section, "automatic_step_climbs", "Automatic step climbs master option (experimental)", "Enabled", "Disabled"),
            TwoWords(section, "tiller_realistic", "Nosewheel steering", "Realistic", "Rudder"),
            Switch(section, "throttle_lockout_enabled", "Throttle lockout"),
            TwoWords(section, "displays_bootup_realistic", "Displays bootup speed", "Realistic", "Fast"),
            Switch(section, "baro_sync", "Sync barometers"),
            TwoWords(section, "cockpit_door_initial_open", "Initial cockpit door", "Open", "Closed"),
            TwoWords(section, "parking_brake_realistic", "Parking brake mode", "Realistic", "Easy"),
            Pick(section, "irs_align_time", "IRS alignment time", Bbs757ValueType.Integer,
                Choice("Instant", 0L), Choice("1 min", 1L), Choice("2 min", 2L), Choice("5 min", 5L),
                Choice("Full (7 min+)", 9L)),
            Pick(section, "loading_speed", "Boarding & load rate", Bbs757ValueType.Integer,
                Choice("Realistic", 0L), Choice("Standard", 1L), Choice("Quick", 2L), Choice("Instant", 3L)),
            Switch(section, "automatic_loading", "Automatic boarding"),
            Switch(section, "auto_connect_ground_services_on_init", "Automatic GPU/jetway"),
            Switch(section, "auto_attach_chocks_on_init", "Initial chocks in place"),
            Switch(section, "dirty_panel_states_enabled", "Dirty panel states"),
            Pick(section, "ground_services_mode", "GND SRVC mode", Bbs757ValueType.Integer,
                Choice("BBS", 0L), Choice("MSFS", 1L)),
        };
    }

    // AIRAC data (efb_settings.json), evidence section 8, which lists this row but was not tapped: the "not set" value
    // (-1) comes from the spec and earlier notes. The pilot's own identifier: never logged. The tablet's AIRAC data page also
    // holds the Navigraph sign-in and the nav data update; the settings window shows only this setting, and those two are on
    // the tablet window's page.
    private static Bbs757Setting[] AiracDataSettings()
    {
        const Bbs757SettingSection section = Bbs757SettingSection.AiracData;
        return new[]
        {
            Box(section, "simbrief_id", "SimBrief ID number", Bbs757SettingKind.Number, Bbs757ValueType.Integer,
                emptyStored: Bbs757Value.Of(-1L), mayLog: false,
                range: new Bbs757NumberRange(Bbs757NumberEntry.Digits, 1, int.MaxValue)),
        };
    }

    // ACARS (efb_settings.json), no tablet page: the ACARS rows of Settings > Aircraft (evidence section 3), then those
    // of AIRAC data (section 8, not tapped: the codes' "not set" value "" comes from the spec and earlier notes). The
    // codes are the pilot's own identifiers: never logged.
    private static Bbs757Setting[] AcarsSettings()
    {
        const Bbs757SettingSection section = Bbs757SettingSection.Acars;
        return new[]
        {
            // On was not tapped when measuring: it could log on to Hoppie with the owner's code (evidence section 3).
            Switch(section, "hoppie_user_enabled", "Hoppie ACARS"),
            Pick(section, "cpdlc_network", "CPDLC network", Bbs757ValueType.Integer,
                Choice("Hoppie", 0L), Choice("SayIntentions", 1L)),
            Box(section, "hoppie_logon_code", "Hoppie logon code", Bbs757SettingKind.Text, Bbs757ValueType.Text,
                emptyStored: Bbs757Value.Of(""), mayLog: false),
            Box(section, "sayintentions_acars_key", "SayIntentions ACARS key", Bbs757SettingKind.Text, Bbs757ValueType.Text,
                emptyStored: Bbs757Value.Of(""), mayLog: false),
        };
    }

    // Failures (efb_settings.json), evidence section 4: the AIRCRAFT app's Failures page, RANDOM tab.
    private static Bbs757Setting[] FailureSettings()
    {
        const Bbs757SettingSection section = Bbs757SettingSection.Failures;
        // The tablet's slider is continuous, 1x to 1000x at 10^(3t) along its track. The app's steps are ten to each
        // factor of ten along that scale, each at most one decimal so the tablet's "N.Nx" shows it exactly; ten steps
        // (Page Up, Page Down) are a factor of ten.
        double[] frequencySteps =
        {
            1, 1.3, 1.6, 2, 2.5, 3.2, 4, 5, 6.3, 8,
            10, 13, 16, 20, 25, 32, 40, 50, 63, 80,
            100, 130, 160, 200, 250, 320, 400, 500, 630, 800,
            1000,
        };
        return new[]
        {
            Slider(section, "failures.random_multiplier", "Random failure frequency", frequencySteps),
            Switch(section, "failures.random_enabled", "Random failures"),
            Switch(section, "failures.notifications_enabled", "Failure notifications"),
        };
    }

    // Livery options (each registration's file), evidence section 5.
    private static Bbs757Setting[] LiveryOptionSettings()
    {
        const Bbs757SettingSection section = Bbs757SettingSection.LiveryOptions;
        return new[]
        {
            // The tablet's one UNITS switch writes the registration's weight unit and, with it, the global units.
            new Bbs757Setting("options.general.weight_unit", FileOf(section), section, "Units",
                Bbs757SettingKind.Choice, Bbs757ValueType.Text,
                new[]
                {
                    new Bbs757SettingChoice("Imperial", Bbs757Value.Of("lbs"), Bbs757Value.Of(1L)),
                    new Bbs757SettingChoice("Metric", Bbs757Value.Of("kg"), Bbs757Value.Of(0L)),
                },
                Also: new Bbs757LinkedKey("units", Bbs757SettingFile.Global)),
            Pick(section, "options.ehsi.up_reference", "EHSI up reference", Bbs757ValueType.Text,
                Choice("HDG", "hdg"), Choice("TRK", "trk")),
            Pick(section, "options.general.vnav_takeoff_activation", "VNAV takeoff behavior", Bbs757ValueType.Text,
                Choice("Option 1", "vnav_button"), Choice("Option 2", "clb_button")),
        };
    }

    // Callouts (each registration's file), evidence section 6, in the tablet's row order. Of the EGPWS callouts, three
    // groups allow one member on at a time: 2500, DH + 100 and the minimums.
    private static Bbs757Setting[] CalloutSettings()
    {
        const Bbs757SettingSection section = Bbs757SettingSection.Callouts;
        var settings = new List<Bbs757Setting>
        {
            Switch(section, "options.pilot_callouts.80_knots", "80 KTS"),
            Switch(section, "options.pilot_callouts.v1", "V1"),
            Switch(section, "options.pilot_callouts.rotate", "Rotate"),
            Switch(section, "options.pilot_callouts.positive_rate", "Positive rate"),
            Switch(section, Egpws + "2500", "2500", exclusiveGroup: "2500"),
            Switch(section, Egpws + "radio_altimeter", "Radio altimeter (2500)", exclusiveGroup: "2500"),
        };

        // The height callouts are named by their number, as the key and as the label.
        foreach (string height in new[] { "1000", "500", "400", "300", "200", "100", "80", "60", "50", "40", "35", "30", "20", "10", "5" })
            settings.Add(Switch(section, Egpws + height, height));

        settings.Add(Switch(section, Egpws + "plus_hundred", "Plus hundred (DH + 100)", exclusiveGroup: "dh100"));
        settings.Add(Switch(section, Egpws + "approaching_dh", "Approaching DH (DH + 100)", exclusiveGroup: "dh100"));
        settings.Add(Switch(section, Egpws + "approaching_minimums", "Approaching minimums (DH + 80)"));
        settings.Add(Switch(section, Egpws + "50_above", "Fifty above (DH + 50)"));
        settings.Add(Switch(section, Egpws + "minimums", "Minimums", exclusiveGroup: "minimums"));
        settings.Add(Switch(section, Egpws + "minimums_minimums", "Minimums minimums", exclusiveGroup: "minimums"));
        settings.Add(Switch(section, Egpws + "minimum", "Minimum", exclusiveGroup: "minimums"));
        settings.Add(Switch(section, Egpws + "decide", "Decide", exclusiveGroup: "minimums"));
        settings.Add(Switch(section, Egpws + "decision_height", "Decision height", exclusiveGroup: "minimums"));
        return settings.ToArray();
    }

    // Cabin audio (each registration's file), evidence section 7. The two announcement categories cannot change while
    // ANNOUNCEMENTS is off.
    private static Bbs757Setting[] CabinAudioSettings()
    {
        const Bbs757SettingSection section = Bbs757SettingSection.CabinAudio;
        return new[]
        {
            Switch(section, Cabin + "announcements", "Announcements"),
            Switch(section, Cabin + "safety_announcements", "Safety related announcements",
                enabledBy: Cabin + "announcements"),
            Switch(section, Cabin + "non_safety_announcements", "Non-safety related announcements",
                enabledBy: Cabin + "announcements"),
            Switch(section, Cabin + "flight_attendant_calls", "Flight attendant calls"),
            Switch(section, Cabin + "passenger_sounds", "Passenger sounds"),
        };
    }
}

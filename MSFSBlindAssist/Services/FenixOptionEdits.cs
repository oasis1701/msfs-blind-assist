using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>One thing an option can be set to: the value the Fenix is sent, and how the pilot hears it.</summary>
public sealed record FenixOptionChoice(string Value, string Label);

/// <summary>What the pilot asked the options window for: F2, Shift+F2 or Ctrl+F2.</summary>
public enum FenixChangeKind
{
    /// <summary>F2: change the option for this simulator session only.</summary>
    Session,

    /// <summary>Shift+F2: make a value the pilot's own, for every flight and livery.</summary>
    Permanent,

    /// <summary>Ctrl+F2: reset the option to the Fenix default.</summary>
    Reset,
}

/// <summary>
/// What the Fenix options window may send when the pilot changes an option, and the exact requests.
/// F2 is the Fenix EFB's own toggle (<c>setSettingUnchecked({key, value: String(value), persist: false})</c>):
/// a session change, so the value resets when MSFS closes or the aircraft or livery changes. Shift+F2 and
/// Ctrl+F2 are the EFB's per-option menu: "Set as user override" sends the same change with persist true, and
/// "Reset to default" runs <c>setDefault(key)</c>, which the EFB enables only for an option whose value comes
/// from the user or the session, whatever its type. F2 and Shift+F2 change only a writable on/off or choice
/// option that is not a credential; Ctrl+F2 resets any writable option that is not a credential.
/// Pure logic: no network, no state, no logging.
/// </summary>
public static class FenixOptionEdits
{
    /// <summary>The mutation the Fenix EFB runs for a change; the variables come from <see cref="Variables"/>.</summary>
    public const string SetSettingDocument =
        "mutation SetSetting($setSetting: SetSettingInput!) { settings { setSetting(setSetting: $setSetting) { success } } }";

    /// <summary>
    /// The mutation the Fenix EFB's "Reset to default" runs, for one option; the variables come from
    /// <see cref="DefaultVariables"/>.
    /// </summary>
    public const string SetDefaultDocument =
        "mutation SetDefault($key: String!) { settings { setDefault(key: $key) { success } } }";

    // The EFB hides this option (it has no row for it), so the app does not offer a change the EFB does not.
    private const string ChartsProviderKey = "charts_provider_type";

    // The options that change the cockpit's panels, so the pilot is asked first (the owner's decision).
    private static readonly string[] CockpitReshaping = { "standby_type", "isis_type", "adirs_type", "acp_type", "dcdu_enabled" };

    /// <summary>
    /// True when the EFB lets the option be written at all: it is writable, is not a credential and is not the
    /// Charts provider (the EFB has no row for it). Any type of option: whether it can be given a value to
    /// pick is <see cref="CanChange"/>.
    /// </summary>
    public static bool CanWrite(FenixSetting s)
        => s.Writable && !FenixOptionCatalog.IsSecret(s.Key)
           && !string.Equals(s.Key, ChartsProviderKey, StringComparison.Ordinal);

    /// <summary>
    /// True when the option is <see cref="CanWrite">writable</see> and is an on/off option or a choice with at
    /// least two values: what F2 and Shift+F2 offer a value for.
    /// </summary>
    public static bool CanChange(FenixSetting s)
        => CanWrite(s) && (IsBoolean(s) || (IsChoice(s) && s.PossibleValues.Count >= 2));

    /// <summary>
    /// What the option can be set to, as the EFB sends it: "true" or "false" for an on/off option (On first),
    /// else each of the Fenix's possible values with the label the options window shows for it. Empty for any
    /// other kind of option.
    /// </summary>
    public static IReadOnlyList<FenixOptionChoice> Choices(FenixSetting s)
    {
        if (IsBoolean(s))
            return new[] { new FenixOptionChoice("true", "On"), new FenixOptionChoice("false", "Off") };
        if (IsChoice(s))
            return s.PossibleValues.Select(v => new FenixOptionChoice(v, FenixOptionCatalog.ChoiceLabel(s.Key, v))).ToList();
        return Array.Empty<FenixOptionChoice>();
    }

    /// <summary>
    /// True when the pilot can reset the option to the Fenix default: it can be <see cref="CanWrite">written</see>
    /// and the EFB's own rule holds, that its value comes from the user or the session. Any type of option,
    /// a typed value (Registration) as well as an on/off or choice one, as the EFB's "Reset to default" does.
    /// A default, livery or unknown source has no change of the pilot's to reset.
    /// </summary>
    public static bool CanReset(FenixSetting s)
        => CanWrite(s) && FenixOptionCatalog.SourceKind(s.Source) is "user" or "session";

    /// <summary>
    /// The variables of <see cref="SetSettingDocument"/>: the value is always a string, and <c>persist</c> is
    /// false unless the caller asks for a permanent change, so by default the change lasts for this simulator
    /// session only.
    /// </summary>
    public static object Variables(string key, string value, bool persist = false)
        => new { setSetting = new { key, value, persist } };

    /// <summary>
    /// Whether a change is sent with <c>persist</c> true: only Shift+F2, making a value the pilot's own.
    /// F2 is a session change, and a reset sends <see cref="SetDefaultDocument"/>, which has no such flag.
    /// </summary>
    public static bool Persists(FenixChangeKind kind) => kind == FenixChangeKind.Permanent;

    /// <summary>The variables of <see cref="SetDefaultDocument"/>: just the key.</summary>
    public static object DefaultVariables(string key) => new { key };

    /// <summary>True only when the Fenix answered <c>settings.setSetting.success</c> as the JSON value true.</summary>
    public static bool Succeeded(JObject? data)
        => data?.SelectToken("settings.setSetting.success") is JValue { Type: JTokenType.Boolean } success && (bool)success;

    /// <summary>True only when the Fenix answered <c>settings.setDefault.success</c> as the JSON value true.</summary>
    public static bool DefaultSucceeded(JObject? data)
        => data?.SelectToken("settings.setDefault.success") is JValue { Type: JTokenType.Boolean } success && (bool)success;

    /// <summary>True for the options that reshape the cockpit's panels, which the pilot confirms first.</summary>
    public static bool NeedsConfirm(FenixSetting s) => CockpitReshaping.Contains(s.Key, StringComparer.Ordinal);

    /// <summary>The question asked before a cockpit-reshaping change.</summary>
    public static string ConfirmText(string optionLabel, string choiceLabel)
        => $"Change {optionLabel} to {choiceLabel}? This changes your cockpit panels for this simulator session, until you change aircraft or livery.";

    /// <summary>The question asked before a permanent change.</summary>
    public static string PermanentConfirmText(string optionLabel, string choiceLabel)
        => $"Make {optionLabel} {choiceLabel} your permanent setting? It applies to every flight and livery until you change it.";

    /// <summary>The question asked before a reset to the Fenix default.</summary>
    public static string ResetConfirmText(string optionLabel)
        => $"Reset {optionLabel} to the Fenix default?";

    // Added to the permanent and the reset question for an option that reshapes the cockpit's panels.
    private const string ReshapesCockpit = " This changes your cockpit panels.";

    /// <summary>
    /// The Yes/No question a change asks before anything is sent, or null when it asks none. F2 asks only
    /// before a change that reshapes the cockpit (<see cref="NeedsConfirm"/>); Shift+F2 and Ctrl+F2 always
    /// ask, and say so too when the option reshapes the cockpit. Never null for a permanent change or a reset.
    /// <paramref name="choiceLabel"/> is the chosen value as the pilot hears it (unused for a reset).
    /// </summary>
    public static string? Question(FenixChangeKind kind, FenixSetting option, string optionLabel, string choiceLabel)
    {
        bool cockpit = NeedsConfirm(option);
        return kind switch
        {
            FenixChangeKind.Permanent => PermanentConfirmText(optionLabel, choiceLabel) + (cockpit ? ReshapesCockpit : ""),
            FenixChangeKind.Reset => ResetConfirmText(optionLabel) + (cockpit ? ReshapesCockpit : ""),
            _ => cockpit ? ConfirmText(optionLabel, choiceLabel) : null,
        };
    }

    /// <summary>
    /// What the read-back says when the Fenix accepted a request but, after the bounded wait
    /// (<see cref="FenixSettingReadBack.AwaitAsync"/>), still reads the option as not applied: the option
    /// as the Fenix now has it (<paramref name="rowText"/>, its row), said as kept, not made permanent, or not reset.
    /// </summary>
    public static string NotAppliedText(FenixChangeKind kind, string rowText) => kind switch
    {
        FenixChangeKind.Permanent => $"Not made permanent: {rowText}.",
        FenixChangeKind.Reset => $"Not reset: {rowText}.",
        _ => $"Fenix kept {rowText}.",
    };

    /// <summary>
    /// Whether the Fenix has applied a request, judged on a read after it was accepted. A session change
    /// applied when the option now reads the value that was sent; a permanent one also needs the value to
    /// come from the user (a value the session or a livery overrides is not yet the pilot's own); a reset
    /// applied when the option no longer reads as it did (a different value, or a different source: the
    /// Fenix default may equal the old override) since the default itself is not known here. Two on/off
    /// values match whatever their spelling. <paramref name="before"/> is the option as the pilot saw it
    /// before the request; <paramref name="sentValue"/> is the value a change asked for.
    /// </summary>
    public static bool Applied(FenixChangeKind kind, FenixSetting before, string? sentValue, FenixSetting now)
        => kind switch
        {
            FenixChangeKind.Session => FenixOptionCatalog.SameValue(now.Value, sentValue),
            FenixChangeKind.Permanent => FenixOptionCatalog.SameValue(now.Value, sentValue) && FenixOptionCatalog.SourceKind(now.Source) == "user",
            _ => !SameOrBothMissing(now.Value, before.Value)
                 || FenixOptionCatalog.SourceKind(now.Source) != FenixOptionCatalog.SourceKind(before.Source),
        };

    /// <summary>
    /// Whether the options window's read-back wait is over for one answered read of the options: the read
    /// shows the change (<paramref name="applied"/> on the option's row), or it lacks the option's key
    /// altogether. A later read is not expected to bring a missing option back, so the window says "not read
    /// back" at once instead of waiting out all 16 reads (about 4 s). A read the Fenix did not answer is
    /// the wait's own concern, not judged here.
    /// </summary>
    internal static bool ReadBackSettled(IReadOnlyList<FenixSetting> read, string key, Func<FenixSetting, bool> applied)
        => read.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal)) is not { } option || applied(option);

    // Two values that are the same one, or that are both missing (SameValue says a missing value matches nothing).
    private static bool SameOrBothMissing(string? a, string? b) => (a is null && b is null) || FenixOptionCatalog.SameValue(a, b);

    /// <summary>What the window says when the pilot presses a change key for an option that kind of change is not offered for.</summary>
    internal const string CannotChangeText = "MSFS Blind Assist cannot change this option.";

    /// <summary>
    /// What the window says instead of acting when a key is pressed on an option, or null when the key may go
    /// ahead. F2 and Shift+F2 need <see cref="CanChange"/> (an on/off or choice option); Ctrl+F2 needs only
    /// <see cref="CanWrite"/>, then a change of the pilot's or the session's to reset (<see cref="CanReset"/>):
    /// "cannot change" is said for an option the EFB does not let you write, "no change of yours" for one it
    /// does but whose value is the default's or a livery's.
    /// </summary>
    internal static string? RefusalText(FenixChangeKind kind, string optionLabel, FenixSetting option)
    {
        if (kind != FenixChangeKind.Reset) return CanChange(option) ? null : CannotChangeText;
        if (!CanWrite(option)) return CannotChangeText;
        return CanReset(option) ? null : $"{optionLabel} has no change of yours to reset.";
    }

    /// <summary>
    /// Whether the screen reader reads the option's row by itself after the change, so the app must not speak
    /// it again (CORE-7). The re-read rewrites the text of the focused tree row in place and NVDA reads a
    /// focused row whose text changes; that holds only while the options tree has focus, its selected node is
    /// this option's, and its text is no longer what it was before the write (<paramref name="textBefore"/>,
    /// null when that node was not this option's). When the text did not change the reader says nothing, and
    /// the app speaks the row. Exact comparison, culture-invariant.
    /// </summary>
    internal static bool ScreenReaderReadsRow(bool treeHasFocus, bool selectedIsThisOption, string? textBefore, string textAfter)
        => treeHasFocus && selectedIsThisOption && textBefore is not null
           && !string.Equals(textBefore, textAfter, StringComparison.Ordinal);

    private static bool IsBoolean(FenixSetting s) => string.Equals(s.Type, "System.Boolean", StringComparison.Ordinal);

    private static bool IsChoice(FenixSetting s) => string.Equals(s.Type, "Enum", StringComparison.Ordinal);
}

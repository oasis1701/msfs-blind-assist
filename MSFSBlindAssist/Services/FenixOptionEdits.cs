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
/// from the user or the session. Only a writable on/off or choice option that is not a credential can change.
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

    // The options that change the cockpit's panels, so the pilot is asked first (owner answer 5).
    private static readonly string[] CockpitReshaping = { "standby_type", "isis_type", "adirs_type", "acp_type", "dcdu_enabled" };

    /// <summary>
    /// True when the option is writable, is not a credential, is not the Charts provider, and is an on/off
    /// option or a choice with at least two values.
    /// </summary>
    public static bool CanChange(FenixSetting s)
    {
        if (!s.Writable || FenixOptionCatalog.IsSecret(s.Key)) return false;
        if (string.Equals(s.Key, ChartsProviderKey, StringComparison.Ordinal)) return false;
        return IsBoolean(s) || (IsChoice(s) && s.PossibleValues.Count >= 2);
    }

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
    /// True when the pilot can reset the option to the Fenix default: it can change at all
    /// (<see cref="CanChange"/>) and the EFB's own rule holds, that its value comes from the user or the
    /// session. A default, livery or unknown source has no change of the pilot's to reset.
    /// </summary>
    public static bool CanReset(FenixSetting s)
        => CanChange(s) && FenixOptionCatalog.SourceKind(s.Source) is "user" or "session";

    /// <summary>
    /// The variables of <see cref="SetSettingDocument"/>: the value is always a string, and <c>persist</c> is
    /// false unless the caller asks for a permanent change, so by default the change lasts for this simulator
    /// session only.
    /// </summary>
    public static object Variables(string key, string value, bool persist = false)
        => new { setSetting = new { key, value, persist } };

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
        => $"Change {optionLabel} to {choiceLabel}? This changes your cockpit panels until MSFS closes.";

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
    /// What the read-back says when the Fenix accepted a request but, after the retry, still reads the
    /// option as not applied: the option as the Fenix now has it (<paramref name="rowText"/>, its row), said
    /// as kept, not made permanent, or not reset.
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

    // Two values that are the same one, or that are both missing (SameValue says a missing value matches nothing).
    private static bool SameOrBothMissing(string? a, string? b) => (a is null && b is null) || FenixOptionCatalog.SameValue(a, b);

    private static bool IsBoolean(FenixSetting s) => string.Equals(s.Type, "System.Boolean", StringComparison.Ordinal);

    private static bool IsChoice(FenixSetting s) => string.Equals(s.Type, "Enum", StringComparison.Ordinal);
}

using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>One thing an option can be set to: the value the Fenix is sent, and how the pilot hears it.</summary>
public sealed record FenixOptionChoice(string Value, string Label);

/// <summary>
/// What the Fenix options window may send when the pilot changes an option with F2, and the exact request.
/// It is the Fenix EFB's own toggle (<c>setSettingUnchecked({key, value: String(value), persist: false})</c>):
/// a session change, so the value resets when MSFS closes or the aircraft or livery changes. Only a writable
/// on/off or choice option that is not a credential can change. Pure logic: no network, no state, no logging.
/// </summary>
public static class FenixOptionEdits
{
    /// <summary>The mutation the Fenix EFB runs for a change; the variables come from <see cref="Variables"/>.</summary>
    public const string SetSettingDocument =
        "mutation SetSetting($setSetting: SetSettingInput!) { settings { setSetting(setSetting: $setSetting) { success } } }";

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
    /// The variables of <see cref="SetSettingDocument"/>: the value is always a string, and <c>persist</c> is
    /// always false, so the change lasts for this simulator session only.
    /// </summary>
    public static object Variables(string key, string value)
        => new { setSetting = new { key, value, persist = false } };

    /// <summary>True only when the Fenix answered <c>settings.setSetting.success</c> as the JSON value true.</summary>
    public static bool Succeeded(JObject? data)
        => data?.SelectToken("settings.setSetting.success") is JValue { Type: JTokenType.Boolean } success && (bool)success;

    /// <summary>True for the options that reshape the cockpit's panels, which the pilot confirms first.</summary>
    public static bool NeedsConfirm(FenixSetting s) => CockpitReshaping.Contains(s.Key, StringComparer.Ordinal);

    /// <summary>The question asked before a cockpit-reshaping change.</summary>
    public static string ConfirmText(string optionLabel, string choiceLabel)
        => $"Change {optionLabel} to {choiceLabel}? This changes your cockpit panels until MSFS closes.";

    /// <summary>
    /// Whether two option values are the same one: two on/off values match whatever their spelling ("True" is
    /// "true"), any other pair must match exactly. A missing value matches nothing.
    /// </summary>
    public static bool SameValue(string? a, string? b)
    {
        if (a is null || b is null) return false;
        return bool.TryParse(a, out bool x) && bool.TryParse(b, out bool y)
            ? x == y
            : string.Equals(a, b, StringComparison.Ordinal);
    }

    private static bool IsBoolean(FenixSetting s) => string.Equals(s.Type, "System.Boolean", StringComparison.Ordinal);

    private static bool IsChoice(FenixSetting s) => string.Equals(s.Type, "Enum", StringComparison.Ordinal);
}

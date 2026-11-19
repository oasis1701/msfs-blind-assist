namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One MCP button light: the variable key the app reads it under, the L:var it is (its button's
/// decal lights from it), the button's map node, and the spoken name, which is the button's panel
/// label. The light var has the node's own name, so the key must differ from it.
/// </summary>
public sealed record Bbs757McpLight(string Key, string LightVar, string Node, string Label);

/// <summary>
/// The BlueBird 757's MCP button lights, <c>L:B757_MCP_&lt;name&gt;_BUTTON</c> (each button's decal
/// emissive code reads it, ORed with the lights test). Pure: the table and the words. A press of one
/// of these buttons reads back its light ("CMD R on", CORE-7) instead of "pressed"; the lights are
/// announced on change, with Ctrl+M rows; the autopilot window (Ctrl+P) shows them on its buttons.
/// HOLD_1 is HDG HOLD and HOLD_2 is ALT HOLD (stage 10a's evidence); CMD 1 to 3 are the 757's
/// CMD L, C and R.
/// </summary>
public static class Bbs757McpLights
{
    public static IReadOnlyList<Bbs757McpLight> All { get; } = new[]
    {
        Light("THR", "THR"),
        Light("SPD", "SPD"),
        Light("LNAV", "LNAV"),
        Light("VNAV", "VNAV"),
        Light("FLCH", "FLCH"),
        Light("HOLD_1", "HDG HOLD"),
        Light("VS", "V/S"),
        Light("HOLD_2", "ALT HOLD"),
        Light("BCRS_1", "BCRS 1"),
        Light("LOC", "LOC"),
        Light("APP", "APP"),
        Light("CMD_1", "CMD L"),
        Light("CMD_2", "CMD C"),
        Light("CMD_3", "CMD R"),
    };

    private static Bbs757McpLight Light(string name, string label)
        => new($"BBS757_MCP_LIGHT_{name}", $"B757_MCP_{name}_BUTTON", $"B757_MCP_{name}_BUTTON", label);

    private static readonly Dictionary<string, Bbs757McpLight> ByNode = All.ToDictionary(l => l.Node, StringComparer.Ordinal);
    private static readonly Dictionary<string, Bbs757McpLight> ByKey = All.ToDictionary(l => l.Key, StringComparer.Ordinal);

    /// <summary>The light of the MCP button with this map node, or null.</summary>
    public static Bbs757McpLight? ForNode(string node) => ByNode.GetValueOrDefault(node);

    /// <summary>The light read under this variable key, or null.</summary>
    public static Bbs757McpLight? ForKey(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>"On", "Off", or "unavailable" when it was not read.</summary>
    public static string State(double? value)
        => value is double v && double.IsFinite(v) ? (v > 0.5 ? "On" : "Off") : "unavailable";

    /// <summary>"CMD R on", "CMD R off", "CMD R unavailable".</summary>
    public static string Phrase(string label, double? value) => $"{label} {State(value).ToLowerInvariant()}";

    /// <summary>
    /// What an autopilot window press says when the aircraft did not take it: the light read back as it
    /// was before the press, "CMD L did not engage" (off and still off) or "CMD L did not disengage" (on
    /// and still on); null when it changed or either read is missing (the window's caption says the rest).
    /// </summary>
    public static string? RefusedPhrase(string label, double? before, double? after)
    {
        if (before is not double b || after is not double a || !double.IsFinite(b) || !double.IsFinite(a)) return null;
        bool wasOn = b > 0.5, isOn = a > 0.5;
        if (wasOn != isOn) return null;
        return wasOn ? $"{label} did not disengage" : $"{label} did not engage";
    }
}

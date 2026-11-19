using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The mode buttons of the four MCP value windows (Ctrl+H, S, A and V), as the PMDG 737's windows
/// carry theirs: each window the buttons that go with its value, by map node, and their access keys.
/// Pure; the definition takes each button's label from the map (or <see cref="CaptionOverride"/>), its
/// state from the MCP light and its press from the panel's path (BBS757Definition.Mcp.cs).
/// </summary>
public static class Bbs757McpWindowLayout
{
    /// <summary>The IAS/MACH knob's push: BlueBird's speed intervention (<c>L:B757_MCP_IAS_MACH_KNOB_BUTTON</c>).</summary>
    public const string SpeedInterveneNode = "B757_MCP_AP_SPD_HOLD_SW";

    /// <summary>The IAS/MACH changeover button: a Mach or knots set presses it when the window shows the other units.</summary>
    public const string ChangeoverNode = "B757_MCP_AP_SPD_MACH_SW";

    private static readonly string[] Heading = { "B757_MCP_AP_HDG_SEL_SW", "B757_MCP_HOLD_1_BUTTON", "B757_MCP_LNAV_BUTTON" };
    // The knob's own actions first, as the PMDG 737 window has Speed Intervene first (Gus, 2026-10-29: the box, then
    // intervene, then the changeover, then the modes).
    private static readonly string[] Speed = { SpeedInterveneNode, "B757_MCP_AP_SPD_MACH_SW", "B757_MCP_SPD_BUTTON", "B757_MCP_THR_BUTTON", "B757_MCP_FLCH_BUTTON" };
    private static readonly string[] Altitude = { "B757_MCP_HOLD_2_BUTTON", "B757_MCP_VNAV_BUTTON", "B757_MCP_FLCH_BUTTON" };
    private static readonly string[] VerticalSpeed = { "B757_MCP_VS_BUTTON" };

    /// <summary>
    /// Each button's access key by node, fixed so a relabelled control keeps its key and the keys match
    /// the PMDG windows (ruling R57): Alt+I is intervene, Alt+M the IAS/MACH changeover. FLCH sits in two
    /// windows with one key, Alt+L as the PMDG 737's Level Change (Gus, 2026-10-29: Alt+F was not where he
    /// looked for it).
    /// </summary>
    private static readonly Dictionary<string, char> Keys = new(StringComparer.Ordinal)
    {
        ["B757_MCP_AP_HDG_SEL_SW"] = 'S', ["B757_MCP_HOLD_1_BUTTON"] = 'H', ["B757_MCP_LNAV_BUTTON"] = 'L',
        ["B757_MCP_SPD_BUTTON"] = 'S', ["B757_MCP_THR_BUTTON"] = 'T', ["B757_MCP_FLCH_BUTTON"] = 'L',
        ["B757_MCP_AP_SPD_MACH_SW"] = 'M', [SpeedInterveneNode] = 'I',
        ["B757_MCP_HOLD_2_BUTTON"] = 'H', ["B757_MCP_VNAV_BUTTON"] = 'V',
        ["B757_MCP_VS_BUTTON"] = 'V',
    };

    /// <summary>Window captions that say what a button does where the map's label does not ("AP SPD Hold").</summary>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        [SpeedInterveneNode] = "Speed Intervene",
        [Bbs757AutopilotLayout.AutopilotDisconnectNode] = "A/P Disconnect",   // the map's "Captain Yoke A/P Disconnect" stays on the panel
    };

    /// <summary>
    /// A window's buttons by node, in order: heading HDG SEL, HDG HOLD, LNAV; speed Speed Intervene,
    /// IAS/MACH, SPD, THR, FLCH; altitude ALT HOLD, VNAV, FLCH; vertical speed V/S. None for any other action.
    /// </summary>
    public static IReadOnlyList<string> NodesFor(HotkeyAction action) => action switch
    {
        HotkeyAction.FCUSetHeading => Heading,
        HotkeyAction.FCUSetSpeed => Speed,
        HotkeyAction.FCUSetAltitude => Altitude,
        HotkeyAction.FCUSetVS => VerticalSpeed,
        _ => Array.Empty<string>(),
    };

    /// <summary>The button's fixed access key (Alt and this letter), or null for a node no window carries.</summary>
    public static char? AccessKeyFor(string node) => Keys.TryGetValue(node, out char key) ? key : null;

    /// <summary>The window caption that replaces the map's label for this node, or null to use the label.</summary>
    public static string? CaptionOverride(string node) => Overrides.GetValueOrDefault(node);

    /// <summary>
    /// A window's button texts for these nodes, in order: <see cref="CaptionOverride"/> or
    /// <paramref name="labelOf"/>'s label, with its fixed key marked on the last word that starts with
    /// it ("HDG &amp;HOLD", "IAS/&amp;MACH", "Speed &amp;Intervene"); a word starts after a space or a slash.
    /// A text where no word starts with the key marks its first letter that is the key ("F&amp;LCH"); a text
    /// without the letter shows no key rather than a wrong one. An ampersand in a label is doubled, so it
    /// shows as itself rather than marking a key.
    /// </summary>
    public static IReadOnlyList<string> Captions(IReadOnlyList<string> nodes, Func<string, string> labelOf)
    {
        var captions = new string[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            string text = CaptionOverride(nodes[i]) ?? labelOf(nodes[i]);
            int at = AccessKeyFor(nodes[i]) is char key ? KeyPlace(text, key) : -1;
            captions[i] = at < 0 ? Escape(text) : $"{Escape(text[..at])}&{Escape(text[at..])}";
        }
        return captions;
    }

    // The last word that starts with the key, else the key's first letter anywhere; -1 when the text has none.
    private static int KeyPlace(string text, char key)
    {
        for (int i = text.Length - 1; i >= 0; i--)
        {
            bool wordStart = i == 0 || text[i - 1] is ' ' or '/';
            if (wordStart && char.ToUpperInvariant(text[i]) == key) return i;
        }
        for (int i = 0; i < text.Length; i++)
            if (char.ToUpperInvariant(text[i]) == key) return i;
        return -1;
    }

    private static string Escape(string text) => text.Replace("&", "&&");
}

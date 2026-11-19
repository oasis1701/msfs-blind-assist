namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The "757 Autopilot" window's buttons, in order: the three CMD buttons, the autothrottle arm and
/// flight director switches, the mode buttons in the MCP's own left-to-right order, then Speed
/// Intervene, A/T Disconnect and A/P Disconnect (the captain's yoke button). Each names its map node and its fixed access key;
/// a light button's state is its MCP light (<see cref="Bbs757McpLights"/>), A/T Arm's is BlueBird's
/// armed flag (<see cref="AutothrottleArmedVar"/>), Speed Intervene's the speed window's blank flag,
/// a switch's its map position, and A/T Disconnect and A/P Disconnect have none (a second A/P Disconnect
/// press silences the warning on BlueBird, the owner, 2026-11-05). Pure: the window takes the labels, the
/// states and the presses from the definition
/// (BBS757Definition.Autopilot.cs).
/// </summary>
public static class Bbs757AutopilotLayout
{
    /// <summary>A/T ARM: a toggle on <c>L:AT_ARM_SWITCH</c> (0 On, 10 Off, the map's positions).</summary>
    public const string AutothrottleArmNode = "B757_MCP_SW_AP_throttle";
    public const string CaptainFdNode = "B757_MCP_SW_FD_Cap";
    public const string FirstOfficerFdNode = "B757_MCP_SW_FD_FO";

    /// <summary>
    /// BlueBird's own autothrottle-armed flag, <c>L:BBS_AUTO_THROTTLE</c> (1 armed, 0 not): what the
    /// A/T Arm button shows, the A/T's real state (ruling R40). The switch's click code sets the flag
    /// with the switch, so the two agree whenever the click moved them; every BlueBird panel state
    /// loads the flag 1 with the switch ON (session 7). A press still flips the switch.
    /// </summary>
    public const string AutothrottleArmedVar = "BBS_AUTO_THROTTLE";

    /// <summary>The IAS/MACH knob's push: BlueBird's speed intervention, captioned from the speed window's blank flag.</summary>
    public const string SpeedInterveneNode = Bbs757McpWindowLayout.SpeedInterveneNode;

    /// <summary>The thrust levers' A/T disconnect button: a momentary press with no state to show.</summary>
    public const string AutothrottleDisconnectNode = "B757_AT_DISC_1";

    /// <summary>
    /// The captain's yoke A/P disconnect button (the map's "Captain Yoke A/P Disconnect"): a held press
    /// with no state to show, whose press code fires K:AUTOPILOT_OFF. On BlueBird a second press silences
    /// the disconnect warning (the owner, 2026-11-05). The MCP's disengage bar stays on the MCP panel.
    /// </summary>
    public const string AutopilotDisconnectNode = "btn_yoke_autopilot_disengage_pilot";

    /// <summary>The window's buttons by node, in order: the sixteen engage and mode controls, then Speed Intervene, A/T Disconnect and A/P Disconnect.</summary>
    public static IReadOnlyList<string> Nodes { get; } = new[]
    {
        "B757_MCP_CMD_1_BUTTON", "B757_MCP_CMD_2_BUTTON", "B757_MCP_CMD_3_BUTTON",
        AutothrottleArmNode, CaptainFdNode, FirstOfficerFdNode,
        "B757_MCP_THR_BUTTON", "B757_MCP_SPD_BUTTON",
        "B757_MCP_LNAV_BUTTON", "B757_MCP_VNAV_BUTTON", "B757_MCP_FLCH_BUTTON",
        "B757_MCP_HOLD_1_BUTTON", "B757_MCP_HOLD_2_BUTTON", "B757_MCP_VS_BUTTON",
        "B757_MCP_LOC_BUTTON", "B757_MCP_APP_BUTTON",
        SpeedInterveneNode, AutothrottleDisconnectNode, AutopilotDisconnectNode,
    };

    /// <summary>
    /// Each button's fixed access key by node (Alt and the letter presses it). Fixed rather than taken from
    /// the label, so a relabelled control keeps its key; several labels (ALT HOLD, V/S, LOC) hold no free
    /// letter of their own, so the window handles the keys itself and names each to the screen reader.
    /// </summary>
    private static readonly Dictionary<string, char> Keys = new(StringComparer.Ordinal)
    {
        ["B757_MCP_CMD_1_BUTTON"] = 'L', ["B757_MCP_CMD_2_BUTTON"] = 'C', ["B757_MCP_CMD_3_BUTTON"] = 'R',
        [AutothrottleArmNode] = 'A', [CaptainFdNode] = 'F', [FirstOfficerFdNode] = 'O',
        ["B757_MCP_THR_BUTTON"] = 'T', ["B757_MCP_SPD_BUTTON"] = 'S',
        ["B757_MCP_LNAV_BUTTON"] = 'N', ["B757_MCP_VNAV_BUTTON"] = 'V', ["B757_MCP_FLCH_BUTTON"] = 'H',
        ["B757_MCP_HOLD_1_BUTTON"] = 'D', ["B757_MCP_HOLD_2_BUTTON"] = 'E', ["B757_MCP_VS_BUTTON"] = 'Y',
        ["B757_MCP_LOC_BUTTON"] = 'G', ["B757_MCP_APP_BUTTON"] = 'P',
        [SpeedInterveneNode] = 'I', [AutothrottleDisconnectNode] = 'X', [AutopilotDisconnectNode] = 'B',
    };

    /// <summary>The button's fixed access key (Alt and this letter), or null for a node the window does not carry.</summary>
    public static char? AccessKeyFor(string node) => Keys.TryGetValue(node, out char key) ? key : null;

    /// <summary>
    /// The switches among them: their state is not a light, so the window asks the sim for it while shown,
    /// and a press moves them to their other position.
    /// </summary>
    public static IReadOnlyList<string> SwitchNodes { get; } = new[] { AutothrottleArmNode, CaptainFdNode, FirstOfficerFdNode };

    /// <summary>
    /// What the window reads on request while shown: the three switches (a press flips a switch from
    /// its own position) and the A/T's armed flag (the A/T Arm caption).
    /// </summary>
    public static IReadOnlyList<string> RequestedKeys { get; } = SwitchNodes.Append(AutothrottleArmedVar).ToArray();

    /// <summary>The A/T Arm caption's state from <see cref="AutothrottleArmedVar"/>: "On", "Off", or "unavailable" when it was not read.</summary>
    public static string AutothrottleState(double? armed)
        => armed is double v && double.IsFinite(v) ? (v > 0.5 ? "On" : "Off") : "unavailable";

    /// <summary>
    /// What an A/T Arm press says when the armed flag reads back as it was before it (so the caption
    /// cannot change, ruling R29's style): "A/T Arm did not arm" (off and still off), "A/T Arm did not
    /// disarm" (on and still on); null when it changed or either read is missing. The press flips the
    /// switch, so this is heard when the switch and the flag disagreed.
    /// </summary>
    public static string? AutothrottleRefusedPhrase(string label, double? before, double? after)
    {
        if (before is not double b || after is not double a || !double.IsFinite(b) || !double.IsFinite(a)) return null;
        bool wasArmed = b > 0.5, isArmed = a > 0.5;
        if (wasArmed != isArmed) return null;
        return wasArmed ? $"{label} did not disarm" : $"{label} did not arm";
    }

    /// <summary>
    /// A switch's state from its read value: the name of the map position it is at, or "unavailable"
    /// when it was not read or is at none of them.
    /// </summary>
    public static string SwitchState(Bbs757Control control, double? value)
    {
        if (value is not double v || !double.IsFinite(v)) return "unavailable";
        int i = Bbs757Rpn.PositionIndex(control, v);
        return i >= 0 ? control.Positions[i].Label : "unavailable";
    }

    /// <summary>
    /// The position a press of a two-position switch moves it to: the other one; null when the value
    /// is not one of its two positions (a toggle's press flips it, so nothing is pressed then).
    /// </summary>
    public static double? FlipTarget(Bbs757Control control, double? value)
    {
        if (control.Positions.Count != 2 || value is not double v || !double.IsFinite(v)) return null;
        int i = Bbs757Rpn.PositionIndex(control, v);
        return i < 0 ? null : control.Positions[1 - i].Value;
    }
}

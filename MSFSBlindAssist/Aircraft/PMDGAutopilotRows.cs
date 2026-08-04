namespace MSFSBlindAssist.Aircraft;

/// <summary>How a row is actuated and rendered.</summary>
public enum ApRowKind
{
    /// <summary>One-shot press. CDA parameter 1; parameter 0 is a no-op, not a release.</summary>
    Momentary,
    /// <summary>Two-position switch. Rendered as a button that flips and shows its state.</summary>
    Toggle,
    /// <summary>Multi-position switch. Rendered as a ComboBox, never a cycling button.</summary>
    Selector,
}

/// <summary>
/// One row of the Ctrl+P autopilot window, as pure data.
/// <para>
/// <paramref name="StateField"/> is the PMDG CDA field the row reads for its live
/// state, which is NOT always the varDef's Name: the 737's momentary MCP buttons
/// are named for the switch but read an annunciator (MCP_CmdA -> MCP_annunCMD_A),
/// while the 777's are named for the annunciator directly. It is "" for controls
/// with no readable state (the disconnects), whose buttons show a bare label.
/// </para>
/// </summary>
public record ApRowSpec(string Label, ApRowKind Kind, string VarKey, string StateField);

/// <summary>
/// The engage-cluster row tables for the PMDG Ctrl+P autopilot window.
/// <para>
/// Engage cluster ONLY. The per-axis mode buttons (LNAV, VNAV, LVL CHG, HDG SEL /
/// HDG HOLD, ALT HOLD, VS/FPA) deliberately live in the Ctrl+H/S/A/V value dialogs
/// instead, where both defs already expose them with live state — duplicating them
/// here would put the same control in two places. Same split as IFly737AutopilotWindow.
/// </para>
/// </summary>
public static class PMDGAutopilotRows
{
    public static IReadOnlyList<ApRowSpec> For777() => s_777;

    private static readonly ApRowSpec[] s_777 =
    {
        new("AP Left",         ApRowKind.Momentary, "MCP_AP_L",          "MCP_annunAP_0"),
        new("AP Right",        ApRowKind.Momentary, "MCP_AP_R",          "MCP_annunAP_1"),
        new("F/D Left",        ApRowKind.Toggle,    "MCP_FD_L",          "MCP_FD_Sw_On_0"),
        new("F/D Right",       ApRowKind.Toggle,    "MCP_FD_R",          "MCP_FD_Sw_On_1"),
        new("A/T Arm Left",    ApRowKind.Toggle,    "MCP_ATArm_L",       "MCP_ATArm_Sw_On_0"),
        new("A/T Arm Right",   ApRowKind.Toggle,    "MCP_ATArm_R",       "MCP_ATArm_Sw_On_1"),
        new("Approach",        ApRowKind.Momentary, "MCP_APP",           "MCP_annunAPP"),
        new("LOC",             ApRowKind.Momentary, "MCP_LOC",           "MCP_annunLOC"),
        new("A/T",             ApRowKind.Momentary, "MCP_AT",            "MCP_annunAT"),
        new("Disengage Bar",   ApRowKind.Toggle,    "MCP_DisengageBar",  "MCP_DisengageBar"),
        new("A/P Disconnect",  ApRowKind.Momentary, "YOKE_APDisc",       ""),
        new("A/T Disconnect",  ApRowKind.Momentary, "ENG_ATDisengage_1", ""),
        new("Bank Limit",      ApRowKind.Selector,  "MCP_BankLimitSel",  "MCP_BankLimitSel"),
    };

    public static IReadOnlyList<ApRowSpec> For737() => s_737;

    private static readonly ApRowSpec[] s_737 =
    {
        new("CMD A",             ApRowKind.Momentary, "MCP_CmdA",         "MCP_annunCMD_A"),
        new("CMD B",             ApRowKind.Momentary, "MCP_CmdB",         "MCP_annunCMD_B"),
        new("CWS A",             ApRowKind.Momentary, "MCP_CwsA",         "MCP_annunCWS_A"),
        new("CWS B",             ApRowKind.Momentary, "MCP_CwsB",         "MCP_annunCWS_B"),
        new("F/D Captain",       ApRowKind.Toggle,    "MCP_FDSw_0",       "MCP_FDSw_0"),
        new("F/D First Officer", ApRowKind.Toggle,    "MCP_FDSw_1",       "MCP_FDSw_1"),
        new("Approach",          ApRowKind.Momentary, "MCP_AppBtn",       "MCP_annunAPP"),
        new("VOR LOC",           ApRowKind.Momentary, "MCP_VorLoc",       "MCP_annunVOR_LOC"),
        new("A/T Arm",           ApRowKind.Toggle,    "MCP_ATArmSw",      "MCP_ATArmSw"),
        new("Disengage Bar",     ApRowKind.Toggle,    "MCP_DisengageBar", "MCP_DisengageBar"),
        new("A/P Disconnect",    ApRowKind.Momentary, "YOKE_APDisc",      ""),
        new("A/T Disconnect",    ApRowKind.Momentary, "CS_ATDisc_1",      ""),
        new("Bank Limit",        ApRowKind.Selector,  "MCP_BankLimitSel", "MCP_BankLimitSel"),
    };
}

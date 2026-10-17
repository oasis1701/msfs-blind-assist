namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>Where an autoflight window button's state comes from.</summary>
public enum A300ButtonState
{
    /// <summary>A one-shot press with no state: a knob push or pull, a disconnect, TOGA.</summary>
    None,
    /// <summary>An FCU push button, shown by its lamp (<see cref="A300FcuState.ByButton"/>).</summary>
    Lamp,
    /// <summary>A two-position switch, shown by its own position in its row's words.</summary>
    Switch,
}

/// <summary>One button of an autoflight window: the panel row it presses, its Alt-key letter ('\0' for
/// none), and where its state comes from.</summary>
public sealed record A300WindowButton(string RowKey, char Mnemonic, A300ButtonState State);

/// <summary>One combo of the autopilot window: a multi-position switch row, named for its side.</summary>
public sealed record A300WindowSelector(string RowKey, char Mnemonic, string Label);

/// <summary>
/// The A300's input-mode autoflight windows (spec 2026-10-05): the FCU value boxes (Ctrl+S, H, A, V)
/// carry their axis's knob push and pull and mode buttons, and Ctrl+P's window carries the engage
/// cluster, the approach modes, the disconnects, TOGA, the bank limit and the flight directors. Each
/// control appears in one window only, as on the PMDG.
///
/// Every label is the control's panel row name (<see cref="A300PanelLayout"/>), so one control has
/// one name across the app, plus, for a knob push or pull, what it does in iniBuilds' own tooltip
/// words (the map's <c>action</c>), unless those words only repeat the name ("SET SPEED KNOB" on the
/// speed knob pull). Pure.
/// </summary>
public static class A300AutoflightWindows
{
    public static readonly IReadOnlyList<A300WindowButton> Speed = new[]
    {
        new A300WindowButton("A300_SPEED_KNOB_PUSH", 'P', A300ButtonState.None),
        new A300WindowButton("A300_SPEED_KNOB_PULL", 'L', A300ButtonState.None),
        new A300WindowButton(A300FcuState.SpeedMachButtonKey, 'M', A300ButtonState.Lamp),
    };

    public static readonly IReadOnlyList<A300WindowButton> Heading = new[]
    {
        new A300WindowButton("A300_HEADING_KNOB_PUSH", 'P', A300ButtonState.None),
        new A300WindowButton("A300_HEADING_KNOB_PULL", 'L', A300ButtonState.None),
        new A300WindowButton("A300_HDGSEL_BUTTON", 'S', A300ButtonState.Lamp),
        new A300WindowButton("A300_NAV_BUTTON", 'N', A300ButtonState.Lamp),
    };

    public static readonly IReadOnlyList<A300WindowButton> Altitude = new[]
    {
        new A300WindowButton("A300_ALT_KNOB_PUSH", 'P', A300ButtonState.None),
        new A300WindowButton("A300_ALT_KNOB_PULL", 'L', A300ButtonState.None),
        new A300WindowButton("A300_ALTHLD_BUTTON", 'H', A300ButtonState.Lamp),
        new A300WindowButton("A300_LVLCH_BUTTON", 'C', A300ButtonState.Lamp),
        new A300WindowButton("A300_PROFILE_BUTTON", 'R', A300ButtonState.Lamp),
    };

    public static readonly IReadOnlyList<A300WindowButton> VerticalSpeed = new[]
    {
        new A300WindowButton("A300_VS_KNOB_PUSH", 'P', A300ButtonState.None),
        new A300WindowButton("A300_VS_KNOB_PULL", 'L', A300ButtonState.None),
    };

    public static readonly IReadOnlyList<A300WindowButton> AutopilotButtons = new[]
    {
        new A300WindowButton("A300_AP_SWITCH_1", '1', A300ButtonState.Switch),
        new A300WindowButton("A300_AP_SWITCH_2", '2', A300ButtonState.Switch),
        new A300WindowButton("A300_CWS_PUSH", 'W', A300ButtonState.Lamp),
        new A300WindowButton("A300_VL_BUTTON", 'V', A300ButtonState.Lamp),
        new A300WindowButton("A300_LAND_BUTTON", 'L', A300ButtonState.Lamp),
        new A300WindowButton("A300_ATHR_BUTTON", 'T', A300ButtonState.Lamp),
        new A300WindowButton("A300_ATS_1", '\0', A300ButtonState.Switch),
        new A300WindowButton("A300_ATS_2", '\0', A300ButtonState.Switch),
        new A300WindowButton("A300_CPT_AP_DISCO", 'D', A300ButtonState.None),
        new A300WindowButton("A300_AT_DISCO1", 'O', A300ButtonState.None),
        new A300WindowButton("A300_TOGA_SEL", 'G', A300ButtonState.None),
        new A300WindowButton("A300_BANK_KNOB", 'B', A300ButtonState.Switch),
    };

    /// <summary>The flight director selectors' rows are both named "Flight director" (each on its own
    /// side's panel); in one window they need their side.</summary>
    public static readonly IReadOnlyList<A300WindowSelector> AutopilotSelectors = new[]
    {
        new A300WindowSelector("A300_CPT_FLT_DIR", 'F', "Captain flight director"),
        new A300WindowSelector("A300_FO_FLT_DIR", 'R', "First officer flight director"),
    };

    /// <summary>A button's text, before its Alt-key letter is marked.</summary>
    public static string LabelFor(A300WindowButton button, string rowName, string? action) => WithAction(rowName, action);

    /// <summary>"Heading knob push, aircraft heading": the name, plus the action words unless every one
    /// of them (bar "set") is already in the name.</summary>
    public static string WithAction(string name, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return name;
        string words = ActionWords(action);
        var nameWords = Words(name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool repeats = Words(words).Where(w => !w.Equals("set", StringComparison.OrdinalIgnoreCase)).All(nameWords.Contains);
        return repeats ? name : $"{name}, {words}";
    }

    /// <summary>iniBuilds' upper-case words spoken (<see cref="A300PanelLayout.SpokenWord"/>), starting
    /// lower case unless they start with an abbreviation or a number ("set QNH pressure", "TOGA lock").</summary>
    public static string ActionWords(string action)
    {
        string spoken = A300PanelLayout.SpokenWord(action);
        return spoken.Length > 1 && char.IsUpper(spoken[0]) && char.IsLower(spoken[1])
            ? char.ToLowerInvariant(spoken[0]) + spoken.Substring(1)
            : spoken;
    }

    /// <summary>Whether the open value box must close before <paramref name="requestedTitle"/> opens.
    /// Every A300 value box is one window type (ValueInputForm), and the tracked-window registry keys on
    /// the type, so without this a second hotkey re-showed the first box: Ctrl+H brought back an open
    /// speed box.</summary>
    public static bool ReplacesOpenBox(string? openTitle, string requestedTitle) =>
        openTitle != null && !string.Equals(openTitle, requestedTitle, StringComparison.Ordinal);

    private static IEnumerable<string> Words(string text) =>
        text.Split(new[] { ' ', ',', '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
}

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// The constructors every Learjet panel is built from, plus the cache plumbing.
///
/// A READOUT HOTKEY CAN ONLY READ WHAT IS IN THE CACHE, and the cache is keyed by MSFSBA
/// variable KEY. Batch membership is Continuous AND IsAnnounced; a readout that must be cached
/// is therefore IsAnnounced and then silenced in ProcessSimVarUpdate through
/// SilentCachedReadouts — never by IsAnnounced = false, which would drop it out of the batch.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    /// <summary>Two-position vendor switch: L:GENERIC_&lt;node&gt; 0/1. Continuous + announced.</summary>
    private static void AddSwitch(Dictionary<string, SimVarDefinition> v, string key, string node,
        string display, string off = "Off", string on = "On", string? help = null)
    {
        v[key] = new SimVarDefinition
        {
            Name = "GENERIC_" + node,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on },
            HelpText = help
        };
    }

    /// <summary>Three-position vendor switch: L:GENERIC_Momentary_&lt;node&gt; 0/1/2 in the vendor's own words.</summary>
    private static void AddThreeWay(Dictionary<string, SimVarDefinition> v, string key, string node,
        string display, string pos0, string pos1, string pos2, string? help = null)
    {
        v[key] = new SimVarDefinition
        {
            Name = "GENERIC_Momentary_" + node,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = pos0, [1] = pos1, [2] = pos2 },
            HelpText = help
        };
    }

    /// <summary>Rotary selector: L:XMLVAR_&lt;node&gt;_Position 0..N-1.</summary>
    private static void AddRotary(Dictionary<string, SimVarDefinition> v, string key, string node,
        string display, string[] positions, string? help = null)
    {
        var desc = new Dictionary<double, string>();
        for (int i = 0; i < positions.Length; i++) desc[i] = positions[i];
        v[key] = new SimVarDefinition
        {
            Name = "XMLVAR_" + node + "_Position",
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = desc,
            HelpText = help
        };
    }

    /// <summary>Any other L:var-backed state with described values, written by the panel's own router.</summary>
    private static void AddLVarState(Dictionary<string, SimVarDefinition> v, string key, string lvar,
        string display, Dictionary<double, string> descriptions, string? help = null)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = descriptions,
            HelpText = help
        };
    }

    /// <summary>A stock SimVar-backed switch (bool), written by the panel's router with a conditional toggle.</summary>
    private static void AddSimSwitch(Dictionary<string, SimVarDefinition> v, string key, string simvar,
        string display, string off = "Off", string on = "On", string? help = null, string units = "bool")
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on },
            HelpText = help
        };
    }

    /// <summary>A stock SimVar-backed selector with several described positions, written by the router.</summary>
    private static void AddSimState(Dictionary<string, SimVarDefinition> v, string key, string simvar,
        string display, Dictionary<double, string> descriptions, string units = "number", string? help = null)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = descriptions,
            HelpText = help
        };
    }

    /// <summary>Dimmer / percentage knob: a 0-100 slider over any L:var. Silent — a knob moves continuously.</summary>
    private static void AddDimmer(Dictionary<string, SimVarDefinition> v, string key, string lvar,
        string display, string? help = null, double max = 100)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsSlider = true,
            SliderMin = 0,
            SliderMax = max,
            HelpText = help
        };
    }

    /// <summary>
    /// Momentary push button. Rendered as a button; MainForm routes the press to
    /// HandleUIVariableSet(key, 1), where the panel's router pulses or fires whatever the
    /// button really does. The description pair is what makes MainForm take the button path.
    /// </summary>
    private static void AddButton(Dictionary<string, SimVarDefinition> v, string key, string name,
        string display, string? help = null)
    {
        v[key] = new SimVarDefinition
        {
            Name = name,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Never,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Released", [1] = "Pressed" },
            RenderAsButton = true,
            IsMomentary = true,
            SuppressRestingButtonState = true,
            HelpText = help
        };
    }

    /// <summary>
    /// Typed value entry. The key MUST contain "_SET" — that is what makes MainForm render a
    /// text box and a Set button — and the panel's router receives the parsed number.
    /// <paramref name="currentKey"/> names the cached readout the box is pre-filled from.
    /// </summary>
    private static void AddTyped(Dictionary<string, SimVarDefinition> v, string key, string display,
        string units, string? help = null, string? currentKey = null)
    {
        if (!key.Contains("_SET")) throw new ArgumentException("typed entry keys must contain _SET", nameof(key));
        v[key] = new SimVarDefinition
        {
            Name = key,
            DisplayName = "Set " + display,
            Type = SimVarType.LVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.Never,
            IsAnnounced = false,
            CurrentValueSourceKey = currentKey ?? string.Empty,
            HelpText = help
        };
    }

    /// <summary>Read-only numeric row from an L:var (OnRequest — read when the panel is scanned).</summary>
    private static void AddReadout(Dictionary<string, SimVarDefinition> v, string key, string lvar,
        string display, string units, string format)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = format
        };
    }

    /// <summary>Read-only numeric row from a stock SimVar.</summary>
    private static void AddSimReadout(Dictionary<string, SimVarDefinition> v, string key, string simvar,
        string display, string units, string format)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = format
        };
    }

    /// <summary>Read-only described state (a lamp, a valve, a mode) from an L:var or a SimVar.</summary>
    private static void AddFlag(Dictionary<string, SimVarDefinition> v, string key, string name,
        string display, string off, string on, bool simvar = false)
    {
        v[key] = new SimVarDefinition
        {
            Name = name,
            DisplayName = display,
            Type = simvar ? SimVarType.SimVar : SimVarType.LVar,
            Units = simvar ? "bool" : "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on }
        };
    }

    /// <summary>Read-only described state with more than two positions.</summary>
    private static void AddStateReadout(Dictionary<string, SimVarDefinition> v, string key, string name,
        string display, Dictionary<double, string> descriptions, bool simvar = false, string units = "number")
    {
        v[key] = new SimVarDefinition
        {
            Name = name,
            DisplayName = display,
            Type = simvar ? SimVarType.SimVar : SimVarType.LVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = descriptions
        };
    }

    /// <summary>
    /// A row MSFSBA computes rather than reads: a derived lamp, a fuel line in two units, the
    /// pressurization ladder. The name is the key itself (no such L:var exists, so the sim
    /// answers 0) and TryGetDisplayOverride renders the text from the cache.
    /// </summary>
    private static void AddDerived(Dictionary<string, SimVarDefinition> v, string key, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = key,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };
    }

    /// <summary>
    /// Promote a readout into the batch cache so a hotkey (or a derived lamp) can read it. It is
    /// IsAnnounced only to earn the batch place and is silenced by name in SilentCachedReadouts.
    /// </summary>
    private static void Cache(Dictionary<string, SimVarDefinition> v, string key)
    {
        v[key].UpdateFrequency = UpdateFrequency.Continuous;
        v[key].IsAnnounced = true;
        v[key].ExcludeFromMonitorManager = true;
        SilentCachedReadouts.Add(key);
    }

    /// <summary>Every key that is polled and never spoken.</summary>
    private static readonly HashSet<string> SilentCachedReadouts = new(StringComparer.Ordinal);

    /// <summary>The truthful answer to "will this speak" for the tests.</summary>
    public static IReadOnlyCollection<string> SilentCachedReadoutKeys => SilentCachedReadouts;

    private static bool IsSilentCachedReadout(string key) => SilentCachedReadouts.Contains(key);

    /// <summary>Cache read for the hotkeys and derived lamps. Null until the variable has been delivered.</summary>
    private static double? ReadNow(SimConnectManager simConnect, string key)
        => simConnect.GetCachedVariableValue(key);

    /// <summary>Conditional stock toggle: fires the event only when the SimVar disagrees with the pick.</summary>
    private static void ToggleTo(SimConnectManager simConnect, string simvar, string unit, bool on, string toggleEvent)
        => simConnect.ExecuteCalculatorCode($"(A:{simvar}, {unit}) {(on ? 0 : 1)} == if{{ 1 (>K:{toggleEvent}) }}");

    /// <summary>Vendor B: input event, which also fires the H: the instrument JS listens for.</summary>
    private static void SetInputEvent(SimConnectManager simConnect, string node, double value)
        => simConnect.ExecuteCalculatorCode($"{value:0} (>B:GENERIC_{node}_Set)");

    /// <summary>
    /// Press-and-release of an L:var button: 1 now, 0 after <paramref name="holdMs"/>. The
    /// release runs on the UI thread's timer, because SimConnect writes are not made from a
    /// pool thread anywhere in this app. Every vendor "Momentary" template latches on the 1
    /// and expects the 0 — a press left at 1 is a button held down for the whole session.
    /// </summary>
    private static void Pulse(SimConnectManager simConnect, string lvar, int holdMs = 300)
    {
        simConnect.SetLVar(lvar, 1);
        var t = new System.Windows.Forms.Timer { Interval = Math.Max(50, holdMs) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            simConnect.SetLVar(lvar, 0);
        };
        t.Start();
    }

    /// <summary>Invariant fixed-point for RPN — never default interpolation (see architecture.md).</summary>
    private static string Rpn(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The constructors every C680 panel is built from, plus the cache plumbing. A readout hotkey
/// can only read what is in the batch cache, keyed by MSFSBA variable KEY; batch membership is
/// Continuous AND IsAnnounced, so a cached readout is announced-then-silenced through
/// SilentCachedReadouts, never IsAnnounced = false (which would drop it out of the batch).
/// </summary>
public partial class SkywardC680Definition
{
    /// <summary>Vendor push button / switch: L:&lt;lvar&gt; 0/1, written directly. Continuous + announced.</summary>
    private static void AddSwitch(Dictionary<string, SimVarDefinition> v, string key, string lvar,
        string display, string off = "Off", string on = "On", string? help = null)
        => v[key] = new SimVarDefinition
        {
            Name = lvar, DisplayName = display, Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on }, HelpText = help
        };

    /// <summary>Multi-position selector over an L:var 0..N-1, in the vendor's position words.</summary>
    private static void AddSelector(Dictionary<string, SimVarDefinition> v, string key, string lvar,
        string display, string[] positions, string? help = null)
    {
        var desc = new Dictionary<double, string>();
        for (int i = 0; i < positions.Length; i++) desc[i] = positions[i];
        v[key] = new SimVarDefinition
        {
            Name = lvar, DisplayName = display, Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ValueDescriptions = desc, HelpText = help
        };
    }

    /// <summary>0-100 knob over an L:var. Silent — a knob moves continuously.</summary>
    private static void AddKnob(Dictionary<string, SimVarDefinition> v, string key, string lvar, string display,
        string? help = null, double max = 100)
        => v[key] = new SimVarDefinition
        {
            Name = lvar, DisplayName = display, Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false,
            RenderAsSlider = true, SliderMin = 0, SliderMax = max, HelpText = help
        };

    /// <summary>Stock SimVar-backed switch, written by the router with a conditional toggle.</summary>
    private static void AddSimSwitch(Dictionary<string, SimVarDefinition> v, string key, string simvar,
        string display, string off = "Off", string on = "On", string? help = null, string units = "bool")
        => v[key] = new SimVarDefinition
        {
            Name = simvar, DisplayName = display, Type = SimVarType.SimVar, Units = units,
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on }, HelpText = help
        };

    /// <summary>Stock SimVar-backed selector with described positions, written by the router.</summary>
    private static void AddSimState(Dictionary<string, SimVarDefinition> v, string key, string simvar,
        string display, Dictionary<double, string> descriptions, string units = "number", string? help = null)
        => v[key] = new SimVarDefinition
        {
            Name = simvar, DisplayName = display, Type = SimVarType.SimVar, Units = units,
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ValueDescriptions = descriptions, HelpText = help
        };

    /// <summary>Momentary push button; MainForm routes the press to HandleUIVariableSet(key, 1).</summary>
    private static void AddButton(Dictionary<string, SimVarDefinition> v, string key, string display, string? help = null)
        => v[key] = new SimVarDefinition
        {
            Name = key, DisplayName = display, Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Never, IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Released", [1] = "Pressed" },
            RenderAsButton = true, IsMomentary = true, SuppressRestingButtonState = true, HelpText = help
        };

    /// <summary>Typed value entry. The key MUST contain "_SET" (that renders the text box + Set button).</summary>
    private static void AddTyped(Dictionary<string, SimVarDefinition> v, string key, string display, string units,
        string? help = null, string? currentKey = null)
    {
        if (!key.Contains("_SET")) throw new ArgumentException("typed entry keys must contain _SET", nameof(key));
        v[key] = new SimVarDefinition
        {
            Name = key, DisplayName = "Set " + display, Type = SimVarType.LVar, Units = units,
            UpdateFrequency = UpdateFrequency.Never, IsAnnounced = false,
            CurrentValueSourceKey = currentKey ?? string.Empty, HelpText = help
        };
    }

    /// <summary>Read-only numeric row from an L:var.</summary>
    private static void AddReadout(Dictionary<string, SimVarDefinition> v, string key, string lvar, string display,
        string units, string format)
        => v[key] = new SimVarDefinition
        {
            Name = lvar, DisplayName = display, Type = SimVarType.LVar, Units = units,
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, RenderAsReadOnlyStatus = true, Format = format
        };

    /// <summary>Read-only numeric row from a stock SimVar.</summary>
    private static void AddSimReadout(Dictionary<string, SimVarDefinition> v, string key, string simvar, string display,
        string units, string format)
        => v[key] = new SimVarDefinition
        {
            Name = simvar, DisplayName = display, Type = SimVarType.SimVar, Units = units,
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, RenderAsReadOnlyStatus = true, Format = format
        };

    /// <summary>Read-only two-state row (a lamp, a valve) from an L:var or a SimVar.</summary>
    private static void AddFlag(Dictionary<string, SimVarDefinition> v, string key, string name, string display,
        string off, string on, bool simvar = false)
        => v[key] = new SimVarDefinition
        {
            Name = name, DisplayName = display, Type = simvar ? SimVarType.SimVar : SimVarType.LVar,
            Units = simvar ? "bool" : "number", UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false,
            RenderAsReadOnlyStatus = true, ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on }
        };

    /// <summary>Read-only described state with more than two positions.</summary>
    private static void AddStateReadout(Dictionary<string, SimVarDefinition> v, string key, string name, string display,
        Dictionary<double, string> descriptions, bool simvar = false, string units = "number")
        => v[key] = new SimVarDefinition
        {
            Name = name, DisplayName = display, Type = simvar ? SimVarType.SimVar : SimVarType.LVar, Units = units,
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, RenderAsReadOnlyStatus = true,
            ValueDescriptions = descriptions
        };

    /// <summary>A row MSFSBA computes: the name is the key (no such L:var, the sim answers 0) and TryGetDisplayOverride renders it.</summary>
    private static void AddDerived(Dictionary<string, SimVarDefinition> v, string key, string display)
        => v[key] = new SimVarDefinition
        {
            Name = key, DisplayName = display, Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, RenderAsReadOnlyStatus = true
        };

    /// <summary>
    /// A background state that SPEAKS on change (the master lamps): Continuous + announced with
    /// value-only phrases, so the pilot hears "Master caution" and knows to press Alt+E.
    /// </summary>
    private static void AddAnnounced(Dictionary<string, SimVarDefinition> v, string key, string name, string display,
        string offPhrase, string onPhrase)
        => v[key] = new SimVarDefinition
        {
            Name = name, DisplayName = display, Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, AnnounceValueOnly = true,
            RenderAsReadOnlyStatus = true, ValueDescriptions = new Dictionary<double, string> { [0] = offPhrase, [1] = onPhrase }
        };

    /// <summary>Promote a readout into the batch cache for a hotkey or a derived row; announced to earn the slot, silenced by name.</summary>
    private static void Cache(Dictionary<string, SimVarDefinition> v, string key)
    {
        v[key].UpdateFrequency = UpdateFrequency.Continuous;
        v[key].IsAnnounced = true;
        v[key].ExcludeFromMonitorManager = true;
        SilentCachedReadouts.Add(key);
    }

    private static readonly HashSet<string> SilentCachedReadouts = new(StringComparer.Ordinal);

    /// <summary>The truthful answer to "will this speak" for the tests.</summary>
    public static IReadOnlyCollection<string> SilentCachedReadoutKeys => SilentCachedReadouts;

    private static bool IsSilentCachedReadout(string key) => SilentCachedReadouts.Contains(key);

    /// <summary>Cache read for the hotkeys and derived rows. Null until the variable has been delivered.</summary>
    private static double? ReadNow(SimConnectManager simConnect, string key) => simConnect.GetCachedVariableValue(key);

    /// <summary>Conditional stock toggle: fires the event only when the SimVar disagrees with the pick.</summary>
    private static void ToggleTo(SimConnectManager simConnect, string simvar, string unit, bool on, string toggleEvent)
        => simConnect.ExecuteCalculatorCode($"(A:{simvar}, {unit}) {(on ? 0 : 1)} == if{{ 1 (>K:{toggleEvent}) }}");

    /// <summary>
    /// Press-and-release of an L:var button: 1 now, 0 after <paramref name="holdMs"/> on the UI
    /// thread's timer (SimConnect writes are never made from a pool thread in this app). A press
    /// left at 1 is a button held down for the whole session.
    /// </summary>
    private static void Pulse(SimConnectManager simConnect, string lvar, int holdMs = 300)
    {
        simConnect.SetLVar(lvar, 1);
        var t = new System.Windows.Forms.Timer { Interval = Math.Max(50, holdMs) };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); simConnect.SetLVar(lvar, 0); };
        t.Start();
    }

    /// <summary>Invariant fixed-point for RPN — never default interpolation (see architecture.md).</summary>
    private static string Rpn(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string N(double? v, string format = "0")
        => v == null ? "not read" : v.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The definition's own copy of every delivered value, for derived rows and hotkeys without a SimConnectManager.</summary>
    private readonly Dictionary<string, double> _live = new(StringComparer.Ordinal);
    private double Live(string key) => _live.TryGetValue(key, out var x) ? x : 0;
    private bool Has(string key) => _live.ContainsKey(key);
}

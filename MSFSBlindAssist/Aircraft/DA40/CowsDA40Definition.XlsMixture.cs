using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Mixture and Propeller (DA40-XLS).
///
/// The panel the XLS exists for. The Lycoming is leaned by hand against its exhaust and
/// cylinder head temperatures, and the G1000 draws those as eight BARS with no digits and a
/// "lean assist" that paints a line where each bar peaked. A blind pilot gets the numbers
/// the bars are drawn from - the plugin binds <c>DISP_EGT:n</c> / <c>DISP_CHT:n</c> and
/// nothing else - and the assist as words. Every figure here was measured on the live
/// aircraft through a lean at run-up power and a static full-throttle run
/// (docs/da40-xls-variables.md, Mixture and Propeller):
///
///  • The lean assist is two numbers per cylinder: the highest EGT seen since the Assist
///    softkey went on (<c>DISP_LEAN_PEAK:n</c>) and how far below it the cylinder is now
///    (<c>DISP_LEAN_DELTA:n</c>). Measured: peaks of 1484 F at 14.5-15.5 to 1, and past
///    that two cylinders dropped 350-500 F as the engine went rough. The POH's method is
///    "lean until the FIRST EGT peaks", so the first cylinder to fall <see cref="PeakedAtF"/>
///    below its peak is CALLED, once per assist session; after that the row carries all four.
///  • The red box (<see cref="DA40RedBox"/>) is recomputed from each cylinder's heat and
///    air/fuel ratio, never read from <c>DAMAGE_REDBOX_ITS:n</c>, which holds its last value
///    when the box closes. At run-up power (30.8 kW) it cannot open; at full throttle
///    (47.8 kW) it spans 13.4-15.3 to 1 and full rich sits outside it.
///  • Plug fouling is per PLUG, eight of them, and the worst is named. A saved state can
///    carry a plug over the model's own 100 cap (137 read live), so the figure is clamped.
///  • Shock cooling and cylinder damage are the model's own thresholds
///    (<see cref="DA40CylinderState"/>). ⚠️ With the Engine Damage option OFF none of the
///    damage accumulators run, and the rows say so rather than reporting "clear" as though
///    the engine were being looked after.
///  • Detonation is NOT here. This build's detonation block is commented out of the model
///    and nothing consumes it (see <see cref="DA40CylinderState"/>).
///
/// The mixture and propeller LEVERS stay on Power and Levers - one control, one panel. What
/// is here is the two things done TO them: the aircraft's own "set best mixture" (12.5 to 1,
/// measured as the binding's own walk) and the run-up's propeller cycle, which the NG has no
/// equivalent of - the lever to minimum until the governor has pulled ~300 rpm (2212 → 1560
/// in two seconds, measured), then back, so the hub's oil charge (<c>OP_PROP_OIL_PRIME</c>,
/// 0 → 5.1) is proven before take-off.
///
/// Temperatures are declared in FAHRENHEIT, which is what the gauge draws, and rendered in
/// the pilot's G1000 unit; the arcs are looked up on the raw Fahrenheit value.
/// </summary>
public partial class CowsDA40Definition
{
    private const string MixturePanel = "Mixture and Propeller";

    /// <summary>A cylinder this far below its peak has peaked - the assist's own line.</summary>
    public const double PeakedAtF = 10;

    /// <summary>The prop cycle is done when the rpm has fallen this far, or after the timeout.</summary>
    public const double PropCycleDropRpm = 300;
    public const int PropCycleTimeoutMs = 5000;

    /// <summary>The hub oil charge at which the governor has full authority (Logic 1340ff).</summary>
    public const double PropPrimedAt = 5;

    private static Dictionary<string, SimVarDefinition> BuildXlsMixtureVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_XLS_BEST_MIXTURE"] = new SimVarDefinition
        {
            Name = "DA40_XLS_BEST_MIXTURE",
            DisplayName = "Set Best Mixture",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        v["DA40_XLS_PROP_CYCLE"] = new SimVarDefinition
        {
            Name = "DA40_XLS_PROP_CYCLE",
            DisplayName = "Cycle Propeller",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // ---------- Lean assist ----------

        // The row is the mute for the "cylinder N peaked" call-out. Silent as a number.
        v["DA40_XLS_LEAN_ASSIST"] = new SimVarDefinition
        {
            Name = "DISP_LEAN_ASSIST",
            DisplayName = "Lean Assist",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true
        };
        for (int n = 1; n <= DA40CylinderState.CylinderCount; n++)
        {
            v[$"DA40_XLS_LEAN_PEAK_{n}"] = Capture($"DISP_LEAN_PEAK:{n}", $"Cylinder {n} Peak EGT");
            v[$"DA40_XLS_LEAN_DELTA_{n}"] = Capture($"DISP_LEAN_DELTA:{n}", $"Cylinder {n} Below Peak");
        }

        // ---------- Temperatures: what the bars are drawn from ----------

        v["DA40_XLS_EGT_HOT"] = Temperature("DISP_LEAN_HOTEST", "Hottest EGT");
        v["DA40_XLS_CHT_HOT"] = Temperature("DISP_CHT_HOT", "Hottest Cylinder Head");
        v["DA40_XLS_CHT_HOT_CYL"] = Capture("DISP_CHT_HOT_CYL", "Hottest Cylinder Number");
        for (int n = 1; n <= DA40CylinderState.CylinderCount; n++)
        {
            v[$"DA40_XLS_EGT_{n}"] = Temperature($"DISP_EGT:{n}", $"Cylinder {n} EGT");
            v[$"DA40_XLS_CHT_{n}"] = Temperature($"DISP_CHT:{n}", $"Cylinder {n} Head");
        }

        // The spread between the hottest and coolest exhaust, derived from the four above.
        // Hung on the model's own per-cylinder EGT variation, a real variable; the number
        // shown is the spread.
        v["DA40_XLS_EGT_SPREAD"] = new SimVarDefinition
        {
            Name = "CYL_SPREAD_EGT:1",
            // Named to match cylinders 2-4, which live with the rest of the engine's
            // per-airframe variation on the Engine Variation panel.
            DisplayName = "Cylinder 1 EGT Variation",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        // ---------- The states the mixture puts the engine in ----------

        // Each row hangs on cylinder 1's input; cylinders 2-4 are captured silently.
        v["DA40_XLS_RED_BOX"] = Row("TB_FF_MIXTURE:1", "Red Box");
        v["DA40_XLS_FOULING"] = Row("DAMAGE_MAG_FOUL:1R", "Plug Fouling");
        v["DA40_XLS_SHOCK_COOLING"] = Row("CHT_TEMP_INC:1", "Shock Cooling");
        v["DA40_XLS_CYL_HEALTH"] = Row("DAMAGE_CYL:1", "Cylinder Health");

        for (int n = 1; n <= DA40CylinderState.CylinderCount; n++)
        {
            if (n > 1) v[$"DA40_XLS_AFR_{n}"] = Capture($"TB_FF_MIXTURE:{n}", $"Cylinder {n} Air Fuel");
            v[$"DA40_XLS_HEAT_{n}"] = Capture($"CHT_HEAT_OUTPUT_KW:{n}", $"Cylinder {n} Heat");
            if (n > 1) v[$"DA40_XLS_COOLING_{n}"] = Capture($"CHT_TEMP_INC:{n}", $"Cylinder {n} Cooling Rate");
            if (n > 1) v[$"DA40_XLS_DAMAGE_{n}"] = Capture($"DAMAGE_CYL:{n}", $"Cylinder {n} Damage");
            v[$"DA40_XLS_FOUL_{n}L"] = Capture($"DAMAGE_MAG_FOUL:{n}L", $"Cylinder {n} Left Plug");
            if (n > 1) v[$"DA40_XLS_FOUL_{n}R"] = Capture($"DAMAGE_MAG_FOUL:{n}R", $"Cylinder {n} Right Plug");
        }

        // ---------- Propeller ----------

        // ---------- The regime the mixture lever is in ----------

        // ⚠️ AUTOMIXTURE IS NOT A COCKPIT SWITCH AND NOT SOMETHING THE PILOT SETS HERE. The
        // model DETECTS the simulator's own auto-mixture assistance —
        // `(A:RECIP MIXTURE RATIO:1) * 100 == 9`, or the package's AUTOMIXTURE_FORCE — and
        // ramps this variable 0 to 1 in fifths, dropping it to 0 in one step when the
        // assistance goes away (Logic 1805ff). So it is a READ-ONLY row: the setting lives
        // in the simulator's assistance options, which MSFSBA does not own.
        //
        // It matters because it changes the whole fuel regime, and nothing else says so:
        //
        //  • The mixture VALVE is bypassed. With it off the lever drives MIXTURE_VALVE and
        //    the servo meters fuel; with it on the lever picks an air/fuel TARGET
        //    (AUTOMIXTURE_TARGET: full forward 10 to 1, cut-off 100, a straight line
        //    between) and the model delivers it (Logic 341-437).
        //  • THE ENGINE CANNOT BE FLOODED. Every cylinder's outside charge is clamped at
        //    2 g every tick (Logic 1821ff), which is under the flooded line, so the whole
        //    hot-start problem the Priming panel exists for cannot arise. Priming is still
        //    required — the clamp is a ceiling, not a prime.
        //  • Set Best Mixture snaps to 72 % instead of walking to 12.5 to 1.
        v["DA40_XLS_AUTOMIXTURE"] = new SimVarDefinition
        {
            Name = "AUTOMIXTURE",
            DisplayName = "Automixture",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            // A DESCRIBED STATE, not a number - which is what earns it a call-out under
            // this aeroplane's own rule and a Ctrl+M row to mute it with. The ramp's
            // intermediate fifths match no description and are never spoken:
            // NoteAutomixtureChange consumes every update and speaks only the edge.
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On"
            }
        };

        v["DA40_XLS_PROP_PRIME"] = new SimVarDefinition
        {
            Name = "OP_PROP_OIL_PRIME",
            DisplayName = "Propeller Hub Oil",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        return v;
    }

    /// <summary>A Fahrenheit gauge figure: batched so the hotkeys can read it, silent, rendered in the pilot's unit.</summary>
    private static SimVarDefinition Temperature(string name, string displayName) => new()
    {
        Name = name,
        DisplayName = displayName,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        RenderAsReadOnlyStatus = true,
        ExcludeFromMonitorManager = true,
        Units = "fahrenheit",
        Format = "F0"
    };

    /// <summary>A derived-state row: batched (its call-out is spoken from the chain), a Ctrl+M mute of its own.</summary>
    private static SimVarDefinition Row(string name, string displayName) => new()
    {
        Name = name,
        DisplayName = displayName,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        RenderAsReadOnlyStatus = true
    };

    private static readonly List<string> XlsMixtureControls = new()
    {
        "DA40_XLS_BEST_MIXTURE",
        "DA40_XLS_PROP_CYCLE"
    };

    // The assist first - it is what a pilot leaning is listening for - then the two
    // hottest, the eight bars, the mixture itself, the states, then the propeller.
    private static readonly List<string> XlsMixtureDisplay = new()
    {
        // First, because it frames every row under it: with automixture on the lever is
        // setting a ratio rather than a valve, and the engine cannot be flooded.
        "DA40_XLS_AUTOMIXTURE",
        "DA40_XLS_LEAN_ASSIST",
        "DA40_XLS_EGT_HOT",
        "DA40_XLS_CHT_HOT",
        "DA40_XLS_EGT_1", "DA40_XLS_EGT_2", "DA40_XLS_EGT_3", "DA40_XLS_EGT_4",
        "DA40_XLS_EGT_SPREAD",
        "DA40_XLS_CHT_1", "DA40_XLS_CHT_2", "DA40_XLS_CHT_3", "DA40_XLS_CHT_4",
        "DA40_XLS_AFR",
        "DA40_XLS_FUEL_FLOW",
        "DA40_XLS_RED_BOX",
        "DA40_XLS_FOULING",
        "DA40_XLS_SHOCK_COOLING",
        "DA40_XLS_CYL_HEALTH",
        "DA40_XLS_RPM",
        "DA40_XLS_TARGET_RPM",
        "DA40_XLS_MAP",
        "DA40_XLS_PROP_PRIME"
    };

    /// <summary>Every batched key here is silent as a number; the states speak instead.</summary>
    public static readonly string[] XlsMixtureCapturedKeys = BuildXlsMixtureCapturedKeys();

    private static string[] BuildXlsMixtureCapturedKeys()
    {
        var keys = new List<string>
        {
            "DA40_XLS_LEAN_ASSIST", "DA40_XLS_EGT_HOT", "DA40_XLS_CHT_HOT", "DA40_XLS_CHT_HOT_CYL",
            "DA40_XLS_RED_BOX", "DA40_XLS_FOULING", "DA40_XLS_SHOCK_COOLING", "DA40_XLS_CYL_HEALTH"
        };
        for (int n = 1; n <= DA40CylinderState.CylinderCount; n++)
        {
            keys.Add($"DA40_XLS_LEAN_PEAK_{n}"); keys.Add($"DA40_XLS_LEAN_DELTA_{n}");
            keys.Add($"DA40_XLS_EGT_{n}"); keys.Add($"DA40_XLS_CHT_{n}");
            keys.Add($"DA40_XLS_HEAT_{n}"); keys.Add($"DA40_XLS_FOUL_{n}L");
            if (n > 1) { keys.Add($"DA40_XLS_AFR_{n}"); keys.Add($"DA40_XLS_COOLING_{n}"); keys.Add($"DA40_XLS_DAMAGE_{n}"); keys.Add($"DA40_XLS_FOUL_{n}R"); }
        }
        return keys.ToArray();
    }

    // ==================================================================================
    // Controls
    // ==================================================================================

    private System.Windows.Forms.Timer? _propCycleTimer;
    private double _propCycleReturn = 100;
    private double _propCycleStartRpm;
    private double _propCycleMinRpm;
    private DateTime _propCycleStarted;
    private SimConnectManager? _propCycleSim;
    private ScreenReaderAnnouncer? _propCycleAnnouncer;

    private bool HandleXlsMixtureSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_XLS_BEST_MIXTURE":
                // The binding toggles this flag; the Logic walks INPUT_MIXTURE toward an
                // average of 12.5 to 1 while it is set, and clears it on arrival. Written
                // directly (not the K-event) for the same reason as the auto-start. A
                // second press mid-walk is byte-identical, so unique.
                simConnect.ExecuteCalculatorCodeUnique("1 (>L:MIXTURE_SET_BEST)");
                return true;

            case "DA40_XLS_PROP_CYCLE":
                StartPropCycle(simConnect, announcer);
                return true;
        }
        return false;
    }

    /// <summary>
    /// Lever to minimum, wait for the governor to pull <see cref="PropCycleDropRpm"/> (or the
    /// timeout - a cold, unprimed hub responds slowly, which is the reason for the item),
    /// then the lever back to where the pilot had it. Speaks what the cycle did, once.
    /// </summary>
    private void StartPropCycle(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (_propCycleTimer != null) { announcer.AnnounceImmediate("Propeller cycle already running"); return; }
        if (_magRpmNow < DA40MagnetoCheck.RunningRpm) { announcer.AnnounceImmediate("Engine not running"); return; }

        _propCycleReturn = simConnect.GetCachedVariableValue("DA40_XLS_PROP_SET") ?? 100;
        _propCycleStartRpm = _magRpmNow;
        _propCycleMinRpm = _magRpmNow;
        _propCycleStarted = DateTime.UtcNow;
        _propCycleSim = simConnect;
        _propCycleAnnouncer = announcer;

        simConnect.ExecuteCalculatorCodeUnique("0 (>L:INPUT_PROPELLER)");

        _propCycleTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _propCycleTimer.Tick += OnPropCycleTick;
        _propCycleTimer.Start();
    }

    private void OnPropCycleTick(object? sender, EventArgs e)
    {
        _propCycleMinRpm = Math.Min(_propCycleMinRpm, _magRpmNow);
        bool dropped = _propCycleStartRpm - _magRpmNow >= PropCycleDropRpm;
        bool timedOut = (DateTime.UtcNow - _propCycleStarted).TotalMilliseconds >= PropCycleTimeoutMs;
        if (!dropped && !timedOut) return;

        StopPropCycle();
        _propCycleSim?.ExecuteCalculatorCodeUnique(
            $"{_propCycleReturn.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} (>L:INPUT_PROPELLER)");
        _propCycleAnnouncer?.AnnounceImmediate(dropped
            ? $"Propeller cycled, {_propCycleStartRpm:F0} down to {_propCycleMinRpm:F0}, lever back"
            : $"Propeller did not respond, {_propCycleStartRpm:F0} down to {_propCycleMinRpm:F0} in five seconds, lever back");
    }

    private void StopPropCycle()
    {
        if (_propCycleTimer == null) return;
        _propCycleTimer.Stop();
        _propCycleTimer.Tick -= OnPropCycleTick;
        _propCycleTimer.Dispose();
        _propCycleTimer = null;
    }

    // ==================================================================================
    // The captured state, and what is spoken from it
    // ==================================================================================

    private bool _leanAssistOn;
    private readonly double[] _leanPeak = new double[DA40CylinderState.CylinderCount];
    private readonly double[] _leanDelta = new double[DA40CylinderState.CylinderCount];
    private readonly bool[] _leanPeakSpoken = new bool[DA40CylinderState.CylinderCount];
    private readonly double[] _egt = new double[DA40CylinderState.CylinderCount];
    private readonly double[] _cht = new double[DA40CylinderState.CylinderCount];
    private int _chtHotCyl;
    private readonly double[] _afr = new double[DA40CylinderState.CylinderCount];
    private readonly double[] _heatKw = new double[DA40CylinderState.CylinderCount];
    private readonly double[] _cooling = new double[DA40CylinderState.CylinderCount];
    private readonly double[] _damage = new double[DA40CylinderState.CylinderCount];
    private readonly double[] _plugs = new double[DA40CylinderState.CylinderCount * 2];
    private bool? _damageEnabled;
    private string? _redBoxSpoken;
    private double _foulWorstSpoken = -1;
    private bool _shockSpoken;
    private double[]? _damageSpoken;

    /// <summary>
    /// Captures the Mixture panel's inputs and speaks its states on their crossings.
    /// Returns false always: the numbers are silent and the shared keys keep their own.
    /// </summary>
    private bool NoteXlsMixtureChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (varKey == "DA40_OPT_DAMAGE") { _damageEnabled = value > 0.5; return false; }
        if (!varKey.StartsWith("DA40_XLS_", StringComparison.Ordinal)) return false;

        int n;
        switch (varKey)
        {
            case "DA40_XLS_LEAN_ASSIST":
                bool on = value > 0.5;
                if (on != _leanAssistOn) { _leanAssistOn = on; Array.Clear(_leanPeakSpoken); }
                return false;
            case "DA40_XLS_CHT_HOT_CYL": _chtHotCyl = (int)Math.Round(value); return false;
            case "DA40_XLS_RED_BOX": _afr[0] = value; NoteRedBox(announcer); return false;
            case "DA40_XLS_FOULING": _plugs[0] = value; NoteFouling(announcer); return false;
            case "DA40_XLS_SHOCK_COOLING": _cooling[0] = value; NoteShockCooling(announcer); return false;
            case "DA40_XLS_CYL_HEALTH": _damage[0] = value; NoteDamage(announcer); return false;
        }

        if (TryCyl(varKey, "DA40_XLS_LEAN_PEAK_", out n)) { _leanPeak[n] = value; return false; }
        if (TryCyl(varKey, "DA40_XLS_LEAN_DELTA_", out n)) { _leanDelta[n] = value; NotePeak(n, announcer); return false; }
        if (TryCyl(varKey, "DA40_XLS_EGT_", out n)) { _egt[n] = value; return false; }
        if (TryCyl(varKey, "DA40_XLS_CHT_", out n)) { _cht[n] = value; return false; }
        if (TryCyl(varKey, "DA40_XLS_AFR_", out n)) { _afr[n] = value; NoteRedBox(announcer); return false; }
        if (TryCyl(varKey, "DA40_XLS_HEAT_", out n)) { _heatKw[n] = value; NoteRedBox(announcer); return false; }
        if (TryCyl(varKey, "DA40_XLS_COOLING_", out n)) { _cooling[n] = value; NoteShockCooling(announcer); return false; }
        if (TryCyl(varKey, "DA40_XLS_DAMAGE_", out n)) { _damage[n] = value; NoteDamage(announcer); return false; }
        if (TryPlug(varKey, out int p)) { _plugs[p] = value; NoteFouling(announcer); return false; }
        return false;
    }

    private static bool TryCyl(string key, string prefix, out int index)
    {
        index = -1;
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string rest = key.Substring(prefix.Length);
        if (!int.TryParse(rest, out int n) || n < 1 || n > DA40CylinderState.CylinderCount) return false;
        index = n - 1;
        return true;
    }

    /// <summary>DA40_XLS_FOUL_{n}{L|R} → the plug order 1R 1L 2R 2L 3R 3L 4R 4L.</summary>
    private static bool TryPlug(string key, out int index)
    {
        index = -1;
        const string prefix = "DA40_XLS_FOUL_";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Length != prefix.Length + 2) return false;
        int n = key[prefix.Length] - '0';
        char side = key[prefix.Length + 1];
        if (n < 1 || n > DA40CylinderState.CylinderCount || (side != 'L' && side != 'R')) return false;
        index = (n - 1) * 2 + (side == 'R' ? 0 : 1);
        return true;
    }

    /// <summary>"Cylinder 3 peaked at 1484 degrees fahrenheit" - once per cylinder per assist session.</summary>
    private void NotePeak(int n, ScreenReaderAnnouncer announcer)
    {
        if (!_leanAssistOn || _leanPeakSpoken[n] || _leanDelta[n] < PeakedAtF) return;
        _leanPeakSpoken[n] = true;
        if (Muted("DA40_XLS_LEAN_ASSIST")) return;
        announcer.Announce($"Cylinder {n + 1} peaked at {Fahrenheit(_leanPeak[n])}");
    }

    private void NoteRedBox(ScreenReaderAnnouncer announcer)
    {
        string state = DA40RedBox.Describe(_heatKw, _afr);
        if (_redBoxSpoken == null) { _redBoxSpoken = state; return; }   // baseline-first
        if (state == _redBoxSpoken) return;
        _redBoxSpoken = state;
        if (Muted("DA40_XLS_RED_BOX")) return;
        announcer.AnnounceImmediate(state == "Clear" ? "Red box clear" : state + (_damageEnabled == false ? ", engine damage is off" : ""));
    }

    private void NoteFouling(ScreenReaderAnnouncer announcer)
    {
        int w = DA40CylinderState.WorstPlug(_plugs);
        double worst = Math.Min(100, _plugs[w]);
        if (_foulWorstSpoken < 0) { _foulWorstSpoken = worst; return; }
        string? callout = DA40CylinderState.FoulingCallout(_foulWorstSpoken, worst, DA40CylinderState.PlugName(w));
        _foulWorstSpoken = Math.Max(_foulWorstSpoken, worst);
        if (callout == null || Muted("DA40_XLS_FOULING")) return;
        announcer.Announce(callout);
    }

    private void NoteShockCooling(ScreenReaderAnnouncer announcer)
    {
        string state = DA40CylinderState.DescribeShockCooling(_cooling);
        bool shocking = state != "None";
        if (shocking == _shockSpoken) return;
        _shockSpoken = shocking;
        if (!shocking || Muted("DA40_XLS_SHOCK_COOLING")) return;
        announcer.AnnounceImmediate(state);
    }

    /// <summary>A fall of five points on any cylinder - damage happens during something.</summary>
    private void NoteDamage(ScreenReaderAnnouncer announcer)
    {
        if (_damageSpoken == null) { _damageSpoken = (double[])_damage.Clone(); return; }
        for (int i = 0; i < _damage.Length; i++)
        {
            if (_damage[i] - _damageSpoken[i] < 5) continue;
            _damageSpoken[i] = _damage[i];
            if (Muted("DA40_XLS_CYL_HEALTH")) continue;
            announcer.Announce(_damage[i] > DA40CylinderState.DeadDamage
                ? $"Cylinder {i + 1} dead"
                : $"Cylinder {i + 1} damaged, {100 - _damage[i]:F0} percent");
        }
    }

    private string Fahrenheit(double f) => TryUnitText("fahrenheit", f, "F0", out string t) ? t : $"{f:F0}";

    private bool TryGetXlsMixtureDisplayOverride(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            case "DA40_XLS_LEAN_ASSIST":
                displayText = DescribeLeanAssist();
                return true;

            case "DA40_XLS_CHT_HOT":
                displayText = (_chtHotCyl >= 1 ? $"Cylinder {_chtHotCyl}, " : "")
                    + DA40InstrumentBands.Annotate(varKey, value, Fahrenheit(value));
                return true;

            case "DA40_XLS_EGT_SPREAD":
                double max = _egt.Max(), min = _egt.Min();
                displayText = max <= 0 ? "Not available yet" : $"{Fahrenheit(max - min)} between the hottest and coolest";
                return true;

            case "DA40_XLS_RED_BOX":
                displayText = DA40RedBox.Describe(_heatKw, _afr)
                    + (_damageEnabled == false ? " (engine damage is off, nothing accrues)" : "");
                return true;

            case "DA40_XLS_FOULING":
                var clamped = _plugs.Select(p => Math.Min(100, p)).ToArray();
                displayText = DA40CylinderState.DescribeFouling(clamped)
                    + (_damageEnabled == false ? " (engine damage is off)" : "");
                return true;

            case "DA40_XLS_SHOCK_COOLING":
                displayText = DA40CylinderState.DescribeShockCooling(_cooling);
                return true;

            case "DA40_XLS_CYL_HEALTH":
                displayText = DA40CylinderState.DescribeHealth(_damage)
                    + (_damageEnabled == false ? " (engine damage is off)" : "");
                return true;

            case "DA40_XLS_PROP_PRIME":
                displayText = value >= PropPrimedAt ? "Primed" : $"{value:F1} of 5, cycle the propeller";
                return true;

            case "DA40_XLS_AUTOMIXTURE":
                displayText = DescribeAutomixture(value);
                return true;
        }

        displayText = string.Empty;
        return false;
    }

    /// <summary>
    /// ⚠️ THE VALUE RAMPS, SO THE EDGE IS WHAT IS SPOKEN, NOT THE NUMBER. The model adds
    /// 0.2 per tick until it reaches 1 (Logic 1805ff), so the generic announcer would have
    /// read five steps of a number that means one thing. It is a described state, not a
    /// quantity, and the two states are the only two worth hearing.
    /// </summary>
    public static string DescribeAutomixture(double value)
        => value >= 1 ? "On" : value <= 0 ? "Off" : "Engaging";

    private bool? _automixtureSpoken;

    /// <summary>
    /// Speaks the settled edge. Baseline-first: the first reading is recorded and not
    /// spoken, because a pilot loading the aeroplane into an assistance setting they chose
    /// has not had anything change. It returns TRUE either way — the generic announcer must
    /// never read the ramp.
    /// </summary>
    private bool NoteAutomixtureChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_XLS_AUTOMIXTURE") return false;

        bool? now = value >= 1 ? true : value <= 0 ? false : (bool?)null;
        if (now == null) return true;                    // mid-ramp: nothing settled yet
        if (_automixtureSpoken == now) return true;

        bool first = _automixtureSpoken == null;
        _automixtureSpoken = now;
        if (first || Muted("DA40_XLS_AUTOMIXTURE")) return true;

        announcer.Announce(now == true ? "Automixture on" : "Automixture off");
        return true;
    }

    private string DescribeLeanAssist()
    {
        // ⚠️ NAME THE SOFTKEY BY NUMBER. Lean assist is a SOFTKEY, so by this project's own
        // rule it does not get a panel switch - it belongs to the display window - and that
        // makes this row the only place a blind pilot learns where it is. "the Assist
        // softkey" left them walking twelve keys; it is number 10 on the XLS Engine page,
        // pressed live and watched toggle L:DISP_LEAN_ASSIST 1 to 0.
        if (!_leanAssistOn)
            return "Off - MFD Engine page, softkey 10, Assist";
        var parts = new List<string>();
        for (int i = 0; i < DA40CylinderState.CylinderCount; i++)
        {
            if (_leanPeak[i] <= 0) continue;
            parts.Add(_leanDelta[i] < PeakedAtF
                ? $"cylinder {i + 1} at peak {Fahrenheit(_leanPeak[i])}"
                : $"cylinder {i + 1} {Fahrenheit(_leanDelta[i])} below its peak of {Fahrenheit(_leanPeak[i])}");
        }
        return parts.Count == 0 ? "On, waiting for a peak" : "On: " + string.Join("; ", parts);
    }

    /// <summary>Forgets the captured state and stops a running prop cycle. Called when the aircraft is switched away.</summary>
    private void ResetXlsMixtureState()
    {
        StopPropCycle();
        _leanAssistOn = false;
        Array.Clear(_leanPeak); Array.Clear(_leanDelta); Array.Clear(_leanPeakSpoken);
        Array.Clear(_egt); Array.Clear(_cht); _chtHotCyl = 0;
        Array.Clear(_afr); Array.Clear(_heatKw); Array.Clear(_cooling); Array.Clear(_damage); Array.Clear(_plugs);
        _damageEnabled = null;
        _redBoxSpoken = null;
        _foulWorstSpoken = -1;
        _shockSpoken = false;
        _damageSpoken = null;
        _automixtureSpoken = null;
    }
}

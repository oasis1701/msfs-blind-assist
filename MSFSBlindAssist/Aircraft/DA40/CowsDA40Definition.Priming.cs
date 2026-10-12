using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Priming (DA40-XLS).
///
/// The injected Lycoming has no primer knob. Priming is the idle/starting jet loading the
/// induction with the pump on and the mixture forward (POH p.6), and the fuel sits OUTSIDE
/// the cylinders until the crank draws it in. A sighted pilot watches the fuel-flow gauge
/// for "above 4.5 gph" and, if the option is on, the priming-assist bar on the EIS. Neither
/// tells them what this panel does: how many grams are in the cylinders, how many the model
/// wants, and whether it has tipped over into flooded.
///
/// Every number here is the aircraft's own (docs/da40-xls-variables.md, Priming):
///
///  • <c>ENG_FUEL_OUTSIDE_CYL_GRAM:1-4</c> is the charge per cylinder, always live.
///  • Required is 1.5 / atomisation / evaporation per cylinder; flooded is 1.6 / evaporation
///    / atomisation — the autostart script's own "skip if flooded" test, any cylinder over.
///    A six percent window. <see cref="DA40Priming"/> transcribes both.
///  • The vendor's priming-assist pair (<c>ASSIST_PRIME_*</c>) is the same sum, but it
///    computes only with the MFD option on, the pump on and the engine stopped — and its
///    PERCENTAGE counts the lines against a fixed 10.5 g of fill, so after a mixture-cut
///    shutdown it read 78 % while the cylinders held three times their charge. So the state
///    row is derived from the cylinders directly, works with the option off, and says the
///    two numbers rather than a percentage; the vendor's gauge is shown beside it, labelled.
///  • Vapour forming in a hot line is <c>ENG_FUEL_LINE_BOIL:1-4</c> (indexed; the bare name
///    is a phantom), random once a line passes 100 on the model's scale. The electric pump
///    is the model's cure, exactly as in the aeroplane.
///
/// ⚠️ WHILE THE ENGINE RUNS THE CYLINDERS HOLD ~0.6-1.3 g, WHICH IS OVER THE FLOODED LINE.
/// Flooded is a STOPPED-engine judgement; the row says "Engine running" above 400 rpm and the
/// call-outs are gated the same way, or this panel would shout Flooded through the cruise.
///
/// THE STATE IS SPOKEN ON THE CROSSING, NOT AFTER A SETTLE. Measured hot: the jet loaded
/// 2.5 g per cylinder per second, so primed-to-flooded took under a fifth of a second and a
/// settle timer would only ever have said "flooded". A pilot cannot prime this engine hot by
/// timing; the honest answer is to say so and give the AFM's own remedy (4.5 (c)): pump off,
/// mixture fully aft, throttle mid, crank until it coughs — 4.8 g cleared in one second and
/// 32 g in 4.7, measured — then mixture forward. Flooded is recoverable and the panel says so.
/// </summary>
public partial class CowsDA40Definition
{
    private const string PrimingPanel = "Priming";

    private static Dictionary<string, SimVarDefinition> BuildPrimingVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Control ----------

        // The POH's reset: "removes all the fuel in the engine and cools the fuel lines".
        // Not on the Simulation → Reset panel, which carries only the NG-era resets, so it
        // is not a duplicate. The AFM crank-it-out is the other remedy and is in the help.
        v["DA40_PRIME_CLEAR"] = new SimVarDefinition
        {
            Name = "DA40_PRIME_CLEAR",
            DisplayName = "Clear Flooding",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // ---------- The cylinders, and what classifies them ----------

        // Cylinder 1 is the displayed row: its override renders the whole engine's state from
        // the four captured charges. The other three and the two coefficients are captured
        // silently. ⚠️ ONE batched key per SimVar name - the vapour row below reuses line 1's
        // boil key for the same reason.
        v["DA40_PRIME_CYL_1"] = new SimVarDefinition
        {
            Name = "ENG_FUEL_OUTSIDE_CYL_GRAM:1",
            DisplayName = "Priming",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true
            // Stays in the Monitor Manager: this row is the Ctrl+M mute for the Primed /
            // Flooded call-outs, which speak from the crossing and check it themselves.
        };

        for (int n = 2; n <= DA40Priming.Cylinders; n++)
        {
            v[$"DA40_PRIME_CYL_{n}"] = Capture($"ENG_FUEL_OUTSIDE_CYL_GRAM:{n}", $"Cylinder {n} Fuel");
        }
        for (int n = 1; n <= DA40Priming.Cylinders; n++)
        {
            v[$"DA40_PRIME_ATOM_{n}"] = Capture($"ENG_ATOMISE_CYL:{n}", $"Cylinder {n} Atomisation");
        }
        v["DA40_PRIME_EVAP"] = Capture("ENG_INT_MANI_EVAP:1", "Manifold Evaporation");

        // ---------- Vapour ----------

        v["DA40_PRIME_BOIL_1"] = new SimVarDefinition
        {
            Name = "ENG_FUEL_LINE_BOIL:1",
            DisplayName = "Vapour",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true
        };
        for (int n = 2; n <= DA40Priming.Cylinders; n++)
        {
            v[$"DA40_PRIME_BOIL_{n}"] = Capture($"ENG_FUEL_LINE_BOIL:{n}", $"Line {n} Vapour");
        }

        // ---------- The vendor's gauge, labelled as such ----------

        v["DA40_PRIME_ASSIST_OPTION"] = new SimVarDefinition
        {
            Name = "ASSIST_PRIME",
            DisplayName = "Priming Assist",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                // ⚠️ "Off", not "Off, on the Engine page menu". A ValueDescriptions
                // label is read EVERY time the row is scanned, so a label carrying
                // directions becomes a sentence the pilot hears on every pass. Where the
                // setting lives is in docs/da40.md.
                [0] = "Off",
                [1] = "On"
            }
        };

        v["DA40_PRIME_ASSIST_ACTIVE"] = new SimVarDefinition
        {
            Name = "ASSIST_PRIME_ACTIVE",
            DisplayName = "Assist Gauge",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                // ⚠️ "Idle", not the prerequisites. Same rule: a state label names the
                // STATE; what it would take to leave it is in docs/da40.md.
                [0] = "Idle",
                [1] = "Computing"
            }
        };

        v["DA40_PRIME_GAUGE"] = new SimVarDefinition
        {
            Name = "ASSIST_PRIME_PERCENT",
            DisplayName = "Assist Gauge Percent",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Units = "percent",
            Format = "F0"
        };

        // ---------- Heat ----------

        v["DA40_PRIME_LINE_TEMP"] = new SimVarDefinition
        {
            Name = "ENG_FUEL_LINE_TEMP:1",
            DisplayName = "Fuel Line Temperature",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        v["DA40_PRIME_FUEL_TEMP"] = new SimVarDefinition
        {
            Name = "FUEL_TEMP",
            DisplayName = "Fuel Temperature",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Units = "celsius",
            Format = "F0"
        };

        v["DA40_PRIME_FIREWALL_TEMP"] = new SimVarDefinition
        {
            Name = "ENG_FUEL_FIREWALL_TEMP_C",
            DisplayName = "Firewall Fuel Temperature",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Units = "celsius",
            Format = "F0"
        };

        return v;
    }

    /// <summary>A silently batched input: polled so a callback can read it, never a row, never spoken.</summary>
    private static SimVarDefinition Capture(string name, string displayName) => new()
    {
        Name = name,
        DisplayName = displayName,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ExcludeFromMonitorManager = true
    };

    private static readonly List<string> PrimingControls = new()
    {
        "DA40_PRIME_CLEAR"
    };

    // The state first - it is the answer - then what a pilot checks while priming, then the
    // vendor's gauge, then the heat that decides whether this is a hot start.
    private static readonly List<string> PrimingDisplay = new()
    {
        "DA40_PRIME_CYL_1",
        "DA40_PRIME_BOIL_1",
        "DA40_XLS_FUEL_FLOW",
        "DA40_PRIME_ASSIST_OPTION",
        "DA40_PRIME_ASSIST_ACTIVE",
        "DA40_PRIME_GAUGE",
        "DA40_PRIME_LINE_TEMP",
        "DA40_PRIME_FUEL_TEMP",
        "DA40_PRIME_FIREWALL_TEMP"
    };

    /// <summary>Every silently captured priming input, for the silent-readout set.</summary>
    public static readonly string[] PrimingCapturedKeys =
    {
        "DA40_PRIME_CYL_1", "DA40_PRIME_CYL_2", "DA40_PRIME_CYL_3", "DA40_PRIME_CYL_4",
        "DA40_PRIME_ATOM_1", "DA40_PRIME_ATOM_2", "DA40_PRIME_ATOM_3", "DA40_PRIME_ATOM_4",
        "DA40_PRIME_EVAP",
        "DA40_PRIME_BOIL_1", "DA40_PRIME_BOIL_2", "DA40_PRIME_BOIL_3", "DA40_PRIME_BOIL_4"
    };

    private bool HandlePrimingSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_PRIME_CLEAR") return false;

        simConnect.SetLVar("RESET_FLOOD", 1);
        // The state row and its call-out confirm the result when the cylinders empty; this
        // only says the reset went.
        announcer.AnnounceImmediate("Flooding reset");
        return true;
    }

    // ==================================================================================
    // The state, captured and spoken
    // ==================================================================================

    private readonly double[] _primeCyl = new double[DA40Priming.Cylinders];
    private readonly double[] _primeAtom = { 1, 1, 1, 1 };
    private readonly bool[] _primeBoil = new bool[DA40Priming.Cylinders];
    private double _primeEvap;
    private PrimeState? _primeSpoken;
    private ScreenReaderAnnouncer? _primeAnnouncer;

    private bool PrimeEngineRunning => _magRpmNow >= DA40MagnetoCheck.RunningRpm;

    /// <summary>
    /// Captures the inputs and speaks a change of state. Returns false always: nothing here
    /// is the generic announcer's business, and the numbers themselves are silent.
    /// </summary>
    private bool NotePrimingChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (!varKey.StartsWith("DA40_PRIME_", StringComparison.Ordinal)) return false;

        _primeAnnouncer = announcer;
        bool classify = true;

        if (TryIndex(varKey, "DA40_PRIME_CYL_", out int c)) _primeCyl[c] = value;
        else if (TryIndex(varKey, "DA40_PRIME_ATOM_", out int a)) _primeAtom[a] = value;
        else if (varKey == "DA40_PRIME_EVAP") _primeEvap = value;
        else if (TryIndex(varKey, "DA40_PRIME_BOIL_", out int b)) { _primeBoil[b] = value > 0; classify = false; }
        else classify = false;

        if (!classify) return false;

        // A running engine is not a priming question; forget the last spoken state so the
        // first reading after a shutdown is a fresh baseline, not a change.
        if (PrimeEngineRunning || _primeEvap <= 0)
        {
            _primeSpoken = null;
            return false;
        }

        var state = DA40Priming.Classify(_primeCyl, _primeAtom, _primeEvap);
        if (_primeSpoken == null)
        {
            _primeSpoken = state;   // baseline-first: the first reading is not a change
            return false;
        }
        if (state == _primeSpoken) return false;
        _primeSpoken = state;

        // ⚠️ THE Ctrl+M MUTE IS CHECKED HERE. Spoken on the crossing, from inside the update
        // - but for a key the generic gate never sees, because it is silenced - so the row's
        // mute has to be honoured by hand. Immediate: primed-to-flooded is a fifth of a
        // second, and the pilot is holding the mixture forward waiting for the word.
        if (Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains("DA40_PRIME_CYL_1")) return false;
        _primeAnnouncer?.AnnounceImmediate(
            DA40Priming.Describe(state, _primeCyl.Sum(), DA40Priming.RequiredTotal(_primeAtom[0], _primeEvap)));
        return false;
    }

    private static bool TryIndex(string key, string prefix, out int index)
    {
        index = -1;
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (!int.TryParse(key.AsSpan(prefix.Length), out int n) || n < 1 || n > DA40Priming.Cylinders) return false;
        index = n - 1;
        return true;
    }

    private bool TryGetPrimingDisplayOverride(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            case "DA40_PRIME_CYL_1":
                if (PrimeEngineRunning) { displayText = "Engine running"; return true; }
                if (_primeEvap <= 0) { displayText = "Not available yet"; return true; }
                displayText = DA40Priming.Describe(
                    DA40Priming.Classify(_primeCyl, _primeAtom, _primeEvap),
                    _primeCyl.Sum(),
                    DA40Priming.RequiredTotal(_primeAtom[0], _primeEvap));
                return true;

            case "DA40_PRIME_BOIL_1":
                var lines = new List<string>();
                for (int i = 0; i < _primeBoil.Length; i++) if (_primeBoil[i]) lines.Add((i + 1).ToString());
                displayText = lines.Count == 0 ? "None" : "Forming in line " + string.Join(", ", lines);
                return true;

            case "DA40_PRIME_LINE_TEMP":
                displayText = value.ToString("F0");
                return true;
        }

        displayText = string.Empty;
        return false;
    }

    /// <summary>Forgets the captured state. Called when the aircraft is switched away.</summary>
    private void ResetPrimingState()
    {
        Array.Clear(_primeCyl);
        Array.Clear(_primeBoil);
        _primeEvap = 0;
        _primeSpoken = null;
        _primeAnnouncer = null;
    }
}

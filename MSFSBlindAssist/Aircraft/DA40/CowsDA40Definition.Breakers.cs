using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Circuit Breakers. Both variants.
///
/// All THIRTY-FOUR of them, which is every breaker the aeroplane has — the list, the
/// L:var behind each one and the label are read straight out of the model's own
/// CircuitBreakers.xml, so this cannot drift from the cockpit. Each is a plain toggle:
/// the breaker's click code is `(L:CB_XXX) ! (>L:CB_XXX)`, and 0 is IN, 1 is PULLED.
///
/// THEY REALLY DO SOMETHING, which is worth saying because a breaker panel that only
/// remembers its own switch positions would be indistinguishable from one that works.
/// Verified live: pulling CB_FLP extinguished FLAP_LIGHT:1 and restoring it relit the
/// lamp — the same gating the Annunciators panel reports.
///
/// EVERY BREAKER ANNOUNCES. A breaker is not a control a pilot operates for fun; when one
/// moves without being touched, something has failed, and that is exactly the background
/// change the announcement model exists for.
///
/// Each panel also carries ONE readout: how many of its breakers are out. The checklist
/// says "Circuit breakers ... CHECKED IN" three separate times, and answering that by
/// tabbing thirty-four combos is not checking, it is auditing. The count comes from
/// values cached as each breaker reports, which is why every breaker is Continuous.
///
/// There is no COPILOT breaker panel: the planning sketch had one and the aeroplane has
/// no such set. It was removed rather than left to render blank.
/// </summary>
public partial class CowsDA40Definition
{
    private const string CbEngineFuelPanel = "Engine and Fuel";
    private const string CbFlightInstrumentsPanel = "Flight Instruments";
    private const string CbAvionicsPanel = "Avionics";
    private const string CbBusPowerPanel = "Bus and Power";
    private const string CbLightingPanel = "Lighting";
    private const string CbAirframeSystemsPanel = "Airframe Systems";

    /// <summary>
    /// ⚠️ THE TWO AEROPLANES DO NOT SHARE ALL THIRTY-FOUR BREAKERS, AND THIS FILE USED TO SAY
    /// "Both variants". Counted out of each model's own CircuitBreakers.xml: 28 are common,
    /// and SIX differ each way. So the XLS panel was offering six breakers its aeroplane does
    /// not have - ECU A, ECU B, Fuel Pump A, Fuel Pump B, Power and Fuel Transfer, every one
    /// of them an Austro/NG item - while MISSING six it does, including the ALTERNATOR and
    /// the FUEL PUMP, both of which the AFM's emergency procedures tell a pilot to pull.
    ///
    /// Every breaker is named by its PLACARD, verbatim, from DA40BreakerPlacards — which also
    /// records why the six XLS names once taken from the real Diamond AFM were WRONG for this
    /// model (the row called "Autopilot Breaker" cut alternator protection).
    /// </summary>
    private Dictionary<string, SimVarDefinition> BuildBreakerVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Engine and Fuel ----------
        if (IsNG)
        {
            AddBreaker(v, "DA40_CB_ECU_A", "CB_ECA");
            AddBreaker(v, "DA40_CB_ECU_B", "CB_ECB");
            AddBreaker(v, "DA40_CB_FUEL_A", "CB_FPA");
            AddBreaker(v, "DA40_CB_FUEL_B", "CB_FPB");
            AddBreaker(v, "DA40_CB_XFR", "CB_XFR");
        }
        else
        {
            // The Lycoming has ONE electric pump, so one breaker rather than the NG's pair.
            AddBreaker(v, "DA40_CB_FUEL_PUMP", "CB_FUP");
        }

        AddBreaker(v, "DA40_CB_ENG_INST", "CB_ENG");
        AddBreaker(v, "DA40_CB_START", "CB_STR");
        AddBreakerCount(v, "DA40_CB_CBENGINEFUEL_OUT", "CB_ENG");

        // ---------- Flight Instruments ----------
        AddBreaker(v, "DA40_CB_ADC", "CB_ADC");
        AddBreaker(v, "DA40_CB_AHRS", "CB_AHR");
        AddBreaker(v, "DA40_CB_HORIZON", "CB_HOR");
        AddBreaker(v, "DA40_CB_PITOT", "CB_PIT");
        if (!IsNG) AddBreaker(v, "DA40_CB_TURN_BANK", "CB_TAS");
        AddBreakerCount(v, "DA40_CB_CBFLIGHTINSTRUMENTS_OUT", "CB_ADC");

        // ---------- Avionics ----------
        AddBreaker(v, "DA40_CB_PFD", "CB_PFD");
        AddBreaker(v, "DA40_CB_MFD", "CB_MFD");
        AddBreaker(v, "DA40_CB_COM1", "CB_CM1");
        AddBreaker(v, "DA40_CB_COM2", "CB_CM2");
        AddBreaker(v, "DA40_CB_GPSNAV1", "CB_GP1");
        AddBreaker(v, "DA40_CB_GPSNAV2", "CB_GP2");
        AddBreaker(v, "DA40_CB_XPDR", "CB_XPR");
        AddBreaker(v, "DA40_CB_AUDIO", "CB_AUD");
        if (!IsNG) AddBreaker(v, "DA40_CB_AUTOPILOT", "CB_APT");
        AddBreakerCount(v, "DA40_CB_CBAVIONICS_OUT", "CB_PFD");

        // ---------- Bus and Power ----------
        AddBreaker(v, "DA40_CB_BATT", "CB_BAT");
        if (IsNG) AddBreaker(v, "DA40_CB_PWR", "CB_PWR");
        else AddBreaker(v, "DA40_CB_ALTERNATOR", "CB_ALT");
        AddBreaker(v, "DA40_CB_ESS_TIE", "CB_ESS");
        AddBreaker(v, "DA40_CB_MAIN_TIE", "CB_MAN");
        AddBreaker(v, "DA40_CB_MASTER", "CB_MTC");
        AddBreaker(v, "DA40_CB_AV_BUS", "CB_AVN");
        AddBreaker(v, "DA40_CB_AV_FAN", "CB_AVF");
        AddBreakerCount(v, "DA40_CB_CBBUSPOWER_OUT", "CB_BAT");

        // ---------- Lighting ----------
        AddBreaker(v, "DA40_CB_LANDING", "CB_LDL");
        AddBreaker(v, "DA40_CB_TAXI", "CB_TXM");
        AddBreaker(v, "DA40_CB_STROBE", "CB_STB");
        AddBreaker(v, "DA40_CB_POSITION", "CB_POS");
        AddBreaker(v, "DA40_CB_FLOOD", "CB_FLD");
        AddBreaker(v, "DA40_CB_INST_LT", "CB_INT");
        AddBreakerCount(v, "DA40_CB_CBLIGHTING_OUT", "CB_LDL");

        // ---------- Airframe Systems ----------
        AddBreaker(v, "DA40_CB_FLAPS", "CB_FLP");
        AddBreaker(v, "DA40_CB_AFCS", "CB_AFC");
        if (!IsNG)
        {
            AddBreaker(v, "DA40_CB_ANNUNCIATOR", "CB_ACN");
            AddBreaker(v, "DA40_CB_FAN_OAT", "CB_FAN");
        }
        AddBreakerCount(v, "DA40_CB_CBAIRFRAMESYSTEMS_OUT", "CB_FLP");

        return v;
    }

    /// <summary>0 is IN, 1 is PULLED — the model's own click code toggles the L:var.</summary>
    private static void AddBreaker(Dictionary<string, SimVarDefinition> v, string key, string lvar)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            // " Breaker" is part of the NAME, not decoration. A breaker carries the name
            // of the thing it feeds, so in the Monitor Manager - which is one flat list
            // with no panel around it - "Landing Light" appeared three times over: the
            // switch, this breaker, and the failure, with nothing to tell them apart.
            // The placard, verbatim (DA40BreakerPlacards), then " Breaker".
            DisplayName = DA40BreakerPlacards.For(lvar) + " Breaker",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "In",
                [1] = "PULLED"
            }
        };
    }

    /// <summary>
    /// The per-panel "how many are out" row. Bound to one of the panel's own breakers so
    /// it has a value to be rendered with; the text is replaced entirely.
    /// </summary>
    private static void AddBreakerCount(Dictionary<string, SimVarDefinition> v, string key,
        string anyLvar)
    {
        v[key] = new SimVarDefinition
        {
            Name = anyLvar,
            DisplayName = "Breakers Out",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };
    }

    /// <summary>
    /// ⚠️ VARIANT-SPLIT, FOR THE REASON IN BuildBreakerVariables. A panel row naming a
    /// breaker the aeroplane does not have is worse than a missing one: the pilot reaches
    /// for it in a checklist and finds a control that cannot do anything.
    /// </summary>
    private List<string> CbEngineFuelControlsFor() => IsNG
        ? new List<string>
        {
            "DA40_CB_ECU_A", "DA40_CB_ECU_B", "DA40_CB_FUEL_A", "DA40_CB_FUEL_B",
            "DA40_CB_ENG_INST", "DA40_CB_START", "DA40_CB_XFR"
        }
        : new List<string>
        {
            "DA40_CB_FUEL_PUMP", "DA40_CB_ENG_INST", "DA40_CB_START"
        };

    private static readonly List<string> CbEngineFuelDisplay = new() { "DA40_CB_CBENGINEFUEL_OUT" };

    private List<string> CbFlightInstrumentsControlsFor()
    {
        var l = new List<string> { "DA40_CB_ADC", "DA40_CB_AHRS", "DA40_CB_HORIZON", "DA40_CB_PITOT" };
        if (!IsNG) l.Add("DA40_CB_TURN_BANK");
        return l;
    }

    private static readonly List<string> CbFlightInstrumentsDisplay = new() { "DA40_CB_CBFLIGHTINSTRUMENTS_OUT" };

    private static readonly List<string> CbAvionicsControls = new()
    {
        "DA40_CB_PFD",
        "DA40_CB_MFD",
        "DA40_CB_COM1",
        "DA40_CB_COM2",
        "DA40_CB_GPSNAV1",
        "DA40_CB_GPSNAV2",
        "DA40_CB_XPDR",
        "DA40_CB_AUDIO"
    };

    private List<string> CbAvionicsControlsFor()
    {
        var l = new List<string>(CbAvionicsControls);
        if (!IsNG) l.Add("DA40_CB_AUTOPILOT");
        return l;
    }

    private static readonly List<string> CbAvionicsDisplay = new() { "DA40_CB_CBAVIONICS_OUT" };

    private static readonly List<string> CbBusPowerControls = new()
    {
        "DA40_CB_BATT",
        "DA40_CB_ESS_TIE",
        "DA40_CB_MAIN_TIE",
        "DA40_CB_MASTER",
        "DA40_CB_AV_BUS",
        "DA40_CB_AV_FAN"
    };

    private List<string> CbBusPowerControlsFor()
    {
        var l = new List<string>(CbBusPowerControls);
        // The NG's "Power" breaker is not on the XLS; the XLS has the ALTERNATOR instead,
        // which the AFM's alternator-failure drill tells the pilot to reset.
        if (IsNG) l.Insert(1, "DA40_CB_PWR"); else l.Insert(1, "DA40_CB_ALTERNATOR");
        return l;
    }

    private static readonly List<string> CbBusPowerDisplay = new() { "DA40_CB_CBBUSPOWER_OUT" };

    private static readonly List<string> CbLightingControls = new()
    {
        "DA40_CB_LANDING",
        "DA40_CB_TAXI",
        "DA40_CB_STROBE",
        "DA40_CB_POSITION",
        "DA40_CB_FLOOD",
        "DA40_CB_INST_LT"
    };

    private static readonly List<string> CbLightingDisplay = new() { "DA40_CB_CBLIGHTING_OUT" };

    private static readonly List<string> CbAirframeSystemsControls = new()
    {
        "DA40_CB_FLAPS",
        "DA40_CB_AFCS"
    };

    private List<string> CbAirframeSystemsControlsFor()
    {
        var l = new List<string>(CbAirframeSystemsControls);
        if (!IsNG) { l.Add("DA40_CB_ANNUNCIATOR"); l.Add("DA40_CB_FAN_OAT"); }
        return l;
    }

    private static readonly List<string> CbAirframeSystemsDisplay = new() { "DA40_CB_CBAIRFRAMESYSTEMS_OUT" };

    /// <summary>
    /// Every breaker, for the panel wiring and the per-panel counts.
    ///
    /// ⚠️ A METHOD, NOT A STATIC FIELD, because four of the six groups now differ between the
    /// variants - a static initializer cannot ask which aeroplane this is.
    /// </summary>
    private Dictionary<string, List<string>> BreakerPanelsFor() => new()
    {
        [CbEngineFuelPanel] = CbEngineFuelControlsFor(),
        [CbFlightInstrumentsPanel] = CbFlightInstrumentsControlsFor(),
        [CbAvionicsPanel] = CbAvionicsControlsFor(),
        [CbBusPowerPanel] = CbBusPowerControlsFor(),
        [CbLightingPanel] = CbLightingControls,
        [CbAirframeSystemsPanel] = CbAirframeSystemsControlsFor(),
    };

    private static readonly Dictionary<string, string> BreakerCountKeys = new()
    {
        ["DA40_CB_CBENGINEFUEL_OUT"] = CbEngineFuelPanel,
        ["DA40_CB_CBFLIGHTINSTRUMENTS_OUT"] = CbFlightInstrumentsPanel,
        ["DA40_CB_CBAVIONICS_OUT"] = CbAvionicsPanel,
        ["DA40_CB_CBBUSPOWER_OUT"] = CbBusPowerPanel,
        ["DA40_CB_CBLIGHTING_OUT"] = CbLightingPanel,
        ["DA40_CB_CBAIRFRAMESYSTEMS_OUT"] = CbAirframeSystemsPanel,
    };

    // Latest breaker positions, filled by ProcessSimVarUpdate. The counts are computed
    // from these rather than re-read, because a display override has no SimConnect.
    private readonly Dictionary<string, double> _breakerState = new();

    private bool HandleBreakerSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (!varKey.StartsWith("DA40_CB_") || !GetVariables().TryGetValue(varKey, out var def))
        {
            return false;
        }

        if (!BreakerPanelsFor().Values.Any(list => list.Contains(varKey))) return false;

        simConnect.SetLVar(def.Name, value >= 0.5 ? 1 : 0);
        return true;
    }

    private bool TryGetBreakerDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        if (!BreakerCountKeys.TryGetValue(varKey, out var panel)) return false;

        var keys = BreakerPanelsFor()[panel];
        int outCount = keys.Count(k => _breakerState.TryGetValue(k, out var s) && s >= 0.5);

        displayText = outCount == 0
            ? $"none — all {keys.Count} in"
            : $"{outCount} of {keys.Count} PULLED";
        return true;
    }
}

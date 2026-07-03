using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for the PMDG 737 (NG3) family. Variables, panels, hotkey
/// routing, MCP direct-set dialogs, and announcement logic. Tasks C2–C13 fill
/// in the data dictionaries and behavior methods. The NG3 SDK is shared across
/// the 737 variants, so this definition serves all of them.
/// </summary>
public class PMDG737Definition : BaseAircraftDefinition, IPMDGAircraft
{
    public override string AircraftName => "PMDG 737";
    public override string AircraftCode => "PMDG_737";

    // EFB accessibility is supported on the 738 — it renders the identical EFB app as the 777,
    // so the same Coherent in-page agent (coherent-pmdg-efb-agent.js) reads it. Opened with
    // Shift+T via the shared FbwEfbForm over CoherentPmdgEfbClient (no Community-folder bridge).
    public bool HasEFBSupport => true;

    // Cached merged variables dictionary — built once on first access.
    // All callers are read-only so sharing a single instance is safe.
    private Dictionary<string, SimConnect.SimVarDefinition>? _cachedVariables;

    // Cached set of RenderAsButton keys that are NOT annunciators.
    // Used in ProcessSimVarUpdate to suppress raw value announcements
    // without re-allocating GetVariables() on every call.
    private HashSet<string>? _suppressedButtonKeys;

    private HashSet<string> SuppressedButtonKeys =>
        _suppressedButtonKeys ??= BuildSuppressedButtonKeys();

    private HashSet<string> BuildSuppressedButtonKeys()
    {
        var set = new HashSet<string>();
        foreach (var kvp in GetVariables())
        {
            if (kvp.Value.RenderAsButton && !kvp.Value.Name.Contains("_annun"))
                set.Add(kvp.Key);
        }
        return set;
    }

    // ---------------------------------------------------------------------
    // Suppression state for ProcessSimVarUpdate (Task C11).
    //
    // For variables whose initial value should be absorbed silently (so we
    // don't shout a flood of baseline announcements on connect), the NaN
    // sentinel means "no baseline captured yet". For press counters, the
    // 0 sentinel doubles as "no baseline yet" — the press counters are
    // unsigned bytes the panel never sets to 0 deliberately.
    // ---------------------------------------------------------------------
    private double _lastAnnouncedAltimeter = double.NaN;
    // Last-announced FLT/LAND ALT window values (feet). NaN = nothing announced
    // yet; since `value == NaN` is always false, the FIRST change announces (the
    // connect baseline never reaches ProcessSimVarUpdate — MainForm absorbs it on
    // the initial-snapshot early-return). A panel-initiated set pre-loads these so
    // the monitor does not double-announce after HandleUIVariableSet confirmed.
    private double _lastFltAltWindow = double.NaN;
    private double _lastLandAltWindow = double.NaN;
    private double _lastCom1Active = double.NaN;
    private double _lastCom2Active = double.NaN;
    private double _lastCom1Standby = double.NaN;
    private double _lastCom2Standby = double.NaN;
    private double _lastSquawkCode = double.NaN;
    private byte _lastAttendPressCount;
    private byte _lastGrdCallPressCount;

    // Stabilizer trim, in units. Last announced value rounded to 0.1 unit, or
    // NaN until the baseline is absorbed silently on connect. See the
    // MON_PMDG737_StabTrim case in ProcessSimVarUpdate.
    private double _lastAnnouncedStabTrim = double.NaN;

    // Speed-brake lever position. The NG3 SDK exposes no lever-position field
    // (the 777 has FCTL_Speedbrake_Lever; the 737 does not) and PMDG does not
    // drive the stock SPOILERS HANDLE POSITION SimVar, so the analog handle
    // position comes from the PMDG L-var switch_679_73X. The L-var sweeps
    // CONTINUOUSLY as the lever animates (verified live — e.g. 62 / 337 caught
    // mid-move), and continuous monitoring only fires on change, so a value that
    // settles at a detent produces one final event then silence. We therefore
    // announce on a trailing-edge SETTLE timer: every change restarts the timer
    // and the resting detent is announced once movement stops. Detent values
    // were verified live against MSFS NG3 (TFM's 272 for flight was a P3D value;
    // it reads 337 here). Positions that come to rest between detents say nothing.
    private readonly object _speedBrakeLock = new object();
    private double _speedBrakeLatestValue = double.NaN;
    private int _lastSpeedBrakeDetentAnnounced = int.MinValue;
    private System.Threading.Timer? _speedBrakeSettleTimer;
    private ScreenReaderAnnouncer? _speedBrakeAnnouncer;
    private const int SpeedBrakeSettleMs = 300;

    // EFIS Minimums knob step sizes per click on the PMDG NG3 737. RADIO mode
    // (DH) clicks in 1-ft increments; BARO mode (DA) clicks in 20-ft increments.
    // Values are empirical and may need tuning if a tester reports drift —
    // change here and rebuild; no other code depends on these.
    private const int MINS_STEP_FT_RADIO = 1;
    private const int MINS_STEP_FT_BARO  = 20;
    // Safety cap on a single Set operation. ~40 seconds worst-case at 40ms/click.
    private const int MINS_MAX_CLICKS_PER_SET = 1000;

    private static readonly (double Value, string Label)[] SpeedBrakeDetents =
    {
        (0,   "Speed brake down"),
        (100, "Speed brake armed"),
        (250, "Speed brake 50 percent"),
        (337, "Speed brake flight"),
        (400, "Speed brake fully deployed"),
    };

    // The detent index the handle is resting at (within tolerance), or -1 if it
    // came to rest between named detents.
    private static int SettledSpeedBrakeDetent(double value)
    {
        for (int i = 0; i < SpeedBrakeDetents.Length; i++)
            if (Math.Abs(value - SpeedBrakeDetents[i].Value) <= 10.0) return i;
        return -1;
    }

    // Fires SpeedBrakeSettleMs after the handle stops moving (the timer is
    // restarted on every value change). Announces the resting detent once,
    // skipping the initial baseline and unchanged positions. Runs on a
    // thread-pool thread; ScreenReaderAnnouncer.Announce is queue-locked and
    // safe to call off the UI thread.
    private void OnSpeedBrakeSettle(object? state)
    {
        // Runs on a threadpool thread (System.Threading.Timer). An unhandled throw
        // here would crash the process, so guard the whole callback.
        try
        {
            double value;
            ScreenReaderAnnouncer? announcer;
            lock (_speedBrakeLock)
            {
                value = _speedBrakeLatestValue;
                announcer = _speedBrakeAnnouncer;
            }
            if (announcer == null || double.IsNaN(value)) return;

            int idx = SettledSpeedBrakeDetent(value);
            if (idx < 0) return;  // resting between detents — say nothing

            bool announce;
            lock (_speedBrakeLock)
            {
                if (idx == _lastSpeedBrakeDetentAnnounced) return;
                announce = _lastSpeedBrakeDetentAnnounced != int.MinValue;  // skip baseline
                _lastSpeedBrakeDetentAnnounced = idx;
            }
            if (announce)
                announcer.Announce(SpeedBrakeDetents[idx].Label);
        }
        catch { /* never let a timer callback take down the app */ }
    }

    // Flap-position announcement state. The NG3 SDK exposes only the analog
    // trailing-edge needle (MAIN_TEFlapsNeedle, degrees) — no commanded-detent
    // field — so we snap to the nearest detent and debounce (announce a new
    // detent only after two consecutive settled samples, so a multi-step
    // extension doesn't chatter every intermediate detent it sweeps past).
    private int _lastFlapDetentAnnounced = int.MinValue;
    private int _flapDetentCandidate = int.MinValue;
    private static readonly double[] FlapDetentDegs = { 0, 1, 2, 5, 10, 15, 25, 30, 40 };

    // The detent the needle is settled at (within tolerance), or -1 mid-transit.
    private static int SettledFlapDetent(double needleDeg)
    {
        foreach (double d in FlapDetentDegs)
            if (Math.Abs(needleDeg - d) <= 0.6) return (int)d;
        return -1;
    }

    // Nearest detent regardless of transit (for the on-demand "L" readout).
    private static int NearestFlapDetent(double needleDeg)
    {
        int best = 0; double bestDiff = double.MaxValue;
        foreach (double d in FlapDetentDegs)
        {
            double diff = Math.Abs(needleDeg - d);
            if (diff < bestDiff) { bestDiff = diff; best = (int)d; }
        }
        return best;
    }

    // PMDG 737 MCP supports direct SetValue events for SPD/HDG/ALT/VS (NG3 SDK
    // exposes EVT_*_SET style events just like the 777).
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // MEASURED 2026-06-11 (KATL, B738, 2 turns): the 0.8 s prior over-led —
    // both rollouts stopped consistently 8.7° SHORT of the new heading (the
    // pilot already anticipates 737 rollouts themselves, so tone lead stacks
    // on top of their habit). Halved as a conservative step; raw residuals
    // suggested ~0. Re-measure once the pilot has flown with the cue a while —
    // their own anticipation may relax as they learn to trust it.
    public override double TaxiTurnLeadSeconds => 0.4;

    // =========================================================================
    // Panel Structure
    // =========================================================================

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "Electrical", "ADIRU", "Hydraulics", "Fuel", "Engines",
                "Anti-Ice", "Air Systems", "Lights", "Signs", "Oxygen",
                "Wipers", "Flight Controls", "Flight Recorder"
            },
            ["Forward Panel"] = new List<string>
            {
                "Landing Gear", "Autobrake", "GPWS", "Instruments"
            },
            ["Pedestal"] = new List<string>
            {
                "Control Stand", "Transponder/TCAS", "Fire Protection", "Cargo Fire",
                "Radio", "Calls", "Flight Deck Door", "Boris Audio Works"
            },
            ["Glareshield"] = new List<string>
            {
                "MCP", "Warnings", "EFIS Captain", "EFIS First Officer", "Display Select"
            },
            ["Interior"] = new List<string>
            {
                "Cockpit Furniture", "Cabin Bins", "Cabin Items", "Galley"
            },
        };
    }

    // =========================================================================
    // Variables — scaffold (populated in Tasks C5–C8)
    // =========================================================================

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        if (_cachedVariables != null)
            return _cachedVariables;

        var variables = GetBaseVariables();
        // The PMDG NG3 does not drive the stock ELEVATOR TRIM POSITION SimVar
        // (it stays pinned within ±0.04° regardless of actual stabilizer trim),
        // so the base-class trim announcement is meaningless for this aircraft.
        // Drop it and announce the real stab-trim units from the PMDG L-var
        // ElevTrimTT instead (MON_PMDG737_StabTrim, added in GetPMDGVariables).
        variables.Remove("MON_ElevatorTrim");
        var pmdgVars = GetPMDGVariables();
        foreach (var kvp in pmdgVars)
            variables[kvp.Key] = kvp.Value;
        _cachedVariables = variables;
        return variables;
    }

    private Dictionary<string, SimConnect.SimVarDefinition> GetPMDGVariables()
    {
        // Local helper builders to keep the dictionary literal terse.
        static SimConnect.SimVarDefinition Toggle(string name, string display, string off = "Off", string on = "On") =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on }
            };
        static SimConnect.SimVarDefinition Selector(string name, string display, params string[] positions)
        {
            var dict = new Dictionary<double, string>();
            for (int i = 0; i < positions.Length; i++) dict[i] = positions[i];
            return new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = dict
            };
        }
        static SimConnect.SimVarDefinition Annun(string name, string display) =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            };
        // Cabin/cargo door open-or-closed annunciator. The NG3 SDK models each
        // door as a single bool (set = the overhead DOORS annunciator is lit =
        // door open/unlatched). There is NO armed / closing / opening state — the
        // 777 gets those from its multi-state DOOR_state[] array, but the 737 SDK
        // has only the boolean, so the 737 announces open/closed only. Same shape
        // as Annun but with door-appropriate "open"/"closed" labels instead of
        // "on"/"off" (which read as the odd "Forward entry door off").
        static SimConnect.SimVarDefinition Door(string name, string display) =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "closed", [1] = "open" }
            };
        // AnnunInverted: for annunciators named `annunOFF` / `annunBUS_OFF` / `annunSOURCE_OFF` etc.
        // These lamps light when the system they describe is OFF (the lamp NAME is the abnormal
        // condition the lamp signals). TFM's `_offOrOnStates` (0:on, 1:off) treats byte=1 as
        // the "off" announcement because byte=1 means the lamp is lit, which means the system
        // is in the "off" condition the lamp is named for. Without this inversion the default
        // Annun helper would announce `<DisplayName>: on` when the lamp lights, which contradicts
        // the lamp's semantic meaning ("X is OFF" — the lamp says X is off, not that X is on).
        static SimConnect.SimVarDefinition AnnunInverted(string name, string display) =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "on", [1] = "off" }
            };
        static SimConnect.SimVarDefinition Numeric(string name, string display) =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            };
        static SimConnect.SimVarDefinition Display(string name, string display) =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            };
        static SimConnect.SimVarDefinition Quantity(string name, string display) =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = false
            };
        // Continuous-numeric read-only TextBox for cockpit gauge readouts
        // (cabin altitude, DP, duct pressure, APU EGT, fuel temp, etc.).
        // Renders via the MainForm `RenderAsReadOnlyStatus + Units + no
        // ValueDescriptions` branch. IsAnnounced=false so the gauge doesn't
        // speak every broadcast — the user reads the value by Tab-focusing
        // the TextBox.
        static SimConnect.SimVarDefinition Readout(string name, string display,
                                                   string units, string format = "F0",
                                                   double scale = 1.0, double offset = 0.0) =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = false,
                RenderAsReadOnlyStatus = true,
                Units = units,
                Format = format,
                Scale = scale,
                Offset = offset
            };
        static SimConnect.SimVarDefinition Momentary(string name, string display, string? stateVar = null) =>
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
                StateVariable = stateVar
            };

        var d = new Dictionary<string, SimConnect.SimVarDefinition>();

        // Stabilizer trim, in units (~0–17). The NG3 SDK exposes no trim
        // position field (only the cutout switches) and PMDG does not drive the
        // stock ELEVATOR TRIM POSITION SimVar, so the real value comes from the
        // PMDG L-var ElevTrimTT — the same source TFM used and the value the
        // cockpit stab-trim indicator shows. Announced (in units) by the
        // MON_PMDG737_StabTrim case in ProcessSimVarUpdate; the stock-SimVar
        // trim announcement is removed in GetVariables.
        d["MON_PMDG737_StabTrim"] = new SimConnect.SimVarDefinition
        {
            Name = "ElevTrimTT",
            DisplayName = "Stab Trim",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true  // custom announcement in ProcessSimVarUpdate
        };

        // Speed-brake lever position. The NG3 SDK exposes no lever-position
        // field and PMDG does not drive the stock SPOILERS HANDLE POSITION
        // SimVar, so the handle position comes from the PMDG L-var
        // switch_679_73X (same source TFM used). Snapped to detents and
        // announced by the MON_PMDG737_SpeedBrake case in ProcessSimVarUpdate.
        d["MON_PMDG737_SpeedBrake"] = new SimConnect.SimVarDefinition
        {
            Name = "switch_679_73X",
            DisplayName = "Speed Brake",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true  // custom announcement in ProcessSimVarUpdate
        };

        // =================================================================
        // AFT OVERHEAD — ADIRU (IRS)
        // =================================================================
        // TFM: `_IRSDisplaySelectorStates` (indices 1..4: TK/GS, PPOS, WIND, HDG/STAT);
        // SDK line 52: `unsigned char IRS_DisplaySelector; // Positions 0..4` (5 positions
        // declared, no inline labels). Resolved with TFM's 4-position dispatch space
        // (indices 1..4): SDK has more positions than TFM and position 0 is the unreachable
        // TEST detent (sprung-return on the physical ISDU). The previous 5-position dict
        // (0:TK/GS, 1:PPOS, 2:WIND, 3:HDG/STS, 4:SYS) was off-by-one for every pick and
        // exposed a phantom SYS detent that doesn't exist on the NG3 ISDU. Build the dict
        // manually because the Selector helper always starts at index 0.
        d["IRS_DisplaySelector"] = new SimConnect.SimVarDefinition
        {
            Name = "IRS_DisplaySelector",
            DisplayName = "ISDU Display Selector",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [1] = "TK/GS",
                [2] = "PPOS",
                [3] = "WIND",
                [4] = "HDG/STS",
            },
        };
        d["IRS_SysDisplay_R"]    = Toggle("IRS_SysDisplay_R", "IRS Sys Display", "Left", "Right");
        d["IRS_annunGPS"]        = Annun("IRS_annunGPS", "GPS");
        d["IRS_annunALIGN_0"]    = Annun("IRS_annunALIGN_0", "IRS Left ALIGN");
        d["IRS_annunALIGN_1"]    = Annun("IRS_annunALIGN_1", "IRS Right ALIGN");
        d["IRS_annunON_DC_0"]    = Annun("IRS_annunON_DC_0", "IRS Left ON DC");
        d["IRS_annunON_DC_1"]    = Annun("IRS_annunON_DC_1", "IRS Right ON DC");
        d["IRS_annunFAULT_0"]    = Annun("IRS_annunFAULT_0", "IRS Left FAULT");
        d["IRS_annunFAULT_1"]    = Annun("IRS_annunFAULT_1", "IRS Right FAULT");
        d["IRS_annunDC_FAIL_0"]  = Annun("IRS_annunDC_FAIL_0", "IRS Left DC FAIL");
        d["IRS_annunDC_FAIL_1"]  = Annun("IRS_annunDC_FAIL_1", "IRS Right DC FAIL");
        d["IRS_ModeSelector_0"]  = Selector("IRS_ModeSelector_0", "IRS Left Mode", "OFF", "ALIGN", "NAV", "ATT");
        d["IRS_ModeSelector_1"]  = Selector("IRS_ModeSelector_1", "IRS Right Mode", "OFF", "ALIGN", "NAV", "ATT");
        d["IRS_DisplayLeft"]     = Display("IRS_DisplayLeft", "ISDU Left Display");
        d["IRS_DisplayRight"]    = Display("IRS_DisplayRight", "ISDU Right Display");

        // AFS
        d["AFS_AutothrottleServosConnected"] = Annun("AFS_AutothrottleServosConnected", "Autothrottle Servos");
        d["AFS_ControlsPitch"]               = Annun("AFS_ControlsPitch", "Autopilot Controls Pitch");
        d["AFS_ControlsRoll"]                = Annun("AFS_ControlsRoll", "Autopilot Controls Roll");

        // PSEU
        d["WARN_annunPSEU"] = Annun("WARN_annunPSEU", "PSEU");

        // Service interphone
        d["COMM_ServiceInterphoneSw"] = Toggle("COMM_ServiceInterphoneSw", "Service Interphone");

        // Dome light
        d["LTS_DomeWhiteSw"] = Selector("LTS_DomeWhiteSw", "Dome Light", "DIM", "OFF", "BRIGHT");

        // Engine aft overhead
        d["ENG_EECSwitch_0"]            = Toggle("ENG_EECSwitch_0", "EEC Left", "OFF", "ON");
        d["ENG_EECSwitch_1"]            = Toggle("ENG_EECSwitch_1", "EEC Right", "OFF", "ON");
        d["ENG_annunREVERSER_0"]        = Annun("ENG_annunREVERSER_0", "Engine 1 REVERSER");
        d["ENG_annunREVERSER_1"]        = Annun("ENG_annunREVERSER_1", "Engine 2 REVERSER");
        d["ENG_annunENGINE_CONTROL_0"]  = Annun("ENG_annunENGINE_CONTROL_0", "Engine 1 ENGINE CONTROL");
        d["ENG_annunENGINE_CONTROL_1"]  = Annun("ENG_annunENGINE_CONTROL_1", "Engine 2 ENGINE CONTROL");
        d["ENG_annunALTN_0"]            = Annun("ENG_annunALTN_0", "Engine 1 ALTN");
        d["ENG_annunALTN_1"]            = Annun("ENG_annunALTN_1", "Engine 2 ALTN");
        d["ENG_StartValve_0"]           = new SimConnect.SimVarDefinition
        {
            Name = "ENG_StartValve_0", DisplayName = "Engine 1 Start Valve",
            Type = SimConnect.SimVarType.PMDGVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        };
        d["ENG_StartValve_1"]           = new SimConnect.SimVarDefinition
        {
            Name = "ENG_StartValve_1", DisplayName = "Engine 2 Start Valve",
            Type = SimConnect.SimVarType.PMDGVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        };

        // Oxygen
        // TFM: `_normalOrOffStates` (0:off, 1:normal); SDK line 89: `bool OXY_SwNormal; // true: NORMAL  false: ON`.
        // Resolved with SDK because TFM and SDK have the same position count (2) but disagree
        // on the label for byte 0 — SDK explicitly says false (byte 0) = ON, not "off".
        // The switch is physically ON ↔ NORMAL on the NG3 cockpit, not OFF ↔ NORMAL.
        d["OXY_SwNormal"]         = Toggle("OXY_SwNormal", "Passenger Oxygen", "ON", "Normal");
        d["OXY_annunPASS_OXY_ON"] = Annun("OXY_annunPASS_OXY_ON", "PASS OXY ON");
        // 3-position spring-loaded crew oxygen TEST / NORMAL / RESET switches —
        // one per side (L = Captain, R = First Officer). SDK exposes the events
        // but no state-byte readback, so we surface only the TEST direction as
        // a single momentary press per side (the standard LEFTSINGLE+LEFTRELEASE
        // pattern from SendPMDGMomentaryToggle drives the "up" direction, which
        // on this switch is the TEST detent). RESET is rarely used (only after
        // a deployment cycle) and is intentionally omitted; revisit if needed.
        d["OXY_TestL"] = Momentary("OXY_TestL", "Oxygen Test (Captain)");
        d["OXY_TestR"] = Momentary("OXY_TestR", "Oxygen Test (First Officer)");

        // Gear overhead annunciators
        d["GEAR_annunOvhdLEFT"]  = Annun("GEAR_annunOvhdLEFT", "Gear Left");
        d["GEAR_annunOvhdNOSE"]  = Annun("GEAR_annunOvhdNOSE", "Gear Nose");
        d["GEAR_annunOvhdRIGHT"] = Annun("GEAR_annunOvhdRIGHT", "Gear Right");

        // Flight recorder / CVR
        d["FLTREC_SwNormal"]   = Toggle("FLTREC_SwNormal", "Flight Recorder", "TEST", "Normal");
        // TFM: `_offOrOnStates` (0:on, 1:off); SDK line 99: `bool FLTREC_annunOFF;` (no inline comment).
        // Resolved with TFM's inverted convention: this is an `annunOFF` lamp — it lights when
        // the flight recorder system is OFF. byte=1 means the lamp is lit → announce "off"
        // (the system is in the off condition the lamp names). Default Annun would announce
        // "on" when the lamp lights, which contradicts the lamp's semantic meaning.
        d["FLTREC_annunOFF"]   = AnnunInverted("FLTREC_annunOFF", "Flight Recorder OFF");
        d["CVR_annunTEST"]     = Annun("CVR_annunTEST", "CVR TEST");

        // =================================================================
        // FORWARD OVERHEAD — Flight Controls
        // =================================================================
        d["FCTL_FltControl_Sw_0"] = Selector("FCTL_FltControl_Sw_0", "Flight Control A", "STBY/RUD", "OFF", "ON");
        d["FCTL_FltControl_Sw_1"] = Selector("FCTL_FltControl_Sw_1", "Flight Control B", "STBY/RUD", "OFF", "ON");
        d["FCTL_Spoiler_Sw_0"]    = Toggle("FCTL_Spoiler_Sw_0", "Spoiler A");
        d["FCTL_Spoiler_Sw_1"]    = Toggle("FCTL_Spoiler_Sw_1", "Spoiler B");
        d["FCTL_YawDamper_Sw"]    = Toggle("FCTL_YawDamper_Sw", "Yaw Damper");
        d["FCTL_AltnFlaps_Sw_ARM"] = Toggle("FCTL_AltnFlaps_Sw_ARM", "Alternate Flaps Arm", "OFF", "ARM");
        d["FCTL_AltnFlaps_Control_Sw"] = Selector("FCTL_AltnFlaps_Control_Sw", "Alternate Flaps Control",
            "UP", "OFF", "DOWN");
        // LE Devices Test omitted: PMDG NG3 fires the cockpit click sound when
        // EVT_OH_LE_DEVICES_TEST_SWITCH is sent, but the only LE-related SDK
        // fields (MAIN_annunLE_FLAPS_TRANSIT / _EXT) don't change state during
        // the test — the per-device LE position grid lights aren't exposed in
        // the SDK at all. A blind pilot would have no way to observe the test
        // result, so the button was removed (verified via simconnect-mcp:
        // sending the event produced an audible click but zero observable
        // state delta in any LE-related field).
        d["FCTL_annunFC_LOW_PRESSURE_0"] = Annun("FCTL_annunFC_LOW_PRESSURE_0", "Flight Control A LOW PRESSURE");
        d["FCTL_annunFC_LOW_PRESSURE_1"] = Annun("FCTL_annunFC_LOW_PRESSURE_1", "Flight Control B LOW PRESSURE");
        d["FCTL_annunYAW_DAMPER"]        = Annun("FCTL_annunYAW_DAMPER", "YAW DAMPER");
        d["FCTL_annunLOW_QUANTITY"]      = Annun("FCTL_annunLOW_QUANTITY", "LOW QUANTITY");
        d["FCTL_annunLOW_PRESSURE"]      = Annun("FCTL_annunLOW_PRESSURE", "Flight Control LOW PRESSURE");
        d["FCTL_annunLOW_STBY_RUD_ON"]   = Annun("FCTL_annunLOW_STBY_RUD_ON", "STBY RUD ON");
        d["FCTL_annunFEEL_DIFF_PRESS"]   = Annun("FCTL_annunFEEL_DIFF_PRESS", "FEEL DIFF PRESS");
        d["FCTL_annunSPEED_TRIM_FAIL"]   = Annun("FCTL_annunSPEED_TRIM_FAIL", "SPEED TRIM FAIL");
        d["FCTL_annunMACH_TRIM_FAIL"]    = Annun("FCTL_annunMACH_TRIM_FAIL", "MACH TRIM FAIL");
        d["FCTL_annunAUTO_SLAT_FAIL"]    = Annun("FCTL_annunAUTO_SLAT_FAIL", "AUTO SLAT FAIL");

        // Navigation / Displays
        d["NAVDIS_VHFNavSelector"]      = Selector("NAVDIS_VHFNavSelector", "VHF NAV Source",
            "BOTH ON 1", "NORMAL", "BOTH ON 2");
        d["NAVDIS_IRSSelector"]         = Selector("NAVDIS_IRSSelector", "IRS Source",
            "BOTH ON L", "NORMAL", "BOTH ON R");
        d["NAVDIS_FMCSelector"]         = Selector("NAVDIS_FMCSelector", "FMC Source",
            "BOTH ON L", "NORMAL", "BOTH ON R");
        d["NAVDIS_SourceSelector"]      = Selector("NAVDIS_SourceSelector", "Displays Source",
            "ALL ON 1", "AUTO", "ALL ON 2");
        d["NAVDIS_ControlPaneSelector"] = Selector("NAVDIS_ControlPaneSelector", "Control Panel Source",
            "BOTH ON 1", "NORMAL", "BOTH ON 2");

        // Fuel
        d["FUEL_CrossFeedSw"]       = Toggle("FUEL_CrossFeedSw", "Fuel Crossfeed", "Closed", "Open");
        d["FUEL_PumpFwdSw_0"]       = Toggle("FUEL_PumpFwdSw_0", "Fuel Pump 1 Forward");
        d["FUEL_PumpFwdSw_1"]       = Toggle("FUEL_PumpFwdSw_1", "Fuel Pump 2 Forward");
        d["FUEL_PumpAftSw_0"]       = Toggle("FUEL_PumpAftSw_0", "Fuel Pump 1 Aft");
        d["FUEL_PumpAftSw_1"]       = Toggle("FUEL_PumpAftSw_1", "Fuel Pump 2 Aft");
        d["FUEL_PumpCtrSw_0"]       = Toggle("FUEL_PumpCtrSw_0", "Fuel Pump Center Left");
        d["FUEL_PumpCtrSw_1"]       = Toggle("FUEL_PumpCtrSw_1", "Fuel Pump Center Right");
        d["FUEL_AuxFwd_0"]          = Toggle("FUEL_AuxFwd_0", "Aux Fuel Forward A");
        d["FUEL_AuxFwd_1"]          = Toggle("FUEL_AuxFwd_1", "Aux Fuel Forward B");
        d["FUEL_AuxAft_0"]          = Toggle("FUEL_AuxAft_0", "Aux Fuel Aft A");
        d["FUEL_AuxAft_1"]          = Toggle("FUEL_AuxAft_1", "Aux Fuel Aft B");
        d["FUEL_FWDBleed"]          = Toggle("FUEL_FWDBleed", "Aux Fuel Forward Bleed");
        d["FUEL_AFTBleed"]          = Toggle("FUEL_AFTBleed", "Aux Fuel Aft Bleed");
        d["FUEL_GNDXfr"]            = Toggle("FUEL_GNDXfr", "Ground Transfer");
        // SDK 142–145: these fuel-valve "annunciators" are actually 3-state
        // (unsigned char), not the 2-state bool the helper Annun assumes:
        //   FUEL_annunENG_VALVE_CLOSED[2]  // 0: Closed  1: Open  2: In transit (bright)
        //   FUEL_annunSPAR_VALVE_CLOSED[2] // 0: Closed  1: Open  2: In transit (bright)
        //   FUEL_annunXFEED_VALVE_OPEN     // 0: Closed  1: Open  2: In transit (dim)
        // Treating them as 2-state Annun with OnlyAnnounceValueDescriptionMatches
        // silently swallowed the "In transit" state — the pilot was never told
        // when a valve was mid-travel. Use Selector to surface all three states.
        d["FUEL_annunENG_VALVE_CLOSED_0"]  = Selector("FUEL_annunENG_VALVE_CLOSED_0", "Engine 1 Valve",
            "Closed", "Open", "In transit");
        d["FUEL_annunENG_VALVE_CLOSED_1"]  = Selector("FUEL_annunENG_VALVE_CLOSED_1", "Engine 2 Valve",
            "Closed", "Open", "In transit");
        d["FUEL_annunSPAR_VALVE_CLOSED_0"] = Selector("FUEL_annunSPAR_VALVE_CLOSED_0", "Spar Valve 1",
            "Closed", "Open", "In transit");
        d["FUEL_annunSPAR_VALVE_CLOSED_1"] = Selector("FUEL_annunSPAR_VALVE_CLOSED_1", "Spar Valve 2",
            "Closed", "Open", "In transit");
        d["FUEL_annunFILTER_BYPASS_0"]     = Annun("FUEL_annunFILTER_BYPASS_0", "Fuel Filter 1 Bypass");
        d["FUEL_annunFILTER_BYPASS_1"]     = Annun("FUEL_annunFILTER_BYPASS_1", "Fuel Filter 2 Bypass");
        d["FUEL_annunXFEED_VALVE_OPEN"]    = Selector("FUEL_annunXFEED_VALVE_OPEN", "Crossfeed Valve",
            "Closed", "Open", "In transit");
        d["FUEL_annunLOWPRESS_Fwd_0"]      = Annun("FUEL_annunLOWPRESS_Fwd_0", "Fuel Pump 1 Forward Low Press");
        d["FUEL_annunLOWPRESS_Fwd_1"]      = Annun("FUEL_annunLOWPRESS_Fwd_1", "Fuel Pump 2 Forward Low Press");
        d["FUEL_annunLOWPRESS_Aft_0"]      = Annun("FUEL_annunLOWPRESS_Aft_0", "Fuel Pump 1 Aft Low Press");
        d["FUEL_annunLOWPRESS_Aft_1"]      = Annun("FUEL_annunLOWPRESS_Aft_1", "Fuel Pump 2 Aft Low Press");
        d["FUEL_annunLOWPRESS_Ctr_0"]      = Annun("FUEL_annunLOWPRESS_Ctr_0", "Fuel Pump Center Left Low Press");
        d["FUEL_annunLOWPRESS_Ctr_1"]      = Annun("FUEL_annunLOWPRESS_Ctr_1", "Fuel Pump Center Right Low Press");
        d["FUEL_QtyCenter"] = Quantity("FUEL_QtyCenter", "Fuel Center Tank");
        d["FUEL_QtyLeft"]   = Quantity("FUEL_QtyLeft",   "Fuel Left Tank");
        d["FUEL_QtyRight"]  = Quantity("FUEL_QtyRight",  "Fuel Right Tank");
        // Fuel temperature — continuous-numeric readout on the Fuel panel.
        // Low temps at altitude signal fuel-icing risk (cross-monitor with
        // anti-ice switches).
        d["FUEL_FuelTempNeedle"]    = Readout("FUEL_FuelTempNeedle", "Fuel Temperature", "°C", "F0");

        // Electrical
        d["ELEC_annunBAT_DISCHARGE"] = Annun("ELEC_annunBAT_DISCHARGE", "Battery Discharge");
        d["ELEC_annunTR_UNIT"]       = Annun("ELEC_annunTR_UNIT", "TR Unit");
        d["ELEC_annunELEC"]          = Annun("ELEC_annunELEC", "ELEC");
        d["ELEC_DCMeterSelector"]    = Selector("ELEC_DCMeterSelector", "DC Meter",
            "STBY PWR", "BAT BUS", "BAT", "AUX BAT", "TR1", "TR2", "TR3", "TEST");
        d["ELEC_ACMeterSelector"]    = Selector("ELEC_ACMeterSelector", "AC Meter",
            "STBY PWR", "GRD PWR", "GEN 1", "APU GEN", "GEN 2", "INV", "TEST");
        // SDK enum: ELEC_BatSelector { 0=OFF, 1=BAT, 2=ON } — but the actual NG3
        // cockpit lever has only two physical detents (user-confirmed in 2026-05-22
        // testing). The SDK position 2 ("ON") is an enum-only phantom that's
        // never reachable on the actual switch; PMDG reports the up detent as
        // byte=1 (BAT) and treats that as the operational ON state. Expose two
        // positions only: UI "ON" maps to SDK byte 1.
        d["ELEC_BatSelector"]        = Toggle("ELEC_BatSelector", "Battery", "OFF", "ON");
        d["ELEC_CabUtilSw"]          = Toggle("ELEC_CabUtilSw", "Cabin Utility");
        d["ELEC_IFEPassSeatSw"]      = Toggle("ELEC_IFEPassSeatSw", "IFE Pass Seats");
        d["ELEC_annunDRIVE_0"]       = Annun("ELEC_annunDRIVE_0", "Drive 1");
        d["ELEC_annunDRIVE_1"]       = Annun("ELEC_annunDRIVE_1", "Drive 2");
        d["ELEC_annunSTANDBY_POWER_OFF"] = Annun("ELEC_annunSTANDBY_POWER_OFF", "Standby Power Off");
        d["ELEC_IDGDisconnectSw_0"]  = new SimConnect.SimVarDefinition
        {
            Name = "ELEC_IDGDisconnectSw_0", DisplayName = "IDG 1 Disconnect",
            Type = SimConnect.SimVarType.PMDGVar, UpdateFrequency = SimConnect.UpdateFrequency.Never,
            RenderAsButton = true, IsMomentary = true
        };
        d["ELEC_IDGDisconnectSw_1"]  = new SimConnect.SimVarDefinition
        {
            Name = "ELEC_IDGDisconnectSw_1", DisplayName = "IDG 2 Disconnect",
            Type = SimConnect.SimVarType.PMDGVar, UpdateFrequency = SimConnect.UpdateFrequency.Never,
            RenderAsButton = true, IsMomentary = true
        };
        d["ELEC_StandbyPowerSelector"] = Selector("ELEC_StandbyPowerSelector", "Standby Power",
            "BAT", "OFF", "AUTO");
        d["ELEC_annunGRD_POWER_AVAILABLE"] = Annun("ELEC_annunGRD_POWER_AVAILABLE", "Ground Power Available");
        // GRD POWER: same "honest push-button pair" pattern as APU GEN 1/2.
        // The only PMDG signal we found that tracks GRD POWER specifically
        // (ELEC_BusPowered[9] = AC GROUND SVC) reads "powered" whenever ANY
        // source is feeding the AC transfer system, so it false-reads ON when
        // APU is running with the bus transfer set to AUTO. PMDG exposes no
        // GPU-vs-APU discriminator on this bus. Rather than display a state
        // that could be wrong, expose two stateless action buttons.
        d["ELEC_GrdPwrSw_On"]        = Momentary("ELEC_GrdPwrSw_On",  "Ground Power On");
        d["ELEC_GrdPwrSw_Off"]       = Momentary("ELEC_GrdPwrSw_Off", "Ground Power Off");
        d["ELEC_BusTransSw_AUTO"]    = Toggle("ELEC_BusTransSw_AUTO", "Bus Transfer", "OFF", "AUTO");
        d["ELEC_GenSw_0"]            = Toggle("ELEC_GenSw_0", "Generator 1");
        d["ELEC_GenSw_1"]            = Toggle("ELEC_GenSw_1", "Generator 2");
        // APU GEN 1/2: rendered as two momentary push buttons each ("On" / "Off")
        // because PMDG NG3 exposes no reliable per-switch position signal — the
        // ELEC_APUGenSw[] bool is a spring-loaded transient (always reads 1 at
        // rest), and the single shared ELEC_annunAPU_GEN_OFF_BUS annunciator
        // can't discriminate APU GEN 1 vs APU GEN 2. Rather than display a
        // state that could be out of sync with reality, we expose stateless
        // action buttons that just dispatch a directional press. Dispatch
        // shape per button is in _directionalPushMap below.
        d["ELEC_APUGenSw_0_On"]      = Momentary("ELEC_APUGenSw_0_On",  "APU Gen 1 On");
        d["ELEC_APUGenSw_0_Off"]     = Momentary("ELEC_APUGenSw_0_Off", "APU Gen 1 Off");
        d["ELEC_APUGenSw_1_On"]      = Momentary("ELEC_APUGenSw_1_On",  "APU Gen 2 On");
        d["ELEC_APUGenSw_1_Off"]     = Momentary("ELEC_APUGenSw_1_Off", "APU Gen 2 Off");
        // TFM: `_offOrOnStates` (0:on, 1:off); SDK lines 171-174: bus-off annunciator arrays,
        // no inline comments. Resolved with TFM's inverted convention: these are `annunOFF`
        // lamps — they light when the named bus/source is OFF. byte=1 means the lamp is lit
        // → the bus IS off → announce "off". Default Annun would announce "on" when the lamp
        // lights, which contradicts the lamp's semantic meaning. Applies to all eight bus-off
        // annunciators below.
        d["ELEC_annunTRANSFER_BUS_OFF_0"] = AnnunInverted("ELEC_annunTRANSFER_BUS_OFF_0", "Transfer Bus 1 Off");
        d["ELEC_annunTRANSFER_BUS_OFF_1"] = AnnunInverted("ELEC_annunTRANSFER_BUS_OFF_1", "Transfer Bus 2 Off");
        d["ELEC_annunSOURCE_OFF_0"]  = AnnunInverted("ELEC_annunSOURCE_OFF_0", "Source 1 Off");
        d["ELEC_annunSOURCE_OFF_1"]  = AnnunInverted("ELEC_annunSOURCE_OFF_1", "Source 2 Off");
        d["ELEC_annunGEN_BUS_OFF_0"] = AnnunInverted("ELEC_annunGEN_BUS_OFF_0", "Gen Bus 1 Off");
        d["ELEC_annunGEN_BUS_OFF_1"] = AnnunInverted("ELEC_annunGEN_BUS_OFF_1", "Gen Bus 2 Off");
        d["ELEC_annunAPU_GEN_OFF_BUS"] = AnnunInverted("ELEC_annunAPU_GEN_OFF_BUS", "APU Gen Off Bus");
        d["ELEC_MeterDisplayTop"]    = Display("ELEC_MeterDisplayTop", "Electrical Meter Top Display");
        d["ELEC_MeterDisplayBottom"] = Display("ELEC_MeterDisplayBottom", "Electrical Meter Bottom Display");
        // ELEC_BusPowered[16] — bus-powered indicators (SDK line 299, `bool[16]`). All 16 are
        // read-only annunciators surfaced by TFM in its `BUSES` panel. Per the user-approved
        // 29-panel functional structure, these are auto-announced (IsAnnounced=true via Annun)
        // and NOT placed in any panel — 16 entries would dominate the Electrical panel which
        // is already dense. Index → label per TFM's BUSES panel ordering.
        d["ELEC_BusPowered_0"]  = Annun("ELEC_BusPowered_0",  "DC bus Hot Battery");
        d["ELEC_BusPowered_1"]  = Annun("ELEC_BusPowered_1",  "DC bus Hot Battery Switched");
        d["ELEC_BusPowered_2"]  = Annun("ELEC_BusPowered_2",  "DC bus Battery");
        d["ELEC_BusPowered_3"]  = Annun("ELEC_BusPowered_3",  "DC bus Standby");
        d["ELEC_BusPowered_4"]  = Annun("ELEC_BusPowered_4",  "DC bus 1");
        d["ELEC_BusPowered_5"]  = Annun("ELEC_BusPowered_5",  "DC bus 2");
        d["ELEC_BusPowered_6"]  = Annun("ELEC_BusPowered_6",  "DC bus Ground Service");
        d["ELEC_BusPowered_7"]  = Annun("ELEC_BusPowered_7",  "AC bus Transfer 1");
        d["ELEC_BusPowered_8"]  = Annun("ELEC_BusPowered_8",  "AC bus Transfer 2");
        d["ELEC_BusPowered_9"]  = Annun("ELEC_BusPowered_9",  "AC bus Ground Service 1");
        d["ELEC_BusPowered_10"] = Annun("ELEC_BusPowered_10", "AC bus Ground Service 2");
        d["ELEC_BusPowered_11"] = Annun("ELEC_BusPowered_11", "AC bus Main 1");
        d["ELEC_BusPowered_12"] = Annun("ELEC_BusPowered_12", "AC bus Main 2");
        d["ELEC_BusPowered_13"] = Annun("ELEC_BusPowered_13", "AC bus Galley 1");
        d["ELEC_BusPowered_14"] = Annun("ELEC_BusPowered_14", "AC bus Galley 2");
        d["ELEC_BusPowered_15"] = Annun("ELEC_BusPowered_15", "AC bus Standby");

        // APU
        d["APU_annunMAINT"]            = Annun("APU_annunMAINT", "APU MAINT");
        d["APU_annunLOW_OIL_PRESSURE"] = Annun("APU_annunLOW_OIL_PRESSURE", "APU LOW OIL PRESSURE");
        d["APU_annunFAULT"]            = Annun("APU_annunFAULT", "APU FAULT");
        d["APU_annunOVERSPEED"]        = Annun("APU_annunOVERSPEED", "APU OVERSPEED");
        // APU EGT — continuous-numeric readout shown on the Electrical panel
        // next to APU Selector. Ramps during start; important for hot-start
        // detection (NG3 APU max EGT during start is ~760°C).
        d["APU_EGTNeedle"]             = Readout("APU_EGTNeedle", "APU EGT", "°C", "F0");

        // Wipers
        d["OH_WiperLSelector"] = Selector("OH_WiperLSelector", "Wiper Left", "PARK", "INT", "LOW", "HIGH");
        d["OH_WiperRSelector"] = Selector("OH_WiperRSelector", "Wiper Right", "PARK", "INT", "LOW", "HIGH");

        // Center overhead
        d["AIR_EquipCoolingSupplyNORM"]  = Toggle("AIR_EquipCoolingSupplyNORM", "Equip Cooling Supply", "ALTERNATE", "Normal");
        d["AIR_EquipCoolingExhaustNORM"] = Toggle("AIR_EquipCoolingExhaustNORM", "Equip Cooling Exhaust", "ALTERNATE", "Normal");
        d["AIR_annunEquipCoolingSupplyOFF"]  = Annun("AIR_annunEquipCoolingSupplyOFF", "Equip Cooling Supply Off");
        d["AIR_annunEquipCoolingExhaustOFF"] = Annun("AIR_annunEquipCoolingExhaustOFF", "Equip Cooling Exhaust Off");
        d["LTS_annunEmerNOT_ARMED"]      = Annun("LTS_annunEmerNOT_ARMED", "Emergency Lights NOT ARMED");
        d["LTS_EmerExitSelector"]        = Selector("LTS_EmerExitSelector", "Emergency Exit Lights",
            "OFF", "ARMED", "ON");
        // TFM: under-modeled as 2-position off/on switch.
        // SDK line 215: `unsigned char COMM_NoSmokingSelector; // 0: OFF  1: AUTO  2: ON`.
        // Resolved with SDK: this is a 3-position physical selector on the NG3 cockpit
        // (OFF/AUTO/ON). TFM is wrong; keep our 3-position dispatch.
        d["COMM_NoSmokingSelector"]      = Selector("COMM_NoSmokingSelector", "No Smoking", "OFF", "AUTO", "ON");
        d["COMM_FastenBeltsSelector"]    = Selector("COMM_FastenBeltsSelector", "Fasten Seat Belts", "OFF", "AUTO", "ON");
        d["COMM_annunCALL"]              = Annun("COMM_annunCALL", "Cabin Call");
        d["COMM_annunPA_IN_USE"]         = Annun("COMM_annunPA_IN_USE", "PA In Use");

        // Anti-Ice — Window heat (4 windows: 1=L FWD, 2=L SIDE, 3=R FWD, 4=R SIDE per SDK ordering)
        d["ICE_annunOVERHEAT_0"]  = Annun("ICE_annunOVERHEAT_0", "Window 1 Overheat");
        d["ICE_annunOVERHEAT_1"]  = Annun("ICE_annunOVERHEAT_1", "Window 2 Overheat");
        d["ICE_annunOVERHEAT_2"]  = Annun("ICE_annunOVERHEAT_2", "Window 3 Overheat");
        d["ICE_annunOVERHEAT_3"]  = Annun("ICE_annunOVERHEAT_3", "Window 4 Overheat");
        d["ICE_annunON_0"]        = Annun("ICE_annunON_0", "Window 1 Heat On");
        d["ICE_annunON_1"]        = Annun("ICE_annunON_1", "Window 2 Heat On");
        d["ICE_annunON_2"]        = Annun("ICE_annunON_2", "Window 3 Heat On");
        d["ICE_annunON_3"]        = Annun("ICE_annunON_3", "Window 4 Heat On");
        d["ICE_WindowHeatSw_0"]   = Toggle("ICE_WindowHeatSw_0", "Window 1 Heat");
        d["ICE_WindowHeatSw_1"]   = Toggle("ICE_WindowHeatSw_1", "Window 2 Heat");
        d["ICE_WindowHeatSw_2"]   = Toggle("ICE_WindowHeatSw_2", "Window 3 Heat");
        d["ICE_WindowHeatSw_3"]   = Toggle("ICE_WindowHeatSw_3", "Window 4 Heat");
        d["ICE_annunCAPT_PITOT"]      = Annun("ICE_annunCAPT_PITOT", "Captain Pitot");
        d["ICE_annunL_ELEV_PITOT"]    = Annun("ICE_annunL_ELEV_PITOT", "Left Elevator Pitot");
        d["ICE_annunL_ALPHA_VANE"]    = Annun("ICE_annunL_ALPHA_VANE", "Left Alpha Vane");
        d["ICE_annunL_TEMP_PROBE"]    = Annun("ICE_annunL_TEMP_PROBE", "Left Temp Probe");
        d["ICE_annunFO_PITOT"]        = Annun("ICE_annunFO_PITOT", "First Officer Pitot");
        d["ICE_annunR_ELEV_PITOT"]    = Annun("ICE_annunR_ELEV_PITOT", "Right Elevator Pitot");
        d["ICE_annunR_ALPHA_VANE"]    = Annun("ICE_annunR_ALPHA_VANE", "Right Alpha Vane");
        d["ICE_annunAUX_PITOT"]       = Annun("ICE_annunAUX_PITOT", "Aux Pitot");
        d["ICE_ProbeHeatSw_0"]        = Toggle("ICE_ProbeHeatSw_0", "Probe Heat 1");
        d["ICE_ProbeHeatSw_1"]        = Toggle("ICE_ProbeHeatSw_1", "Probe Heat 2");
        d["ICE_annunVALVE_OPEN_0"]    = Annun("ICE_annunVALVE_OPEN_0", "Wing Anti-Ice Valve 1 Open");
        d["ICE_annunVALVE_OPEN_1"]    = Annun("ICE_annunVALVE_OPEN_1", "Wing Anti-Ice Valve 2 Open");
        d["ICE_annunCOWL_ANTI_ICE_0"] = Annun("ICE_annunCOWL_ANTI_ICE_0", "Cowl 1 Anti-Ice");
        d["ICE_annunCOWL_ANTI_ICE_1"] = Annun("ICE_annunCOWL_ANTI_ICE_1", "Cowl 2 Anti-Ice");
        d["ICE_annunCOWL_VALVE_OPEN_0"] = Annun("ICE_annunCOWL_VALVE_OPEN_0", "Cowl 1 Valve Open");
        d["ICE_annunCOWL_VALVE_OPEN_1"] = Annun("ICE_annunCOWL_VALVE_OPEN_1", "Cowl 2 Valve Open");
        d["ICE_WingAntiIceSw"]        = Toggle("ICE_WingAntiIceSw", "Wing Anti-Ice");
        d["ICE_EngAntiIceSw_0"]       = Toggle("ICE_EngAntiIceSw_0", "Engine 1 Anti-Ice");
        d["ICE_EngAntiIceSw_1"]       = Toggle("ICE_EngAntiIceSw_1", "Engine 2 Anti-Ice");
        d["ICE_WindowHeatTestSw"]     = Selector("ICE_WindowHeatTestSw", "Window Heat Test",
            "OVHT", "Neutral", "PWR TEST");

        // Hydraulics
        d["HYD_annunLOW_PRESS_eng_0"]   = Annun("HYD_annunLOW_PRESS_eng_0", "Eng 1 Hyd Low Pressure");
        d["HYD_annunLOW_PRESS_eng_1"]   = Annun("HYD_annunLOW_PRESS_eng_1", "Eng 2 Hyd Low Pressure");
        // TFM: array-index swap on `HYD_PumpSw_elec` / `HYD_annunLOW_PRESS_elec` / `HYD_annunOVERHEAT_elec`
        // — TFM labels its "Electric hydraulic pump #1" off SDK index [1] and "#2" off [0].
        // SDK lines 242-245: arrays declared without inline comments.
        // Resolved with TFM's swap because (a) the SDK event ID sequence corroborates it —
        // EVT_OH_HYD_ELEC2 = event_base+167 and EVT_OH_HYD_ELEC1 = event_base+168 (ELEC2
        // assigned the LOWER ID, which in PMDG SDK ordering means ELEC2 corresponds to the
        // FIRST array slot), and (b) we already map "HYD_PumpSw_elec_0 → EVT_OH_HYD_ELEC1"
        // and "_1 → EVT_OH_HYD_ELEC2" in `_simpleEventMap`, but that compensation only fires
        // on user-initiated toggles; the cached read from `HYD_PumpSw_elec[0]` reports
        // pump 2's physical state. Swapping the display labels (so `_0` is presented as
        // "Pump 2" and `_1` as "Pump 1") restores parity between the announced state and
        // the cockpit pump number. The underlying SimVar keys are unchanged.
        d["HYD_annunLOW_PRESS_elec_0"]  = Annun("HYD_annunLOW_PRESS_elec_0", "Elec 2 Hyd Low Pressure");
        d["HYD_annunLOW_PRESS_elec_1"]  = Annun("HYD_annunLOW_PRESS_elec_1", "Elec 1 Hyd Low Pressure");
        d["HYD_annunOVERHEAT_elec_0"]   = Annun("HYD_annunOVERHEAT_elec_0", "Elec 2 Hyd Overheat");
        d["HYD_annunOVERHEAT_elec_1"]   = Annun("HYD_annunOVERHEAT_elec_1", "Elec 1 Hyd Overheat");
        d["HYD_PumpSw_eng_0"]           = Toggle("HYD_PumpSw_eng_0", "Eng 1 Hyd Pump");
        d["HYD_PumpSw_eng_1"]           = Toggle("HYD_PumpSw_eng_1", "Eng 2 Hyd Pump");
        d["HYD_PumpSw_elec_0"]          = Toggle("HYD_PumpSw_elec_0", "Elec 2 Hyd Pump");
        d["HYD_PumpSw_elec_1"]          = Toggle("HYD_PumpSw_elec_1", "Elec 1 Hyd Pump");

        // Air systems
        // TFM: `_airSourceSelectorStates` (cryptic abbreviations: scant/sfwd/saft/cfwd/caft/pckl/pckr).
        // SDK line 248: `unsigned char AIR_TempSourceSelector; // Positions 0..6` (no inline labels).
        // Resolved with the documented 737NG cockpit labels (SUPPLY DUCT, PASS CAB, CONT CAB,
        // PACK L, PACK R, RAM A, RAM B). Both TFM and SDK declare 7 positions; SDK has no label
        // detail to adjudicate against, but our labels match the standard 737NG temp source
        // knob detents per the FCOM. TFM's abbreviations look like internal field-namespace
        // identifiers, not user-facing labels.
        d["AIR_TempSourceSelector"]     = Selector("AIR_TempSourceSelector", "Temperature Source",
            "SUPPLY DUCT", "PASS CAB", "CONT CAB", "PACK L", "PACK R", "RAM A", "RAM B");
        d["AIR_TrimAirSwitch"]          = Toggle("AIR_TrimAirSwitch", "Trim Air");
        d["AIR_annunZoneTemp_0"]        = Annun("AIR_annunZoneTemp_0", "Forward Zone Temp");
        d["AIR_annunZoneTemp_1"]        = Annun("AIR_annunZoneTemp_1", "Cabin Zone Temp");
        d["AIR_annunZoneTemp_2"]        = Annun("AIR_annunZoneTemp_2", "Aft Zone Temp");
        d["AIR_annunDualBleed"]         = Annun("AIR_annunDualBleed", "Dual Bleed");
        d["AIR_annunRamDoorL"]          = Annun("AIR_annunRamDoorL", "Ram Door Left");
        d["AIR_annunRamDoorR"]          = Annun("AIR_annunRamDoorR", "Ram Door Right");
        d["AIR_RecircFanSwitch_0"]      = Toggle("AIR_RecircFanSwitch_0", "Recirc Fan Left");
        d["AIR_RecircFanSwitch_1"]      = Toggle("AIR_RecircFanSwitch_1", "Recirc Fan Right");
        d["AIR_PackSwitch_0"]           = Selector("AIR_PackSwitch_0", "Pack Left", "OFF", "AUTO", "HIGH");
        d["AIR_PackSwitch_1"]           = Selector("AIR_PackSwitch_1", "Pack Right", "OFF", "AUTO", "HIGH");
        d["AIR_BleedAirSwitch_0"]       = Toggle("AIR_BleedAirSwitch_0", "Engine 1 Bleed");
        d["AIR_BleedAirSwitch_1"]       = Toggle("AIR_BleedAirSwitch_1", "Engine 2 Bleed");
        d["AIR_APUBleedAirSwitch"]      = Toggle("AIR_APUBleedAirSwitch", "APU Bleed");
        d["AIR_IsolationValveSwitch"]   = Selector("AIR_IsolationValveSwitch", "Isolation Valve",
            "CLOSE", "AUTO", "OPEN");
        d["AIR_annunPackTripOff_0"]     = Annun("AIR_annunPackTripOff_0", "Pack Left Trip Off");
        d["AIR_annunPackTripOff_1"]     = Annun("AIR_annunPackTripOff_1", "Pack Right Trip Off");
        d["AIR_annunWingBodyOverheat_0"]= Annun("AIR_annunWingBodyOverheat_0", "Wing Body Overheat Left");
        d["AIR_annunWingBodyOverheat_1"]= Annun("AIR_annunWingBodyOverheat_1", "Wing Body Overheat Right");
        d["AIR_annunBleedTripOff_0"]    = Annun("AIR_annunBleedTripOff_0", "Bleed Trip Off Left");
        d["AIR_annunBleedTripOff_1"]    = Annun("AIR_annunBleedTripOff_1", "Bleed Trip Off Right");
        d["AIR_annunAUTO_FAIL"]         = Annun("AIR_annunAUTO_FAIL", "Pressurization Auto Fail");
        d["AIR_annunOFFSCHED_DESCENT"]  = Annun("AIR_annunOFFSCHED_DESCENT", "Off Schedule Descent");
        d["AIR_annunALTN"]              = Annun("AIR_annunALTN", "Pressurization ALTN");
        d["AIR_annunMANUAL"]            = Annun("AIR_annunMANUAL", "Pressurization MANUAL");
        // TFM: `_neutralClosedOrOpenStates` (0:opened, 1:neutral, 2:closed) — REVERSED.
        // SDK line 292: `unsigned int AIR_OutflowValveSwitch; // 0=CLOSE  1=NEUTRAL  2=OPEN`.
        // Resolved with SDK: TFM's dict has byte 0 and byte 2 labelled swapped from the SDK
        // header. Ours matches the SDK directly. Keep as-is.
        d["AIR_OutflowValveSwitch"]     = Selector("AIR_OutflowValveSwitch", "Outflow Valve",
            "CLOSE", "Neutral", "OPEN");
        d["AIR_PressurizationModeSelector"] = Selector("AIR_PressurizationModeSelector",
            "Pressurization Mode", "AUTO", "ALTN", "MAN");

        // Pressurization flight/landing altitude ENTRY (numeric _SET inputs).
        // Dispatched in HandleUIVariableSet to the PMDG "Direct Control" events
        // EVT_OH_PRESS_FLT_ALT_SET / _LAND_ALT_SET (literal feet). Synthetic
        // Name (not a struct field) + Never frequency, mirroring the existing
        // EFIS_MinsValueFt_*_SET inputs.
        d["AIR_FltAlt_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "_SYNTHETIC_AIR_FltAlt",
            DisplayName = "Flight Altitude",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            IsAnnounced = false,
        };
        d["AIR_LandAlt_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "_SYNTHETIC_AIR_LandAlt",
            DisplayName = "Landing Altitude",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            IsAnnounced = false,
        };
        d["AIR_FltAlt_SET"].CurrentValueSourceKey  = "AIR_FltAltWindow";
        d["AIR_LandAlt_SET"].CurrentValueSourceKey = "AIR_LandAltWindow";

        // Background monitors for FLT/LAND ALT — the numeric "window" values
        // that track the knob live (the char[6] display strings are obsolete
        // per the SDK; the uint windows update reliably — verified). Continuous
        // + IsAnnounced so a change from ANY source (knob, add-on, state load)
        // is spoken via ProcessSimVarUpdate. NOT placed on any panel.
        d["AIR_FltAltWindow"]  = Numeric("AIR_FltAltWindow",  "Flight Altitude");
        d["AIR_LandAltWindow"] = Numeric("AIR_LandAltWindow", "Landing Altitude");

        // Pressurization, duct-pressure, and cabin-temperature readouts —
        // continuous-numeric SDK fields rendered as read-only TextBoxes on
        // the Air Systems panel. IsAnnounced=false (no auto-announce every
        // broadcast); the pilot reads the current value by Tab-focusing the
        // field and letting the screen reader speak the text.
        d["AIR_CabinAltNeedle"]      = Readout("AIR_CabinAltNeedle", "Cabin Altitude", "ft", "F0");
        d["AIR_CabinDPNeedle"]       = Readout("AIR_CabinDPNeedle", "Cabin Differential Pressure", "PSI", "F2");
        d["AIR_CabinVSNeedle"]       = Readout("AIR_CabinVSNeedle", "Cabin Vertical Speed", "ft/min", "F0");
        // SDK doc says CabinValveNeedle is 0..1, but PMDG actually returns 0..100
        // (verified live: outflow valve fully open on the ground reads 100.0, not
        // 1.0). So format as plain integer percent — using "P0" would multiply
        // by another ×100 and display 10000 %.
        d["AIR_CabinValveNeedle"]    = Readout("AIR_CabinValveNeedle", "Outflow Valve Position", "%", "F0");
        d["AIR_DuctPress_0"]         = Readout("AIR_DuctPress_0", "Duct Pressure Left", "PSI", "F0");
        d["AIR_DuctPress_1"]         = Readout("AIR_DuctPress_1", "Duct Pressure Right", "PSI", "F0");
        // SDK doc says TemperatureNeedle is in °C, but PMDG actually returns °F
        // (verified live: supply duct read 141.11 cold-and-dark, which is
        // ~60 °C — plausible only as Fahrenheit; 141 °C is structurally
        // impossible). Apply the F→C transform: C = F·(5/9) + (-160/9).
        d["AIR_TemperatureNeedle"]   = Readout("AIR_TemperatureNeedle", "Temperature", "°C", "F0",
                                                scale: 5.0 / 9.0, offset: -160.0 / 9.0);
        // Bleed-overheat detection self-test. Pure momentary push; SDK exposes
        // only the event, no state field.
        d["AIR_BleedOvhtTest"]       = Momentary("AIR_BleedOvhtTest", "Bleed Overheat Test");

        // Doors
        d["DOOR_annunFWD_ENTRY"]          = Door("DOOR_annunFWD_ENTRY", "Forward Entry Door");
        d["DOOR_annunFWD_SERVICE"]        = Door("DOOR_annunFWD_SERVICE", "Forward Service Door");
        d["DOOR_annunAIRSTAIR"]           = Door("DOOR_annunAIRSTAIR", "Airstair Door");
        d["DOOR_annunLEFT_FWD_OVERWING"]  = Door("DOOR_annunLEFT_FWD_OVERWING", "Left Forward Overwing");
        d["DOOR_annunRIGHT_FWD_OVERWING"] = Door("DOOR_annunRIGHT_FWD_OVERWING", "Right Forward Overwing");
        d["DOOR_annunFWD_CARGO"]          = Door("DOOR_annunFWD_CARGO", "Forward Cargo");
        d["DOOR_annunEQUIP"]              = Door("DOOR_annunEQUIP", "Equipment Hatch");
        d["DOOR_annunLEFT_AFT_OVERWING"]  = Door("DOOR_annunLEFT_AFT_OVERWING", "Left Aft Overwing");
        d["DOOR_annunRIGHT_AFT_OVERWING"] = Door("DOOR_annunRIGHT_AFT_OVERWING", "Right Aft Overwing");
        d["DOOR_annunAFT_CARGO"]          = Door("DOOR_annunAFT_CARGO", "Aft Cargo");
        d["DOOR_annunAFT_ENTRY"]          = Door("DOOR_annunAFT_ENTRY", "Aft Entry Door");
        d["DOOR_annunAFT_SERVICE"]        = Door("DOOR_annunAFT_SERVICE", "Aft Service Door");

        // =================================================================
        // BOTTOM OVERHEAD — Landing lights, APU, engine start, ignition, exterior
        // =================================================================
        d["LTS_LandingLtRetractableSw_0"] = Selector("LTS_LandingLtRetractableSw_0",
            "Landing Light Left Retractable", "RETRACT", "EXTEND", "ON");
        d["LTS_LandingLtRetractableSw_1"] = Selector("LTS_LandingLtRetractableSw_1",
            "Landing Light Right Retractable", "RETRACT", "EXTEND", "ON");
        d["LTS_LandingLtFixedSw_0"]       = Toggle("LTS_LandingLtFixedSw_0", "Landing Light Left Fixed");
        d["LTS_LandingLtFixedSw_1"]       = Toggle("LTS_LandingLtFixedSw_1", "Landing Light Right Fixed");
        d["LTS_RunwayTurnoffSw_0"]        = Toggle("LTS_RunwayTurnoffSw_0", "Runway Turnoff Left");
        d["LTS_RunwayTurnoffSw_1"]        = Toggle("LTS_RunwayTurnoffSw_1", "Runway Turnoff Right");
        d["LTS_TaxiSw"]                   = Toggle("LTS_TaxiSw", "Taxi Lights");
        d["APU_Selector"]                 = Selector("APU_Selector", "APU", "OFF", "ON", "START");
        // TFM: `_engineStartModeStates` (0:grd, 1:auto, 2:cont, 3:flt) — position 1 labelled "auto".
        // SDK line 301: `unsigned char ENG_StartSelector[2]; // 0: GRD  1: OFF  2: CONT  3: FLT`.
        // Resolved with SDK: TFM labels position 1 as "auto" but SDK explicitly says "OFF",
        // and the 737NG engine start selector is documented as GRD/OFF/CONT/FLT (no AUTO
        // detent). Ours matches the SDK directly. Keep as-is.
        d["ENG_StartSelector_0"]          = Selector("ENG_StartSelector_0", "Engine 1 Start",
            "GRD", "OFF", "CONT", "FLT");
        d["ENG_StartSelector_1"]          = Selector("ENG_StartSelector_1", "Engine 2 Start",
            "GRD", "OFF", "CONT", "FLT");
        d["ENG_IgnitionSelector"]         = Selector("ENG_IgnitionSelector", "Ignition",
            "IGN L", "BOTH", "IGN R");
        // Fuel control levers (also called "start levers" / "fuel cutoff levers")
        // on the throttle quadrant — one per engine. Combo with state tracking.
        //
        //   READ:  Display state is derived from PMDG's FUEL_annunENG_VALVE_CLOSED
        //          byte (verified 2026-05-24 via test rig — values cycle correctly
        //          on dispatch: 0=Closed/CUTOFF, 1=Open/RUN, 2=In transit). The
        //          derivation collapses 2→1 since "in transit" means the lever
        //          was just commanded to RUN and is animating. See
        //          RaiseDerivedFieldOverrides in PMDGNG3DataManager.
        //
        //   WRITE: Dispatched via TransmitClientEvent + mouse flag:
        //              Run    (target=1) → LEFTSINGLE  (0x20000000) — verified
        //              Cutoff (target=0) → RIGHTSINGLE (0x80000000) — verified
        //          Verified live with the standalone PMDGDispatchTester rig
        //          (FUEL_annunENG_VALVE_CLOSED cycled 0→2→1 on LEFTSINGLE and
        //          1→2→0 on RIGHTSINGLE for both levers). Special case in
        //          HandleUIVariableSet handles the dispatch.
        d["ENG_StartLever_0"]             = Selector("ENG_StartLever_0", "Engine 1 Fuel Lever",
            "Cutoff", "Run");
        d["ENG_StartLever_1"]             = Selector("ENG_StartLever_1", "Engine 2 Fuel Lever",
            "Cutoff", "Run");
        d["LTS_LogoSw"]                   = Toggle("LTS_LogoSw", "Logo Lights");
        d["LTS_PositionSw"]               = Selector("LTS_PositionSw", "Position Lights",
            "STEADY", "OFF", "STROBE & STEADY");
        d["LTS_AntiCollisionSw"]          = Toggle("LTS_AntiCollisionSw", "Anti-Collision");
        d["LTS_WingSw"]                   = Toggle("LTS_WingSw", "Wing Lights");
        d["LTS_WheelWellSw"]              = Toggle("LTS_WheelWellSw", "Wheel Well Lights");

        // =================================================================
        // GLARESHIELD — Warnings
        // =================================================================
        d["WARN_annunFIRE_WARN_0"]      = Annun("WARN_annunFIRE_WARN_0", "Fire Warning Captain");
        d["WARN_annunFIRE_WARN_1"]      = Annun("WARN_annunFIRE_WARN_1", "Fire Warning First Officer");
        d["WARN_annunMASTER_CAUTION_0"] = Annun("WARN_annunMASTER_CAUTION_0", "Master Caution Captain");
        d["WARN_annunMASTER_CAUTION_1"] = Annun("WARN_annunMASTER_CAUTION_1", "Master Caution First Officer");
        d["WARN_annunFLT_CONT"]   = Annun("WARN_annunFLT_CONT", "FLT CONT");
        d["WARN_annunIRS"]        = Annun("WARN_annunIRS", "IRS Warning");
        d["WARN_annunFUEL"]       = Annun("WARN_annunFUEL", "Fuel Warning");
        d["WARN_annunELEC"]       = Annun("WARN_annunELEC", "Electrical Warning");
        d["WARN_annunAPU"]        = Annun("WARN_annunAPU", "APU Warning");
        d["WARN_annunOVHT_DET"]   = Annun("WARN_annunOVHT_DET", "Overheat / Detection");
        d["WARN_annunANTI_ICE"]   = Annun("WARN_annunANTI_ICE", "Anti-Ice Warning");
        d["WARN_annunHYD"]        = Annun("WARN_annunHYD", "Hydraulics Warning");
        d["WARN_annunDOORS"]      = Annun("WARN_annunDOORS", "Doors Warning");
        d["WARN_annunENG"]        = Annun("WARN_annunENG", "Engine Warning");
        d["WARN_annunOVERHEAD"]   = Annun("WARN_annunOVERHEAD", "Overhead Warning");
        d["WARN_annunAIR_COND"]   = Annun("WARN_annunAIR_COND", "Air Conditioning Warning");

        // Master Warning / Master Caution recall (momentary). Press once to
        // silence the corresponding light. PMDG NG3 recalls both sides on
        // either press, so the LEFT-side events are sufficient.
        d["WARN_ResetFireWarning"]  = Momentary("WARN_ResetFireWarning",  "Clear Fire Warning");
        d["WARN_ResetMasterCaution"] = Momentary("WARN_ResetMasterCaution", "Clear Master Caution");
        // Cabin altitude warning horn cutout — silences the warning horn after
        // a depressurization event so the crew can use the radios. Pure
        // momentary push; SDK exposes only the event, no state field.
        d["WARN_CabAltHornCutout"]   = Momentary("WARN_CabAltHornCutout", "Cabin Altitude Horn Cutout");

        // =================================================================
        // GLARESHIELD — EFIS Captain / First Officer
        // =================================================================
        d["EFIS_MinsSelBARO_0"]  = Toggle("EFIS_MinsSelBARO_0", "Captain Mins Mode", "RADIO", "BARO");
        d["EFIS_MinsSelBARO_1"]  = Toggle("EFIS_MinsSelBARO_1", "First Officer Mins Mode", "RADIO", "BARO");
        // EFIS Minimums altitude input. The PMDG NG3 SDK exposes no readback
        // for the minimums VALUE (only the RADIO/BARO mode toggle above), so
        // absolute setting uses a stateless RST-then-rotate-up dispatch in
        // HandleUIVariableSet. The synthetic Name is non-existent in the SDK
        // struct, so auto-monitoring will silently skip it; the _SET suffix
        // routes the var through MainForm's TextBox+Button render branch.
        d["EFIS_MinsValueFt_0_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "_SYNTHETIC_EFIS_MinsValueFt_0",
            DisplayName = "Captain Minimums (feet)",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            IsAnnounced = false,
        };
        d["EFIS_MinsValueFt_1_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "_SYNTHETIC_EFIS_MinsValueFt_1",
            DisplayName = "First Officer Minimums (feet)",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            IsAnnounced = false,
        };
        d["EFIS_BaroSelHPA_0"]   = Toggle("EFIS_BaroSelHPA_0", "Captain Baro Units", "inHg", "hPa");
        d["EFIS_BaroSelHPA_1"]   = Toggle("EFIS_BaroSelHPA_1", "First Officer Baro Units", "inHg", "hPa");
        d["EFIS_VORADFSel1_0"]   = Selector("EFIS_VORADFSel1_0", "Captain Bearing Pointer 1",
            "VOR", "OFF", "ADF");
        d["EFIS_VORADFSel1_1"]   = Selector("EFIS_VORADFSel1_1", "First Officer Bearing Pointer 1",
            "VOR", "OFF", "ADF");
        d["EFIS_VORADFSel2_0"]   = Selector("EFIS_VORADFSel2_0", "Captain Bearing Pointer 2",
            "VOR", "OFF", "ADF");
        d["EFIS_VORADFSel2_1"]   = Selector("EFIS_VORADFSel2_1", "First Officer Bearing Pointer 2",
            "VOR", "OFF", "ADF");
        d["EFIS_ModeSel_0"]      = Selector("EFIS_ModeSel_0", "Captain ND Mode",
            "APP", "VOR", "MAP", "PLAN");
        d["EFIS_ModeSel_1"]      = Selector("EFIS_ModeSel_1", "First Officer ND Mode",
            "APP", "VOR", "MAP", "PLAN");
        d["EFIS_RangeSel_0"]     = Selector("EFIS_RangeSel_0", "Captain ND Range",
            "5", "10", "20", "40", "80", "160", "320", "640");
        d["EFIS_RangeSel_1"]     = Selector("EFIS_RangeSel_1", "First Officer ND Range",
            "5", "10", "20", "40", "80", "160", "320", "640");

        // =================================================================
        // GLARESHIELD — Mode Control Panel (MCP)
        // =================================================================
        d["MCP_Course_0"]   = Numeric("MCP_Course_0", "Course Captain");
        d["MCP_Course_1"]   = Numeric("MCP_Course_1", "Course First Officer");
        d["MCP_IASMach"]    = Numeric("MCP_IASMach", "Speed");
        d["MCP_IASBlank"]   = Annun("MCP_IASBlank", "Speed Window Blank");
        d["MCP_Heading"]    = Numeric("MCP_Heading", "Heading");
        d["MCP_Altitude"]   = Numeric("MCP_Altitude", "Altitude");
        d["MCP_VertSpeed"]  = Numeric("MCP_VertSpeed", "Vertical Speed");
        d["MCP_VertSpeedBlank"] = Annun("MCP_VertSpeedBlank", "VS Window Blank");
        d["MCP_FDSw_0"]     = Toggle("MCP_FDSw_0", "Flight Director Captain");
        d["MCP_FDSw_1"]     = Toggle("MCP_FDSw_1", "Flight Director First Officer");
        d["MCP_ATArmSw"]    = Toggle("MCP_ATArmSw", "Autothrottle Arm", "OFF", "ARM");
        d["MCP_BankLimitSel"] = Selector("MCP_BankLimitSel", "Bank Limit",
            "10", "15", "20", "25", "30");
        d["MCP_DisengageBar"] = Toggle("MCP_DisengageBar", "AP Disengage Bar", "Up", "Down");
        d["MCP_annunFD_0"]   = Annun("MCP_annunFD_0", "FD Captain Master");
        d["MCP_annunFD_1"]   = Annun("MCP_annunFD_1", "FD First Officer Master");
        d["MCP_annunATArm"]  = Annun("MCP_annunATArm", "AT Arm");
        d["MCP_annunN1"]     = Annun("MCP_annunN1", "N1");
        d["MCP_annunSPEED"]  = Annun("MCP_annunSPEED", "Speed");
        d["MCP_annunVNAV"]   = Annun("MCP_annunVNAV", "VNAV");
        d["MCP_annunLVL_CHG"] = Annun("MCP_annunLVL_CHG", "Level Change");
        d["MCP_annunHDG_SEL"] = Annun("MCP_annunHDG_SEL", "Heading Select");
        d["MCP_annunLNAV"]   = Annun("MCP_annunLNAV", "LNAV");
        d["MCP_annunVOR_LOC"] = Annun("MCP_annunVOR_LOC", "VOR LOC");
        d["MCP_annunAPP"]    = Annun("MCP_annunAPP", "Approach");
        d["MCP_annunALT_HOLD"] = Annun("MCP_annunALT_HOLD", "Altitude Hold");
        d["MCP_annunVS"]     = Annun("MCP_annunVS", "Vertical Speed");
        d["MCP_annunCMD_A"]  = Annun("MCP_annunCMD_A", "CMD A");
        d["MCP_annunCWS_A"]  = Annun("MCP_annunCWS_A", "CWS A");
        d["MCP_annunCMD_B"]  = Annun("MCP_annunCMD_B", "CMD B");
        d["MCP_annunCWS_B"]  = Annun("MCP_annunCWS_B", "CWS B");
        // MCP autopilot push buttons (momentary). Dispatched via LEFTSINGLE+
        // LEFTRELEASE (see the momentary branch in HandleUIVariableSet) — verified
        // in-sim 2026-05-25: CDA param=1 does not commit these. The engaged state
        // is reflected by the MCP_annun* lamps above.
        d["MCP_CmdA"]    = Momentary("MCP_CmdA",    "CMD A",    "MCP_annunCMD_A");
        d["MCP_CmdB"]    = Momentary("MCP_CmdB",    "CMD B",    "MCP_annunCMD_B");
        d["MCP_CwsA"]    = Momentary("MCP_CwsA",    "CWS A",    "MCP_annunCWS_A");
        d["MCP_CwsB"]    = Momentary("MCP_CwsB",    "CWS B",    "MCP_annunCWS_B");
        d["MCP_AppBtn"]  = Momentary("MCP_AppBtn",  "Approach", "MCP_annunAPP");
        d["MCP_VorLoc"]  = Momentary("MCP_VorLoc",  "VOR LOC",  "MCP_annunVOR_LOC");
        // SDK lines 542-593: MCP_IASOverspeedFlash / MCP_IASUnderspeedFlash / MCP_indication_powered
        // (all bool). These are warning-light / indication STATES, not controls — announced on change
        // (see ProcessSimVarUpdate) but NOT listed in the MCP panel (mirrors the 777).
        d["MCP_IASOverspeedFlash"]  = Annun("MCP_IASOverspeedFlash",  "MCP Overspeed");
        d["MCP_IASUnderspeedFlash"] = Annun("MCP_IASUnderspeedFlash", "MCP Underspeed");
        d["MCP_indication_powered"] = Annun("MCP_indication_powered", "MCP Powered");

        // =================================================================
        // FORWARD PANEL — NWS, AP/AT annunciators, DU selectors, autobrake, etc.
        // =================================================================
        d["MAIN_NoseWheelSteeringSwNORM"] = Toggle("MAIN_NoseWheelSteeringSwNORM",
            "Nose Wheel Steering", "ALT", "Normal");
        d["MAIN_annunBELOW_GS_0"] = Annun("MAIN_annunBELOW_GS_0", "Below G/S Captain");
        d["MAIN_annunBELOW_GS_1"] = Annun("MAIN_annunBELOW_GS_1", "Below G/S First Officer");
        // SDK: MAIN_MainPanelDUSel — "0: OUTBD PFD ... 4 MFD for Capt; reverse sequence for FO".
        // Captain enum 0..4: OUTBD PFD, OUTBD ND, INBD PFD, INBD ND, MFD.
        // FO selector reads the same physical positions in reverse enum order, so the FO list
        // is the captain list reversed. Middle positions (OUTBD ND / INBD PFD / INBD ND) are
        // inferred from the standard 737NG-800 cockpit layout — verify in sim.
        d["MAIN_MainPanelDUSel_0"] = Selector("MAIN_MainPanelDUSel_0", "Main Panel DU Captain",
            "OUTBD PFD", "OUTBD ND", "INBD PFD", "INBD ND", "MFD");
        d["MAIN_MainPanelDUSel_1"] = Selector("MAIN_MainPanelDUSel_1", "Main Panel DU First Officer",
            "MFD", "INBD ND", "INBD PFD", "OUTBD ND", "OUTBD PFD");
        // SDK: MAIN_LowerDUSel — "0: ENG PRI ... 2 ND for Capt; reverse sequence for FO".
        // Middle position (NORM) inferred from the standard 737NG-800 cockpit layout — verify in sim.
        d["MAIN_LowerDUSel_0"]    = Selector("MAIN_LowerDUSel_0", "Lower DU Captain",
            "ENG PRI", "NORM", "ND");
        d["MAIN_LowerDUSel_1"]    = Selector("MAIN_LowerDUSel_1", "Lower DU First Officer",
            "ND", "NORM", "ENG PRI");
        d["MAIN_annunAP_0"]       = Annun("MAIN_annunAP_0", "AP Disengage Captain");
        d["MAIN_annunAP_1"]       = Annun("MAIN_annunAP_1", "AP Disengage First Officer");
        d["MAIN_annunAP_Amber_0"] = Annun("MAIN_annunAP_Amber_0", "AP Disengage Amber Captain");
        d["MAIN_annunAP_Amber_1"] = Annun("MAIN_annunAP_Amber_1", "AP Disengage Amber First Officer");
        d["MAIN_annunAT_0"]       = Annun("MAIN_annunAT_0", "AT Disengage Captain");
        d["MAIN_annunAT_1"]       = Annun("MAIN_annunAT_1", "AT Disengage First Officer");
        d["MAIN_annunAT_Amber_0"] = Annun("MAIN_annunAT_Amber_0", "AT Disengage Amber Captain");
        d["MAIN_annunAT_Amber_1"] = Annun("MAIN_annunAT_Amber_1", "AT Disengage Amber First Officer");
        // MAIN_annunFMC_0/_1 (the amber FMC caution light) intentionally NOT registered:
        // it illuminates whenever the CDU posts a scratchpad message, which is already
        // announced via CDU_annunMSG_0/_1. Announcing both is redundant. The 777 has no
        // FMC-caution annunciator either — match that pattern.
        d["MAIN_DisengageTestSelector_0"] = Selector("MAIN_DisengageTestSelector_0",
            "Disengage Test Captain", "1", "OFF", "2");
        d["MAIN_DisengageTestSelector_1"] = Selector("MAIN_DisengageTestSelector_1",
            "Disengage Test First Officer", "1", "OFF", "2");
        d["MAIN_annunSPEEDBRAKE_ARMED"]      = Annun("MAIN_annunSPEEDBRAKE_ARMED", "Speedbrake Armed");
        d["MAIN_annunSPEEDBRAKE_DO_NOT_ARM"] = Annun("MAIN_annunSPEEDBRAKE_DO_NOT_ARM", "Speedbrake Do Not Arm");
        d["MAIN_annunSPEEDBRAKE_EXTENDED"]   = Annun("MAIN_annunSPEEDBRAKE_EXTENDED", "Speedbrake Extended");
        d["MAIN_annunSTAB_OUT_OF_TRIM"]      = Annun("MAIN_annunSTAB_OUT_OF_TRIM", "Stab Out of Trim");
        d["MAIN_LightsSelector"]  = Selector("MAIN_LightsSelector", "Indicator Lights",
            "TEST", "BRT", "DIM");
        d["MAIN_RMISelector1_VOR"] = Toggle("MAIN_RMISelector1_VOR", "RMI 1", "ADF", "VOR");
        d["MAIN_RMISelector2_VOR"] = Toggle("MAIN_RMISelector2_VOR", "RMI 2", "ADF", "VOR");
        // SDK enum positions must match the order below verbatim — dropdown index becomes
        // the position parameter dispatched to the sim, so any reorder silently maps the
        // user's pick to the wrong physical selector position.
        d["MAIN_N1SetSelector"]    = Selector("MAIN_N1SetSelector", "N1 Set",
            "2", "1", "AUTO", "BOTH");                                  // SDK 391: 0:2 1:1 2:AUTO 3:BOTH
        d["MAIN_SpdRefSelector"]   = Selector("MAIN_SpdRefSelector", "Speed Reference",
            "SET", "AUTO", "V1", "VR", "WT", "VREF", "BUG");            // SDK 392: 0:SET 1:AUTO 2:V1 3:VR 4:WT 5:VREF 6:Bug
        d["MAIN_FuelFlowSelector"] = Selector("MAIN_FuelFlowSelector", "Fuel Flow",
            "RESET", "RATE", "USED");                                   // SDK 393: 0:RESET 1:RATE 2:USED
        // TFM: `_autoBrakeSelectorStates` (0:RTO 1:off 2:disarm 3:1 4:2 5:3).
        // SDK line 394: `unsigned char MAIN_AutobrakeSelector; // 0: RTO  1: OFF ... 5: MAX`.
        // Resolved with SDK: TFM exposes a "disarm" detent at position 2 and tops out at "3";
        // SDK says position 5 = MAX. The 737NG-800 autobrake selector is documented as
        // RTO/OFF/1/2/3/MAX (6 positions, with MAX, no DISARM — that's a 777 detent). TFM is
        // likely cross-pollinated from the 777 autobrake dict. Ours matches the SDK. Keep as-is.
        d["MAIN_AutobrakeSelector"] = new SimConnect.SimVarDefinition
        {
            Name = "MAIN_AutobrakeSelector",
            DisplayName = "Autobrake",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            PreventTextInput = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "RTO", [1] = "OFF", [2] = "1", [3] = "2", [4] = "3", [5] = "MAX"
            }
        };
        d["MAIN_annunANTI_SKID_INOP"]    = Annun("MAIN_annunANTI_SKID_INOP", "Anti-Skid Inop");
        d["MAIN_annunAUTO_BRAKE_DISARM"] = Annun("MAIN_annunAUTO_BRAKE_DISARM", "Auto Brake Disarm");
        // LE Flaps Transit light toggles on/off every time the flaps travel —
        // announcing it is chatty noise (the flap-position callout already
        // conveys movement). Keep it defined/monitored but NOT announced.
        d["MAIN_annunLE_FLAPS_TRANSIT"]  = new SimConnect.SimVarDefinition
        {
            Name = "MAIN_annunLE_FLAPS_TRANSIT",
            DisplayName = "LE Flaps Transit",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = false
        };
        // LE Flaps Ext light illuminates when the leading-edge devices are fully
        // extended — redundant with the flap-position callout, so keep it defined/
        // monitored but NOT announced (mirrors the LE Flaps Transit handling above).
        d["MAIN_annunLE_FLAPS_EXT"]      = new SimConnect.SimVarDefinition
        {
            Name = "MAIN_annunLE_FLAPS_EXT",
            DisplayName = "LE Flaps Ext",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = false
        };
        // TFM: [0]=nose, [1]=left, [2]=right. SDK lines 400-401: arrays declared without
        // inline comments. Resolved with the [0]=Left, [1]=Nose, [2]=Right convention because
        // the PMDG NG3 struct orders the related overhead annunciators as
        // `GEAR_annunOvhdLEFT` (line 93), `GEAR_annunOvhdNOSE` (94), `GEAR_annunOvhdRIGHT` (95)
        // — Left-Nose-Right is the documented PMDG struct ordering for gear-related state.
        // Both TFM and ours agree on [2]=Right; the disagreement is [0]/[1]. Verify in sim
        // during gear extension/retraction; if a tester reports the wrong leg appears in
        // transit, swap the "Left" and "Nose" labels on _0 and _1.
        d["MAIN_annunGEAR_transit_0"]    = Annun("MAIN_annunGEAR_transit_0", "Left Gear Transit");
        d["MAIN_annunGEAR_transit_1"]    = Annun("MAIN_annunGEAR_transit_1", "Nose Gear Transit");
        d["MAIN_annunGEAR_transit_2"]    = Annun("MAIN_annunGEAR_transit_2", "Right Gear Transit");
        d["MAIN_annunGEAR_locked_0"]     = Annun("MAIN_annunGEAR_locked_0", "Left Gear Locked Down");
        d["MAIN_annunGEAR_locked_1"]     = Annun("MAIN_annunGEAR_locked_1", "Nose Gear Locked Down");
        d["MAIN_annunGEAR_locked_2"]     = Annun("MAIN_annunGEAR_locked_2", "Right Gear Locked Down");
        d["MAIN_GearLever"]              = Selector("MAIN_GearLever", "Gear Lever", "UP", "OFF", "DOWN");
        // Trailing-edge flap needle (analog, degrees). Monitored only — the
        // flap-position announcement snaps it to a detent in ProcessSimVarUpdate.
        // IsAnnounced=false so the generic path doesn't read out the raw float.
        d["MAIN_TEFlapsNeedle_0"] = new SimConnect.SimVarDefinition
        {
            Name = "MAIN_TEFlapsNeedle_0",
            DisplayName = "Flaps",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = false
        };
        d["MAIN_annunCABIN_ALTITUDE"]    = Annun("MAIN_annunCABIN_ALTITUDE", "Cabin Altitude");
        d["MAIN_annunTAKEOFF_CONFIG"]    = Annun("MAIN_annunTAKEOFF_CONFIG", "Takeoff Config");

        // HGS forward annunciators
        d["HGS_annun_AIII"]    = Annun("HGS_annun_AIII", "HGS AIII");
        d["HGS_annun_NO_AIII"] = Annun("HGS_annun_NO_AIII", "HGS NO AIII");
        d["HGS_annun_FLARE"]   = Annun("HGS_annun_FLARE", "HGS FLARE");
        d["HGS_annun_RO"]      = Annun("HGS_annun_RO", "HGS RO");
        d["HGS_annun_RO_CTN"]  = Annun("HGS_annun_RO_CTN", "HGS RO CTN");
        d["HGS_annun_RO_ARM"]  = Annun("HGS_annun_RO_ARM", "HGS RO ARM");
        d["HGS_annun_TO"]      = Annun("HGS_annun_TO", "HGS TO");
        d["HGS_annun_TO_CTN"]  = Annun("HGS_annun_TO_CTN", "HGS TO CTN");
        d["HGS_annun_APCH"]    = Annun("HGS_annun_APCH", "HGS APCH");
        d["HGS_annun_TO_WARN"] = Annun("HGS_annun_TO_WARN", "HGS TO WARN");
        d["HGS_annun_Bar"]     = Annun("HGS_annun_Bar", "HGS Bar");
        d["HGS_annun_FAIL"]    = Annun("HGS_annun_FAIL", "HGS Fail");

        // Lower forward panel — GPWS
        d["GPWS_annunINOP"]            = Annun("GPWS_annunINOP", "GPWS Inop");
        d["GPWS_FlapInhibitSw_NORM"]   = Toggle("GPWS_FlapInhibitSw_NORM", "Flap Inhibit", "INHIBIT", "Normal");
        d["GPWS_GearInhibitSw_NORM"]   = Toggle("GPWS_GearInhibitSw_NORM", "Gear Inhibit", "INHIBIT", "Normal");
        d["GPWS_TerrInhibitSw_NORM"]   = Toggle("GPWS_TerrInhibitSw_NORM", "Terrain Inhibit", "INHIBIT", "Normal");
        // GPWS system test — momentary push; plays the EGPWS self-test sequence
        // (visual + audible). SDK exposes only the event, no state field.
        d["GPWS_SysTest"]              = Momentary("GPWS_SysTest", "GPWS System Test");

        // =================================================================
        // CONTROL STAND — CDU annunciators, COMM counters, ACP, stab trim, fire, xpdr, etc.
        // =================================================================
        // CDU annunciators — NG3 SDK has only 2 CDUs (0 = Left/Captain, 1 = Right/F.O.).
        // DisplayName follows the 777 "CDU <TYPE> <position>" pattern, using Left/Right
        // (not Capt/F.O.) so announcements read e.g. "CDU EXEC Left: on" like the 777.
        d["CDU_annunEXEC_0"]  = Annun("CDU_annunEXEC_0", "CDU EXEC Left");
        d["CDU_annunEXEC_1"]  = Annun("CDU_annunEXEC_1", "CDU EXEC Right");
        d["CDU_annunCALL_0"]  = Annun("CDU_annunCALL_0", "CDU CALL Left");
        d["CDU_annunCALL_1"]  = Annun("CDU_annunCALL_1", "CDU CALL Right");
        d["CDU_annunFAIL_0"]  = Annun("CDU_annunFAIL_0", "CDU FAIL Left");
        d["CDU_annunFAIL_1"]  = Annun("CDU_annunFAIL_1", "CDU FAIL Right");
        d["CDU_annunMSG_0"]   = Annun("CDU_annunMSG_0", "CDU MSG Left");
        d["CDU_annunMSG_1"]   = Annun("CDU_annunMSG_1", "CDU MSG Right");
        d["CDU_annunOFST_0"]  = Annun("CDU_annunOFST_0", "CDU OFST Left");
        d["CDU_annunOFST_1"]  = Annun("CDU_annunOFST_1", "CDU OFST Right");

        // COMM press counters
        d["COMM_Attend_PressCount"]  = new SimConnect.SimVarDefinition
        {
            Name = "COMM_Attend_PressCount",
            DisplayName = "Attendant Call",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        };
        d["COMM_GrdCall_PressCount"] = new SimConnect.SimVarDefinition
        {
            Name = "COMM_GrdCall_PressCount",
            DisplayName = "Ground Call",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        };

        // ACP selected mic & receivers.
        // The SDK array is 3-wide for binary compatibility, but the 737 cockpit has no
        // observer ACP — index 2 always reads as 0 and is not exposed in the panel.
        // SDK line 721: `byte[3] COMM_SelectedMic` — single-selection enum 0..7 per ACP.
        // Per-position labels match TFM's `_micSelectorStates`. Each position has its own
        // dedicated EVT_ACP_*_MIC_X event (not a stepwise selector) — dispatch is
        // special-cased in HandleUIVariableSet by looking up the per-position event.
        d["COMM_SelectedMic_0"] = Selector("COMM_SelectedMic_0", "Captain Selected Mic",
            "VHF1", "VHF2", "VHF3", "HF1", "HF2", "FLT", "SVC", "PA");
        d["COMM_SelectedMic_1"] = Selector("COMM_SelectedMic_1", "First Officer Selected Mic",
            "VHF1", "VHF2", "VHF3", "HF1", "HF2", "FLT", "SVC", "PA");
        // SDK line 723: `uint[3] COMM_ReceiverSwitches` — bitmask of currently-selected
        // receivers per ACP (ACP_SEL_RECV_* flags, SDK lines 610-623). Each receiver button
        // is an independent momentary toggle (EVT_ACP_*_REC_X events), so the UI can't
        // model this as a single-selection selector. Exposed as Display for status query
        // (value rendered as bitmask decode in ProcessSimVarUpdate). Index 2 is the overhead
        // ACP — surfaced because the overhead ACP DOES exist on the 737 (the observer one
        // doesn't). TFM exposes all three.
        d["COMM_ReceiverSwitches_0"] = Display("COMM_ReceiverSwitches_0", "Captain Receivers");
        d["COMM_ReceiverSwitches_1"] = Display("COMM_ReceiverSwitches_1", "First Officer Receivers");
        d["COMM_ReceiverSwitches_2"] = Display("COMM_ReceiverSwitches_2", "Overhead Receivers");

        // Stab trim
        d["TRIM_StabTrimMainElecSw_NORMAL"] = Toggle("TRIM_StabTrimMainElecSw_NORMAL",
            "Main Elec Stab Trim", "CUTOUT", "Normal");
        d["TRIM_StabTrimAutoPilotSw_NORMAL"] = Toggle("TRIM_StabTrimAutoPilotSw_NORMAL",
            "AP Stab Trim", "CUTOUT", "Normal");
        d["TRIM_StabTrimSw_NORMAL"] = Toggle("TRIM_StabTrimSw_NORMAL",
            "Stab Trim Override", "OVERRIDE", "Normal");
        d["PED_annunParkingBrake"]  = Annun("PED_annunParkingBrake", "Parking Brake");

        // Fire protection
        d["FIRE_OvhtDetSw_0"] = Selector("FIRE_OvhtDetSw_0", "Engine 1 Overheat Detect", "A", "NORMAL", "B");
        d["FIRE_OvhtDetSw_1"] = Selector("FIRE_OvhtDetSw_1", "Engine 2 Overheat Detect", "A", "NORMAL", "B");
        d["FIRE_annunENG_OVERHEAT_0"] = Annun("FIRE_annunENG_OVERHEAT_0", "Engine 1 Overheat");
        d["FIRE_annunENG_OVERHEAT_1"] = Annun("FIRE_annunENG_OVERHEAT_1", "Engine 2 Overheat");
        // SDK 459: unsigned char FIRE_DetTestSw — "0: FAULT/INOP  1: neutral  2: OVHT/FIRE".
        // Spring-loaded 3-position selector, NOT a momentary push button. Treating it as
        // momentary made every press snap to position 1 ("neutral") regardless of the
        // dropdown choice; selector dispatch via _simpleEventMap sends (int)value as target.
        d["FIRE_DetTestSw"] = Selector("FIRE_DetTestSw", "Fire Detection Test",
            "FAULT/INOP", "Neutral", "OVHT/FIRE");
        // Fire handle positions (read-only state; pressed via momentary buttons below).
        // NG3 index labeling: 0 = Engine 1, 1 = APU, 2 = Engine 2. Inferred from sequential
        // SDK event-ID ordering (EVT_FIRE_HANDLE_ENGINE_1_TOP=697, _APU_TOP=698, _ENGINE_2_TOP=699;
        // EVT_FIRE_UNLOCK_SWITCH_ENGINE_1=976, _APU=977, _ENGINE_2=978). Requires an active
        // fire scenario to verify in sim — handles are mechanically locked otherwise.
        // Values 0..4 per SDK: In / Blocked / Out / Turned Left / Turned Right.
        //
        // TFM disagrees: it uses [0]=Left/Eng1, [1]=Right/Eng2, [2]=APU. We don't follow
        // TFM here because the SDK event-ID sequence (697/698/699 = Eng1/APU/Eng2) is a
        // stronger technical signal than TFM's convention. If a tester reports the wrong
        // handle moves on a fire press, swap the DisplayName strings on _1 and _2 (and on
        // FIRE_HandleIlluminated_1 / _2). Documented in docs/pmdg-737.md.
        d["FIRE_HandlePos_0"] = new SimConnect.SimVarDefinition
        {
            Name = "FIRE_HandlePos_0",
            DisplayName = "Engine 1 fire handle position",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                { 0, "in" }, { 1, "blocked" }, { 2, "out" }, { 3, "turned left" }, { 4, "turned right" }
            }
        };
        d["FIRE_HandlePos_1"] = new SimConnect.SimVarDefinition
        {
            Name = "FIRE_HandlePos_1",
            DisplayName = "APU fire handle position",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                { 0, "in" }, { 1, "blocked" }, { 2, "out" }, { 3, "turned left" }, { 4, "turned right" }
            }
        };
        d["FIRE_HandlePos_2"] = new SimConnect.SimVarDefinition
        {
            Name = "FIRE_HandlePos_2",
            DisplayName = "Engine 2 fire handle position",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                { 0, "in" }, { 1, "blocked" }, { 2, "out" }, { 3, "turned left" }, { 4, "turned right" }
            }
        };
        // Synthetic momentary press buttons for fire handles.
        // TOP = primary press (advances handle state); BOTTOM = secondary press.
        // PMDG events are momentary — parameter is ignored, each press advances by one position.
        d["FIRE_EngineHandle_1_Press"]       = Momentary("FIRE_EngineHandle_1_Press",       "Press engine 1 fire handle (top)");
        d["FIRE_EngineHandle_2_Press"]       = Momentary("FIRE_EngineHandle_2_Press",       "Press engine 2 fire handle (top)");
        d["FIRE_APUHandle_Press"]            = Momentary("FIRE_APUHandle_Press",            "Press APU fire handle (top)");
        d["FIRE_EngineHandle_1_PressBottom"] = Momentary("FIRE_EngineHandle_1_PressBottom", "Press engine 1 fire handle (bottom)");
        d["FIRE_EngineHandle_2_PressBottom"] = Momentary("FIRE_EngineHandle_2_PressBottom", "Press engine 2 fire handle (bottom)");
        d["FIRE_APUHandle_PressBottom"]      = Momentary("FIRE_APUHandle_PressBottom",      "Press APU fire handle (bottom)");

        // Control stand / pedestal momentary buttons. TO/GA and AT-disengage are
        // momentary presses (LEFTSINGLE+RELEASE via the momentary dispatch path).
        // Parking brake is intentionally NOT exposed yet: in-sim the NG3 park-brake
        // lever event (and the standard PARKING_BRAKES toggle) did not move
        // PED_annunParkingBrake, likely the real toe-brake-pressure latch gate — a
        // control would appear inert, so it is deferred pending further investigation.
        d["CS_TOGA_1"]   = Momentary("CS_TOGA_1",   "TO/GA Left");
        d["CS_TOGA_2"]   = Momentary("CS_TOGA_2",   "TO/GA Right");
        d["CS_ATDisc_1"] = Momentary("CS_ATDisc_1", "Autothrottle Disengage Left");
        d["CS_ATDisc_2"] = Momentary("CS_ATDisc_2", "Autothrottle Disengage Right");
        // Same array convention as FIRE_HandlePos: [0]=Eng1, [1]=APU, [2]=Eng2.
        d["FIRE_HandleIlluminated_0"] = Annun("FIRE_HandleIlluminated_0", "Engine 1 Fire Handle Illuminated");
        d["FIRE_HandleIlluminated_1"] = Annun("FIRE_HandleIlluminated_1", "APU Fire Handle Illuminated");
        d["FIRE_HandleIlluminated_2"] = Annun("FIRE_HandleIlluminated_2", "Engine 2 Fire Handle Illuminated");
        d["FIRE_annunWHEEL_WELL"]         = Annun("FIRE_annunWHEEL_WELL", "Wheel Well Fire");
        d["FIRE_annunFAULT"]              = Annun("FIRE_annunFAULT", "Fire Fault");
        d["FIRE_annunAPU_DET_INOP"]       = Annun("FIRE_annunAPU_DET_INOP", "APU Detection Inop");
        d["FIRE_annunAPU_BOTTLE_DISCHARGE"] = Annun("FIRE_annunAPU_BOTTLE_DISCHARGE", "APU Bottle Discharge");
        d["FIRE_annunBOTTLE_DISCHARGE_0"] = Annun("FIRE_annunBOTTLE_DISCHARGE_0", "Engine 1 Bottle Discharge");
        d["FIRE_annunBOTTLE_DISCHARGE_1"] = Annun("FIRE_annunBOTTLE_DISCHARGE_1", "Engine 2 Bottle Discharge");
        d["FIRE_ExtinguisherTestSw"] = Selector("FIRE_ExtinguisherTestSw", "Extinguisher Test",
            "1", "Neutral", "2");
        d["FIRE_annunExtinguisherTest_0"] = Annun("FIRE_annunExtinguisherTest_0", "Extinguisher Test Left");
        d["FIRE_annunExtinguisherTest_1"] = Annun("FIRE_annunExtinguisherTest_1", "Extinguisher Test Right");
        d["FIRE_annunExtinguisherTest_2"] = Annun("FIRE_annunExtinguisherTest_2", "Extinguisher Test APU");

        // Cargo fire
        d["CARGO_annunExtTest_0"] = Annun("CARGO_annunExtTest_0", "Cargo Fwd Ext Test");
        d["CARGO_annunExtTest_1"] = Annun("CARGO_annunExtTest_1", "Cargo Aft Ext Test");
        d["CARGO_DetSelect_0"]    = Selector("CARGO_DetSelect_0", "Cargo Fwd Det Select", "A", "NORM", "B");
        d["CARGO_DetSelect_1"]    = Selector("CARGO_DetSelect_1", "Cargo Aft Det Select", "A", "NORM", "B");
        d["CARGO_ArmedSw_0"]      = Toggle("CARGO_ArmedSw_0", "Cargo Fwd Arm", "OFF", "ARM");
        d["CARGO_ArmedSw_1"]      = Toggle("CARGO_ArmedSw_1", "Cargo Aft Arm", "OFF", "ARM");
        d["CARGO_annunFWD"]       = Annun("CARGO_annunFWD", "Cargo Forward Fire");
        d["CARGO_annunAFT"]       = Annun("CARGO_annunAFT", "Cargo Aft Fire");
        d["CARGO_annunDETECTOR_FAULT"] = Annun("CARGO_annunDETECTOR_FAULT", "Cargo Detector Fault");
        d["CARGO_annunDISCH"]     = Annun("CARGO_annunDISCH", "Cargo Bottle Discharge");

        // HGS pedestal annunciators
        d["HGS_annunRWY"]   = Annun("HGS_annunRWY", "HGS Runway");
        d["HGS_annunGS"]    = Annun("HGS_annunGS", "HGS Glideslope");
        d["HGS_annunFAULT"] = Annun("HGS_annunFAULT", "HGS Pedestal Fault");
        d["HGS_annunCLR"]   = Annun("HGS_annunCLR", "HGS Clear");

        // Transponder
        d["XPDR_XpndrSelector_2"] = Toggle("XPDR_XpndrSelector_2", "Transponder", "1", "2");
        d["XPDR_AltSourceSel_2"]  = Toggle("XPDR_AltSourceSel_2", "Transponder Alt Source", "1", "2");
        // SDK 485: "0: STBY  1: ALT RPTG OFF ... 4: TA/RA". Positions 2/3 inferred as the
        // standard 737NG-800 transponder mode labels; aligned with PMDG777Definition's wording.
        d["XPDR_ModeSel"]         = Selector("XPDR_ModeSel", "Transponder Mode",
            "Stby", "Alt Rptg Off", "Xpndr", "TA Only", "TA/RA");
        d["XPDR_annunFAIL"]       = Annun("XPDR_annunFAIL", "Transponder Fail");
        // TCAS self-test — momentary push; plays the TCAS test sequence (TA/RA
        // audio + display callouts). SDK exposes only the event, no state field.
        d["XPDR_TcasTest"]        = Momentary("XPDR_TcasTest", "TCAS Test");
        // Transponder IDENT — momentary push; squawks IDENT to make the aircraft
        // blip flash on ATC radar for ~18 seconds. SDK exposes only the event.
        d["XPDR_Ident"]           = Momentary("XPDR_Ident", "Ident");

        // Flight deck door
        d["PED_annunLOCK_FAIL"]   = Annun("PED_annunLOCK_FAIL", "Door Lock Fail");
        d["PED_annunAUTO_UNLK"]   = Annun("PED_annunAUTO_UNLK", "Door Auto Unlock");
        // PED_FltDkDoorSel: positions 0 (UNLKD), 1 (AUTO Pushed In) and 3 (DENY) are
        // spring-loaded — only position 2 (AUTO) is stable. A combo selector can't
        // reach the spring-loaded positions (they immediately return to AUTO), so this
        // is exposed as a read-only status field. The two synthetic momentary buttons
        // below dispatch EVT_FLT_DK_DOOR_KNOB with parameter 0 (Unlock) or 3 (Deny)
        // for the press-and-spring-back cockpit interaction.
        d["PED_FltDkDoorSel"]     = new SimConnect.SimVarDefinition
        {
            Name = "PED_FltDkDoorSel",
            DisplayName = "Flight Deck Door",
            Type = SimConnect.SimVarType.PMDGVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "UNLKD", [1] = "AUTO Pushed In", [2] = "AUTO", [3] = "DENY"
            }
        };
        d["PED_FltDkDoor_Unlock"] = Momentary("PED_FltDkDoor_Unlock", "Unlock flight deck door");
        d["PED_FltDkDoor_Deny"]   = Momentary("PED_FltDkDoor_Deny",   "Deny flight deck entry");

        // =================================================================
        // FMS — V-speeds, flaps, cruise / landing alt, transition, perf
        // =================================================================
        d["FMC_TakeoffFlaps"]      = Numeric("FMC_TakeoffFlaps", "Takeoff Flaps");
        d["FMC_V1"]                = Numeric("FMC_V1", "V1");
        d["FMC_VR"]                = Numeric("FMC_VR", "VR");
        d["FMC_V2"]                = Numeric("FMC_V2", "V2");
        d["FMC_LandingFlaps"]      = Numeric("FMC_LandingFlaps", "Landing Flaps");
        d["FMC_LandingVREF"]       = Numeric("FMC_LandingVREF", "VREF");
        d["FMC_CruiseAlt"]         = Numeric("FMC_CruiseAlt", "Cruise Altitude");
        d["FMC_LandingAltitude"]   = Numeric("FMC_LandingAltitude", "Landing Altitude");
        d["FMC_TransitionAlt"]     = Numeric("FMC_TransitionAlt", "Transition Altitude");
        d["FMC_TransitionLevel"]   = Numeric("FMC_TransitionLevel", "Transition Level");
        d["FMC_PerfInputComplete"] = Toggle("FMC_PerfInputComplete", "Performance Input Complete", "Incomplete", "Complete");
        d["FMC_DistanceToTOD"]     = Quantity("FMC_DistanceToTOD", "Distance To Top of Descent");
        d["FMC_DistanceToDest"]    = Quantity("FMC_DistanceToDest", "Distance To Destination");
        d["FMC_flightNumber"]      = Display("FMC_flightNumber", "Flight Number");

        // General / misc
        d["WeightInKg"]          = Toggle("WeightInKg", "Weight Units", "Pounds", "Kilograms");
        d["GPWS_V1CallEnabled"]  = Toggle("GPWS_V1CallEnabled", "GPWS V1 Call");
        d["GroundConnAvailable"] = Toggle("GroundConnAvailable", "Ground Connections", "Not Available", "Available");

        // =================================================================
        // STANDARD SIMVARS (NOT PMDG SDK)
        // =================================================================
        d["ALTIMETER_SETTING"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG",
            DisplayName = "Altimeter Setting",
            Type = SimConnect.SimVarType.SimVar,
            Units = "inHg",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        };
        d["TRANSPONDER_CODE_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "TRANSPONDER CODE:1",
            DisplayName = "Squawk Code",
            Type = SimConnect.SimVarType.SimVar,
            Units = "BCO16",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        };
        d["COM1_ActiveFreq"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:1",
            DisplayName = "COM1 Active",
            Type = SimConnect.SimVarType.SimVar,
            Units = "MHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            PreventTextInput = true
        };
        d["COM_STANDBY_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:1",
            DisplayName = "COM1 Standby",
            Type = SimConnect.SimVarType.SimVar,
            Units = "MHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        };
        d["COM1_RADIO_SWAP"] = new SimConnect.SimVarDefinition
        {
            Name = "COM_STBY_RADIO_SWAP",
            DisplayName = "COM1 Swap",
            Type = SimConnect.SimVarType.Event,
            RenderAsButton = true,
            IsMomentary = true,
            HelpText = "Swap COM1 active and standby frequencies"
        };
        d["COM2_ActiveFreq"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:2",
            DisplayName = "COM2 Active",
            Type = SimConnect.SimVarType.SimVar,
            Units = "MHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            PreventTextInput = true
        };
        d["COM_STANDBY_FREQUENCY_SET:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:2",
            DisplayName = "COM2 Standby",
            Type = SimConnect.SimVarType.SimVar,
            Units = "MHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        };
        d["COM2_RADIO_SWAP"] = new SimConnect.SimVarDefinition
        {
            Name = "COM2_RADIO_SWAP",
            DisplayName = "COM2 Swap",
            Type = SimConnect.SimVarType.Event,
            RenderAsButton = true,
            IsMomentary = true,
            HelpText = "Swap COM2 active and standby frequencies"
        };

        // =================================================================
        // BORIS / xBAW AUDIO WORKS SOUNDPACK (737)
        // -----------------------------------------------------------------
        // Mirrors the 777's Boris Audio Works panel, but the 737 xBAW soundset
        // uses DIFFERENT backing variables (verified against the soundset's
        // sound.xml and in-sim 2026-05-26):
        //   Headphone Simulation -> L:ANR_onoff      (RTPC LOCALVAR_A20)
        //   Hydraulic Pump Model -> L:HydPumpMfg     (RTPC SIMVAR_ON_RUNWAY)
        //   Passenger Chatter    -> L:switch_277_73X (paxchatter <Requires> <=0)
        // Unlike the 777's PMDG-owned switch_NNN_a (which revert a raw write and
        // need K:ROTOR_BRAKE), all three of these are plain L-vars that accept a
        // direct SetLVar and persist — see the LVar branch in HandleUIVariableSet.
        // switch_102_73X (Emer Exit Lights, the soundset's other chatter gate) is
        // deliberately NOT exposed: its normal flight position already satisfies
        // the <=0 gate and it's a safety system, exactly like the 777's
        // switch_49_a. switch_277_73X is the safety-neutral audio selector.
        // =================================================================
        d["ANR_onoff"] = new SimConnect.SimVarDefinition
        {
            Name = "ANR_onoff",
            DisplayName = "Headphone Simulation",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };
        d["HydPumpMfg"] = new SimConnect.SimVarDefinition
        {
            Name = "HydPumpMfg",
            DisplayName = "Hydraulic Pump Model",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Vickers 1", [1] = "Vickers 2" }
        };
        // Passenger chatter: the soundset gates paxchatter on switch_277_73X <= 0,
        // so 0 = chatter audible (On), 100 = muted (Off). ReverseDisplayOrder puts
        // "Off" at the top of the combo to match every other on/off control,
        // despite On being the zero value (same treatment as the 777's chatter).
        d["switch_277_73X"] = new SimConnect.SimVarDefinition
        {
            Name = "switch_277_73X",
            DisplayName = "Passenger Chatter",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ReverseDisplayOrder = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [100] = "Off" }
        };

        // =================================================================
        // CABIN & COCKPIT FURNITURE (737)
        // -----------------------------------------------------------------
        // All L-var names and polarities are read directly from PMDG's own
        // model behavior XMLs (verified 2026-06-11/12):
        //   - 73X_Cockpit_Behavior.xml      (windows, visors, shades, headrests,
        //                                    rudder-pedal adjust)
        //   - 73X_Cabin_Ceiling_Behavior.xml (overhead bins — "SPACE BIN nL/nR")
        //   - 73X_Cabin_Walls_Behavior.xml   (passenger window blinds, cabin lights)
        //   - 73X_Galley_Fwd/Aft_Behavior.xml (galley lights, curtains, lav doors)
        // These are plain L-vars PMDG's VC click-spots toggle directly, so a
        // direct SetLVar reproduces the cockpit/cabin interaction exactly
        // (same mechanism as the xBAW vars above). The sliding windows are the
        // one exception — PMDG's click code ALSO fires K:TOGGLE_AIRCRAFT_EXIT_FAST
        // (exit 16 = Captain, 17 = F/O) and honors an L:CanOpenWindows guard, so
        // those are special-cased in HandleUIVariableSet.
        // Attendant/Ground call buttons use the SDK events (CDA, parameter 1 —
        // live-verified 2026-06-11: press counters increment; note parameter 0
        // ALSO registers as a press, same as the CDU keys). The existing
        // COMM_*_PressCount announce handler provides the audible confirmation.
        // =================================================================
        d["COMM_AttendCallBtn"] = Momentary("COMM_AttendCallBtn", "Call Attendants");
        d["COMM_AttendCallBtn"].HelpText = "Press the overhead ATTEND button (cabin chime)";
        d["COMM_GrndCallBtn"] = Momentary("COMM_GrndCallBtn", "Call Ground Crew");
        d["COMM_GrndCallBtn"].HelpText =
            "Press the overhead GRD CALL button. The ground-crew horn keeps sounding until pressed again";

        // Sliding cockpit windows. PMDG only allows opening when L:CanOpenWindows
        // is set (on the ground, slow, unpressurized) — when blocked, the set is
        // silently ignored by the guard and the state announcement simply won't fire.
        SimConnect.SimVarDefinition WindowVar(string lvar, string display) => new()
        {
            Name = lvar,
            DisplayName = display,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" },
            HelpText = "Sliding cockpit window. Only opens on the ground at low speed"
        };
        d["Window_OpenClose_CA"] = WindowVar("Window_OpenClose_CA", "Captain Window");
        d["Window_OpenClose_FO"] = WindowVar("Window_OpenClose_FO", "First Officer Window");

        // Sun visors (0 = stowed, 1 = deployed across the window). PMDG blocks
        // deploying while the same-side window is open — mirrored in dispatch.
        SimConnect.SimVarDefinition CabinToggle(string lvar, string display, string off, string on, string? help = null) => new()
        {
            Name = lvar,
            DisplayName = display,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on },
            HelpText = help
        };
        d["visors_stow_unstow_L"] = CabinToggle("visors_stow_unstow_L", "Captain Sun Visor", "Stowed", "Deployed");
        d["visors_stow_unstow_R"] = CabinToggle("visors_stow_unstow_R", "First Officer Sun Visor", "Stowed", "Deployed");

        // Cockpit window shades (only fitted when the airframe's WindowShadesOption
        // is enabled; toggling without the option fitted has no visible effect).
        d["shade_LF"] = CabinToggle("shade_LF", "Window Shade Left Forward", "Stowed", "Extended");
        d["shade_LR"] = CabinToggle("shade_LR", "Window Shade Left Rear", "Stowed", "Extended");
        d["shade_RF"] = CabinToggle("shade_RF", "Window Shade Right Forward", "Stowed", "Extended");
        d["shade_RR"] = CabinToggle("shade_RR", "Window Shade Right Rear", "Stowed", "Extended");

        // Seat headrests + rudder-pedal adjust — drag positions 0–100 (50 = the
        // position PMDG initializes at spawn). Clamped in HandleUIVariableSet.
        // The varKey carries a "_SET" suffix so MainForm renders a TextBox +
        // Set button (the numeric-input control); without it, a no-
        // ValueDescriptions var falls through to the plain-button branch whose
        // click always dispatches value 1 — the position slammed to 1/100 on
        // every press and the control "only went to one position".
        SimConnect.SimVarDefinition DragVar(string lvar, string display) => new()
        {
            Name = lvar,
            DisplayName = display,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            HelpText = "Position 0 to 100. 50 is the default"
        };
        d["headrest_CA_drag_h_SET"] = DragVar("headrest_CA_drag_h", "Captain Headrest");
        d["headrest_FO_drag_h_SET"] = DragVar("headrest_FO_drag_h", "First Officer Headrest");
        d["rudder_ped_adjust_CA_drag_h_SET"] = DragVar("rudder_ped_adjust_CA_drag_h", "Captain Rudder Pedals");
        d["rudder_ped_adjust_FO_drag_h_SET"] = DragVar("rudder_ped_adjust_FO_drag_h", "First Officer Rudder Pedals");

        // Jumpseat + armrests — SDK events with a POSITION parameter (CDA param
        // 0/1), state read back from the PMDG-owned switch anim L-vars (0/100).
        // Live-verified 2026-06-12 with PMDGDispatchTester:
        //   cda 71633 1 → L:switch_2001_73X = 100 (jumpseat extended)
        //   cda 71633 0 → L:switch_2001_73X = 0   (jumpseat stowed)
        //   cda 70638 1/0 → L:switch_1006_73X = 100/0 (CA left armrest)
        // The original Momentary-button design always sent parameter 1, which is
        // why these "worked once" (extend) and never went back. The switch L-vars
        // are SDK-owned read-backs (a raw SetLVar would revert — same family as
        // the 777's switch_NNN_a), so the EVENT is the actuator and the L-var
        // only drives the combo display — dispatch is intercepted BEFORE the
        // generic LVar SetLVar branch in HandleUIVariableSet.
        SimConnect.SimVarDefinition SdkFurnitureCombo(string lvar, string display, string atZero, string atHundred) => new()
        {
            Name = lvar,
            DisplayName = display,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = atZero, [100] = atHundred }
        };
        d["switch_2001_73X"] = SdkFurnitureCombo("switch_2001_73X", "Jumpseat", "Stowed", "Extended");
        d["switch_1006_73X"] = SdkFurnitureCombo("switch_1006_73X", "Captain Left Armrest", "Down", "Up");
        d["switch_1007_73X"] = SdkFurnitureCombo("switch_1007_73X", "Captain Right Armrest", "Down", "Up");
        d["switch_1008_73X"] = SdkFurnitureCombo("switch_1008_73X", "First Officer Left Armrest", "Down", "Up");
        d["switch_1009_73X"] = SdkFurnitureCombo("switch_1009_73X", "First Officer Right Armrest", "Down", "Up");

        // Overhead bins — every "SPACE BIN" click-spot in the cabin ceiling.
        // Rows 1–18 left/right, plus the two small 3L/3R bins fitted on the
        // TC cabin layout. Bins that don't exist on the loaded cabin config
        // are harmless no-ops.
        foreach (int row in Enumerable.Range(1, 18))
        {
            foreach (var (suffix, sideLabel) in new[] { ("L", "Left"), ("R", "Right") })
            {
                string lvar = $"bin_{row}{suffix}_anim";
                d[lvar] = CabinToggle(lvar, $"Bin {row} {sideLabel}", "Closed", "Open");
            }
        }
        d["bin_3LTC_anim"] = CabinToggle("bin_3LTC_anim", "Bin 3 Left Small", "Closed", "Open");
        d["bin_3RTC_anim"] = CabinToggle("bin_3RTC_anim", "Bin 3 Right Small", "Closed", "Open");
        d["CABIN_AllBinsOpen"]  = Momentary("CABIN_AllBinsOpen",  "Open All Bins");
        d["CABIN_AllBinsClose"] = Momentary("CABIN_AllBinsClose", "Close All Bins");

        // Passenger window blinds — exposed as all-up/all-down composites only
        // (87 individual blinds would drown the panel; per-blind L-vars are
        // window{1..41}{L,R}_blind_anim + 14ROS + 4 EE-row, 1 = down).
        d["CABIN_AllBlindsUp"]   = Momentary("CABIN_AllBlindsUp",   "Raise All Window Blinds");
        d["CABIN_AllBlindsDown"] = Momentary("CABIN_AllBlindsDown", "Lower All Window Blinds");

        // Cabin & galley lighting, curtains, lavatory doors.
        d["cabin_ceiling_light_fwd"] = CabinToggle("cabin_ceiling_light_fwd", "Cabin Ceiling Lights Forward", "Off", "On");
        d["cabin_ceiling_light_aft"] = CabinToggle("cabin_ceiling_light_aft", "Cabin Ceiling Lights Aft", "Off", "On");
        d["fwd_galley_lights"] = CabinToggle("fwd_galley_lights", "Forward Galley Lights", "Off", "On");
        d["aft_galley_lights"] = CabinToggle("aft_galley_lights", "Aft Galley Lights", "Off", "On");
        d["galley_fwd_curtain_anim"] = CabinToggle("galley_fwd_curtain_anim", "Forward Galley Curtain", "Open", "Drawn");
        d["galley_aft_curtain_anim"] = CabinToggle("galley_aft_curtain_anim", "Aft Galley Curtain", "Open", "Drawn");
        d["fwd_lavatory_handle_anim"] = CabinToggle("fwd_lavatory_handle_anim", "Forward Lavatory Door", "Closed", "Open");
        d["aft_left_lavatory_handle_anim"] = CabinToggle("aft_left_lavatory_handle_anim", "Aft Left Lavatory Door", "Closed", "Open");
        d["aft_right_lavatory_handle_anim"] = CabinToggle("aft_right_lavatory_handle_anim", "Aft Right Lavatory Door", "Closed", "Open");
        d["divider_curtain_left_anim"]  = CabinToggle("divider_curtain_left_anim",  "Divider Curtain Left",  "Open", "Drawn");
        d["divider_curtain_right_anim"] = CabinToggle("divider_curtain_right_anim", "Divider Curtain Right", "Open", "Drawn");

        // --- Cockpit storage cubbies (binders/docs placed or removed) + the
        //     cubby bar. "BINDERS REMOVE/STOW" on the centre cubby hides a
        //     timed easter egg: with the cubby bar raised, toggling
        //     L:CubbyTrigger makes PMDG's own model Update reveal a cookie
        //     stash (CookiesVis/CubbyTimer) — live-verified end-to-end.
        d["side_cubby_CA"]      = CabinToggle("side_cubby_CA",      "Captain Side Cubby", "Empty", "Stocked");
        d["side_cubby_FO"]      = CabinToggle("side_cubby_FO",      "First Officer Side Cubby", "Empty", "Stocked");
        d["doc_cubby_ca"]       = CabinToggle("doc_cubby_ca",       "Captain Document Cubby", "Empty", "Stocked");
        d["doc_cubby_fo"]       = CabinToggle("doc_cubby_fo",       "First Officer Document Cubby", "Empty", "Stocked");
        d["glareshield_cubby"]  = CabinToggle("glareshield_cubby",  "Glareshield Cubby", "Empty", "Stocked");
        d["cubby_bar_drag_v_SET"] = DragVar("cubby_bar_drag_v", "Cubby Bar");
        d["CubbyTrigger"] = CabinToggle("CubbyTrigger", "Binder Cookie Stash", "Hidden", "Revealed",
            "PMDG's hidden cookie stash behind the binders. Revealing raises the cubby bar automatically");
        d["CA_CupholderDrink"] = Momentary("CA_CupholderDrink", "Captain Cupholder Drink");
        d["CA_CupholderDrink"].HelpText = "Cycle a random drink into the captain's cupholder";
        d["FO_CupholderDrink"] = Momentary("FO_CupholderDrink", "First Officer Cupholder Drink");
        d["FO_CupholderDrink"].HelpText = "Cycle a random drink into the first officer's cupholder";

        // --- Galley equipment (all plain L-vars, write-stick verified). The
        //     water on/off and cold/warm pushbutton pairs are radio buttons —
        //     PMDG's click code sets one and clears the other; the combo key
        //     is the "active" button of the pair and dispatch writes both.
        d["water_on_btn"]   = CabinToggle("water_on_btn",   "Galley Water", "Off", "On");
        d["water_warm_btn"] = CabinToggle("water_warm_btn", "Galley Water Temperature", "Cold", "Warm");
        d["sink1_tap"] = CabinToggle("sink1_tap", "Forward Sink Tap 1", "Off", "Running");
        d["sink2_tap"] = CabinToggle("sink2_tap", "Forward Sink Tap 2", "Off", "Running");
        d["coffee1_valve"] = CabinToggle("coffee1_valve", "Forward Coffee Valve", "Closed", "Open");
        d["coffee2_valve"] = CabinToggle("coffee2_valve", "Aft Coffee Valve", "Closed", "Open");
        d["sanitizer1_pump"] = CabinToggle("sanitizer1_pump", "Aft Sanitizer Pump 1", "Up", "Pressed");
        d["sanitizer2_pump"] = CabinToggle("sanitizer2_pump", "Aft Sanitizer Pump 2", "Up", "Pressed");
        d["powerplug1_slide"] = CabinToggle("powerplug1_slide", "Forward Power Outlet 1", "Covered", "Open");
        d["powerplug2_slide"] = CabinToggle("powerplug2_slide", "Forward Power Outlet 2", "Covered", "Open");
        d["powerplug3_slide"] = CabinToggle("powerplug3_slide", "Aft Power Outlet 1", "Covered", "Open");
        d["powerplug4_slide"] = CabinToggle("powerplug4_slide", "Aft Power Outlet 2", "Covered", "Open");
        d["secret_compartment_one"] = CabinToggle("secret_compartment_one", "Secret Compartment 1", "Closed", "Open");
        d["secret_compartment_two"] = CabinToggle("secret_compartment_two", "Secret Compartment 2", "Closed", "Open");

        // --- Forward airstair control panel (fwd galley) + FAP ground-service
        //     switch. These switches have NO entries in the public SDK header,
        //     but PMDG's switch-number == event-offset convention holds:
        //     event_base + 1646/1648/1654/1658/2050 all move the corresponding
        //     switch_NNNN_73X read-back L-vars (live-verified 2026-06-12).
        //     Extend/retract/standby are pushbuttons (press-and-release
        //     dispatch); lights is a 3-position selector (param 0/1/2 →
        //     L-var 0/50/100); ground service is a 2-position switch.
        //     The airstair itself only exists when the airframe option is
        //     fitted — without it the switches are harmless no-ops.
        d["AIRSTAIR_Extend"]  = Momentary("AIRSTAIR_Extend",  "Extend Airstair");
        d["AIRSTAIR_Retract"] = Momentary("AIRSTAIR_Retract", "Retract Airstair");
        d["AIRSTAIR_Standby"] = Momentary("AIRSTAIR_Standby", "Airstair Standby");
        d["switch_1654_73X"] = new SimConnect.SimVarDefinition
        {
            Name = "switch_1654_73X",
            DisplayName = "Airstair Lights",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            // Anim positions 0/50/100 = selector detents 0/1/2. Middle/full
            // labels are a best guess (panel labels unreadable in navdata) —
            // flip if a tester reports otherwise.
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [50] = "Dim", [100] = "Bright" }
        };
        d["switch_2050_73X"] = new SimConnect.SimVarDefinition
        {
            Name = "switch_2050_73X",
            DisplayName = "Ground Service Switch",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            HelpText = "Flight attendant panel ground-service power switch",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [100] = "On" }
        };

        return d;
    }

    // =========================================================================
    // Panel Controls — scaffold (populated in Task C9)
    // =========================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            // ===== Overhead =====
            ["Electrical"] = new List<string>
            {
                "ELEC_BatSelector", "ELEC_StandbyPowerSelector",
                "ELEC_DCMeterSelector", "ELEC_ACMeterSelector",
                "ELEC_GenSw_0", "ELEC_GenSw_1",
                "ELEC_APUGenSw_0_On", "ELEC_APUGenSw_0_Off",
                "ELEC_APUGenSw_1_On", "ELEC_APUGenSw_1_Off",
                "ELEC_BusTransSw_AUTO",
                "ELEC_GrdPwrSw_On", "ELEC_GrdPwrSw_Off",
                "ELEC_IDGDisconnectSw_0", "ELEC_IDGDisconnectSw_1",
                "ELEC_CabUtilSw", "ELEC_IFEPassSeatSw",
                "APU_Selector", "APU_EGTNeedle"
            },
            ["ADIRU"] = new List<string>
            {
                // IRS mode selectors come first, then the ISDU display selectors.
                "IRS_ModeSelector_0", "IRS_ModeSelector_1",
                "IRS_DisplaySelector", "IRS_SysDisplay_R",
                "IRS_DisplayLeft", "IRS_DisplayRight"
            },
            ["Hydraulics"] = new List<string>
            {
                "HYD_PumpSw_eng_0", "HYD_PumpSw_eng_1",
                "HYD_PumpSw_elec_0", "HYD_PumpSw_elec_1"
            },
            ["Fuel"] = new List<string>
            {
                "FUEL_CrossFeedSw",
                "FUEL_PumpFwdSw_0", "FUEL_PumpFwdSw_1",
                "FUEL_PumpAftSw_0", "FUEL_PumpAftSw_1",
                "FUEL_PumpCtrSw_0", "FUEL_PumpCtrSw_1",
                "FUEL_AuxFwd_0", "FUEL_AuxFwd_1",
                "FUEL_AuxAft_0", "FUEL_AuxAft_1",
                "FUEL_FWDBleed", "FUEL_AFTBleed", "FUEL_GNDXfr",
                "FUEL_FuelTempNeedle"
            },
            ["Engines"] = new List<string>
            {
                "ENG_EECSwitch_0", "ENG_EECSwitch_1",
                "ENG_IgnitionSelector",
                // Each engine's fuel lever sits directly after its start switch
                // (Start 1, Fuel 1, Start 2, Fuel 2) — mirrors the PMDG 777 layout.
                "ENG_StartSelector_0", "ENG_StartLever_0",
                "ENG_StartSelector_1", "ENG_StartLever_1",
            },
            ["Anti-Ice"] = new List<string>
            {
                "ICE_WingAntiIceSw",
                "ICE_EngAntiIceSw_0", "ICE_EngAntiIceSw_1",
                "ICE_WindowHeatSw_0", "ICE_WindowHeatSw_1",
                "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3",
                "ICE_WindowHeatTestSw",
                "ICE_ProbeHeatSw_0", "ICE_ProbeHeatSw_1"
            },
            ["Air Systems"] = new List<string>
            {
                "AIR_TempSourceSelector", "AIR_TrimAirSwitch",
                "AIR_RecircFanSwitch_0", "AIR_RecircFanSwitch_1",
                "AIR_PackSwitch_0", "AIR_PackSwitch_1",
                "AIR_BleedAirSwitch_0", "AIR_BleedAirSwitch_1", "AIR_APUBleedAirSwitch",
                "AIR_IsolationValveSwitch",
                "AIR_OutflowValveSwitch", "AIR_PressurizationModeSelector",
                "AIR_FltAlt_SET", "AIR_LandAlt_SET",
                "AIR_EquipCoolingSupplyNORM", "AIR_EquipCoolingExhaustNORM",
                // Continuous readouts (pressurization, duct pressure, cabin temp)
                "AIR_CabinAltNeedle", "AIR_CabinDPNeedle", "AIR_CabinVSNeedle",
                "AIR_CabinValveNeedle",
                "AIR_DuctPress_0", "AIR_DuctPress_1",
                "AIR_TemperatureNeedle",
                // Bleed overheat self-test
                "AIR_BleedOvhtTest"
            },
            ["Lights"] = new List<string>
            {
                "LTS_LandingLtRetractableSw_0", "LTS_LandingLtRetractableSw_1",
                "LTS_LandingLtFixedSw_0", "LTS_LandingLtFixedSw_1",
                "LTS_RunwayTurnoffSw_0", "LTS_RunwayTurnoffSw_1",
                "LTS_TaxiSw",
                "LTS_LogoSw", "LTS_PositionSw", "LTS_AntiCollisionSw",
                "LTS_WingSw", "LTS_WheelWellSw",
                "LTS_DomeWhiteSw"
            },
            ["Signs"] = new List<string>
            {
                "LTS_EmerExitSelector",
                "COMM_NoSmokingSelector", "COMM_FastenBeltsSelector"
            },
            ["Oxygen"] = new List<string>
            {
                "OXY_SwNormal",
                "OXY_TestL", "OXY_TestR"
            },
            ["Wipers"] = new List<string>
            {
                "OH_WiperLSelector", "OH_WiperRSelector"
            },
            ["Flight Controls"] = new List<string>
            {
                "FCTL_FltControl_Sw_0", "FCTL_FltControl_Sw_1",
                "FCTL_Spoiler_Sw_0", "FCTL_Spoiler_Sw_1",
                "FCTL_YawDamper_Sw",
                "FCTL_AltnFlaps_Sw_ARM", "FCTL_AltnFlaps_Control_Sw"
            },
            ["Flight Recorder"] = new List<string>
            {
                "FLTREC_SwNormal"
            },

            // ===== Glareshield =====
            ["Warnings"] = new List<string>
            {
                "WARN_annunFIRE_WARN_0", "WARN_annunFIRE_WARN_1",
                "WARN_annunMASTER_CAUTION_0", "WARN_annunMASTER_CAUTION_1",
                "WARN_ResetFireWarning", "WARN_ResetMasterCaution",
                "WARN_CabAltHornCutout"
            },
            ["EFIS Captain"] = new List<string>
            {
                "EFIS_MinsSelBARO_0", "EFIS_MinsValueFt_0_SET",
                "EFIS_BaroSelHPA_0",
                "EFIS_VORADFSel1_0", "EFIS_VORADFSel2_0",
                "EFIS_ModeSel_0", "EFIS_RangeSel_0"
            },
            ["EFIS First Officer"] = new List<string>
            {
                "EFIS_MinsSelBARO_1", "EFIS_MinsValueFt_1_SET",
                "EFIS_BaroSelHPA_1",
                "EFIS_VORADFSel1_1", "EFIS_VORADFSel2_1",
                "EFIS_ModeSel_1", "EFIS_RangeSel_1"
            },
            ["MCP"] = new List<string>
            {
                "MCP_FDSw_0", "MCP_FDSw_1",
                "MCP_ATArmSw", "MCP_BankLimitSel", "MCP_DisengageBar",
                "MCP_CmdA", "MCP_CmdB", "MCP_CwsA", "MCP_CwsB",
                "MCP_AppBtn", "MCP_VorLoc"
            },
            ["Display Select"] = new List<string>
            {
                "MAIN_MainPanelDUSel_0", "MAIN_MainPanelDUSel_1",
                "MAIN_LowerDUSel_0", "MAIN_LowerDUSel_1",
                "NAVDIS_VHFNavSelector", "NAVDIS_IRSSelector", "NAVDIS_FMCSelector",
                "NAVDIS_SourceSelector", "NAVDIS_ControlPaneSelector"
            },

            // ===== Forward Panel =====
            ["Landing Gear"] = new List<string>
            {
                "MAIN_GearLever"
            },
            ["Autobrake"] = new List<string>
            {
                "MAIN_AutobrakeSelector"
            },
            ["GPWS"] = new List<string>
            {
                "GPWS_FlapInhibitSw_NORM", "GPWS_GearInhibitSw_NORM", "GPWS_TerrInhibitSw_NORM",
                "GPWS_SysTest"
            },
            ["Instruments"] = new List<string>
            {
                "MAIN_DisengageTestSelector_0", "MAIN_DisengageTestSelector_1",
                "MAIN_LightsSelector",
                "MAIN_RMISelector1_VOR", "MAIN_RMISelector2_VOR",
                "MAIN_NoseWheelSteeringSwNORM",
                "MAIN_N1SetSelector", "MAIN_SpdRefSelector", "MAIN_FuelFlowSelector"
            },

            // ===== Pedestal =====
            // Real pedestal controls are added by the Control Stand tasks below.
            // FMC performance readouts (V-speeds, perf-complete, alts, flight
            // number) are NOT controls — their SimVarDefinitions remain
            // IsAnnounced=true so they announce on change; the exact values are
            // queryable via the CDU (Shift+T).
            ["Control Stand"] = new List<string>
            {
                "TRIM_StabTrimMainElecSw_NORMAL", "TRIM_StabTrimAutoPilotSw_NORMAL", "TRIM_StabTrimSw_NORMAL",
                "CS_TOGA_1", "CS_TOGA_2",
                "CS_ATDisc_1", "CS_ATDisc_2"
            },
            ["Transponder/TCAS"] = new List<string>
            {
                "XPDR_XpndrSelector_2", "XPDR_AltSourceSel_2", "XPDR_ModeSel",
                "TRANSPONDER_CODE_SET",
                "XPDR_Ident", "XPDR_TcasTest"
            },
            ["Fire Protection"] = new List<string>
            {
                "FIRE_OvhtDetSw_0", "FIRE_OvhtDetSw_1",
                "FIRE_DetTestSw", "FIRE_ExtinguisherTestSw",
                // FIRE_HandlePos_N pairs with each handle's press buttons: [0]=Eng1, [1]=APU, [2]=Eng2.
                "FIRE_HandlePos_0", "FIRE_EngineHandle_1_Press", "FIRE_EngineHandle_1_PressBottom",
                "FIRE_HandlePos_1", "FIRE_APUHandle_Press",      "FIRE_APUHandle_PressBottom",
                "FIRE_HandlePos_2", "FIRE_EngineHandle_2_Press", "FIRE_EngineHandle_2_PressBottom"
            },
            ["Cargo Fire"] = new List<string>
            {
                "CARGO_DetSelect_0", "CARGO_DetSelect_1",
                "CARGO_ArmedSw_0", "CARGO_ArmedSw_1"
            },
            // Radios and crew-call/audio split into "Radio" + "Calls" to mirror the 777.
            // The dead Attendant/Ground-call press-counters are intentionally NOT listed
            // (their SimVarDefinitions + auto-announce handler remain).
            ["Radio"] = new List<string>
            {
                "COM1_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:1", "COM1_RADIO_SWAP",
                "COM2_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:2", "COM2_RADIO_SWAP"
            },
            ["Calls"] = new List<string>
            {
                "COMM_SelectedMic_0", "COMM_SelectedMic_1",
                "COMM_ServiceInterphoneSw",
                // Overhead ATTEND / GRD CALL push buttons (live-verified CDA
                // param-1 dispatch). The press-count announce handler speaks
                // "Attend call" / "Ground call" as the confirmation.
                "COMM_AttendCallBtn", "COMM_GrndCallBtn"
            },
            ["Flight Deck Door"] = new List<string>
            {
                "PED_FltDkDoorSel",                          // read-only status (AUTO at rest)
                "PED_FltDkDoor_Unlock", "PED_FltDkDoor_Deny", // momentary press buttons
                "PED_annunLOCK_FAIL", "PED_annunAUTO_UNLK"
            },
            // Stab-trim cutout switches moved to the Control Stand panel (pedestal).

            // Pedestal — Boris Audio Works Soundpack (737 xBAW). Backing L-vars
            // differ from the 777 (see the GetVariables block); switch_102_73X
            // (emer exit lights) is intentionally not exposed.
            ["Boris Audio Works"] = new List<string>
            {
                "ANR_onoff", "HydPumpMfg", "switch_277_73X"
            },

            // ===== Cabin & Furniture =====
            // All L-var-backed (see the CABIN & COCKPIT FURNITURE region in
            // GetVariables for sources + polarity verification).
            ["Cockpit Furniture"] = new List<string>
            {
                "Window_OpenClose_CA", "Window_OpenClose_FO",
                "visors_stow_unstow_L", "visors_stow_unstow_R",
                "shade_LF", "shade_LR", "shade_RF", "shade_RR",
                "headrest_CA_drag_h_SET", "headrest_FO_drag_h_SET",
                "rudder_ped_adjust_CA_drag_h_SET", "rudder_ped_adjust_FO_drag_h_SET",
                "switch_2001_73X",
                "switch_1006_73X", "switch_1007_73X",
                "switch_1008_73X", "switch_1009_73X",
                "side_cubby_CA", "side_cubby_FO",
                "doc_cubby_ca", "doc_cubby_fo", "glareshield_cubby",
                "cubby_bar_drag_v_SET", "CubbyTrigger",
                "CA_CupholderDrink", "FO_CupholderDrink"
            },
            ["Cabin Bins"] = BuildCabinBinControls(),
            ["Cabin Items"] = new List<string>
            {
                "CABIN_AllBlindsUp", "CABIN_AllBlindsDown",
                "cabin_ceiling_light_fwd", "cabin_ceiling_light_aft",
                "fwd_galley_lights", "aft_galley_lights",
                "galley_fwd_curtain_anim", "galley_aft_curtain_anim",
                "divider_curtain_left_anim", "divider_curtain_right_anim",
                "fwd_lavatory_handle_anim",
                "aft_left_lavatory_handle_anim", "aft_right_lavatory_handle_anim"
            },
            ["Galley"] = new List<string>
            {
                "water_on_btn", "water_warm_btn",
                "sink1_tap", "sink2_tap",
                "coffee1_valve", "coffee2_valve",
                "sanitizer1_pump", "sanitizer2_pump",
                "powerplug1_slide", "powerplug2_slide",
                "powerplug3_slide", "powerplug4_slide",
                "secret_compartment_one", "secret_compartment_two",
                "AIRSTAIR_Extend", "AIRSTAIR_Retract", "AIRSTAIR_Standby",
                "switch_1654_73X", "switch_2050_73X"
            },
        };
    }

    // Composite buttons first, then bins in row order (1L, 1R, 2L, ... 18R) with
    // the TC-layout small bins after their full-size row-3 siblings.
    private static List<string> BuildCabinBinControls()
    {
        var list = new List<string> { "CABIN_AllBinsOpen", "CABIN_AllBinsClose" };
        foreach (int row in Enumerable.Range(1, 18))
        {
            list.Add($"bin_{row}L_anim");
            list.Add($"bin_{row}R_anim");
            if (row == 3)
            {
                list.Add("bin_3LTC_anim");
                list.Add("bin_3RTC_anim");
            }
        }
        return list;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();
    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // =========================================================================
    // Event ID dictionary — scaffold (populated in Task C2)
    // PMDG 737 NG3 SDK third-party event range starts at THIRD_PARTY_EVENT_ID_MIN.
    // =========================================================================
    private const int event_base = 0x00011000;  // THIRD_PARTY_EVENT_ID_MIN

    /// <summary>
    /// Maps PMDG 737 event names to their numeric event IDs.
    /// Used when sending events via the PMDG SDK control area.
    /// Source: PMDG_NG3_SDK.h — all <c>#define EVT_*</c> constants in SDK order.
    /// Each entry's offset matches <c>THIRD_PARTY_EVENT_ID_MIN + N</c> from the header.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> EventIds =
        new Dictionary<string, int>
        {
            // ===== Overhead — Electric =====
            { "EVT_OH_ELEC_BATTERY_SWITCH",                  event_base + 1 },
            { "EVT_OH_ELEC_BATTERY_GUARD",                   event_base + 2 },
            { "EVT_OH_ELEC_DC_METER",                        event_base + 3 },
            { "EVT_OH_ELEC_AC_METER",                        event_base + 4 },
            { "EVT_OH_ELEC_GALLEY",                          event_base + 974 },   // -600/700 only
            { "EVT_OH_ELEC_CAB_UTIL",                        event_base + 5 },     // -800/900 only
            { "EVT_OH_ELEC_IFE",                             event_base + 6 },     // -800/900 only
            { "EVT_OH_ELEC_STBY_PWR_SWITCH",                 event_base + 10 },
            { "EVT_OH_ELEC_STBY_PWR_GUARD",                  event_base + 11 },
            { "EVT_OH_ELEC_DISCONNECT_1_SWITCH",             event_base + 12 },
            { "EVT_OH_ELEC_DISCONNECT_1_GUARD",              event_base + 13 },
            { "EVT_OH_ELEC_DISCONNECT_2_SWITCH",             event_base + 14 },
            { "EVT_OH_ELEC_DISCONNECT_2_GUARD",              event_base + 15 },
            { "EVT_OH_ELEC_GRD_PWR_SWITCH",                  event_base + 17 },
            { "EVT_OH_ELEC_BUS_TRANSFER_SWITCH",             event_base + 18 },
            { "EVT_OH_ELEC_BUS_TRANSFER_GUARD",              event_base + 19 },
            { "EVT_OH_ELEC_GEN1_SWITCH",                     event_base + 27 },
            { "EVT_OH_ELEC_APU_GEN1_SWITCH",                 event_base + 28 },
            { "EVT_OH_ELEC_APU_GEN2_SWITCH",                 event_base + 29 },
            { "EVT_OH_ELEC_GEN2_SWITCH",                     event_base + 30 },
            { "EVT_OH_ELEC_MAINT_SWITCH",                    event_base + 93 },

            // ===== Overhead — Fuel =====
            { "EVT_OH_FUEL_PUMP_1_AFT",                      event_base + 37 },
            { "EVT_OH_FUEL_PUMP_1_FORWARD",                  event_base + 38 },
            { "EVT_OH_FUEL_PUMP_2_FORWARD",                  event_base + 39 },
            { "EVT_OH_FUEL_PUMP_2_AFT",                      event_base + 40 },
            { "EVT_OH_FUEL_PUMP_L_CENTER",                   event_base + 45 },
            { "EVT_OH_FUEL_PUMP_R_CENTER",                   event_base + 46 },
            { "EVT_OH_FUEL_CROSSFEED",                       event_base + 49 },
            { "EVT_OH_FUEL_AUX_FWD_A",                       event_base + 2009 },
            { "EVT_OH_FUEL_AUX_FWD_B",                       event_base + 2010 },
            { "EVT_OH_FUEL_AUX_AFT_A",                       event_base + 2011 },
            { "EVT_OH_FUEL_AUX_AFT_B",                       event_base + 2012 },
            { "EVT_OH_FUEL_FWD_BLD",                         event_base + 2013 },
            { "EVT_OH_FUEL_AFT_BLD",                         event_base + 2014 },
            { "EVT_OH_FUEL_GND_XFR_GUARD",                   event_base + 2018 },
            { "EVT_OH_FUEL_GND_XFR_SW",                      event_base + 2019 },

            // ===== Overhead — Lights =====
            { "EVT_OH_LAND_LIGHTS_GUARD",                    event_base + 110 },
            { "EVT_OH_LIGHTS_L_RETRACT",                     event_base + 111 },
            { "EVT_OH_LIGHTS_R_RETRACT",                     event_base + 112 },
            { "EVT_OH_LIGHTS_L_FIXED",                       event_base + 113 },
            { "EVT_OH_LIGHTS_R_FIXED",                       event_base + 114 },
            { "EVT_OH_LIGHTS_L_TURNOFF",                     event_base + 115 },
            { "EVT_OH_LIGHTS_R_TURNOFF",                     event_base + 116 },
            { "EVT_OH_LIGHTS_TAXI",                          event_base + 117 },
            { "EVT_OH_LIGHTS_APU_START",                     event_base + 118 },
            { "EVT_OH_LIGHTS_L_ENGINE_START",                event_base + 119 },
            { "EVT_OH_LIGHTS_IGN_SEL",                       event_base + 120 },
            { "EVT_OH_LIGHTS_R_ENGINE_START",                event_base + 121 },
            { "EVT_OH_LIGHTS_LOGO",                          event_base + 122 },
            { "EVT_OH_LIGHTS_POS_STROBE",                    event_base + 123 },
            { "EVT_OH_LIGHTS_ANT_COL",                       event_base + 124 },
            { "EVT_OH_LIGHTS_WING",                          event_base + 125 },
            { "EVT_OH_LIGHTS_WHEEL_WELL",                    event_base + 126 },
            { "EVT_OH_LIGHTS_L_ENGINE_START_INOUT",          event_base + 127 },
            { "EVT_OH_LIGHTS_R_ENGINE_START_INOUT",          event_base + 128 },
            { "EVT_OH_LIGHTS_COMPASS",                       event_base + 982 },

            // ===== Overhead — Center Part =====
            { "EVT_OH_CB_LIGHT_CONTROL",                     event_base + 94 },
            { "EVT_OH_PANEL_LIGHT_CONTROL",                  event_base + 95 },
            { "EVT_OH_EC_SUPPLY_SWITCH",                     event_base + 96 },
            { "EVT_OH_EC_EXHAUST_SWITCH",                    event_base + 97 },
            { "EVT_OH_EMER_EXIT_LIGHT_SWITCH",               event_base + 100 },
            { "EVT_OH_EMER_EXIT_LIGHT_GUARD",                event_base + 101 },
            { "EVT_OH_NO_SMOKING_LIGHT_SWITCH",              event_base + 103 },
            { "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH",            event_base + 104 },

            // ===== Overhead — Miscellaneous =====
            { "EVT_OH_ATTND_CALL_SWITCH",                    event_base + 105 },
            { "EVT_OH_GRND_CALL_SWITCH",                     event_base + 106 },
            { "EVT_OH_WIPER_LEFT_CONTROL",                   event_base + 36 },
            { "EVT_OH_WIPER_RIGHT_CONTROL",                  event_base + 109 },
            { "EVT_OH_EFIS_HDG_REF_TOGGLE",                  event_base + 6920 },  // BBJ polar nav option

            // ===== Overhead — NAVDSP =====
            { "EVT_OH_NAVDSP_DISPLAYS_SOURCE_SEL",           event_base + 58 },
            { "EVT_OH_NAVDSP_CONTROL_PANEL_SEL",             event_base + 59 },
            { "EVT_OH_NAVDSP_FMC_SEL",                       event_base + 60 },
            { "EVT_OH_NAVDSP_IRS_SEL",                       event_base + 61 },
            { "EVT_OH_NAVDSP_VHF_NAV_SEL",                   event_base + 62 },

            // ===== Overhead — Flight Controls =====
            { "EVT_OH_YAW_DAMPER",                           event_base + 63 },
            { "EVT_OH_ALT_FLAPS_MASTER_SWITCH",              event_base + 73 },
            { "EVT_OH_ALT_FLAPS_MASTER_GUARD",               event_base + 74 },
            { "EVT_OH_SPOILER_A_SWITCH",                     event_base + 65 },
            { "EVT_OH_SPOILER_A_GUARD",                      event_base + 66 },
            { "EVT_OH_SPOILER_B_SWITCH",                     event_base + 67 },
            { "EVT_OH_SPOILER_B_GUARD",                      event_base + 68 },
            { "EVT_OH_ALT_FLAPS_POS_SWITCH",                 event_base + 75 },
            { "EVT_OH_FCTL_A_SWITCH",                        event_base + 78 },
            { "EVT_OH_FCTL_A_GUARD",                         event_base + 79 },
            { "EVT_OH_FCTL_B_SWITCH",                        event_base + 80 },
            { "EVT_OH_FCTL_B_GUARD",                         event_base + 81 },

            // ===== Overhead — CVR =====
            { "EVT_OH_CVR_TEST",                             event_base + 178 },
            { "EVT_OH_CVR_ERASE",                            event_base + 180 },

            // ===== Overhead — Hydraulics =====
            { "EVT_OH_HYD_ENG1",                             event_base + 165 },
            { "EVT_OH_HYD_ELEC2",                            event_base + 167 },
            { "EVT_OH_HYD_ELEC1",                            event_base + 168 },
            { "EVT_OH_HYD_ENG2",                             event_base + 166 },

            // ===== Overhead — Ice =====
            { "EVT_OH_ICE_WINDOW_HEAT_1",                    event_base + 135 },
            { "EVT_OH_ICE_WINDOW_HEAT_2",                    event_base + 136 },
            { "EVT_OH_ICE_WINDOW_HEAT_3",                    event_base + 138 },
            { "EVT_OH_ICE_WINDOW_HEAT_4",                    event_base + 139 },
            { "EVT_OH_ICE_WINDOW_HEAT_TEST",                 event_base + 137 },
            { "EVT_OH_ICE_PROBE_HEAT_1",                     event_base + 140 },
            { "EVT_OH_ICE_PROBE_HEAT_2",                     event_base + 141 },
            { "EVT_OH_ICE_TAT_TEST",                         event_base + 142 },
            { "EVT_OH_ICE_WING_ANTIICE",                     event_base + 156 },
            { "EVT_OH_ICE_ENGINE_ANTIICE_1",                 event_base + 157 },
            { "EVT_OH_ICE_ENGINE_ANTIICE_2",                 event_base + 158 },

            // ===== Overhead — Pneumatics / Air Cond =====
            // --- -600/700 panel only ---
            { "EVT_OH_AIRCOND_TEMP_SOURCE_SELECTOR",         event_base + 187 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_CONT",           event_base + 191 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_CABIN",          event_base + 192 },
            // --- -800/900 panel only ---
            { "EVT_OH_AIRCOND_TEMP_SOURCE_SELECTOR_800",     event_base + 313 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_CONT_800",       event_base + 305 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_FWD_800",        event_base + 306 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_AFT_800",        event_base + 307 },
            { "EVT_OH_AIRCOND_TRIM_AIR_SWITCH_800",          event_base + 311 },
            // --- Bleed Air ---
            { "EVT_OH_BLEED_RECIRC_FAN_L_SWITCH",            event_base + 872 },
            { "EVT_OH_BLEED_RECIRC_FAN_R_SWITCH",            event_base + 196 },
            { "EVT_OH_BLEED_OVHT_TEST_BUTTON",               event_base + 199 },
            { "EVT_OH_BLEED_PACK_L_SWITCH",                  event_base + 200 },
            { "EVT_OH_BLEED_PACK_R_SWITCH",                  event_base + 201 },
            { "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH",         event_base + 202 },
            { "EVT_OH_BLEED_TRIP_RESET_BUTTON",              event_base + 209 },
            { "EVT_OH_BLEED_ENG_1_SWITCH",                   event_base + 210 },
            { "EVT_OH_BLEED_APU_SWITCH",                     event_base + 211 },
            { "EVT_OH_BLEED_ENG_2_SWITCH",                   event_base + 212 },

            // ===== Overhead — Cabin Pressurization =====
            { "EVT_OH_PRESS_FLT_ALT_KNOB",                   event_base + 218 },
            { "EVT_OH_PRESS_LAND_ALT_KNOB",                  event_base + 220 },
            { "EVT_OH_PRESS_VALVE_SWITCH",                   event_base + 222 },
            { "EVT_OH_PRESS_SELECTOR",                       event_base + 223 },

            // ===== Overhead — Cabin Altitude =====
            { "EVT_OH_CAB_ALT_HORN_CUTOUT_BUTTON",           event_base + 183 },

            // ===== Aft Overhead — LE Devices =====
            { "EVT_OH_LE_DEVICES_TEST_SWITCH",               event_base + 224 },

            // ===== Aft Overhead — Service Interphone =====
            { "EVT_OH_SERVICE_INTERPHONE_SWITCH",            event_base + 257 },

            // ===== Aft Overhead — Dome =====
            { "EVT_OH_DOME_SWITCH",                          event_base + 258 },

            // ===== Aft Overhead — ISDU =====
            { "EVT_ISDU_DSPL_SEL",                           event_base + 229 },
            { "EVT_ISDU_DSPL_SEL_BRT",                       event_base + 230 },
            { "EVT_ISDU_SYS_DSPL",                           event_base + 231 },
            { "EVT_ISDU_KBD_1",                              event_base + 232 },
            { "EVT_ISDU_KBD_2",                              event_base + 233 },
            { "EVT_ISDU_KBD_3",                              event_base + 234 },
            { "EVT_ISDU_KBD_4",                              event_base + 235 },
            { "EVT_ISDU_KBD_5",                              event_base + 236 },
            { "EVT_ISDU_KBD_6",                              event_base + 237 },
            { "EVT_ISDU_KBD_7",                              event_base + 238 },
            { "EVT_ISDU_KBD_8",                              event_base + 239 },
            { "EVT_ISDU_KBD_9",                              event_base + 240 },
            { "EVT_ISDU_KBD_ENT",                            event_base + 241 },
            { "EVT_ISDU_KBD_0",                              event_base + 243 },
            { "EVT_ISDU_KBD_CLR",                            event_base + 244 },
            { "EVT_IRU_MSU_LEFT",                            event_base + 255 },
            { "EVT_IRU_MSU_LEFT_INOUT",                      event_base + 259 },
            { "EVT_IRU_MSU_RIGHT",                           event_base + 256 },
            { "EVT_IRU_MSU_RIGHT_INOUT",                     event_base + 260 },
            { "EVT_WLAN_SWITCH",                             event_base + 888 },
            { "EVT_WLAN_GUARD",                              event_base + 889 },

            // ===== Aft Overhead — Engine Control =====
            { "EVT_OH_EEC_L_GUARD",                          event_base + 267 },
            { "EVT_OH_EEC_L_SWITCH",                         event_base + 268 },
            { "EVT_OH_EEC_R_GUARD",                          event_base + 270 },
            { "EVT_OH_EEC_R_SWITCH",                         event_base + 271 },

            // ===== Aft Overhead — Oxygen =====
            { "EVT_OH_OXY_PASS_SWITCH",                      event_base + 264 },
            { "EVT_OH_OXY_PASS_GUARD",                       event_base + 265 },
            { "EVT_OH_OXY_TEST_RESET_SWITCH_L",              event_base + 983 },
            { "EVT_OH_OXY_TEST_RESET_SWITCH_R",              event_base + 9832 },
            { "EVT_OH_OXY_RED_BUTTON_L",                     event_base + 9831 },
            { "EVT_OH_OXY_RED_BUTTON_R",                     event_base + 9833 },

            // ===== Aft Overhead — Flight Recorder & Warning =====
            { "EVT_OH_FLTREC_SWITCH",                        event_base + 298 },
            { "EVT_OH_FLTREC_GUARD",                         event_base + 299 },
            { "EVT_OH_WARNING_TEST_MACH_IAS_1_PUSH",         event_base + 301 },
            { "EVT_OH_WARNING_TEST_MACH_IAS_2_PUSH",         event_base + 302 },
            { "EVT_OH_WARNING_TEST_STALL_1_PUSH",            event_base + 303 },
            { "EVT_OH_WARNING_TEST_STALL_2_PUSH",            event_base + 304 },
            { "EVT_OH_VOICEREC_SWITCH",                      event_base + 2981 },

            // ===== Overhead — Test Gauge =====
            { "EVT_OH_TRIM_AIR_SWITCH_TOGGLE",               event_base + 15200 },
            { "EVT_OH_WING_BODY_OVERHEAT_TEST_PUSH",         event_base + 15201 },

            // ===== Integrated Standby Flight Display (ISFD) =====
            { "EVT_ISFD_APP",                                event_base + 987 },
            { "EVT_ISFD_HP_IN",                              event_base + 986 },
            { "EVT_ISFD_PLUS",                               event_base + 988 },
            { "EVT_ISFD_MINUS",                              event_base + 989 },
            { "EVT_ISFD_ATT_RST",                            event_base + 990 },
            { "EVT_ISFD_BARO",                               event_base + 991 },
            { "EVT_ISFD_BARO_PUSH",                          event_base + 993 },
            { "EVT_ISFD_MENU",                               event_base + 2021 },
            { "EVT_ISFD_ADJUST",                             event_base + 2022 },
            { "EVT_ISFD_ADJUST_PUSH",                        event_base + 2023 },

            // ===== Analog Standby Instruments =====
            { "EVT_STANDBY_ADI_APPR_MODE",                   event_base + 474 },
            { "EVT_STANDBY_ADI_CAGE_KNOB",                   event_base + 476 },
            { "EVT_STANDBY_ALT_BARO_KNOB",                   event_base + 492 },
            { "EVT_RMI_LEFT_SELECTOR",                       event_base + 497 },
            { "EVT_RMI_RIGHT_SELECTOR",                      event_base + 498 },

            // ===== Glareshield — MCP =====
            { "EVT_MCP_COURSE_SELECTOR_L",                   event_base + 376 },
            { "EVT_MCP_FD_SWITCH_L",                         event_base + 378 },
            { "EVT_MCP_AT_ARM_SWITCH",                       event_base + 380 },
            { "EVT_MCP_N1_SWITCH",                           event_base + 381 },
            { "EVT_MCP_SPEED_SWITCH",                        event_base + 382 },
            { "EVT_MCP_CO_SWITCH",                           event_base + 383 },
            { "EVT_MCP_SPEED_SELECTOR",                      event_base + 384 },
            { "EVT_MCP_VNAV_SWITCH",                         event_base + 386 },
            { "EVT_MCP_SPD_INTV_SWITCH",                     event_base + 387 },
            { "EVT_MCP_BANK_ANGLE_SELECTOR",                 event_base + 389 },
            { "EVT_MCP_HEADING_SELECTOR",                    event_base + 390 },
            { "EVT_MCP_LVL_CHG_SWITCH",                      event_base + 391 },
            { "EVT_MCP_HDG_SEL_SWITCH",                      event_base + 392 },
            { "EVT_MCP_APP_SWITCH",                          event_base + 393 },
            { "EVT_MCP_ALT_HOLD_SWITCH",                     event_base + 394 },
            { "EVT_MCP_VS_SWITCH",                           event_base + 395 },
            { "EVT_MCP_VOR_LOC_SWITCH",                      event_base + 396 },
            { "EVT_MCP_LNAV_SWITCH",                         event_base + 397 },
            { "EVT_MCP_ALTITUDE_SELECTOR",                   event_base + 400 },
            { "EVT_MCP_VS_SELECTOR",                         event_base + 401 },
            { "EVT_MCP_CMD_A_SWITCH",                        event_base + 402 },
            { "EVT_MCP_CMD_B_SWITCH",                        event_base + 403 },
            { "EVT_MCP_CWS_A_SWITCH",                        event_base + 404 },
            { "EVT_MCP_CWS_B_SWITCH",                        event_base + 405 },
            { "EVT_MCP_DISENGAGE_BAR",                       event_base + 406 },
            { "EVT_MCP_FD_SWITCH_R",                         event_base + 407 },
            { "EVT_MCP_COURSE_SELECTOR_R",                   event_base + 409 },
            { "EVT_MCP_ALT_INTV_SWITCH",                     event_base + 885 },

            // ===== Glareshield — EFIS Captain Control Panel =====
            { "EVT_EFIS_CPT_MINIMUMS",                       event_base + 355 },
            { "EVT_EFIS_CPT_MINIMUMS_RADIO_BARO",            event_base + 356 },
            { "EVT_EFIS_CPT_MINIMUMS_RST",                   event_base + 357 },
            { "EVT_EFIS_CPT_VOR_ADF_SELECTOR_L",             event_base + 358 },
            { "EVT_EFIS_CPT_MODE",                           event_base + 359 },
            { "EVT_EFIS_CPT_MODE_CTR",                       event_base + 360 },
            { "EVT_EFIS_CPT_RANGE",                          event_base + 361 },
            { "EVT_EFIS_CPT_RANGE_TFC",                      event_base + 362 },
            { "EVT_EFIS_CPT_FPV",                            event_base + 363 },
            { "EVT_EFIS_CPT_MTRS",                           event_base + 364 },
            { "EVT_EFIS_CPT_BARO",                           event_base + 365 },
            { "EVT_EFIS_CPT_BARO_IN_HPA",                    event_base + 366 },
            { "EVT_EFIS_CPT_BARO_STD",                       event_base + 367 },
            { "EVT_EFIS_CPT_VOR_ADF_SELECTOR_R",             event_base + 368 },
            { "EVT_EFIS_CPT_WXR",                            event_base + 369 },
            { "EVT_EFIS_CPT_STA",                            event_base + 370 },
            { "EVT_EFIS_CPT_WPT",                            event_base + 371 },
            { "EVT_EFIS_CPT_ARPT",                           event_base + 372 },
            { "EVT_EFIS_CPT_DATA",                           event_base + 373 },
            { "EVT_EFIS_CPT_POS",                            event_base + 374 },
            { "EVT_EFIS_CPT_TERR",                           event_base + 375 },

            // ===== Glareshield — EFIS F/O Control Panel =====
            { "EVT_EFIS_FO_MINIMUMS",                        event_base + 411 },
            { "EVT_EFIS_FO_MINIMUMS_RADIO_BARO",             event_base + 412 },
            { "EVT_EFIS_FO_MINIMUMS_RST",                    event_base + 413 },
            { "EVT_EFIS_FO_VOR_ADF_SELECTOR_L",              event_base + 414 },
            { "EVT_EFIS_FO_MODE",                            event_base + 415 },
            { "EVT_EFIS_FO_MODE_CTR",                        event_base + 416 },
            { "EVT_EFIS_FO_RANGE",                           event_base + 417 },
            { "EVT_EFIS_FO_RANGE_TFC",                       event_base + 418 },
            { "EVT_EFIS_FO_FPV",                             event_base + 419 },
            { "EVT_EFIS_FO_MTRS",                            event_base + 420 },
            { "EVT_EFIS_FO_BARO",                            event_base + 421 },
            { "EVT_EFIS_FO_BARO_IN_HPA",                     event_base + 422 },
            { "EVT_EFIS_FO_BARO_STD",                        event_base + 423 },
            { "EVT_EFIS_FO_VOR_ADF_SELECTOR_R",              event_base + 424 },
            { "EVT_EFIS_FO_WXR",                             event_base + 425 },
            { "EVT_EFIS_FO_STA",                             event_base + 426 },
            { "EVT_EFIS_FO_WPT",                             event_base + 427 },
            { "EVT_EFIS_FO_ARPT",                            event_base + 428 },
            { "EVT_EFIS_FO_DATA",                            event_base + 429 },
            { "EVT_EFIS_FO_POS",                             event_base + 430 },
            { "EVT_EFIS_FO_TERR",                            event_base + 431 },

            // ===== Pushback Tug =====
            { "EVT_RELEASE_PUSHBACK_TUG",                    event_base + 995 },

            // ===== Display Select Panel — Captain =====
            { "EVT_DSP_CPT_BELOW_GS_INHIBIT_SWITCH",         event_base + 327 },
            { "EVT_DSP_CPT_MAIN_DU_SELECTOR",                event_base + 335 },
            { "EVT_DSP_CPT_LOWER_DU_SELECTOR",               event_base + 336 },
            { "EVT_DSP_CPT_DISENGAGE_TEST_SWITCH",           event_base + 342 },
            { "EVT_DSP_CPT_AP_RESET_SWITCH",                 event_base + 339 },
            { "EVT_DSP_CPT_AT_RESET_SWITCH",                 event_base + 340 },
            { "EVT_DSP_CPT_FMC_RESET_SWITCH",                event_base + 341 },
            { "EVT_DSP_CPT_MASTER_LIGHTS_SWITCH",            event_base + 346 },

            // ===== Display Select Panel — F/O =====
            { "EVT_DSP_FO_MAIN_DU_SELECTOR",                 event_base + 440 },
            { "EVT_DSP_FO_LOWER_DU_SELECTOR",                event_base + 441 },
            { "EVT_DSP_FO_DISENGAGE_TEST_SWITCH",            event_base + 442 },
            { "EVT_DSP_FO_FMC_RESET_SWITCH",                 event_base + 443 },
            { "EVT_DSP_FO_AT_RESET_SWITCH",                  event_base + 444 },
            { "EVT_DSP_FO_AP_RESET_SWITCH",                  event_base + 445 },
            { "EVT_DSP_FO_BELOW_GS_INHIBIT_SWITCH",          event_base + 446 },

            // ===== Main Panel Misc =====
            { "EVT_MPM_AUTOBRAKE_SELECTOR",                  event_base + 460 },
            { "EVT_MPM_AUTOBRAKE_SELECTOR_INOUT",            event_base + 461 },
            { "EVT_MPM_MFD_SYS_BUTTON",                      event_base + 462 },
            { "EVT_MPM_MFD_ENG_BUTTON",                      event_base + 463 },
            { "EVT_MPM_MFD_C_R_BUTTON",                      event_base + 4621 },
            { "EVT_MPM_SPEED_REFERENCE_SELECTOR",            event_base + 464 },
            { "EVT_MPM_SPEED_REFERENCE_CONTROL",             event_base + 465 },
            { "EVT_MPM_N1SET_SELECTOR",                      event_base + 466 },
            { "EVT_MPM_N1SET_CONTROL",                       event_base + 467 },
            { "EVT_MPM_FUEL_FLOW_SWITCH",                    event_base + 468 },

            // ===== Aux Fuel Cockpit Display =====
            { "EVT_AUX_FUEL_LEFT_TEST_SWITCH",               event_base + 2030 },
            { "EVT_AUX_FUEL_RIGHT_TEST_SWITCH",              event_base + 2031 },
            { "EVT_AUX_FUEL_LEFT_ALERT_SWITCH",              event_base + 2032 },
            { "EVT_AUX_FUEL_RIGHT_ALERT_SWITCH",             event_base + 2033 },
            { "EVT_AUX_FUEL_LEFT_MAINT_SWITCH",              event_base + 2034 },
            { "EVT_AUX_FUEL_RIGHT_MAINT_SWITCH",             event_base + 2035 },

            // ===== 737MAX =====
            { "EVT_MAX_MFD_INFO_BUTTON",                     event_base + 2040 },
            { "EVT_MAX_MFD_ENG_TFR_BUTTON",                  event_base + 2041 },
            { "EVT_MAX_LWR_SEL_LEFT_OUTER_KNOB",             event_base + 2042 },
            { "EVT_MAX_LWR_SEL_LEFT_INNER_KNOB",             event_base + 2043 },
            { "EVT_MAX_LWR_SEL_LEFT_SEL_PUSH",               event_base + 2044 },
            { "EVT_MAX_LWR_SEL_RIGHT_OUTER_KNOB",            event_base + 2045 },
            { "EVT_MAX_LWR_SEL_RIGHT_INNER_KNOB",            event_base + 2046 },
            { "EVT_MAX_LWR_SEL_RIGHT_SEL_PUSH",              event_base + 2047 },
            { "EVT_MAX_GEAR_UNLOCK",                         event_base + 2048 },

            // ===== Gear Panel =====
            { "EVT_GEAR_LEVER",                              event_base + 455 },
            { "EVT_GEAR_LEVER_OFF",                          event_base + 4551 },
            { "EVT_GEAR_LEVER_UNLOCK",                       event_base + 4552 },

            // ===== Nose Wheel Steering =====
            { "EVT_NOSE_WHEEL_STEERING_SWITCH",              event_base + 325 },
            { "EVT_NOSE_WHEEL_STEERING_SWITCH_GUARD",        event_base + 326 },
            { "EVT_TILLER",                                  event_base + 975 },

            // ===== Warning / Caution =====
            { "EVT_FIRE_WARN_LIGHT_LEFT",                    event_base + 347 },
            { "EVT_MASTER_CAUTION_LIGHT_LEFT",               event_base + 348 },
            { "EVT_FIRE_WARN_LIGHT_RIGHT",                   event_base + 439 },
            { "EVT_MASTER_CAUTION_LIGHT_RIGHT",              event_base + 438 },
            { "EVT_SYSTEM_ANNUNCIATOR_PANEL_LEFT",           event_base + 349 },
            { "EVT_SYSTEM_ANNUNCIATOR_PANEL_RIGHT",          event_base + 437 },

            // ===== Lower Main — Brightness =====
            { "EVT_LWRMAIN_CAPT_MAIN_PANEL_BRT",             event_base + 328 },
            { "EVT_LWRMAIN_CAPT_OUTBD_DU_BRT",               event_base + 329 },
            { "EVT_LWRMAIN_CAPT_INBD_DU_BRT",                event_base + 330 },
            { "EVT_LWRMAIN_CAPT_INBD_DU_INNER_BRT",          event_base + 331 },
            { "EVT_LWRMAIN_CAPT_LOWER_DU_BRT",               event_base + 332 },
            { "EVT_LWRMAIN_CAPT_LOWER_DU_INNER_BRT",         event_base + 333 },
            { "EVT_LWRMAIN_CAPT_UPPER_DU_BRT",               event_base + 334 },
            { "EVT_LWRMAIN_CAPT_BACKGROUND_BRT",             event_base + 337 },
            { "EVT_LWRMAIN_CAPT_AFDS_BRT",                   event_base + 338 },
            { "EVT_LWRMAIN_FO_INBD_DU_BRT",                  event_base + 507 },
            { "EVT_LWRMAIN_FO_INBD_DU_INNER_BRT",            event_base + 508 },
            { "EVT_LWRMAIN_FO_MAIN_PANEL_BRT",               event_base + 510 },
            { "EVT_LWRMAIN_FO_OUTBD_DU_BRT",                 event_base + 509 },

            // ===== GPWS =====
            { "EVT_GPWS_SYS_TEST_BTN",                       event_base + 500 },
            { "EVT_GPWS_FLAP_INHIBIT_SWITCH",                event_base + 501 },
            { "EVT_GPWS_FLAP_INHIBIT_GUARD",                 event_base + 502 },
            { "EVT_GPWS_GEAR_INHIBIT_SWITCH",                event_base + 503 },
            { "EVT_GPWS_GEAR_INHIBIT_GUARD",                 event_base + 504 },
            { "EVT_GPWS_TERR_INHIBIT_SWITCH",                event_base + 505 },
            { "EVT_GPWS_TERR_INHIBIT_GUARD",                 event_base + 506 },

            // ===== Chronometers =====
            { "EVT_CHRONO_L_CHR",                            event_base + 314 },
            { "EVT_CHRONO_L_TCSR",                           event_base + 3141 },
            { "EVT_CHRONO_L_TIME_DATE",                      event_base + 315 },
            { "EVT_CHRONO_L_SET",                            event_base + 316 },
            { "EVT_CHRONO_L_PLUS",                           event_base + 317 },
            { "EVT_CHRONO_L_MINUS",                          event_base + 318 },
            { "EVT_CHRONO_L_RESET",                          event_base + 320 },
            { "EVT_CHRONO_L_ET",                             event_base + 321 },
            { "EVT_CHRONO_R_CHR",                            event_base + 523 },
            { "EVT_CHRONO_R_TCSR",                           event_base + 5231 },
            { "EVT_CHRONO_R_TIME_DATE",                      event_base + 524 },
            { "EVT_CHRONO_R_SET",                            event_base + 525 },
            { "EVT_CHRONO_R_PLUS",                           event_base + 526 },
            { "EVT_CHRONO_R_MINUS",                          event_base + 527 },
            { "EVT_CHRONO_R_RESET",                          event_base + 529 },
            { "EVT_CHRONO_R_ET",                             event_base + 530 },
            { "EVT_CLOCK_L",                                 event_base + 890 },
            { "EVT_MIC_L",                                   event_base + 891 },
            { "EVT_MIC_R",                                   event_base + 892 },
            { "EVT_CLOCK_R",                                 event_base + 893 },

            // ===== Side Panel — Chart & Map =====
            { "EVT_CHART_BRT_L",                             event_base + 319 },
            { "EVT_CHART_BRT_R",                             event_base + 322 },
            { "EVT_MAP_BRT_L",                               event_base + 323 },
            { "EVT_MAP_BRT_R",                               event_base + 324 },
            { "EVT_MAP_BRT_L_PUSHPULL",                      event_base + 895 },
            { "EVT_MAP_BRT_R_PUSHPULL",                      event_base + 896 },

            // ===== Control Stand =====
            { "EVT_CONTROL_STAND_TRIM_WHEEL",                event_base + 678 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER",         event_base + 679 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN",    event_base + 6791 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM",     event_base + 6792 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50PCT",   event_base + 6793 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_FLT_DET", event_base + 6794 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP",      event_base + 6795 },
            { "EVT_CONTROL_STAND_REV_THRUST1_LEVER",         event_base + 680 },
            { "EVT_CONTROL_STAND_REV_THRUST2_LEVER",         event_base + 681 },
            { "EVT_CONTROL_STAND_FWD_THRUST1_LEVER",         event_base + 683 },
            { "EVT_CONTROL_STAND_FWD_THRUST2_LEVER",         event_base + 686 },
            { "EVT_CONTROL_STAND_TOGA1_SWITCH",              event_base + 684 },
            { "EVT_CONTROL_STAND_TOGA2_SWITCH",              event_base + 687 },
            { "EVT_CONTROL_STAND_AT1_DISENGAGE_SWITCH",      event_base + 682 },
            { "EVT_CONTROL_STAND_AT2_DISENGAGE_SWITCH",      event_base + 685 },
            { "EVT_CONTROL_STAND_ENG1_START_LEVER",          event_base + 688 },
            { "EVT_CONTROL_STAND_ENG2_START_LEVER",          event_base + 689 },
            { "EVT_CONTROL_STAND_PARK_BRAKE_LEVER",          event_base + 693 },
            { "EVT_CONTROL_STAND_STABTRIM_ELEC_SWITCH",      event_base + 709 },
            { "EVT_CONTROL_STAND_STABTRIM_ELEC_SWITCH_GUARD",event_base + 710 },
            { "EVT_CONTROL_STAND_STABTRIM_AP_SWITCH",        event_base + 711 },
            { "EVT_CONTROL_STAND_STABTRIM_AP_SWITCH_GUARD",  event_base + 712 },
            { "EVT_CONTROL_STAND_HORN_CUTOUT_SWITCH",        event_base + 713 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER",               event_base + 714 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_0",             event_base + 7141 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_1",             event_base + 7142 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_2",             event_base + 7143 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_5",             event_base + 7144 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_10",            event_base + 7145 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_15",            event_base + 7146 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_25",            event_base + 7147 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_30",            event_base + 7148 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_40",            event_base + 7149 },

            // ===== Flight Deck Door Panel =====
            { "EVT_FLT_DK_DOOR_KNOB",                        event_base + 834 },
            { "EVT_STAB_TRIM_OVRD_SWITCH",                   event_base + 830 },
            { "EVT_STAB_TRIM_OVRD_SWITCH_GUARD",             event_base + 831 },

            // ===== VHF NAV Panels =====
            { "EVT_NAV1_TRANSFER_SWITCH",                    event_base + 729 },
            { "EVT_NAV1_TEST_SWICTH",                        event_base + 731 },
            { "EVT_NAV1_INNER_SELECTOR",                     event_base + 732 },
            { "EVT_NAV1_OUTER_SELECTOR",                     event_base + 733 },
            { "EVT_NAV2_TRANSFER_SWITCH",                    event_base + 845 },
            { "EVT_NAV2_TEST_SWICTH",                        event_base + 847 },
            { "EVT_NAV2_OUTER_SELECTOR",                     event_base + 848 },
            { "EVT_NAV2_INNER_SELECTOR",                     event_base + 849 },

            // ===== MMR Panels =====
            { "EVT_MMR1_TRANSFER_SWITCH",                    event_base + 7210 },
            { "EVT_MMR1_TEST_SWITCH",                        event_base + 7211 },
            { "EVT_MMR1_MODE_DN_SWITCH",                     event_base + 7212 },
            { "EVT_MMR1_MODE_UP_SWITCH",                     event_base + 7213 },
            { "EVT_MMR1_KEYPAD_1",                           event_base + 7214 },
            { "EVT_MMR1_KEYPAD_2",                           event_base + 7215 },
            { "EVT_MMR1_KEYPAD_3",                           event_base + 7216 },
            { "EVT_MMR1_KEYPAD_4",                           event_base + 7217 },
            { "EVT_MMR1_KEYPAD_5",                           event_base + 7218 },
            { "EVT_MMR1_KEYPAD_6",                           event_base + 7219 },
            { "EVT_MMR1_KEYPAD_7",                           event_base + 7220 },
            { "EVT_MMR1_KEYPAD_8",                           event_base + 7221 },
            { "EVT_MMR1_KEYPAD_9",                           event_base + 7222 },
            { "EVT_MMR1_KEYPAD_0",                           event_base + 7223 },
            { "EVT_MMR1_KEYPAD_CLR",                         event_base + 7224 },
            { "EVT_MMR2_TRANSFER_SWITCH",                    event_base + 7225 },
            { "EVT_MMR2_TEST_SWITCH",                        event_base + 7226 },
            { "EVT_MMR2_MODE_DN_SWITCH",                     event_base + 7227 },
            { "EVT_MMR2_MODE_UP_SWITCH",                     event_base + 7228 },
            { "EVT_MMR2_KEYPAD_1",                           event_base + 7229 },
            { "EVT_MMR2_KEYPAD_2",                           event_base + 7230 },
            { "EVT_MMR2_KEYPAD_3",                           event_base + 7231 },
            { "EVT_MMR2_KEYPAD_4",                           event_base + 7232 },
            { "EVT_MMR2_KEYPAD_5",                           event_base + 7233 },
            { "EVT_MMR2_KEYPAD_6",                           event_base + 7234 },
            { "EVT_MMR2_KEYPAD_7",                           event_base + 7235 },
            { "EVT_MMR2_KEYPAD_8",                           event_base + 7236 },
            { "EVT_MMR2_KEYPAD_9",                           event_base + 7237 },
            { "EVT_MMR2_KEYPAD_0",                           event_base + 7238 },
            { "EVT_MMR2_KEYPAD_CLR",                         event_base + 7239 },

            // ===== ADF Panel =====
            { "EVT_ADF_MODE_SELECTOR",                       event_base + 818 },
            { "EVT_ADF_TONE_SWITCH",                         event_base + 820 },
            { "EVT_ADF_INNER_SELECTOR",                      event_base + 822 },
            { "EVT_ADF_MIDDLE_SELECTOR",                     event_base + 823 },
            { "EVT_ADF_OUTER_SELECTOR",                      event_base + 824 },
            { "EVT_ADF_TRANSFER_SWITCH",                     event_base + 827 },

            // ===== SELCAL Panel =====
            { "EVT_SELCAL_VHF1_SWITCH",                      event_base + 812 },
            { "EVT_SELCAL_VHF2_SWITCH",                      event_base + 813 },
            { "EVT_SELCAL_VHF3_SWITCH",                      event_base + 814 },
            { "EVT_SELCAL_HF1_SWITCH",                       event_base + 937 },
            { "EVT_SELCAL_HF2_SWITCH",                       event_base + 938 },

            // ===== COMM Panels =====
            // --- COM1 ---
            { "EVT_COM1_TRANSFER_SWITCH",                    event_base + 721 },
            { "EVT_COM1_HF_SENSOR_KNOB",                     event_base + 724 },
            { "EVT_COM1_TEST_SWICTH",                        event_base + 725 },
            { "EVT_COM1_OUTER_SELECTOR",                     event_base + 726 },
            { "EVT_COM1_INNER_SELECTOR",                     event_base + 727 },
            { "EVT_COM1_PNL_OFF_SWITCH",                     event_base + 903 },
            { "EVT_COM1_VHF1_SWITCH",                        event_base + 904 },
            { "EVT_COM1_VHF2_SWITCH",                        event_base + 906 },
            { "EVT_COM1_VHF3_SWITCH",                        event_base + 908 },
            { "EVT_COM1_HF1_SWITCH",                         event_base + 910 },
            { "EVT_COM1_AM_SWITCH",                          event_base + 912 },
            { "EVT_COM1_HF2_SWITCH",                         event_base + 914 },
            // --- COM2 ---
            { "EVT_COM2_TRANSFER_SWITCH",                    event_base + 837 },
            { "EVT_COM2_HF_SENSOR_KNOB",                     event_base + 840 },
            { "EVT_COM2_TEST_SWICTH",                        event_base + 841 },
            { "EVT_COM2_OUTER_SELECTOR",                     event_base + 842 },
            { "EVT_COM2_INNER_SELECTOR",                     event_base + 843 },
            { "EVT_COM2_PNL_OFF_SWITCH",                     event_base + 924 },
            { "EVT_COM2_VHF1_SWITCH",                        event_base + 925 },
            { "EVT_COM2_VHF2_SWITCH",                        event_base + 927 },
            { "EVT_COM2_VHF3_SWITCH",                        event_base + 929 },
            { "EVT_COM2_HF1_SWITCH",                         event_base + 931 },
            { "EVT_COM2_AM_SWITCH",                          event_base + 933 },
            { "EVT_COM2_HF2_SWITCH",                         event_base + 935 },
            // --- COM3 ---
            { "EVT_COM3_TRANSFER_SWITCH",                    event_base + 946 },
            { "EVT_COM3_HF_SENSOR_KNOB",                     event_base + 949 },
            { "EVT_COM3_TEST_SWICTH",                        event_base + 950 },
            { "EVT_COM3_OUTER_SELECTOR",                     event_base + 951 },
            { "EVT_COM3_INNER_SELECTOR",                     event_base + 952 },
            { "EVT_COM3_PNL_OFF_SWITCH",                     event_base + 953 },
            { "EVT_COM3_VHF1_SWITCH",                        event_base + 954 },
            { "EVT_COM3_VHF2_SWITCH",                        event_base + 956 },
            { "EVT_COM3_VHF3_SWITCH",                        event_base + 958 },
            { "EVT_COM3_HF1_SWITCH",                         event_base + 960 },
            { "EVT_COM3_AM_SWITCH",                          event_base + 962 },
            { "EVT_COM3_HF2_SWITCH",                         event_base + 964 },

            // ===== Audio Control Panel — Captain =====
            { "EVT_ACP_CAPT_MIC_VHF1",                       event_base + 734 },
            { "EVT_ACP_CAPT_MIC_VHF2",                       event_base + 735 },
            { "EVT_ACP_CAPT_MIC_VHF3",                       event_base + 877 },
            { "EVT_ACP_CAPT_MIC_HF1",                        event_base + 878 },
            { "EVT_ACP_CAPT_MIC_HF2",                        event_base + 879 },
            { "EVT_ACP_CAPT_MIC_FLT",                        event_base + 736 },
            { "EVT_ACP_CAPT_MIC_SVC",                        event_base + 737 },
            { "EVT_ACP_CAPT_MIC_PA",                         event_base + 738 },
            { "EVT_ACP_CAPT_REC_VHF1",                       event_base + 739 },
            { "EVT_ACP_CAPT_REC_VHF2",                       event_base + 740 },
            { "EVT_ACP_CAPT_REC_VHF3",                       event_base + 741 },
            { "EVT_ACP_CAPT_REC_HF1",                        event_base + 742 },
            { "EVT_ACP_CAPT_REC_HF2",                        event_base + 880 },
            { "EVT_ACP_CAPT_REC_FLT",                        event_base + 743 },
            { "EVT_ACP_CAPT_REC_SVC",                        event_base + 744 },
            { "EVT_ACP_CAPT_REC_PA",                         event_base + 745 },
            { "EVT_ACP_CAPT_REC_NAV1",                       event_base + 746 },
            { "EVT_ACP_CAPT_REC_NAV2",                       event_base + 747 },
            { "EVT_ACP_CAPT_REC_ADF1",                       event_base + 748 },
            { "EVT_ACP_CAPT_REC_ADF2",                       event_base + 749 },
            { "EVT_ACP_CAPT_REC_MKR",                        event_base + 750 },
            { "EVT_ACP_CAPT_REC_SPKR",                       event_base + 751 },
            { "EVT_ACP_CAPT_RT_IC_SWITCH",                   event_base + 752 },
            { "EVT_ACP_CAPT_MASK_BOOM_SWITCH",               event_base + 753 },
            { "EVT_ACP_CAPT_FILTER_SWITCH",                  event_base + 754 },
            { "EVT_ACP_CAPT_ALT_NORM_SWITCH",                event_base + 755 },

            // ===== Audio Control Panel — F/O =====
            { "EVT_ACP_FO_MIC_VHF1",                         event_base + 850 },
            { "EVT_ACP_FO_MIC_VHF2",                         event_base + 851 },
            { "EVT_ACP_FO_MIC_VHF3",                         event_base + 881 },
            { "EVT_ACP_FO_MIC_HF1",                          event_base + 882 },
            { "EVT_ACP_FO_MIC_HF2",                          event_base + 883 },
            { "EVT_ACP_FO_MIC_FLT",                          event_base + 852 },
            { "EVT_ACP_FO_MIC_SVC",                          event_base + 853 },
            { "EVT_ACP_FO_MIC_PA",                           event_base + 854 },
            { "EVT_ACP_FO_REC_VHF1",                         event_base + 855 },
            { "EVT_ACP_FO_REC_VHF2",                         event_base + 856 },
            { "EVT_ACP_FO_REC_VHF3",                         event_base + 857 },
            { "EVT_ACP_FO_REC_HF1",                          event_base + 858 },
            { "EVT_ACP_FO_REC_HF2",                          event_base + 884 },
            { "EVT_ACP_FO_REC_FLT",                          event_base + 859 },
            { "EVT_ACP_FO_REC_SVC",                          event_base + 860 },
            { "EVT_ACP_FO_REC_PA",                           event_base + 861 },
            { "EVT_ACP_FO_REC_NAV1",                         event_base + 862 },
            { "EVT_ACP_FO_REC_NAV2",                         event_base + 863 },
            { "EVT_ACP_FO_REC_ADF1",                         event_base + 864 },
            { "EVT_ACP_FO_REC_ADF2",                         event_base + 865 },
            { "EVT_ACP_FO_REC_MKR",                          event_base + 866 },
            { "EVT_ACP_FO_REC_SPKR",                         event_base + 867 },
            { "EVT_ACP_FO_VOL_NAV1",                         event_base + 1862 },
            { "EVT_ACP_FO_VOL_NAV2",                         event_base + 1863 },
            { "EVT_ACP_FO_VOL_ADF1",                         event_base + 1864 },
            { "EVT_ACP_FO_VOL_ADF2",                         event_base + 1865 },
            { "EVT_ACP_FO_VOL_MKR",                          event_base + 1866 },
            { "EVT_ACP_FO_RT_IC_SWITCH",                     event_base + 868 },
            { "EVT_ACP_FO_MASK_BOOM_SWITCH",                 event_base + 869 },
            { "EVT_ACP_FO_FILTER_SWITCH",                    event_base + 870 },
            { "EVT_ACP_FO_ALT_NORM_SWITCH",                  event_base + 871 },

            // ===== Audio Control Panel — Observer =====
            { "EVT_ACP_OBS_MIC_VHF1",                        event_base + 291 },
            { "EVT_ACP_OBS_MIC_VHF2",                        event_base + 292 },
            { "EVT_ACP_OBS_MIC_VHF3",                        event_base + 293 },
            { "EVT_ACP_OBS_MIC_HF1",                         event_base + 294 },
            { "EVT_ACP_OBS_MIC_HF2",                         event_base + 295 },
            { "EVT_ACP_OBS_MIC_FLT",                         event_base + 296 },
            { "EVT_ACP_OBS_MIC_SVC",                         event_base + 297 },
            { "EVT_ACP_OBS_MIC_PA",                          event_base + 873 },
            { "EVT_ACP_OBS_REC_VHF1",                        event_base + 286 },
            { "EVT_ACP_OBS_REC_VHF2",                        event_base + 287 },
            { "EVT_ACP_OBS_REC_VHF3",                        event_base + 874 },
            { "EVT_ACP_OBS_REC_HF1",                         event_base + 875 },
            { "EVT_ACP_OBS_REC_HF2",                         event_base + 876 },
            { "EVT_ACP_OBS_REC_FLT",                         event_base + 288 },
            { "EVT_ACP_OBS_REC_SVC",                         event_base + 289 },
            { "EVT_ACP_OBS_REC_PA",                          event_base + 290 },
            { "EVT_ACP_OBS_REC_NAV1",                        event_base + 280 },
            { "EVT_ACP_OBS_REC_NAV2",                        event_base + 281 },
            { "EVT_ACP_OBS_REC_ADF1",                        event_base + 282 },
            { "EVT_ACP_OBS_REC_ADF2",                        event_base + 283 },
            { "EVT_ACP_OBS_REC_MKR",                         event_base + 284 },
            { "EVT_ACP_OBS_REC_SPKR",                        event_base + 285 },
            { "EVT_ACP_OBS_VOL_NAV1",                        event_base + 1280 },
            { "EVT_ACP_OBS_VOL_NAV2",                        event_base + 1281 },
            { "EVT_ACP_OBS_VOL_ADF1",                        event_base + 1282 },
            { "EVT_ACP_OBS_VOL_ADF2",                        event_base + 1283 },
            { "EVT_ACP_OBS_VOL_MKR",                         event_base + 1284 },
            { "EVT_ACP_OBS_RT_IC_SWITCH",                    event_base + 276 },
            { "EVT_ACP_OBS_MASK_BOOM_SWITCH",                event_base + 277 },
            { "EVT_ACP_OBS_FILTER_SWITCH",                   event_base + 278 },
            { "EVT_ACP_OBS_ALT_NORM_SWITCH",                 event_base + 279 },

            // ===== WX Radar Panel =====
            { "EVT_WXR_L_TFR",                               event_base + 790 },
            { "EVT_WXR_L_WX",                                event_base + 791 },
            { "EVT_WXR_L_WX_T",                              event_base + 916 },
            { "EVT_WXR_L_MAP",                               event_base + 792 },
            { "EVT_WXR_L_GC",                                event_base + 793 },
            { "EVT_WXR_AUTO",                                event_base + 917 },
            { "EVT_WXR_TEST",                                event_base + 918 },
            { "EVT_WXR_R_TFR",                               event_base + 919 },
            { "EVT_WXR_R_WX",                                event_base + 796 },
            { "EVT_WXR_R_WX_T",                              event_base + 920 },
            { "EVT_WXR_R_MAP",                               event_base + 797 },
            { "EVT_WXR_R_GC",                                event_base + 921 },
            { "EVT_WXR_L_TILT_CONTROL",                      event_base + 794 },
            { "EVT_WXR_L_GAIN_CONTROL",                      event_base + 923 },
            { "EVT_WXR_R_TILT_CONTROL",                      event_base + 795 },
            { "EVT_WXR_R_GAIN_CONTROL",                      event_base + 922 },

            // ===== TCAS =====
            { "EVT_TCAS_XPNDR",                              event_base + 798 },
            { "EVT_TCAS_MODE",                               event_base + 800 },
            { "EVT_TCAS_TEST",                               event_base + 801 },
            { "EVT_TCAS_ALTSOURCE",                          event_base + 803 },
            { "EVT_TCAS_KNOB1",                              event_base + 804 },
            { "EVT_TCAS_KNOB2",                              event_base + 805 },
            { "EVT_TCAS_IDENT",                              event_base + 806 },
            { "EVT_TCAS_KNOB3",                              event_base + 807 },
            { "EVT_TCAS_KNOB4",                              event_base + 808 },

            // ===== HUD Control Panel =====
            { "EVT_HUD_MODE",                                event_base + 770 },
            { "EVT_HUD_STB",                                 event_base + 771 },
            { "EVT_HUD_RWY",                                 event_base + 772 },
            { "EVT_HUD_GS",                                  event_base + 773 },
            { "EVT_HUD_CLR",                                 event_base + 775 },
            { "EVT_HUD_BRT",                                 event_base + 776 },
            { "EVT_HUD_DIM",                                 event_base + 777 },
            { "EVT_HUD_1",                                   event_base + 778 },
            { "EVT_HUD_2",                                   event_base + 779 },
            { "EVT_HUD_3",                                   event_base + 780 },
            { "EVT_HUD_4",                                   event_base + 781 },
            { "EVT_HUD_5",                                   event_base + 782 },
            { "EVT_HUD_6",                                   event_base + 783 },
            { "EVT_HUD_7",                                   event_base + 784 },
            { "EVT_HUD_8",                                   event_base + 785 },
            { "EVT_HUD_9",                                   event_base + 786 },
            { "EVT_HUD_0",                                   event_base + 788 },
            { "EVT_HUD_ENTER",                               event_base + 787 },
            { "EVT_HUD_TEST",                                event_base + 789 },
            { "EVT_HUD_STOW",                                event_base + 979 },
            { "EVT_HUD_BRIGTHNESS",                          event_base + 980 },
            { "EVT_HUD_AUTO_MAN",                            event_base + 981 },
            { "EVT_HGS_EYEPOINT",                            event_base + 984 },
            { "EVT_NON_HGS_EYEPOINT",                        event_base + 985 },

            // ===== HUD Annunciator Panel =====
            { "EVT_HGS_FAIL_SWITCH",                         event_base + 522 },

            // ===== CDU L (Captain) =====
            { "EVT_CDU_L_L1",                                event_base + 534 },
            { "EVT_CDU_L_L2",                                event_base + 535 },
            { "EVT_CDU_L_L3",                                event_base + 536 },
            { "EVT_CDU_L_L4",                                event_base + 537 },
            { "EVT_CDU_L_L5",                                event_base + 538 },
            { "EVT_CDU_L_L6",                                event_base + 539 },
            { "EVT_CDU_L_R1",                                event_base + 540 },
            { "EVT_CDU_L_R2",                                event_base + 541 },
            { "EVT_CDU_L_R3",                                event_base + 542 },
            { "EVT_CDU_L_R4",                                event_base + 543 },
            { "EVT_CDU_L_R5",                                event_base + 544 },
            { "EVT_CDU_L_R6",                                event_base + 545 },
            { "EVT_CDU_L_INIT_REF",                          event_base + 546 },
            { "EVT_CDU_L_RTE",                               event_base + 547 },
            { "EVT_CDU_L_CLB",                               event_base + 548 },
            { "EVT_CDU_L_CRZ",                               event_base + 549 },
            { "EVT_CDU_L_DES",                               event_base + 550 },
            { "EVT_CDU_L_MENU",                              event_base + 551 },
            { "EVT_CDU_L_LEGS",                              event_base + 552 },
            { "EVT_CDU_L_DEP_ARR",                           event_base + 553 },
            { "EVT_CDU_L_HOLD",                              event_base + 554 },
            { "EVT_CDU_L_PROG",                              event_base + 555 },
            { "EVT_CDU_L_EXEC",                              event_base + 556 },
            { "EVT_CDU_L_N1_LIMIT",                          event_base + 557 },
            { "EVT_CDU_L_FIX",                               event_base + 558 },
            { "EVT_CDU_L_PREV_PAGE",                         event_base + 559 },
            { "EVT_CDU_L_NEXT_PAGE",                         event_base + 560 },
            { "EVT_CDU_L_1",                                 event_base + 561 },
            { "EVT_CDU_L_2",                                 event_base + 562 },
            { "EVT_CDU_L_3",                                 event_base + 563 },
            { "EVT_CDU_L_4",                                 event_base + 564 },
            { "EVT_CDU_L_5",                                 event_base + 565 },
            { "EVT_CDU_L_6",                                 event_base + 566 },
            { "EVT_CDU_L_7",                                 event_base + 567 },
            { "EVT_CDU_L_8",                                 event_base + 568 },
            { "EVT_CDU_L_9",                                 event_base + 569 },
            { "EVT_CDU_L_DOT",                               event_base + 570 },
            { "EVT_CDU_L_0",                                 event_base + 571 },
            { "EVT_CDU_L_PLUS_MINUS",                        event_base + 572 },
            { "EVT_CDU_L_A",                                 event_base + 573 },
            { "EVT_CDU_L_B",                                 event_base + 574 },
            { "EVT_CDU_L_C",                                 event_base + 575 },
            { "EVT_CDU_L_D",                                 event_base + 576 },
            { "EVT_CDU_L_E",                                 event_base + 577 },
            { "EVT_CDU_L_F",                                 event_base + 578 },
            { "EVT_CDU_L_G",                                 event_base + 579 },
            { "EVT_CDU_L_H",                                 event_base + 580 },
            { "EVT_CDU_L_I",                                 event_base + 581 },
            { "EVT_CDU_L_J",                                 event_base + 582 },
            { "EVT_CDU_L_K",                                 event_base + 583 },
            { "EVT_CDU_L_L",                                 event_base + 584 },
            { "EVT_CDU_L_M",                                 event_base + 585 },
            { "EVT_CDU_L_N",                                 event_base + 586 },
            { "EVT_CDU_L_O",                                 event_base + 587 },
            { "EVT_CDU_L_P",                                 event_base + 588 },
            { "EVT_CDU_L_Q",                                 event_base + 589 },
            { "EVT_CDU_L_R",                                 event_base + 590 },
            { "EVT_CDU_L_S",                                 event_base + 591 },
            { "EVT_CDU_L_T",                                 event_base + 592 },
            { "EVT_CDU_L_U",                                 event_base + 593 },
            { "EVT_CDU_L_V",                                 event_base + 594 },
            { "EVT_CDU_L_W",                                 event_base + 595 },
            { "EVT_CDU_L_X",                                 event_base + 596 },
            { "EVT_CDU_L_Y",                                 event_base + 597 },
            { "EVT_CDU_L_Z",                                 event_base + 598 },
            { "EVT_CDU_L_SPACE",                             event_base + 599 },
            { "EVT_CDU_L_DEL",                               event_base + 600 },
            { "EVT_CDU_L_SLASH",                             event_base + 601 },
            { "EVT_CDU_L_CLR",                               event_base + 602 },
            { "EVT_CDU_L_BRITENESS",                         event_base + 605 },

            // ===== CDU R (F/O) =====
            { "EVT_CDU_R_L1",                                event_base + 606 },
            { "EVT_CDU_R_L2",                                event_base + 607 },
            { "EVT_CDU_R_L3",                                event_base + 608 },
            { "EVT_CDU_R_L4",                                event_base + 609 },
            { "EVT_CDU_R_L5",                                event_base + 610 },
            { "EVT_CDU_R_L6",                                event_base + 611 },
            { "EVT_CDU_R_R1",                                event_base + 612 },
            { "EVT_CDU_R_R2",                                event_base + 613 },
            { "EVT_CDU_R_R3",                                event_base + 614 },
            { "EVT_CDU_R_R4",                                event_base + 615 },
            { "EVT_CDU_R_R5",                                event_base + 616 },
            { "EVT_CDU_R_R6",                                event_base + 617 },
            { "EVT_CDU_R_INIT_REF",                          event_base + 618 },
            { "EVT_CDU_R_RTE",                               event_base + 619 },
            { "EVT_CDU_R_CLB",                               event_base + 620 },
            { "EVT_CDU_R_CRZ",                               event_base + 621 },
            { "EVT_CDU_R_DES",                               event_base + 622 },
            { "EVT_CDU_R_MENU",                              event_base + 623 },
            { "EVT_CDU_R_LEGS",                              event_base + 624 },
            { "EVT_CDU_R_DEP_ARR",                           event_base + 625 },
            { "EVT_CDU_R_HOLD",                              event_base + 626 },
            { "EVT_CDU_R_PROG",                              event_base + 627 },
            { "EVT_CDU_R_EXEC",                              event_base + 628 },
            { "EVT_CDU_R_N1_LIMIT",                          event_base + 629 },
            { "EVT_CDU_R_FIX",                               event_base + 630 },
            { "EVT_CDU_R_PREV_PAGE",                         event_base + 631 },
            { "EVT_CDU_R_NEXT_PAGE",                         event_base + 632 },
            { "EVT_CDU_R_1",                                 event_base + 633 },
            { "EVT_CDU_R_2",                                 event_base + 634 },
            { "EVT_CDU_R_3",                                 event_base + 635 },
            { "EVT_CDU_R_4",                                 event_base + 636 },
            { "EVT_CDU_R_5",                                 event_base + 637 },
            { "EVT_CDU_R_6",                                 event_base + 638 },
            { "EVT_CDU_R_7",                                 event_base + 639 },
            { "EVT_CDU_R_8",                                 event_base + 640 },
            { "EVT_CDU_R_9",                                 event_base + 641 },
            { "EVT_CDU_R_DOT",                               event_base + 642 },
            { "EVT_CDU_R_0",                                 event_base + 643 },
            { "EVT_CDU_R_PLUS_MINUS",                        event_base + 644 },
            { "EVT_CDU_R_A",                                 event_base + 645 },
            { "EVT_CDU_R_B",                                 event_base + 646 },
            { "EVT_CDU_R_C",                                 event_base + 647 },
            { "EVT_CDU_R_D",                                 event_base + 648 },
            { "EVT_CDU_R_E",                                 event_base + 649 },
            { "EVT_CDU_R_F",                                 event_base + 650 },
            { "EVT_CDU_R_G",                                 event_base + 651 },
            { "EVT_CDU_R_H",                                 event_base + 652 },
            { "EVT_CDU_R_I",                                 event_base + 653 },
            { "EVT_CDU_R_J",                                 event_base + 654 },
            { "EVT_CDU_R_K",                                 event_base + 655 },
            { "EVT_CDU_R_L",                                 event_base + 656 },
            { "EVT_CDU_R_M",                                 event_base + 657 },
            { "EVT_CDU_R_N",                                 event_base + 658 },
            { "EVT_CDU_R_O",                                 event_base + 659 },
            { "EVT_CDU_R_P",                                 event_base + 660 },
            { "EVT_CDU_R_Q",                                 event_base + 661 },
            { "EVT_CDU_R_R",                                 event_base + 662 },
            { "EVT_CDU_R_S",                                 event_base + 663 },
            { "EVT_CDU_R_T",                                 event_base + 664 },
            { "EVT_CDU_R_U",                                 event_base + 665 },
            { "EVT_CDU_R_V",                                 event_base + 666 },
            { "EVT_CDU_R_W",                                 event_base + 667 },
            { "EVT_CDU_R_X",                                 event_base + 668 },
            { "EVT_CDU_R_Y",                                 event_base + 669 },
            { "EVT_CDU_R_Z",                                 event_base + 670 },
            { "EVT_CDU_R_SPACE",                             event_base + 671 },
            { "EVT_CDU_R_DEL",                               event_base + 672 },
            { "EVT_CDU_R_SLASH",                             event_base + 673 },
            { "EVT_CDU_R_CLR",                               event_base + 674 },
            { "EVT_CDU_R_BRITENESS",                         event_base + 677 },

            // ===== Fire Protection Panel =====
            { "EVT_FIRE_OVHT_DET_SWITCH_1",                  event_base + 694 },
            { "EVT_FIRE_DETECTION_TEST_SWITCH",              event_base + 696 },
            { "EVT_FIRE_HANDLE_ENGINE_1_TOP",                event_base + 697 },
            { "EVT_FIRE_HANDLE_ENGINE_1_BOTTOM",             event_base + 6971 },
            { "EVT_FIRE_HANDLE_APU_TOP",                     event_base + 698 },
            { "EVT_FIRE_HANDLE_APU_BOTTOM",                  event_base + 6981 },
            { "EVT_FIRE_HANDLE_ENGINE_2_TOP",                event_base + 699 },
            { "EVT_FIRE_HANDLE_ENGINE_2_BOTTOM",             event_base + 6991 },
            { "EVT_FIRE_BELL_CUTOUT_SWITCH",                 event_base + 704 },
            { "EVT_FIRE_OVHT_DET_SWITCH_2",                  event_base + 705 },
            { "EVT_FIRE_EXTINGUISHER_TEST_SWITCH",           event_base + 715 },
            { "EVT_FIRE_UNLOCK_SWITCH_ENGINE_1",             event_base + 976 },
            { "EVT_FIRE_UNLOCK_SWITCH_APU",                  event_base + 977 },
            { "EVT_FIRE_UNLOCK_SWITCH_ENGINE_2",             event_base + 978 },

            // ===== Cargo Fire =====
            { "EVT_CARGO_FIRE_DET_SEL_SWITCH_FWD",           event_base + 760 },
            { "EVT_CARGO_FIRE_DET_SEL_SWITCH_AFT",           event_base + 761 },
            { "EVT_CARGO_FIRE_DET_SEL_SWITCH_MAIN",          event_base + 762 },
            { "EVT_CARGO_FIRE_ARM_SWITCH_FWD",               event_base + 763 },
            { "EVT_CARGO_FIRE_ARM_SWITCH_AFT",               event_base + 765 },
            { "EVT_CARGO_FIRE_ARM_SWITCH_MAIN",              event_base + 7651 },
            { "EVT_CARGO_FIRE_DISC_SWITCH_GUARD",            event_base + 768 },
            { "EVT_CARGO_FIRE_DISC_SWITCH",                  event_base + 767 },
            { "EVT_CARGO_FIRE_TEST_SWITCH",                  event_base + 769 },

            // ===== Lav / Supernumerary Smoke (Freighter 700/800) =====
            { "EVT_CARGO_SMOKE_TEST",                        event_base + 905 },
            { "EVT_CARGO_SMOKE_BELL_CUTOUT",                 event_base + 907 },
            { "EVT_CARGO_SMOKE",                             event_base + 909 },
            { "EVT_CARGO_SMOKE_GUARD",                       event_base + 911 },
            { "EVT_LAV_SMOKE_TEST",                          event_base + 913 },
            { "EVT_LAV_SMOKE_BELL_CUTOUT",                   event_base + 915 },

            // ===== Flight Controls — Pedestal =====
            { "EVT_FCTL_AILERON_TRIM",                       event_base + 810 },
            { "EVT_FCTL_RUDDER_TRIM",                        event_base + 811 },

            // ===== Pedestal Lights =====
            { "EVT_PED_FLOOD_CONTROL",                       event_base + 756 },
            { "EVT_PED_PANEL_CONTROL",                       event_base + 757 },

            // ===== EFB L (Captain) — Hardware Buttons =====
            { "EVT_EFB_L_MENU",                              event_base + 1700 },
            { "EVT_EFB_L_BACK",                              event_base + 1701 },
            { "EVT_EFB_L_PAGE_UP",                           event_base + 1702 },
            { "EVT_EFB_L_PAGE_DOWN",                         event_base + 1703 },
            { "EVT_EFB_L_XFR",                               event_base + 1704 },
            { "EVT_EFB_L_ENTER",                             event_base + 1705 },
            { "EVT_EFB_L_ZOOM_IN",                           event_base + 1706 },
            { "EVT_EFB_L_ZOOM_OUT",                          event_base + 1707 },
            { "EVT_EFB_L_ARROW_UP",                          event_base + 1708 },
            { "EVT_EFB_L_ARROW_DOWN",                        event_base + 1709 },
            { "EVT_EFB_L_ARROW_LEFT",                        event_base + 1710 },
            { "EVT_EFB_L_ARROW_RIGHT",                       event_base + 1711 },
            { "EVT_EFB_L_LSK_1L",                            event_base + 1712 },
            { "EVT_EFB_L_LSK_2L",                            event_base + 1713 },
            { "EVT_EFB_L_LSK_3L",                            event_base + 1714 },
            { "EVT_EFB_L_LSK_4L",                            event_base + 1715 },
            { "EVT_EFB_L_LSK_5L",                            event_base + 1716 },
            { "EVT_EFB_L_LSK_6L",                            event_base + 1717 },
            { "EVT_EFB_L_LSK_7L",                            event_base + 1718 },
            { "EVT_EFB_L_LSK_8L",                            event_base + 1719 },
            { "EVT_EFB_L_LSK_1R",                            event_base + 1720 },
            { "EVT_EFB_L_LSK_2R",                            event_base + 1721 },
            { "EVT_EFB_L_LSK_3R",                            event_base + 1722 },
            { "EVT_EFB_L_LSK_4R",                            event_base + 1723 },
            { "EVT_EFB_L_LSK_5R",                            event_base + 1724 },
            { "EVT_EFB_L_LSK_6R",                            event_base + 1725 },
            { "EVT_EFB_L_LSK_7R",                            event_base + 1726 },
            { "EVT_EFB_L_LSK_8R",                            event_base + 1727 },
            { "EVT_EFB_L_BRIGHT_UP",                         event_base + 1728 },
            { "EVT_EFB_L_BRIGHT_DN",                         event_base + 1729 },
            { "EVT_EFB_L_POWER",                             event_base + 1730 },

            // ===== EFB L (Captain) — On-Screen Keyboard =====
            { "EVT_EFB_L_KEY_A",                             event_base + 1731 },
            { "EVT_EFB_L_KEY_B",                             event_base + 1732 },
            { "EVT_EFB_L_KEY_C",                             event_base + 1733 },
            { "EVT_EFB_L_KEY_D",                             event_base + 1734 },
            { "EVT_EFB_L_KEY_E",                             event_base + 1735 },
            { "EVT_EFB_L_KEY_F",                             event_base + 1736 },
            { "EVT_EFB_L_KEY_G",                             event_base + 1737 },
            { "EVT_EFB_L_KEY_H",                             event_base + 1738 },
            { "EVT_EFB_L_KEY_I",                             event_base + 1739 },
            { "EVT_EFB_L_KEY_J",                             event_base + 1740 },
            { "EVT_EFB_L_KEY_K",                             event_base + 1741 },
            { "EVT_EFB_L_KEY_L",                             event_base + 1742 },
            { "EVT_EFB_L_KEY_M",                             event_base + 1743 },
            { "EVT_EFB_L_KEY_N",                             event_base + 1744 },
            { "EVT_EFB_L_KEY_O",                             event_base + 1745 },
            { "EVT_EFB_L_KEY_P",                             event_base + 1746 },
            { "EVT_EFB_L_KEY_Q",                             event_base + 1747 },
            { "EVT_EFB_L_KEY_R",                             event_base + 1748 },
            { "EVT_EFB_L_KEY_S",                             event_base + 1749 },
            { "EVT_EFB_L_KEY_T",                             event_base + 1750 },
            { "EVT_EFB_L_KEY_U",                             event_base + 1751 },
            { "EVT_EFB_L_KEY_V",                             event_base + 1752 },
            { "EVT_EFB_L_KEY_W",                             event_base + 1753 },
            { "EVT_EFB_L_KEY_X",                             event_base + 1754 },
            { "EVT_EFB_L_KEY_Y",                             event_base + 1755 },
            { "EVT_EFB_L_KEY_Z",                             event_base + 1756 },
            { "EVT_EFB_L_KEY_0",                             event_base + 1757 },
            { "EVT_EFB_L_KEY_1",                             event_base + 1758 },
            { "EVT_EFB_L_KEY_2",                             event_base + 1759 },
            { "EVT_EFB_L_KEY_3",                             event_base + 1760 },
            { "EVT_EFB_L_KEY_4",                             event_base + 1761 },
            { "EVT_EFB_L_KEY_5",                             event_base + 1762 },
            { "EVT_EFB_L_KEY_6",                             event_base + 1763 },
            { "EVT_EFB_L_KEY_7",                             event_base + 1764 },
            { "EVT_EFB_L_KEY_8",                             event_base + 1765 },
            { "EVT_EFB_L_KEY_9",                             event_base + 1766 },
            { "EVT_EFB_L_KEY_SPACE",                         event_base + 1767 },
            { "EVT_EFB_L_KEY_PLUS",                          event_base + 1768 },
            { "EVT_EFB_L_KEY_MINUS",                         event_base + 1769 },
            { "EVT_EFB_L_KEY_DOT",                           event_base + 1770 },
            { "EVT_EFB_L_KEY_SLASH",                         event_base + 1771 },
            { "EVT_EFB_L_KEY_BACKSPACE",                     event_base + 1772 },
            { "EVT_EFB_L_KEY_DEL",                           event_base + 1773 },
            { "EVT_EFB_L_KEY_EQUAL",                         event_base + 1774 },
            { "EVT_EFB_L_KEY_MULTIPLY",                      event_base + 1775 },
            { "EVT_EFB_L_KEY_LEFT_PAR",                      event_base + 1776 },
            { "EVT_EFB_L_KEY_RIGHT_PAR",                     event_base + 1777 },
            { "EVT_EFB_L_KEY_QUEST",                         event_base + 1778 },
            { "EVT_EFB_L_KEY_QUOTE",                         event_base + 1779 },
            { "EVT_EFB_L_KEY_COMMA",                         event_base + 1780 },
            { "EVT_EFB_L_KEY_PAGE_UP",                       event_base + 1781 },
            { "EVT_EFB_L_KEY_PAGE_DOWN",                     event_base + 1782 },
            { "EVT_EFB_L_KEY_ENTER",                         event_base + 1783 },
            { "EVT_EFB_L_KEY_ARROW_UP",                      event_base + 1784 },
            { "EVT_EFB_L_KEY_ARROW_DOWN",                    event_base + 1785 },

            // ===== EFB R (F/O) — Hardware Buttons =====
            // EVT_EFB_R_START = EVT_EFB_L_END + 1 = 1731 + 54 + 1 = 1786
            { "EVT_EFB_R_MENU",                              event_base + 1786 },
            { "EVT_EFB_R_BACK",                              event_base + 1787 },
            { "EVT_EFB_R_PAGE_UP",                           event_base + 1788 },
            { "EVT_EFB_R_PAGE_DOWN",                         event_base + 1789 },
            { "EVT_EFB_R_XFR",                               event_base + 1790 },
            { "EVT_EFB_R_ENTER",                             event_base + 1791 },
            { "EVT_EFB_R_ZOOM_IN",                           event_base + 1792 },
            { "EVT_EFB_R_ZOOM_OUT",                          event_base + 1793 },
            { "EVT_EFB_R_ARROW_UP",                          event_base + 1794 },
            { "EVT_EFB_R_ARROW_DOWN",                        event_base + 1795 },
            { "EVT_EFB_R_ARROW_LEFT",                        event_base + 1796 },
            { "EVT_EFB_R_ARROW_RIGHT",                       event_base + 1797 },
            { "EVT_EFB_R_LSK_1L",                            event_base + 1798 },
            { "EVT_EFB_R_LSK_2L",                            event_base + 1799 },
            { "EVT_EFB_R_LSK_3L",                            event_base + 1800 },
            { "EVT_EFB_R_LSK_4L",                            event_base + 1801 },
            { "EVT_EFB_R_LSK_5L",                            event_base + 1802 },
            { "EVT_EFB_R_LSK_6L",                            event_base + 1803 },
            { "EVT_EFB_R_LSK_7L",                            event_base + 1804 },
            { "EVT_EFB_R_LSK_8L",                            event_base + 1805 },
            { "EVT_EFB_R_LSK_1R",                            event_base + 1806 },
            { "EVT_EFB_R_LSK_2R",                            event_base + 1807 },
            { "EVT_EFB_R_LSK_3R",                            event_base + 1808 },
            { "EVT_EFB_R_LSK_4R",                            event_base + 1809 },
            { "EVT_EFB_R_LSK_5R",                            event_base + 1810 },
            { "EVT_EFB_R_LSK_6R",                            event_base + 1811 },
            { "EVT_EFB_R_LSK_7R",                            event_base + 1812 },
            { "EVT_EFB_R_LSK_8R",                            event_base + 1813 },
            // SDK header has these two pointing to EVT_EFB_L_START offsets (+ 28 / + 29 = 1728 / 1729).
            // Not a typo on our side — that's what the header declares.
            { "EVT_EFB_R_BRIGH_UP",                          event_base + 1728 },
            { "EVT_EFB_R_BRIGHT_DN",                         event_base + 1729 },
            { "EVT_EFB_R_POWER",                             event_base + 1816 },

            // ===== EFB R (F/O) — On-Screen Keyboard =====
            // EVT_EFB_R_KEY_START = EVT_EFB_R_START + 31 = 1786 + 31 = 1817
            { "EVT_EFB_R_KEY_A",                             event_base + 1817 },
            { "EVT_EFB_R_KEY_B",                             event_base + 1818 },
            { "EVT_EFB_R_KEY_C",                             event_base + 1819 },
            { "EVT_EFB_R_KEY_D",                             event_base + 1820 },
            { "EVT_EFB_R_KEY_E",                             event_base + 1821 },
            { "EVT_EFB_R_KEY_F",                             event_base + 1822 },
            { "EVT_EFB_R_KEY_G",                             event_base + 1823 },
            { "EVT_EFB_R_KEY_H",                             event_base + 1824 },
            { "EVT_EFB_R_KEY_I",                             event_base + 1825 },
            { "EVT_EFB_R_KEY_J",                             event_base + 1826 },
            { "EVT_EFB_R_KEY_K",                             event_base + 1827 },
            { "EVT_EFB_R_KEY_L",                             event_base + 1828 },
            { "EVT_EFB_R_KEY_M",                             event_base + 1829 },
            { "EVT_EFB_R_KEY_N",                             event_base + 1830 },
            { "EVT_EFB_R_KEY_O",                             event_base + 1831 },
            { "EVT_EFB_R_KEY_P",                             event_base + 1832 },
            { "EVT_EFB_R_KEY_Q",                             event_base + 1833 },
            { "EVT_EFB_R_KEY_R",                             event_base + 1834 },
            { "EVT_EFB_R_KEY_S",                             event_base + 1835 },
            { "EVT_EFB_R_KEY_T",                             event_base + 1836 },
            { "EVT_EFB_R_KEY_U",                             event_base + 1837 },
            { "EVT_EFB_R_KEY_V",                             event_base + 1838 },
            { "EVT_EFB_R_KEY_W",                             event_base + 1839 },
            { "EVT_EFB_R_KEY_X",                             event_base + 1840 },
            { "EVT_EFB_R_KEY_Y",                             event_base + 1841 },
            { "EVT_EFB_R_KEY_Z",                             event_base + 1842 },
            { "EVT_EFB_R_KEY_0",                             event_base + 1843 },
            { "EVT_EFB_R_KEY_1",                             event_base + 1844 },
            { "EVT_EFB_R_KEY_2",                             event_base + 1845 },
            { "EVT_EFB_R_KEY_3",                             event_base + 1846 },
            { "EVT_EFB_R_KEY_4",                             event_base + 1847 },
            { "EVT_EFB_R_KEY_5",                             event_base + 1848 },
            { "EVT_EFB_R_KEY_6",                             event_base + 1849 },
            { "EVT_EFB_R_KEY_7",                             event_base + 1850 },
            { "EVT_EFB_R_KEY_8",                             event_base + 1851 },
            { "EVT_EFB_R_KEY_9",                             event_base + 1852 },
            { "EVT_EFB_R_KEY_SPACE",                         event_base + 1853 },
            { "EVT_EFB_R_KEY_PLUS",                          event_base + 1854 },
            { "EVT_EFB_R_KEY_MINUS",                         event_base + 1855 },
            { "EVT_EFB_R_KEY_DOT",                           event_base + 1856 },
            { "EVT_EFB_R_KEY_SLASH",                         event_base + 1857 },
            { "EVT_EFB_R_KEY_BACKSPACE",                     event_base + 1858 },
            { "EVT_EFB_R_KEY_DEL",                           event_base + 1859 },
            { "EVT_EFB_R_KEY_EQUAL",                         event_base + 1860 },
            { "EVT_EFB_R_KEY_MULTIPLY",                      event_base + 1861 },
            { "EVT_EFB_R_KEY_LEFT_PAR",                      event_base + 1862 },
            { "EVT_EFB_R_KEY_RIGHT_PAR",                     event_base + 1863 },
            { "EVT_EFB_R_KEY_QUEST",                         event_base + 1864 },
            { "EVT_EFB_R_KEY_QUOTE",                         event_base + 1865 },
            { "EVT_EFB_R_KEY_COMMA",                         event_base + 1866 },
            { "EVT_EFB_R_KEY_PAGE_UP",                       event_base + 1867 },
            { "EVT_EFB_R_KEY_PAGE_DOWN",                     event_base + 1868 },
            { "EVT_EFB_R_KEY_ENTER",                         event_base + 1869 },
            { "EVT_EFB_R_KEY_ARROW_UP",                      event_base + 1870 },
            { "EVT_EFB_R_KEY_ARROW_DOWN",                    event_base + 1871 },

            // ===== EFB — Screen Action =====
            { "EVT_EFB_L_SCREEN_ACTION",                     event_base + 1900 },
            { "EVT_EFB_R_SCREEN_ACTION",                     event_base + 1901 },

            // ===== Various =====
            { "EVT_JUMPSEAT_STOW_EXTEND",                    event_base + 2001 },
            // NOT in the public SDK header — discovered via PMDG's switch-number
            // == event-offset convention and live-verified 2026-06-12 (each one
            // moves its switch_NNNN_73X read-back L-var; ground service confirmed
            // two-way 0↔100, lights confirmed 3-position 0/50/100 for param 0/1/2).
            { "EVT_GALLEY_AIRSTAIR_RETRACT",                 event_base + 1646 },
            { "EVT_GALLEY_AIRSTAIR_EXTEND",                  event_base + 1648 },
            { "EVT_GALLEY_AIRSTAIR_LIGHTS",                  event_base + 1654 },
            { "EVT_GALLEY_AIRSTAIR_STANDBY",                 event_base + 1658 },
            { "EVT_FAP_GROUND_SERVICE",                      event_base + 2050 },
            { "EVT_ALT_GEAR_EXT_DOOR",                       event_base + 2002 },
            { "EVT_ALT_GEAR_EXT_HANDLE_RIGHT",               event_base + 2003 },
            { "EVT_ALT_GEAR_EXT_HANDLE_LEFT",                event_base + 2004 },
            { "EVT_ALT_GEAR_EXT_HANDLE_NOSE",                event_base + 2005 },
            { "EVT_COMBINER_COVER",                          event_base + 2006 },
            { "EVT_HIDE_YOKE_CAPT",                          event_base + 2007 },
            { "EVT_HIDE_YOKE_FO",                            event_base + 2008 },
            { "EVT_SPOTLIGHT_L",                             event_base + 2015 },
            { "EVT_SPOTLIGHT_R",                             event_base + 2016 },
            { "EVT_SPOTLIGHT_OBS",                           event_base + 2017 },

            // ===== Grimes Light =====
            { "EVT_GRIMES_LIGHT_CA",                         event_base + 2020 },

            // ===== Yoke Animations =====
            { "EVT_YOKE_L_COUNTER_1",                        event_base + 998 },
            { "EVT_YOKE_L_COUNTER_2",                        event_base + 999 },
            { "EVT_YOKE_L_COUNTER_3",                        event_base + 1000 },
            { "EVT_YOKE_R_COUNTER_1",                        event_base + 1001 },
            { "EVT_YOKE_R_COUNTER_2",                        event_base + 1002 },
            { "EVT_YOKE_R_COUNTER_3",                        event_base + 1003 },
            { "EVT_YOKE_L_AP_DISC_SWITCH",                   event_base + 1004 },
            { "EVT_YOKE_R_AP_DISC_SWITCH",                   event_base + 1005 },
            { "EVT_YOKE_CHECKLIST_L_SWITCH",                 event_base + 7521 },
            { "EVT_YOKE_CHECKLIST_R_SWITCH",                 event_base + 7522 },

            // ===== Captain / F/O Armrests =====
            { "EVT_CA_ARMREST_LEFT_SWITCH",                  event_base + 1006 },
            { "EVT_CA_ARMREST_RIGHT_SWITCH",                 event_base + 1007 },
            { "EVT_FO_ARMREST_LEFT_SWITCH",                  event_base + 1008 },
            { "EVT_FO_ARMREST_RIGHT_SWITCH",                 event_base + 1009 },

            // ===== Custom Shortcuts =====
            { "EVT_LDG_LIGHTS_TOGGLE",                       event_base + 14000 },
            { "EVT_TURNOFF_LIGHTS_TOGGLE",                   event_base + 14001 },
            { "EVT_COCKPIT_LIGHTS_TOGGLE",                   event_base + 14002 },
            { "EVT_COCKPIT_LIGHTS_ON",                       event_base + 14003 },
            { "EVT_COCKPIT_LIGHTS_OFF",                      event_base + 14004 },
            { "EVT_DOOR_FWD_L",                              event_base + 14005 },
            { "EVT_DOOR_FWD_R",                              event_base + 14006 },
            { "EVT_DOOR_AFT_L",                              event_base + 14007 },
            { "EVT_DOOR_AFT_R",                              event_base + 14008 },
            { "EVT_DOOR_OVERWING_EXIT_L",                    event_base + 14009 },
            { "EVT_DOOR_OVERWING_EXIT_R",                    event_base + 14010 },
            { "EVT_DOOR_CARGO_FWD",                          event_base + 14013 },
            { "EVT_DOOR_CARGO_AFT",                          event_base + 14014 },
            { "EVT_DOOR_CARGO_MAIN",                         event_base + 14015 },
            { "EVT_DOOR_EQUIPMENT_HATCH",                    event_base + 14016 },
            { "EVT_DOOR_AIRSTAIR",                           event_base + 14017 },
            { "EVT_LOGO_LIGHTS_TOGGLE",                      event_base + 14018 },

            // ===== MCP Direct Control =====
            { "EVT_MCP_CRS_L_SET",                           event_base + 14500 },
            { "EVT_MCP_CRS_R_SET",                           event_base + 14501 },
            { "EVT_MCP_IAS_SET",                             event_base + 14502 },
            { "EVT_MCP_MACH_SET",                            event_base + 14503 },
            { "EVT_MCP_HDG_SET",                             event_base + 14504 },
            { "EVT_MCP_ALT_SET",                             event_base + 14505 },
            { "EVT_MCP_VS_SET",                              event_base + 14506 },

            // ===== Pressurization Direct Control =====
            { "EVT_OH_PRESS_FLT_ALT_SET",                    event_base + 14507 },
            { "EVT_OH_PRESS_LAND_ALT_SET",                   event_base + 14508 },

            // ===== Panel System Events =====
            // Note: SDK declares EVT_CTRL_ACCELERATION_DISABLE and _ENABLE at the same offset (14600).
            // Both names map to the same numeric ID — the dictionary holds both keys.
            { "EVT_CTRL_ACCELERATION_DISABLE",               event_base + 14600 },
            { "EVT_CTRL_ACCELERATION_ENABLE",                event_base + 14600 },

            // ===== 2D Panel Offset =====
            // Note: NOT relative to event_base — absolute 20000 per SDK header.
            { "EVT_2D_PANEL_OFFSET",                         20000 },
        };

    // =========================================================================
    // Variable → event name mapping (simple toggle and momentary controls)
    //
    // Key format:
    //   - Scalar field "Foo" → key "Foo"
    //   - Array field "Foo[i]" → key "Foo_{i}"
    //
    // Cross-referenced against EventIds at write-time; every event name here
    // must be a key in EventIds.
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, string> _simpleEventMap =
        new Dictionary<string, string>
        {
            // --- Aft overhead: ADIRU (IRS) ---
            ["IRS_DisplaySelector"]        = "EVT_ISDU_DSPL_SEL",
            ["IRS_SysDisplay_R"]           = "EVT_ISDU_SYS_DSPL",
            ["IRS_ModeSelector_0"]         = "EVT_IRU_MSU_LEFT",
            ["IRS_ModeSelector_1"]         = "EVT_IRU_MSU_RIGHT",

            // --- Aft overhead: Service Interphone / Dome ---
            ["COMM_ServiceInterphoneSw"]   = "EVT_OH_SERVICE_INTERPHONE_SWITCH",
            ["LTS_DomeWhiteSw"]            = "EVT_OH_DOME_SWITCH",

            // --- Aft overhead: Engine EEC (guarded — see _guardedMap) ---
            // (no simple entries; ENG_EECSwitch[0/1] is guarded)

            // --- Aft overhead: Oxygen (guarded — see _guardedMap) ---
            // OXY_SwNormal is guarded

            // --- Aft overhead: Flight Recorder (guarded — see _guardedMap) ---
            // FLTREC_SwNormal is guarded

            // --- Forward overhead: Flight Controls ---
            ["FCTL_YawDamper_Sw"]          = "EVT_OH_YAW_DAMPER",
            ["FCTL_AltnFlaps_Control_Sw"]  = "EVT_OH_ALT_FLAPS_POS_SWITCH",
            // FCTL_FltControl_Sw_0/1, FCTL_Spoiler_Sw_0/1, FCTL_AltnFlaps_Sw_ARM
            // are all guarded — see _guardedMap

            // --- Forward overhead: Navigation/Displays selectors ---
            ["NAVDIS_VHFNavSelector"]      = "EVT_OH_NAVDSP_VHF_NAV_SEL",
            ["NAVDIS_IRSSelector"]         = "EVT_OH_NAVDSP_IRS_SEL",
            ["NAVDIS_FMCSelector"]         = "EVT_OH_NAVDSP_FMC_SEL",
            ["NAVDIS_SourceSelector"]      = "EVT_OH_NAVDSP_DISPLAYS_SOURCE_SEL",
            ["NAVDIS_ControlPaneSelector"] = "EVT_OH_NAVDSP_CONTROL_PANEL_SEL",

            // --- Forward overhead: Fuel pumps & crossfeed ---
            ["FUEL_PumpFwdSw_0"]           = "EVT_OH_FUEL_PUMP_1_FORWARD",
            ["FUEL_PumpFwdSw_1"]           = "EVT_OH_FUEL_PUMP_2_FORWARD",
            ["FUEL_PumpAftSw_0"]           = "EVT_OH_FUEL_PUMP_1_AFT",
            ["FUEL_PumpAftSw_1"]           = "EVT_OH_FUEL_PUMP_2_AFT",
            ["FUEL_PumpCtrSw_0"]           = "EVT_OH_FUEL_PUMP_L_CENTER",
            ["FUEL_PumpCtrSw_1"]           = "EVT_OH_FUEL_PUMP_R_CENTER",
            ["FUEL_CrossFeedSw"]           = "EVT_OH_FUEL_CROSSFEED",
            ["FUEL_AuxFwd_0"]              = "EVT_OH_FUEL_AUX_FWD_A",
            ["FUEL_AuxFwd_1"]              = "EVT_OH_FUEL_AUX_FWD_B",
            ["FUEL_AuxAft_0"]              = "EVT_OH_FUEL_AUX_AFT_A",
            ["FUEL_AuxAft_1"]              = "EVT_OH_FUEL_AUX_AFT_B",
            ["FUEL_FWDBleed"]              = "EVT_OH_FUEL_FWD_BLD",
            ["FUEL_AFTBleed"]              = "EVT_OH_FUEL_AFT_BLD",
            // FUEL_GNDXfr is guarded — see _guardedMap

            // --- Forward overhead: Electrical (non-guarded) ---
            ["ELEC_DCMeterSelector"]       = "EVT_OH_ELEC_DC_METER",
            ["ELEC_ACMeterSelector"]       = "EVT_OH_ELEC_AC_METER",
            ["ELEC_CabUtilSw"]             = "EVT_OH_ELEC_CAB_UTIL",
            ["ELEC_IFEPassSeatSw"]         = "EVT_OH_ELEC_IFE",
            ["ELEC_GrdPwrSw"]              = "EVT_OH_ELEC_GRD_PWR_SWITCH",
            ["ELEC_GenSw_0"]               = "EVT_OH_ELEC_GEN1_SWITCH",
            ["ELEC_GenSw_1"]               = "EVT_OH_ELEC_GEN2_SWITCH",
            ["ELEC_APUGenSw_0"]            = "EVT_OH_ELEC_APU_GEN1_SWITCH",
            ["ELEC_APUGenSw_1"]            = "EVT_OH_ELEC_APU_GEN2_SWITCH",
            // ELEC_BatSelector, ELEC_StandbyPowerSelector, ELEC_BusTransSw_AUTO,
            // ELEC_IDGDisconnectSw_0/1 are guarded — see _guardedMap.

            // --- Forward overhead: Wipers ---
            ["OH_WiperLSelector"]          = "EVT_OH_WIPER_LEFT_CONTROL",
            ["OH_WiperRSelector"]          = "EVT_OH_WIPER_RIGHT_CONTROL",

            // --- Center overhead: Equipment cooling / pax signs ---
            ["AIR_EquipCoolingSupplyNORM"] = "EVT_OH_EC_SUPPLY_SWITCH",
            ["AIR_EquipCoolingExhaustNORM"]= "EVT_OH_EC_EXHAUST_SWITCH",
            ["COMM_NoSmokingSelector"]     = "EVT_OH_NO_SMOKING_LIGHT_SWITCH",
            ["COMM_FastenBeltsSelector"]   = "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH",
            // LTS_EmerExitSelector is guarded — see _guardedMap

            // --- Anti-ice ---
            ["ICE_WindowHeatSw_0"]         = "EVT_OH_ICE_WINDOW_HEAT_1",
            ["ICE_WindowHeatSw_1"]         = "EVT_OH_ICE_WINDOW_HEAT_2",
            ["ICE_WindowHeatSw_2"]         = "EVT_OH_ICE_WINDOW_HEAT_3",
            ["ICE_WindowHeatSw_3"]         = "EVT_OH_ICE_WINDOW_HEAT_4",
            ["ICE_WindowHeatTestSw"]       = "EVT_OH_ICE_WINDOW_HEAT_TEST",
            ["ICE_ProbeHeatSw_0"]          = "EVT_OH_ICE_PROBE_HEAT_1",
            ["ICE_ProbeHeatSw_1"]          = "EVT_OH_ICE_PROBE_HEAT_2",
            ["ICE_WingAntiIceSw"]          = "EVT_OH_ICE_WING_ANTIICE",
            ["ICE_EngAntiIceSw_0"]         = "EVT_OH_ICE_ENGINE_ANTIICE_1",
            ["ICE_EngAntiIceSw_1"]         = "EVT_OH_ICE_ENGINE_ANTIICE_2",

            // --- Hydraulics ---
            ["HYD_PumpSw_eng_0"]           = "EVT_OH_HYD_ENG1",
            ["HYD_PumpSw_eng_1"]           = "EVT_OH_HYD_ENG2",
            // HYD_PumpSw_elec_N maps to the SDK array slot N. The PMDG NG3 SDK orders the
            // electric hydraulic pump array as [0]=pump 2, [1]=pump 1 — corroborated by the
            // event_id sequence (EVT_OH_HYD_ELEC2=event_base+167 has the LOWER ID, meaning
            // it corresponds to array slot 0). Display labels on HYD_PumpSw_elec_0/_1 reflect
            // this swap ("Elec 2 Hyd Pump" lives at _0). Event dispatch must follow the same
            // mapping so the user-toggled label, cached state, and physical pump all agree.
            ["HYD_PumpSw_elec_0"]          = "EVT_OH_HYD_ELEC2",
            ["HYD_PumpSw_elec_1"]          = "EVT_OH_HYD_ELEC1",

            // --- Air conditioning / bleed / pack ---
            ["AIR_TempSourceSelector"]     = "EVT_OH_AIRCOND_TEMP_SOURCE_SELECTOR_800",
            ["AIR_TrimAirSwitch"]          = "EVT_OH_AIRCOND_TRIM_AIR_SWITCH_800",
            ["AIR_RecircFanSwitch_0"]      = "EVT_OH_BLEED_RECIRC_FAN_L_SWITCH",
            ["AIR_RecircFanSwitch_1"]      = "EVT_OH_BLEED_RECIRC_FAN_R_SWITCH",
            ["AIR_PackSwitch_0"]           = "EVT_OH_BLEED_PACK_L_SWITCH",
            ["AIR_PackSwitch_1"]           = "EVT_OH_BLEED_PACK_R_SWITCH",
            ["AIR_BleedAirSwitch_0"]       = "EVT_OH_BLEED_ENG_1_SWITCH",
            ["AIR_BleedAirSwitch_1"]       = "EVT_OH_BLEED_ENG_2_SWITCH",
            ["AIR_APUBleedAirSwitch"]      = "EVT_OH_BLEED_APU_SWITCH",
            ["AIR_IsolationValveSwitch"]   = "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH",
            ["AIR_OutflowValveSwitch"]     = "EVT_OH_PRESS_VALVE_SWITCH",
            ["AIR_PressurizationModeSelector"] = "EVT_OH_PRESS_SELECTOR",

            // --- Bottom overhead: Lights ---
            ["LTS_LandingLtRetractableSw_0"] = "EVT_OH_LIGHTS_L_RETRACT",
            ["LTS_LandingLtRetractableSw_1"] = "EVT_OH_LIGHTS_R_RETRACT",
            ["LTS_LandingLtFixedSw_0"]     = "EVT_OH_LIGHTS_L_FIXED",
            ["LTS_LandingLtFixedSw_1"]     = "EVT_OH_LIGHTS_R_FIXED",
            ["LTS_RunwayTurnoffSw_0"]      = "EVT_OH_LIGHTS_L_TURNOFF",
            ["LTS_RunwayTurnoffSw_1"]      = "EVT_OH_LIGHTS_R_TURNOFF",
            ["LTS_TaxiSw"]                 = "EVT_OH_LIGHTS_TAXI",
            ["LTS_LogoSw"]                 = "EVT_OH_LIGHTS_LOGO",
            ["LTS_PositionSw"]             = "EVT_OH_LIGHTS_POS_STROBE",
            ["LTS_AntiCollisionSw"]        = "EVT_OH_LIGHTS_ANT_COL",
            ["LTS_WingSw"]                 = "EVT_OH_LIGHTS_WING",
            ["LTS_WheelWellSw"]            = "EVT_OH_LIGHTS_WHEEL_WELL",

            // --- Bottom overhead: Engine start / APU / ignition ---
            ["APU_Selector"]               = "EVT_OH_LIGHTS_APU_START",
            ["ENG_StartSelector_0"]        = "EVT_OH_LIGHTS_L_ENGINE_START",
            ["ENG_StartSelector_1"]        = "EVT_OH_LIGHTS_R_ENGINE_START",
            ["ENG_IgnitionSelector"]       = "EVT_OH_LIGHTS_IGN_SEL",

            // --- Glareshield: EFIS Captain ---
            ["EFIS_MinsSelBARO_0"]         = "EVT_EFIS_CPT_MINIMUMS_RADIO_BARO",
            ["EFIS_BaroSelHPA_0"]          = "EVT_EFIS_CPT_BARO_IN_HPA",
            ["EFIS_VORADFSel1_0"]          = "EVT_EFIS_CPT_VOR_ADF_SELECTOR_L",
            ["EFIS_VORADFSel2_0"]          = "EVT_EFIS_CPT_VOR_ADF_SELECTOR_R",
            ["EFIS_ModeSel_0"]             = "EVT_EFIS_CPT_MODE",
            ["EFIS_RangeSel_0"]            = "EVT_EFIS_CPT_RANGE",

            // --- Glareshield: EFIS First Officer ---
            ["EFIS_MinsSelBARO_1"]         = "EVT_EFIS_FO_MINIMUMS_RADIO_BARO",
            ["EFIS_BaroSelHPA_1"]          = "EVT_EFIS_FO_BARO_IN_HPA",
            ["EFIS_VORADFSel1_1"]          = "EVT_EFIS_FO_VOR_ADF_SELECTOR_L",
            ["EFIS_VORADFSel2_1"]          = "EVT_EFIS_FO_VOR_ADF_SELECTOR_R",
            ["EFIS_ModeSel_1"]             = "EVT_EFIS_FO_MODE",
            ["EFIS_RangeSel_1"]            = "EVT_EFIS_FO_RANGE",

            // --- Glareshield: MCP (non-knob switches) ---
            ["MCP_FDSw_0"]                 = "EVT_MCP_FD_SWITCH_L",
            ["MCP_FDSw_1"]                 = "EVT_MCP_FD_SWITCH_R",
            ["MCP_ATArmSw"]                = "EVT_MCP_AT_ARM_SWITCH",
            ["MCP_BankLimitSel"]           = "EVT_MCP_BANK_ANGLE_SELECTOR",
            ["MCP_DisengageBar"]           = "EVT_MCP_DISENGAGE_BAR",

            // --- Forward panel: NWS / displays / disengage test / lights ---
            // MAIN_NoseWheelSteeringSwNORM is guarded — see _guardedMap
            ["MAIN_MainPanelDUSel_0"]      = "EVT_DSP_CPT_MAIN_DU_SELECTOR",
            ["MAIN_MainPanelDUSel_1"]      = "EVT_DSP_FO_MAIN_DU_SELECTOR",
            ["MAIN_LowerDUSel_0"]          = "EVT_DSP_CPT_LOWER_DU_SELECTOR",
            ["MAIN_LowerDUSel_1"]          = "EVT_DSP_FO_LOWER_DU_SELECTOR",
            ["MAIN_DisengageTestSelector_0"] = "EVT_DSP_CPT_DISENGAGE_TEST_SWITCH",
            ["MAIN_DisengageTestSelector_1"] = "EVT_DSP_FO_DISENGAGE_TEST_SWITCH",
            ["MAIN_LightsSelector"]        = "EVT_DSP_CPT_MASTER_LIGHTS_SWITCH",
            // MAIN_annunBELOW_GS_0/1 are SDK bool annunciators (read-only). The
            // lamps double as momentary inhibit pushbuttons on the panel
            // (EVT_DSP_*_BELOW_GS_INHIBIT_SWITCH), but the vars are declared as
            // Annun with no Momentary press companion and don't appear in
            // BuildPanelControls — there is no UI surface to invoke the press.
            // Add MAIN_BelowGSInhibit_0/1 Momentary entries here + map below if
            // the inhibit button is ever needed via panel UI.

            // --- Forward panel: RMI / autobrake / spd ref / N1 set / fuel flow ---
            ["MAIN_RMISelector1_VOR"]      = "EVT_RMI_LEFT_SELECTOR",
            ["MAIN_RMISelector2_VOR"]      = "EVT_RMI_RIGHT_SELECTOR",
            ["MAIN_AutobrakeSelector"]     = "EVT_MPM_AUTOBRAKE_SELECTOR",
            ["MAIN_SpdRefSelector"]        = "EVT_MPM_SPEED_REFERENCE_SELECTOR",
            ["MAIN_N1SetSelector"]         = "EVT_MPM_N1SET_SELECTOR",
            ["MAIN_FuelFlowSelector"]      = "EVT_MPM_FUEL_FLOW_SWITCH",
            ["MAIN_GearLever"]             = "EVT_GEAR_LEVER",

            // --- Lower forward panel: GPWS (all guarded — see _guardedMap) ---
            // GPWS_FlapInhibitSw_NORM, GPWS_GearInhibitSw_NORM, GPWS_TerrInhibitSw_NORM
            // are all guarded.

            // --- Control Stand: stab trim / parking / fire / xpdr ---
            ["TRIM_StabTrimMainElecSw_NORMAL"] = "EVT_CONTROL_STAND_STABTRIM_ELEC_SWITCH",
            ["TRIM_StabTrimAutoPilotSw_NORMAL"] = "EVT_CONTROL_STAND_STABTRIM_AP_SWITCH",
            ["TRIM_StabTrimSw_NORMAL"]     = "EVT_STAB_TRIM_OVRD_SWITCH",
            ["FIRE_OvhtDetSw_0"]           = "EVT_FIRE_OVHT_DET_SWITCH_1",
            ["FIRE_OvhtDetSw_1"]           = "EVT_FIRE_OVHT_DET_SWITCH_2",
            ["FIRE_DetTestSw"]             = "EVT_FIRE_DETECTION_TEST_SWITCH",
            ["FIRE_ExtinguisherTestSw"]    = "EVT_FIRE_EXTINGUISHER_TEST_SWITCH",
            // Fire handle TOP momentary presses are guarded — see _guardedMap
            // (the UNLOCK switch must fire before TOP, or the handle stays
            // locked in the In position). BOTTOM presses don't need unlock
            // because they discharge bottles after the handle is out.
            ["FIRE_EngineHandle_1_PressBottom"] = "EVT_FIRE_HANDLE_ENGINE_1_BOTTOM",
            ["FIRE_EngineHandle_2_PressBottom"] = "EVT_FIRE_HANDLE_ENGINE_2_BOTTOM",
            ["FIRE_APUHandle_PressBottom"]      = "EVT_FIRE_HANDLE_APU_BOTTOM",
            ["CARGO_DetSelect_0"]          = "EVT_CARGO_FIRE_DET_SEL_SWITCH_FWD",
            ["CARGO_DetSelect_1"]          = "EVT_CARGO_FIRE_DET_SEL_SWITCH_AFT",
            // CARGO_ArmedSw_0/1 are guarded — see _guardedMap
            ["XPDR_XpndrSelector_2"]       = "EVT_TCAS_XPNDR",
            ["XPDR_AltSourceSel_2"]        = "EVT_TCAS_ALTSOURCE",
            ["XPDR_ModeSel"]               = "EVT_TCAS_MODE",
            ["XPDR_TcasTest"]              = "EVT_TCAS_TEST",
            ["XPDR_Ident"]                 = "EVT_TCAS_IDENT",
            // Overhead — Oxygen TEST (spring-loaded; release returns to NORMAL)
            ["OXY_TestL"]                  = "EVT_OH_OXY_TEST_RESET_SWITCH_L",
            ["OXY_TestR"]                  = "EVT_OH_OXY_TEST_RESET_SWITCH_R",
            // Lower forward panel — GPWS system test
            ["GPWS_SysTest"]               = "EVT_GPWS_SYS_TEST_BTN",
            // Overhead — Air Systems bleed-overheat self-test
            ["AIR_BleedOvhtTest"]          = "EVT_OH_BLEED_OVHT_TEST_BUTTON",
            // Glareshield Warnings — cabin altitude warning horn cutout
            ["WARN_CabAltHornCutout"]      = "EVT_OH_CAB_ALT_HORN_CUTOUT_BUTTON",
            // MCP autopilot push buttons (momentary)
            ["MCP_CmdA"]    = "EVT_MCP_CMD_A_SWITCH",
            ["MCP_CmdB"]    = "EVT_MCP_CMD_B_SWITCH",
            ["MCP_CwsA"]    = "EVT_MCP_CWS_A_SWITCH",
            ["MCP_CwsB"]    = "EVT_MCP_CWS_B_SWITCH",
            ["MCP_AppBtn"]  = "EVT_MCP_APP_SWITCH",
            ["MCP_VorLoc"]  = "EVT_MCP_VOR_LOC_SWITCH",
            // Control stand / pedestal
            ["CS_TOGA_1"]   = "EVT_CONTROL_STAND_TOGA1_SWITCH",
            ["CS_TOGA_2"]   = "EVT_CONTROL_STAND_TOGA2_SWITCH",
            ["CS_ATDisc_1"] = "EVT_CONTROL_STAND_AT1_DISENGAGE_SWITCH",
            ["CS_ATDisc_2"] = "EVT_CONTROL_STAND_AT2_DISENGAGE_SWITCH",
            // Glareshield — Master Warning / Caution recall
            ["WARN_ResetFireWarning"]      = "EVT_FIRE_WARN_LIGHT_LEFT",
            ["WARN_ResetMasterCaution"]    = "EVT_MASTER_CAUTION_LIGHT_LEFT",
        };

    // =========================================================================
    // Cabin & furniture dispatch tables (see the 0-cabin region in
    // HandleUIVariableSet). All shapes live-verified 2026-06-12.
    // =========================================================================
    // Call buttons: momentary press, CDA parameter 1 (parameter 0 also presses).
    private static readonly Dictionary<string, string> _cabinMomentaryEventMap = new()
    {
        ["COMM_AttendCallBtn"] = "EVT_OH_ATTND_CALL_SWITCH",
        ["COMM_GrndCallBtn"]   = "EVT_OH_GRND_CALL_SWITCH",
    };

    // Jumpseat/armrest combos: varKey = the PMDG-owned read-back L-var (combo
    // display, 0/100), value = the SDK event whose CDA parameter IS the position.
    private static readonly Dictionary<string, string> _sdkFurnitureEventMap = new()
    {
        ["switch_2001_73X"] = "EVT_JUMPSEAT_STOW_EXTEND",
        ["switch_1006_73X"] = "EVT_CA_ARMREST_LEFT_SWITCH",
        ["switch_1007_73X"] = "EVT_CA_ARMREST_RIGHT_SWITCH",
        ["switch_1008_73X"] = "EVT_FO_ARMREST_LEFT_SWITCH",
        ["switch_1009_73X"] = "EVT_FO_ARMREST_RIGHT_SWITCH",
        ["switch_2050_73X"] = "EVT_FAP_GROUND_SERVICE",   // FAP ground service, 2-position
    };

    // Airstair pushbuttons: press-and-release (param 1, brief hold, param 0) —
    // these are momentary panel buttons whose switch L-var latches at 100 if
    // never released (observed in probing).
    private static readonly Dictionary<string, string> _pressReleaseEventMap = new()
    {
        ["AIRSTAIR_Extend"]  = "EVT_GALLEY_AIRSTAIR_EXTEND",
        ["AIRSTAIR_Retract"] = "EVT_GALLEY_AIRSTAIR_RETRACT",
        ["AIRSTAIR_Standby"] = "EVT_GALLEY_AIRSTAIR_STANDBY",
    };

    // Galley water pushbutton radio-pairs: combo key = the "active" button of
    // the pair; PMDG's own click code sets one and clears the other.
    //   varKey → (the paired opposite L-var)
    private static readonly Dictionary<string, string> _waterPairMap = new()
    {
        ["water_on_btn"]   = "water_off_btn",
        ["water_warm_btn"] = "water_cold_btn",
    };

    private static async Task PressAndReleaseAsync(
        SimConnect.SimConnectManager simConnect, string eventName, uint eventId)
    {
        simConnect.SendPMDGEvent(eventName, eventId, 1);
        await Task.Delay(350).ConfigureAwait(false);
        simConnect.SendPMDGEvent(eventName, eventId, 0);
    }

    // Overhead bins: rows 1–18 left/right + the two small TC-layout row-3 bins.
    private static readonly string[] s_binLvars = BuildBinLvarList();
    private static string[] BuildBinLvarList()
    {
        var list = new List<string>();
        foreach (int row in Enumerable.Range(1, 18))
        {
            list.Add($"bin_{row}L_anim");
            list.Add($"bin_{row}R_anim");
        }
        list.Add("bin_3LTC_anim");
        list.Add("bin_3RTC_anim");
        return list.ToArray();
    }

    // Passenger window blinds: rows 1–41 left/right, the right-overwing-special
    // 14ROS blind, and the four emergency-exit-row blinds. Extracted from
    // 73X_Cabin_Walls_Behavior.xml; 1 = blind down. Different cabin layouts use
    // different subsets — setting a blind the layout doesn't have is a no-op.
    private static readonly string[] s_blindLvars = BuildBlindLvarList();
    private static string[] BuildBlindLvarList()
    {
        var list = new List<string>();
        foreach (int row in Enumerable.Range(1, 41))
        {
            list.Add($"window{row}L_blind_anim");
            list.Add($"window{row}R_blind_anim");
        }
        list.Add("window14ROS_blind_anim");
        list.Add("windowEEL1_blind_anim");
        list.Add("windowEEL2_blind_anim");
        list.Add("windowEER1_blind_anim");
        list.Add("windowEER2_blind_anim");
        return list.ToArray();
    }

    // =========================================================================
    // Guarded switch table: varKey → (guardEvent, switchEvent)
    //
    // The UI layer fires the guard event first (open the guard), then the
    // switch event (move the switch), then the guard event again (close the
    // guard) when toggling a guarded switch. This dict supplies the two event
    // names for each guarded variable.
    //
    // Fire handles (FIRE_HandlePos[3]) are NOT here — the read-only state
    // vars announce position changes, and the actual pulls are driven by
    // synthetic momentary press buttons (FIRE_EngineHandle_{1,2}_Press[Bottom],
    // FIRE_APUHandle_Press[Bottom]) that route through _simpleEventMap.
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, (string Guard, string Switch)> _guardedMap =
        new Dictionary<string, (string, string)>
        {
            // --- Electrical ---
            ["ELEC_BatSelector"]           = ("EVT_OH_ELEC_BATTERY_GUARD",      "EVT_OH_ELEC_BATTERY_SWITCH"),
            ["ELEC_IDGDisconnectSw_0"]     = ("EVT_OH_ELEC_DISCONNECT_1_GUARD", "EVT_OH_ELEC_DISCONNECT_1_SWITCH"),
            ["ELEC_IDGDisconnectSw_1"]     = ("EVT_OH_ELEC_DISCONNECT_2_GUARD", "EVT_OH_ELEC_DISCONNECT_2_SWITCH"),
            ["ELEC_StandbyPowerSelector"]  = ("EVT_OH_ELEC_STBY_PWR_GUARD",     "EVT_OH_ELEC_STBY_PWR_SWITCH"),
            ["ELEC_BusTransSw_AUTO"]       = ("EVT_OH_ELEC_BUS_TRANSFER_GUARD", "EVT_OH_ELEC_BUS_TRANSFER_SWITCH"),

            // --- Fuel ---
            ["FUEL_GNDXfr"]                = ("EVT_OH_FUEL_GND_XFR_GUARD",      "EVT_OH_FUEL_GND_XFR_SW"),

            // --- Flight Controls ---
            ["FCTL_FltControl_Sw_0"]       = ("EVT_OH_FCTL_A_GUARD",            "EVT_OH_FCTL_A_SWITCH"),
            ["FCTL_FltControl_Sw_1"]       = ("EVT_OH_FCTL_B_GUARD",            "EVT_OH_FCTL_B_SWITCH"),
            ["FCTL_Spoiler_Sw_0"]          = ("EVT_OH_SPOILER_A_GUARD",         "EVT_OH_SPOILER_A_SWITCH"),
            ["FCTL_Spoiler_Sw_1"]          = ("EVT_OH_SPOILER_B_GUARD",         "EVT_OH_SPOILER_B_SWITCH"),
            ["FCTL_AltnFlaps_Sw_ARM"]      = ("EVT_OH_ALT_FLAPS_MASTER_GUARD",  "EVT_OH_ALT_FLAPS_MASTER_SWITCH"),

            // --- Engine EEC ---
            ["ENG_EECSwitch_0"]            = ("EVT_OH_EEC_L_GUARD",             "EVT_OH_EEC_L_SWITCH"),
            ["ENG_EECSwitch_1"]            = ("EVT_OH_EEC_R_GUARD",             "EVT_OH_EEC_R_SWITCH"),

            // --- Oxygen / Flight Recorder / Emergency exit lights ---
            ["OXY_SwNormal"]               = ("EVT_OH_OXY_PASS_GUARD",          "EVT_OH_OXY_PASS_SWITCH"),
            ["FLTREC_SwNormal"]            = ("EVT_OH_FLTREC_GUARD",            "EVT_OH_FLTREC_SWITCH"),
            ["LTS_EmerExitSelector"]       = ("EVT_OH_EMER_EXIT_LIGHT_GUARD",   "EVT_OH_EMER_EXIT_LIGHT_SWITCH"),

            // --- GPWS inhibits ---
            ["GPWS_FlapInhibitSw_NORM"]    = ("EVT_GPWS_FLAP_INHIBIT_GUARD",    "EVT_GPWS_FLAP_INHIBIT_SWITCH"),
            ["GPWS_GearInhibitSw_NORM"]    = ("EVT_GPWS_GEAR_INHIBIT_GUARD",    "EVT_GPWS_GEAR_INHIBIT_SWITCH"),
            ["GPWS_TerrInhibitSw_NORM"]    = ("EVT_GPWS_TERR_INHIBIT_GUARD",    "EVT_GPWS_TERR_INHIBIT_SWITCH"),

            // --- Nose wheel steering ---
            ["MAIN_NoseWheelSteeringSwNORM"] = ("EVT_NOSE_WHEEL_STEERING_SWITCH_GUARD", "EVT_NOSE_WHEEL_STEERING_SWITCH"),

            // --- Cargo fire arm switches ---
            // EVT_CARGO_FIRE_DISC_SWITCH_GUARD covers the discharge switch — the
            // arm switches themselves don't have explicit guard events in the SDK,
            // so they're treated as simple switches (see _simpleEventMap above
            // for the DET sel; the arm switches go here only if guarded). The
            // 737 cargo fire arm switches sit under the cargo fire disch guard
            // — model them as guarded by the disch guard for parity with the
            // pilot mental model (the guard physically covers the entire
            // arm/disch group). If this turns out to be wrong in-sim, the entry
            // can move down to _simpleEventMap.
            ["CARGO_ArmedSw_0"]            = ("EVT_CARGO_FIRE_DISC_SWITCH_GUARD", "EVT_CARGO_FIRE_ARM_SWITCH_FWD"),
            ["CARGO_ArmedSw_1"]            = ("EVT_CARGO_FIRE_DISC_SWITCH_GUARD", "EVT_CARGO_FIRE_ARM_SWITCH_AFT"),

            // --- Fire handle TOP press (UNLOCK + TOP) ---
            // The fire handle is mechanically locked In. The guard slot holds the
            // spring-loaded UNLOCK switch — unlike an ordinary cover (which PMDG
            // NG3 auto-lifts on a click) this is a real momentary control that the
            // dispatch must press immediately before the TOP pull, or the handle
            // stays locked. See _fireHandlePressKeys handling in HandleUIVariableSet.
            // Verified in-sim (PMDG 737, 2026-05-25): UNLOCK then TOP pulls the
            // handle even with no active fire warning; a bare TOP press is a no-op.
            // During a real fire the handle also auto-unlocks, so the explicit
            // UNLOCK is harmless in that case.
            ["FIRE_EngineHandle_1_Press"]  = ("EVT_FIRE_UNLOCK_SWITCH_ENGINE_1", "EVT_FIRE_HANDLE_ENGINE_1_TOP"),
            ["FIRE_EngineHandle_2_Press"]  = ("EVT_FIRE_UNLOCK_SWITCH_ENGINE_2", "EVT_FIRE_HANDLE_ENGINE_2_TOP"),
            ["FIRE_APUHandle_Press"]       = ("EVT_FIRE_UNLOCK_SWITCH_APU",      "EVT_FIRE_HANDLE_APU_TOP"),
        };

    // The three fire-handle "Press" actions whose guard-slot event is the
    // spring-loaded UNLOCK switch rather than an auto-lifted cover. These are
    // dispatched as UNLOCK→TOP momentary presses (see HandleUIVariableSet); the
    // rest of _guardedMap relies on PMDG NG3 lifting the cover internally.
    private static readonly HashSet<string> _fireHandlePressKeys = new()
    {
        "FIRE_EngineHandle_1_Press",
        "FIRE_EngineHandle_2_Press",
        "FIRE_APUHandle_Press",
    };

    /// <summary>
    /// Pulls a 737 fire handle: presses the spring-loaded UNLOCK switch, then the
    /// handle TOP. Both are single LEFTSINGLE TransmitClientEvents (the
    /// WalkPMDGSelector 0→1 shape). The 150 ms gap lets PMDG process the unlock
    /// before the pull — the unlock is momentary and resets once the handle moves,
    /// so the ordering, not the exact delay, is what matters. Verified in-sim
    /// (PMDG 737, 2026-05-25): without the UNLOCK the TOP press is a no-op.
    /// </summary>
    private static async Task PullFireHandleAsync(SimConnect.SimConnectManager simConnect, uint unlockId, uint topId)
    {
        await simConnect.WalkPMDGSelector(unlockId, 0, 1);
        await Task.Delay(150);
        await simConnect.WalkPMDGSelector(topId, 0, 1);
    }

    // =========================================================================
    // Momentary press-to-toggle bool switches — 2-position bool fields whose
    // PMDG NG3 event ID is modeled as a momentary spring-loaded toggle (not a
    // stable detent). CDA direct-position writes have no effect; bare
    // LEFTSINGLE/RIGHTSINGLE click TransmitClientEvents play the switch sound
    // but the bool springs back within a frame. The fix is the FD-switch
    // dispatch pattern from PMDG_NG3_ConnectionTest.cpp:
    //   LEFTSINGLE   then LEFTRELEASE  for target=1 (ON / up)
    //   RIGHTSINGLE  then RIGHTRELEASE for target=0 (OFF / down)
    //
    //   - ELEC_GrdPwrSw  (real Boeing GRD POWER switch is momentary spring-loaded)
    //   - ELEC_GenSw_0   (GEN 1 — user reported "spring back" on the click-walk path)
    //   - ELEC_GenSw_1   (GEN 2 — same)
    //   - ELEC_APUGenSw_0 (APU GEN 1 — user reported "locked in the on position")
    //   - ELEC_APUGenSw_1 (APU GEN 2 — same)
    //
    // The diagnostic log captured at 2026-05-22 17:25-17:26 confirmed CDA-direct
    // writes were reaching PMDG (multiple EVT_OH_ELEC_GRD_PWR_SWITCH param=0
    // entries) but the switch played its sound and snapped back to ON — the
    // signature of an unmatched mouse press without RELEASE.
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, string> _momentaryToggleMap =
        new Dictionary<string, string>
        {
            // Ground Power and APU GEN 1/2 are NOT here — they're two-button
            // push pairs dispatched via _directionalPushMap below (PMDG NG3
            // exposes no reliable state signal that distinguishes their
            // source from other AC sources on shared buses).
            ["ELEC_GenSw_0"]    = "EVT_OH_ELEC_GEN1_SWITCH",
            ["ELEC_GenSw_1"]    = "EVT_OH_ELEC_GEN2_SWITCH",
        };

    // =========================================================================
    // Directional push buttons — momentary buttons that dispatch a specific
    // mouse-flag direction (LEFTSINGLE = press up = "On", RIGHTSINGLE = press
    // down = "Off"). Used for APU GEN 1/2 where PMDG NG3 exposes no reliable
    // per-switch state signal (shared annunciator, spring-loaded bool that
    // always reads 1 at rest). Rather than display a possibly-wrong state,
    // each switch shows two stateless action buttons: "APU Gen N On" /
    // "APU Gen N Off". Each press dispatches the corresponding direction once.
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, (string EventName, bool IsOn)> _directionalPushMap =
        new Dictionary<string, (string, bool)>
        {
            ["ELEC_GrdPwrSw_On"]    = ("EVT_OH_ELEC_GRD_PWR_SWITCH", true),
            ["ELEC_GrdPwrSw_Off"]   = ("EVT_OH_ELEC_GRD_PWR_SWITCH", false),
            ["ELEC_APUGenSw_0_On"]  = ("EVT_OH_ELEC_APU_GEN1_SWITCH", true),
            ["ELEC_APUGenSw_0_Off"] = ("EVT_OH_ELEC_APU_GEN1_SWITCH", false),
            ["ELEC_APUGenSw_1_On"]  = ("EVT_OH_ELEC_APU_GEN2_SWITCH", true),
            ["ELEC_APUGenSw_1_Off"] = ("EVT_OH_ELEC_APU_GEN2_SWITCH", false),
        };

    // (Previously held entries for the fuel-lever push-button pairs. Now empty
    //  because fuel levers are combos with state tracking — see the
    //  ENG_StartLever_X branch in HandleUIVariableSet for dispatch. Kept as
    //  scaffolding for future events that may need TransmitClientEvent + mouse
    //  flag dispatch via a similar map structure.)

    // =========================================================================
    // Spring-loaded selectors — 3-position byte selectors where the outer
    // detents (or one of them) spring back to a rest position after the
    // operator releases them. Dispatched via CDA direct-position (one
    // SetClientData write with the target detent index). Click-walking these
    // via TransmitClientEvent is unsafe: PMDG NG3 starts springing the byte
    // back to rest the moment the first click lands, racing our second click
    // and leaving the cached byte in a transient invalid state.
    //   - APU_Selector            (0=OFF, 1=ON, 2=START — START springs to ON)
    //   - FIRE_DetTestSw          (0=FAULT/INOP, 1=neutral, 2=OVHT/FIRE — both
    //                              outer test detents spring back to neutral)
    //   - FIRE_ExtinguisherTestSw (0=1, 1=neutral, 2=2 — same spring pattern)
    // Pattern source: PMDG_NG3_ConnectionTest.cpp `toggleTaxiLightSwitch` —
    // CDA write with `Control.Parameter = target` is the documented
    // SDK convention for byte/bool position fields.
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, string> _springLoadedSelectorMap =
        new Dictionary<string, string>
        {
            ["APU_Selector"]            = "EVT_OH_LIGHTS_APU_START",
            ["FIRE_DetTestSw"]          = "EVT_FIRE_DETECTION_TEST_SWITCH",
            ["FIRE_ExtinguisherTestSw"] = "EVT_FIRE_EXTINGUISHER_TEST_SWITCH",
        };

    // Selectors that PMDG NG3 dispatches via TransmitClientEvent with an
    // ABSOLUTE position parameter (NOT click-walking with LEFTSINGLE/RIGHTSINGLE).
    // The generic WalkPMDGSelector path inverted the click direction for these
    // selectors, producing the well-known "step Normal to A lands at B" pattern.
    // Same fix shape as ENG_StartSelector/ENG_IgnitionSelector at line ~3478.
    // Verified in user-reported reproductions for FIRE_OvhtDetSw, CARGO_DetSelect,
    // MAIN_*DUSel, and NAVDIS_*Selector; absolute-position dispatch confirmed
    // working at the SDK protocol level for these families.
    private static readonly HashSet<string> _absolutePositionSelectorSet =
        new HashSet<string>
        {
            "FIRE_OvhtDetSw_0", "FIRE_OvhtDetSw_1",
            "CARGO_DetSelect_0", "CARGO_DetSelect_1",
            "MAIN_MainPanelDUSel_0", "MAIN_MainPanelDUSel_1",
            "MAIN_LowerDUSel_0", "MAIN_LowerDUSel_1",
            "NAVDIS_VHFNavSelector", "NAVDIS_IRSSelector", "NAVDIS_FMCSelector",
            "NAVDIS_SourceSelector", "NAVDIS_ControlPaneSelector",
        };

    // =========================================================================
    // Behavior overrides — scaffold (populated in Tasks C10–C12)
    // =========================================================================

    // Standby Power Selector dispatch — TFM convention. PMDG NG3 does not
    // auto-handle the standby power guard, so we must click it open before
    // clicking the switch. Click directions on this control are INVERTED
    // relative to the battery: ClkR moves position UP (toward AUTO=2), ClkL
    // moves DOWN (toward BAT=0). Source: TFM StandbyAuto sends 2x ClkR to
    // reach position 2; StandbyBat sends 2x ClkL to reach position 0.
    private static async Task DispatchStandbyPowerAsync(
        SimConnect.SimConnectManager simConnect,
        uint guardEventId, uint switchEventId,
        int currentPosition, int targetPosition)
    {
        const uint MOUSE_FLAG_LEFTSINGLE  = 0x20000000;
        const uint MOUSE_FLAG_RIGHTSINGLE = 0x80000000;

        // ClkR = up (toward AUTO), ClkL = down (toward BAT) on this control.
        bool steppingUp = targetPosition > currentPosition;
        uint clickFlag = steppingUp ? MOUSE_FLAG_RIGHTSINGLE : MOUSE_FLAG_LEFTSINGLE;
        int steps = Math.Abs(targetPosition - currentPosition);

        // Always click the guard to ensure the cover is lifted before manipulating
        // the switch (PMDG does not snap it open on this specific control).
        simConnect.SendPMDGEventViaTransmitWithTarget(guardEventId, MOUSE_FLAG_RIGHTSINGLE);
        await Task.Delay(75);

        for (int i = 0; i < steps; i++)
        {
            simConnect.SendPMDGEventViaTransmitWithTarget(switchEventId, clickFlag);
            if (i < steps - 1) await Task.Delay(80);
        }
    }

    // Per-detent flaps lever events. The base EVT_CONTROL_STAND_FLAPS_LEVER is a
    public override bool HandleUIVariableSet(
        string varKey, double value,
        SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        // ------------------------------------------------------------------
        // 0. Standard SimConnect events
        //    Handled here to prevent MainForm's redundant announcements.
        // ------------------------------------------------------------------
        if (varDef.Type == SimConnect.SimVarType.Event)
        {
            simConnect.SendEvent(varDef.Name);
            return true;
        }

        // ------------------------------------------------------------------
        // Pressurization FLT ALT / LAND ALT direct-set (numeric _SET inputs).
        //   The PMDG "Direct Control" events take the literal altitude in feet.
        //   The panel knob rounds DOWN — FLT ALT to 500 ft, LAND ALT to 50 ft —
        //   and clamps; we mirror that so the spoken confirmation equals what the
        //   cockpit window shows. Below-sea-level LAND ALT is out of scope (min 0).
        // ------------------------------------------------------------------
        if (varKey == "AIR_FltAlt_SET" || varKey == "AIR_LandAlt_SET")
        {
            bool isFlt = varKey == "AIR_FltAlt_SET";
            string evtName = isFlt ? "EVT_OH_PRESS_FLT_ALT_SET" : "EVT_OH_PRESS_LAND_ALT_SET";
            int step = isFlt ? 500 : 50;
            int maxFt = isFlt ? 42000 : 14000;

            int feet = (int)Math.Round(value);
            if (feet < 0) feet = 0;
            if (feet > maxFt) feet = maxFt;
            feet = (feet / step) * step;   // round DOWN to the knob's step

            if (EventIds.TryGetValue(evtName, out int pressEvId))
            {
                simConnect.SendPMDGEvent(evtName, (uint)pressEvId, feet);
                string label = isFlt ? "Flight altitude" : "Landing altitude";
                announcer.Announce($"{label} set to {feet} feet");
                // Pre-load the monitor guard so the imminent window change does
                // not re-announce the value we just confirmed.
                if (isFlt) _lastFltAltWindow = feet; else _lastLandAltWindow = feet;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 0-cabin. Cabin & cockpit furniture (see the CABIN & COCKPIT
        //     FURNITURE region in GetVariables). All dispatch shapes below
        //     were live-verified closed-loop with PMDGDispatchTester
        //     (lvar/lvarget/cda/kev) on 2026-06-12. These branches MUST run
        //     before the generic LVar branch below.
        // ------------------------------------------------------------------
        // Jumpseat + armrests: SDK event with the POSITION as the CDA
        // parameter (0 = stowed/down, 1 = extended/up). The varKey is the
        // PMDG-owned read-back L-var (drives the combo display); a raw
        // SetLVar to it would revert, so intercept and fire the event.
        // Combo values are 0/100 (the L-var's anim scale) — collapse to 0/1.
        if (_sdkFurnitureEventMap.TryGetValue(varKey, out string? furnEvName) &&
            EventIds.TryGetValue(furnEvName, out int furnEvId))
        {
            int furnTarget = value != 0 ? 1 : 0;
            simConnect.SendPMDGEvent(furnEvName, (uint)furnEvId, furnTarget);
            return true;
        }

        // Sliding windows: replicate PMDG's VC click code exactly — set the
        // anim L-var, fire K:TOGGLE_AIRCRAFT_EXIT_FAST with the exit index
        // (16 = Captain, 17 = F/O), update PMDG's WindowOpen*/WindowClose*
        // mirror flags, all gated on PMDG's L:CanOpenWindows. The guard can
        // only be evaluated atomically in RPN (MobiFlight calculator code);
        // without MobiFlight, fall back to an unguarded set (worst case the
        // L-var animates while PMDG considers windows locked). When the guard
        // blocks, nothing changes and the state monitor stays silent.
        // Verified live (CanOpenWindows=1 at the gate): open → L-var 1, close
        // → L-var 0, exit toggle audible both ways.
        if (varKey == "Window_OpenClose_CA" || varKey == "Window_OpenClose_FO")
        {
            bool isCaptain = varKey == "Window_OpenClose_CA";
            int target = (int)value;
            int exitIndex = isCaptain ? 16 : 17;
            string openFlag  = isCaptain ? "WindowOpenCA"  : "WindowOpenFO";
            string closeFlag = isCaptain ? "WindowCloseCA" : "WindowCloseFO";
            if (simConnect.IsMobiFlightConnected)
            {
                simConnect.ExecuteCalculatorCode(
                    $"(L:CanOpenWindows, number) 0 != (L:{varKey}, bool) {target} != and " +
                    $"if{{ {target} (>L:{varKey}, bool) " +
                    $"{target} {exitIndex} (>K:TOGGLE_AIRCRAFT_EXIT_FAST) " +
                    $"{target} (>L:{openFlag}, bool) {1 - target} (>L:{closeFlag}, bool) }}");
            }
            else
            {
                simConnect.SetLVar(varKey, target);
                simConnect.SendEvent("TOGGLE_AIRCRAFT_EXIT_FAST", (uint)exitIndex);
            }
            return true;
        }

        // Sun visors: PMDG blocks DEPLOYING a visor while the same-side window
        // is open (the visor swings across the window opening). Mirror that
        // guard when deploying; stowing is always allowed.
        if (varKey is "visors_stow_unstow_L" or "visors_stow_unstow_R")
        {
            int target = (int)value;
            string windowLvar = varKey.EndsWith("_L") ? "Window_OpenClose_CA" : "Window_OpenClose_FO";
            if (target == 1 && simConnect.IsMobiFlightConnected)
                simConnect.ExecuteCalculatorCode(
                    $"(L:{windowLvar}, bool) 0 == if{{ 1 (>L:{varKey}, bool) }}");
            else
                simConnect.SetLVar(varKey, target);
            return true;
        }

        // Headrest / rudder-pedal drag positions — numeric text input
        // ("_SET" keys); clamp to the 0–100 anim range (PMDG's ANIM_CODE
        // reads the raw value; out-of-range writes glitch the animation).
        // Write varDef.Name (the bare L-var), not the "_SET" varKey.
        if (varKey is "headrest_CA_drag_h_SET" or "headrest_FO_drag_h_SET"
                    or "rudder_ped_adjust_CA_drag_h_SET" or "rudder_ped_adjust_FO_drag_h_SET")
        {
            double clamped = Math.Clamp(value, 0, 100);
            simConnect.SetLVar(varDef.Name, clamped);
            announcer.AnnounceImmediate($"{varDef.DisplayName} {clamped:F0}");
            return true;
        }

        // Attendant / ground call — SDK events via CDA with parameter 1
        // (live-verified 2026-06-11: press counters increment; parameter 0
        // ALSO registers as a press on this family — same as the CDU keys —
        // so always send 1).
        if (_cabinMomentaryEventMap.TryGetValue(varKey, out string? cabinEvName) &&
            EventIds.TryGetValue(cabinEvName, out int cabinEvId))
        {
            simConnect.SendPMDGEvent(cabinEvName, (uint)cabinEvId, 1);
            return true;
        }

        // Airstair pushbuttons — press-and-release momentary dispatch.
        if (_pressReleaseEventMap.TryGetValue(varKey, out string? prEvName) &&
            EventIds.TryGetValue(prEvName, out int prEvId))
        {
            _ = PressAndReleaseAsync(simConnect, prEvName, (uint)prEvId);
            return true;
        }

        // Airstair lights — 3-position selector; combo values are the anim
        // detents 0/50/100, the event parameter is the detent INDEX 0/1/2
        // (live-verified: param 1 → L-var 50, param 2 → 100).
        if (varKey == "switch_1654_73X" &&
            EventIds.TryGetValue("EVT_GALLEY_AIRSTAIR_LIGHTS", out int lightsEvId))
        {
            simConnect.SendPMDGEvent("EVT_GALLEY_AIRSTAIR_LIGHTS", (uint)lightsEvId, (int)(value / 50.0));
            return true;
        }

        // Galley water pushbutton radio-pairs — write the chosen button AND
        // clear its opposite, exactly as PMDG's click code does.
        if (_waterPairMap.TryGetValue(varKey, out string? oppositeLvar))
        {
            int sel = value != 0 ? 1 : 0;
            simConnect.SetLVar(varKey, sel);
            simConnect.SetLVar(oppositeLvar, 1 - sel);
            return true;
        }

        // Cubby bar position — numeric text input, clamp like the headrests.
        if (varKey == "cubby_bar_drag_v_SET")
        {
            double clampedBar = Math.Clamp(value, 0, 100);
            simConnect.SetLVar(varDef.Name, clampedBar);
            announcer.AnnounceImmediate($"{varDef.DisplayName} {clampedBar:F0}");
            return true;
        }

        // Binder cookie stash — PMDG's model gates the reveal on the cubby
        // bar being raised (>98) and runs the reveal animation itself from a
        // model-side Update loop once L:CubbyTrigger is set (live-verified:
        // trigger 1 → CubbyTimer ramps to 40, CookiesVis 1; trigger 0 resets).
        // Raise the bar automatically when revealing so the gate is satisfied.
        if (varKey == "CubbyTrigger")
        {
            int trig = value != 0 ? 1 : 0;
            if (trig == 1)
                simConnect.SetLVar("cubby_bar_drag_v", 100);
            simConnect.SetLVar("CubbyTrigger", trig);
            return true;
        }

        // Cupholder drinks — PMDG's click spot picks a pseudo-random drink
        // (anim positions 1..6). Cycle deterministically so repeated presses
        // step through every drink; needs MobiFlight for the read-modify-write,
        // otherwise just place drink 1.
        if (varKey is "CA_CupholderDrink" or "FO_CupholderDrink")
        {
            string cupLvar = varKey == "CA_CupholderDrink" ? "ca_cupholder_rand" : "fo_cupholder_rand";
            if (simConnect.IsMobiFlightConnected)
                simConnect.ExecuteCalculatorCode($"(L:{cupLvar}, number) 6 % 1 + (>L:{cupLvar}, number)");
            else
                simConnect.SetLVar(cupLvar, 1);
            return true;
        }

        // Composite bin / blind buttons — one SetLVar per item. The L-vars are
        // plain bools toggled by PMDG's own cabin click-spots (write-stick
        // verified both directions); bins/blinds not fitted on the loaded
        // cabin layout are harmless no-ops.
        if (varKey is "CABIN_AllBinsOpen" or "CABIN_AllBinsClose")
        {
            double binTarget = varKey == "CABIN_AllBinsOpen" ? 1 : 0;
            foreach (string lvar in s_binLvars)
                simConnect.SetLVar(lvar, binTarget);
            return true;
        }
        if (varKey is "CABIN_AllBlindsUp" or "CABIN_AllBlindsDown")
        {
            double blindTarget = varKey == "CABIN_AllBlindsDown" ? 1 : 0;
            foreach (string lvar in s_blindLvars)
                simConnect.SetLVar(lvar, blindTarget);
            return true;
        }

        // ------------------------------------------------------------------
        // 0b. Plain L-vars (Boris/xBAW Audio Works soundpack: ANR_onoff,
        //     HydPumpMfg, switch_277_73X — and the cabin furniture toggles
        //     above that need no special handling: bins, shades, lights,
        //     curtains, lavatory doors). These accept a direct SetLVar and
        //     persist — verified in-sim — unlike the PMDG-owned CDA switches.
        // ------------------------------------------------------------------
        if (varDef.Type == SimConnect.SimVarType.LVar)
        {
            simConnect.SetLVar(varDef.Name, value);
            return true;
        }

        // ------------------------------------------------------------------
        // 0a. COM standby frequency set — validate, convert MHz → Hz, send via
        //     standard SimConnect event. Return true to prevent MainForm's
        //     redundant "Standby frequency set to xxx" announcement.
        // ------------------------------------------------------------------
        if (varKey.StartsWith("COM_STANDBY_FREQUENCY_SET"))
        {
            if (value >= 118.0 && value <= 136.975)
            {
                uint frequencyHz = (uint)Math.Round(value * 1000000);
                string setEvent = varKey.Contains(":2") ? "COM2_STBY_RADIO_SET_HZ" : "COM_STBY_RADIO_SET_HZ";
                simConnect.SendEvent(setEvent, frequencyHz);
            }
            else
            {
                announcer.AnnounceImmediate("Invalid frequency. Range: 118.000 to 136.975");
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 0b. Autobrake selector — TFM dispatches with the absolute target
        //     position via SendControlToFS(EVT_MPM_AUTOBRAKE_SELECTOR, position).
        //     Mirroring that via TransmitClientEvent("#<id>", target). Click-walking
        //     does not move this control; it accepts only direct positions.
        // ------------------------------------------------------------------
        if (varKey == "MAIN_AutobrakeSelector")
        {
            if (EventIds.TryGetValue("EVT_MPM_AUTOBRAKE_SELECTOR", out int abId))
            {
                int target = (int)value;
                var dm = simConnect.PMDGDataManager;
                if (dm != null && (int)dm.GetFieldValue(varDef.Name) == target) return true;
                simConnect.SendPMDGEventViaTransmitWithTarget((uint)abId, (uint)target);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 0c. Standby Power Selector — guarded 3-position switch where PMDG
        //     does NOT auto-handle the guard (unlike the battery). TFM's
        //     StandbyBat/Off/Auto methods explicitly click the guard event
        //     (EVT_OH_ELEC_STBY_PWR_GUARD) BEFORE clicking the switch event
        //     to lift the protective cover. We replicate the same sequence.
        // ------------------------------------------------------------------
        if (varKey == "ELEC_StandbyPowerSelector")
        {
            if (EventIds.TryGetValue("EVT_OH_ELEC_STBY_PWR_GUARD", out int sgId) &&
                EventIds.TryGetValue("EVT_OH_ELEC_STBY_PWR_SWITCH", out int ssId))
            {
                int target = (int)value;
                var dm = simConnect.PMDGDataManager;
                if (dm == null || !dm.IsReady)
                {
                    announcer.AnnounceImmediate("Switch not ready, please try again in a moment.");
                    return true;
                }
                int currentPosition = (int)dm.GetFieldValue(varDef.Name);
                if (currentPosition == target) return true;
                _ = DispatchStandbyPowerAsync(
                    simConnect, (uint)sgId, (uint)ssId, currentPosition, target);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 0c-mins. EFIS Mins value Set — stateless RST-then-rotate-up. The
        //     PMDG NG3 SDK exposes no readback for the minimums VALUE, so we
        //     always reset the knob to zero first via EVT_EFIS_CPT/FO_MINIMUMS_RST,
        //     then rotate up by N clicks to reach the user's target. Step size
        //     depends on the current mode (RADIO = 1 ft/click, BARO = 20 ft/click).
        // ------------------------------------------------------------------
        if (varKey == "EFIS_MinsValueFt_0_SET" || varKey == "EFIS_MinsValueFt_1_SET")
        {
            if (_minsKnobOp is { IsCompleted: false })
            {
                announcer.AnnounceImmediate("Still setting minimums, please wait.");
                return true;
            }
            bool isCaptain = varKey == "EFIS_MinsValueFt_0_SET";
            string modeVar = isCaptain ? "EFIS_MinsSelBARO_0" : "EFIS_MinsSelBARO_1";
            string rstEvent = isCaptain ? "EVT_EFIS_CPT_MINIMUMS_RST" : "EVT_EFIS_FO_MINIMUMS_RST";
            string rotEvent = isCaptain ? "EVT_EFIS_CPT_MINIMUMS"     : "EVT_EFIS_FO_MINIMUMS";

            var dm = simConnect.PMDGDataManager;
            bool baroMode = dm != null && (int)dm.GetFieldValue(modeVar) == 1;
            int stepFt = baroMode ? MINS_STEP_FT_BARO : MINS_STEP_FT_RADIO;
            int maxFt  = baroMode ? 9999 : 1000;
            int target = (int)value;

            if (target < 0 || target > maxFt)
            {
                announcer.AnnounceImmediate(
                    $"Invalid minimums. Enter 0 to {maxFt} feet.");
                return true;
            }

            if (!EventIds.TryGetValue(rstEvent, out int rstEvId) ||
                !EventIds.TryGetValue(rotEvent, out int rotEvId))
            {
                announcer.AnnounceImmediate("Minimums knob event not available.");
                return true;
            }

            _minsKnobOp = ResetThenRotateMinsAsync(
                simConnect, rstEvent, (uint)rstEvId, (uint)rotEvId,
                target, stepFt, baroMode, announcer);
            return true;
        }

        // ------------------------------------------------------------------
        // 0d. Flight deck door momentary buttons. PED_FltDkDoorSel positions
        //     0/1/3 are spring-loaded — they momentarily latch and then PMDG
        //     springs them back to position 2 (AUTO). Single CDA write with
        //     the target position parameter is the documented PMDG pattern
        //     for spring-loaded selectors (same as APU_Selector, etc.).
        // ------------------------------------------------------------------
        if (varKey == "PED_FltDkDoor_Unlock" || varKey == "PED_FltDkDoor_Deny")
        {
            if (EventIds.TryGetValue("EVT_FLT_DK_DOOR_KNOB", out int doorEvId))
            {
                int target = varKey == "PED_FltDkDoor_Unlock" ? 0 : 3;
                simConnect.SendPMDGEvent("EVT_FLT_DK_DOOR_KNOB", (uint)doorEvId, target);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 1. Guarded switches — dispatched via TFM click-walking convention.
        //    PMDG NG3 handles guard physics transparently when the switch event
        //    receives a mouse-click TransmitClientEvent (ClkL/ClkR). Explicit
        //    guard open/close CDA writes caused the switch to spring back to
        //    its prior detent (live-tested 2026-05-22).
        //    Mirrors TFM's CalculateSwitchPosition(useClicks=true) behaviour.
        //    Fire-and-forget; UI doesn't need to await.
        // ------------------------------------------------------------------
        if (_guardedMap.TryGetValue(varKey, out var guardPair))
        {
            if (EventIds.TryGetValue(guardPair.Switch, out int sId))
            {
                int target = (int)value;
                var dm = simConnect.PMDGDataManager;
                if (dm == null || !dm.IsReady)
                {
                    announcer.AnnounceImmediate("Switch not ready, please try again in a moment.");
                    return true;
                }

                // Fire handles need the UNLOCK switch (guard slot) pressed before
                // the TOP pull — PMDG does not auto-lift it like a cover. Fire the
                // UNLOCK→TOP pair instead of click-walking the switch alone.
                if (_fireHandlePressKeys.Contains(varKey) &&
                    EventIds.TryGetValue(guardPair.Guard, out int unlockId))
                {
                    _ = PullFireHandleAsync(simConnect, (uint)unlockId, (uint)sId);
                    return true;
                }

                int currentPosition = (int)dm.GetFieldValue(varDef.Name);
                if (currentPosition == target) return true; // already at target — no-op
                _ = simConnect.WalkPMDGSelector((uint)sId, currentPosition, target);
                return true;
            }
        }

        // ------------------------------------------------------------------
        // 2. MCP FD switches and AT Arm — require MOUSE_FLAG_LEFTSINGLE.
        //    Unlike most PMDG switches, these ignore direct position values
        //    and need the mouse-click flag per the SDK example.
        // ------------------------------------------------------------------
        if (varKey == "MCP_FDSw_0" || varKey == "MCP_FDSw_1" || varKey == "MCP_ATArmSw")
        {
            string mcpEvent = varKey switch
            {
                "MCP_FDSw_0" => "EVT_MCP_FD_SWITCH_L",
                "MCP_FDSw_1" => "EVT_MCP_FD_SWITCH_R",
                _            => "EVT_MCP_AT_ARM_SWITCH"
            };
            if (EventIds.TryGetValue(mcpEvent, out int mcpId))
            {
                int target = (int)value;
                var dm = simConnect.PMDGDataManager;
                if (dm != null && (int)dm.GetFieldValue(varDef.Name) == target)
                {
                    return true; // already at target — no-op
                }
                const int MOUSE_FLAG_LEFTSINGLE = unchecked((int)0x20000000);
                simConnect.SendPMDGEvent(mcpEvent, (uint)mcpId, MOUSE_FLAG_LEFTSINGLE);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 2c. Engine Start Selectors (GRD/OFF/CONT/FLT) and Ignition Selector
        //     (IGN L/BOTH/IGN R) — rotary selectors that PMDG NG3 dispatches
        //     via TransmitClientEvent with an ABSOLUTE position parameter
        //     (verified live 2026-05-24 with the standalone test rig:
        //     evt 69751 N successfully sets ENG_StartSelector[0] to N for
        //     N in {0,1,2,3}). The generic click-walking path was sending
        //     LEFTSINGLE for "stepping up" but on this event family LEFTSINGLE
        //     DECREASES the position — directionally inverted, so every user
        //     interaction stepped the selector the wrong way.
        // ------------------------------------------------------------------
        if (varKey is "ENG_StartSelector_0" or "ENG_StartSelector_1" or "ENG_IgnitionSelector")
        {
            string evName = varKey switch
            {
                "ENG_StartSelector_0" => "EVT_OH_LIGHTS_L_ENGINE_START",
                "ENG_StartSelector_1" => "EVT_OH_LIGHTS_R_ENGINE_START",
                _                     => "EVT_OH_LIGHTS_IGN_SEL",
            };
            if (EventIds.TryGetValue(evName, out int selEvId))
            {
                int target = (int)value;
                var dm = simConnect.PMDGDataManager;
                if (dm != null && (int)dm.GetFieldValue(varDef.Name) == target) return true;
                simConnect.SendPMDGEventViaTransmitWithTarget((uint)selEvId, (uint)target);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 2c1. Fuel Control Levers (combo, Cutoff/Run) — dispatched via
        //     TransmitClientEvent with directional mouse flag. Verified
        //     2026-05-24 with the PMDGDispatchTester rig: both directions
        //     cycle FUEL_annunENG_VALVE_CLOSED correctly (0→2→1 for Run,
        //     1→2→0 for Cutoff). Read state comes from a derived override in
        //     PMDGNG3DataManager that watches the same FUEL_annun byte.
        // ------------------------------------------------------------------
        if (varKey is "ENG_StartLever_0" or "ENG_StartLever_1")
        {
            string leverEvName = varKey == "ENG_StartLever_0"
                ? "EVT_CONTROL_STAND_ENG1_START_LEVER"
                : "EVT_CONTROL_STAND_ENG2_START_LEVER";
            if (EventIds.TryGetValue(leverEvName, out int leverEvId))
            {
                int target = (int)value;
                const uint MOUSE_FLAG_LEFTSINGLE  = 0x20000000u;
                const uint MOUSE_FLAG_RIGHTSINGLE = 0x80000000u;
                uint flag = target != 0 ? MOUSE_FLAG_LEFTSINGLE : MOUSE_FLAG_RIGHTSINGLE;
                simConnect.SendPMDGEventViaTransmitWithTarget((uint)leverEvId, flag);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 2d. Directional push buttons — momentary buttons that dispatch a
        //     specific mouse-flag direction once per press. Used by APU GEN
        //     1/2 (split into separate "On" / "Off" buttons because PMDG NG3
        //     gives no reliable per-switch state signal — see
        //     _directionalPushMap above).
        // ------------------------------------------------------------------
        if (_directionalPushMap.TryGetValue(varKey, out var directional) &&
            EventIds.TryGetValue(directional.EventName, out int dirEvId))
        {
            const int MOUSE_FLAG_LEFTSINGLE  = unchecked((int)0x20000000);
            const int MOUSE_FLAG_RIGHTSINGLE = unchecked((int)0x80000000);
            int flag = directional.IsOn ? MOUSE_FLAG_LEFTSINGLE : MOUSE_FLAG_RIGHTSINGLE;
            simConnect.SendPMDGEvent(directional.EventName, (uint)dirEvId, flag);
            return true;
        }

        // ------------------------------------------------------------------
        // 2e. Momentary press-to-set bool switches — Ground Power and the
        //     four generator switches on the 737 NG.
        //
        //     Verified dispatch shape (2026-05-22, standalone test rig in
        //     PMDGDispatchTester subscribing directly to PMDG_NG3_Data with
        //     PERIOD.ON_SET / FLAG.CHANGED): CDA write with a directional
        //     MOUSE_FLAG_* value in the Parameter field.
        //         LEFTSINGLE  (0x20000000) → set ON   (push up)
        //         RIGHTSINGLE (0x80000000) → set OFF  (push down)
        //     Direct parameter 0/1 is silently ignored. The state PERSISTS
        //     (no spring back) and same-direction repeats are idempotent on
        //     the PMDG side, so we don't need a cache no-op guard. Removing
        //     the guard also fixes the "ON does nothing" complaint when the
        //     CDA snapshot lags the actual cockpit state.
        //
        //     Patterns tried that do NOT work (do not revive):
        //       - bare LEFT/RIGHTSINGLE via TransmitClientEvent (click-walk):
        //         plays sound, state springs back.
        //       - CDA direct-position (Parameter=0/1, taxi-light convention):
        //         silently ignored on this event family.
        //       - paired LEFTSINGLE+LEFTRELEASE via TransmitClientEvent
        //         (FD-switch convention from ConnectionTest.cpp): silently
        //         ignored.
        //       - CDA with MOUSE_FLAG_LEFTSINGLE for both directions (toggle):
        //         worked for ON but cache-mismatch races could flip OFF the
        //         wrong way ("toggling OFF actually turned it on").
        // ------------------------------------------------------------------
        if (_momentaryToggleMap.TryGetValue(varKey, out string? momentaryEventName) &&
            EventIds.TryGetValue(momentaryEventName, out int momentaryEvId))
        {
            int target = (int)value;
            const int MOUSE_FLAG_LEFTSINGLE  = unchecked((int)0x20000000);
            const int MOUSE_FLAG_RIGHTSINGLE = unchecked((int)0x80000000);
            int mouseFlag = target != 0 ? MOUSE_FLAG_LEFTSINGLE : MOUSE_FLAG_RIGHTSINGLE;
            simConnect.SendPMDGEvent(momentaryEventName, (uint)momentaryEvId, mouseFlag);
            // Inform the data manager of the user's intended switch state so
            // it can publish the derived-override event with the right value.
            // PMDG's own snapshot for these switches lies, and the annunciator
            // can't disambiguate APU GEN 1 vs APU GEN 2 (they share one).
            // Without this, after the first user click PMDG's stale snapshot
            // would re-set the display via the next derived-override pass.
            (simConnect.PMDGDataManager as SimConnect.PMDGNG3DataManager)
                ?.NotifyLocalSwitchState(varKey, target != 0);
            return true;
        }

        // ------------------------------------------------------------------
        // 3. Spring-loaded selectors — dispatch via CDA direct position.
        //    APU_Selector (0/1/2 = OFF/ON/START) and the fire-detect /
        //    extinguisher test switches all have a sprung detent that PMDG
        //    NG3 returns to rest automatically. Click-walking these via
        //    TransmitClientEvent left the cached byte in a transient invalid
        //    state (PMDG's spring-back raced our second click), producing
        //    "APU state unknown" announcements and a stale panel display.
        //    One CDA write with the target position is the convention from
        //    PMDG's own ConnectionTest.cpp taxi-light sample; PMDG handles
        //    the spring internally and reflects the rest state in the next
        //    snapshot.
        // ------------------------------------------------------------------
        if (_springLoadedSelectorMap.TryGetValue(varKey, out string? springEventName) &&
            EventIds.TryGetValue(springEventName, out int springEvId))
        {
            int target = (int)value;
            var dm = simConnect.PMDGDataManager;
            if (dm != null && (int)dm.GetFieldValue(varDef.Name) == target) return true;
            simConnect.SendPMDGEvent(springEventName, (uint)springEvId, target);
            return true;
        }

        // ------------------------------------------------------------------
        // 3a. ACP selected mic — direct dispatch via per-position event.
        //     The SDK exposes one momentary event per mic detent (VHF1..PA),
        //     not a single stepwise selector. The byte value chosen in the
        //     UI dropdown maps directly to the matching EVT_ACP_*_MIC_X
        //     event. No-op when already at target. Index 2 (observer ACP)
        //     is intentionally not handled — the 737 has no observer ACP.
        // ------------------------------------------------------------------
        if (varKey == "COMM_SelectedMic_0" || varKey == "COMM_SelectedMic_1")
        {
            int target = (int)value;
            string[] perPositionEvents =
            {
                "MIC_VHF1", "MIC_VHF2", "MIC_VHF3", "MIC_HF1",
                "MIC_HF2",  "MIC_FLT",  "MIC_SVC",  "MIC_PA"
            };
            if (target < 0 || target >= perPositionEvents.Length) return true;

            // Skip if cached state already at target (saves a redundant CDA round-trip
            // and prevents the SDK from re-firing the mic-pressed sound).
            var dm = simConnect.PMDGDataManager;
            if (dm != null && (int)dm.GetFieldValue(varDef.Name) == target) return true;

            string acpPrefix = varKey == "COMM_SelectedMic_0" ? "EVT_ACP_CAPT_" : "EVT_ACP_FO_";
            string micEvtName = acpPrefix + perPositionEvents[target];
            if (EventIds.TryGetValue(micEvtName, out int micEvId))
            {
                simConnect.SendPMDGEvent(micEvtName, (uint)micEvId, 1);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 3b. IRS Mode Selector Unit (left/right rotary). The generic 3+-
        //     position click-walk does NOT move this selector — verified
        //     in-sim 2026-05-25: LEFTSINGLE clicks no-op, but a CDA direct-
        //     position write latches it (OFF→NAV confirmed). Dispatch directly.
        // ------------------------------------------------------------------
        if (varKey == "IRS_ModeSelector_0" || varKey == "IRS_ModeSelector_1")
        {
            if (_simpleEventMap.TryGetValue(varKey, out string? irsEvent) &&
                EventIds.TryGetValue(irsEvent, out int irsId))
            {
                int target = (int)value;
                var dm = simConnect.PMDGDataManager;
                if (dm != null && (int)dm.GetFieldValue(varDef.Name) == target) return true;
                simConnect.SendPMDGEvent(irsEvent, (uint)irsId, target);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 4. Fire handles — handled as synthetic momentary press buttons
        //    (FIRE_EngineHandle_1_Press / FIRE_APUHandle_Press / etc.) which
        //    route through _simpleEventMap below. Per the PMDG SDK each
        //    EVT_FIRE_HANDLE_*_TOP / _BOTTOM press is momentary and advances
        //    the handle one state position — the parameter is ignored. The
        //    read-only FIRE_HandlePos_0/1/2 state vars are NOT user-settable.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // 4a. Absolute-position selector dispatch — for selector families
        //     that PMDG NG3 accepts via TransmitClientEvent with the target
        //     position as the parameter (vs. click-walking). See
        //     _absolutePositionSelectorSet for the list and rationale.
        // ------------------------------------------------------------------
        if (_absolutePositionSelectorSet.Contains(varKey) &&
            _simpleEventMap.TryGetValue(varKey, out string? absEventName) &&
            EventIds.TryGetValue(absEventName, out int absEvId))
        {
            int target = (int)value;
            var dm = simConnect.PMDGDataManager;
            if (dm != null && (int)dm.GetFieldValue(varDef.Name) == target) return true;
            simConnect.SendPMDGEventViaTransmitWithTarget((uint)absEvId, (uint)target);
            return true;
        }

        // ------------------------------------------------------------------
        // 5. Generic _simpleEventMap lookup — covers every remaining mapped
        //    var-key. The parameter shape is determined by the var def:
        //
        //    - RenderAsButton && IsMomentary  → parameter = 1 (press)
        //    - ValueDescriptions.Count >= 2   → parameter = (int)value
        //                                        (toggle or multi-position
        //                                        selector — direct position)
        //    - otherwise                      → parameter = (int)value
        //                                        (single-state toggle)
        //
        //    For ValueDescriptions cases we additionally short-circuit when
        //    the cached struct already shows the switch at the target
        //    position, matching the 777 pattern's no-op guard.
        // ------------------------------------------------------------------
        if (_simpleEventMap.TryGetValue(varKey, out string? eventName) &&
            EventIds.TryGetValue(eventName, out int evId))
        {
            uint eventId = (uint)evId;

            if (varDef.RenderAsButton && varDef.IsMomentary)
            {
                // Momentary push buttons (MCP modes/engage, fire-handle bottom,
                // etc.) commit only with a LEFTSINGLE press followed by a
                // LEFTRELEASE (TransmitClientEvent). A bare CDA param=1 plays the
                // click sound but the state springs back. Verified in-sim
                // 2026-05-25: FD switch + HDG SEL committed via LEFTSINGLE+RELEASE
                // while CDA param=1 did not.
                _ = simConnect.SendPMDGMomentaryToggle(eventId, 1);
                return true;
            }

            int target = (int)value;
            if (varDef.ValueDescriptions.Count >= 2)
            {
                var dm = simConnect.PMDGDataManager;
                if (dm != null && (int)dm.GetFieldValue(varDef.Name) == target)
                {
                    return true; // already at target — no-op
                }
            }

            // 2-position bool toggles: CDA direct-position dispatch.
            // Pattern source: PMDG_NG3_ConnectionTest.cpp `toggleTaxiLightSwitch`
            // — one SetClientData write with `Control.Parameter = target`. PMDG
            // NG3 latches the bool to the target detent in one shot. Confirmed
            // working against the cached snapshot (after the ca72271 SimConnect
            // ID-collision fix landed and snapshots started reporting real
            // values).
            //
            // The earlier "walk via single LEFTSINGLE/RIGHTSINGLE click" approach
            // was incompatible: PMDG NG3 receives the click event (sim plays the
            // physical-switch sound), but a bare LEFTSINGLE without its matching
            // LEFTRELEASE is interpreted as an incomplete momentary press — the
            // bool flicks to the new state for a frame and then springs back to
            // its prior value. Confirmed at the PMDG 737 Ground Power and APU
            // GENERATOR switches in 2026-05-22 user testing (switch played its
            // click sound on every press, but state never latched).
            if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count == 2)
            {
                simConnect.SendPMDGEvent(eventName, eventId, target);
                return true;
            }

            // 3+ position selector — walk through detents one click at a time
            // via TransmitClientEvent LEFTSINGLE/RIGHTSINGLE. Stable-detent
            // selectors (IRS_ModeSelector, ENG_StartSelector, LTS_DomeWhiteSw,
            // EFIS_VORADFSel*, EFIS_ModeSel, EFIS_RangeSel, AIR_OutflowValve,
            // AIR_PressurizationModeSelector, etc.) work with this pattern in
            // user testing — each click advances one detent and the new
            // position latches normally. Spring-loaded selectors are
            // intercepted earlier by the _springLoadedSelectorMap branch.
            if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count >= 3)
            {
                var dm = simConnect.PMDGDataManager;
                if (dm == null || !dm.IsReady)
                {
                    announcer.AnnounceImmediate("Switch not ready, please try again in a moment.");
                    return true;
                }
                int currentPosition = (int)dm.GetFieldValue(varDef.Name);
                _ = simConnect.WalkPMDGSelector(eventId, currentPosition, target);
                return true;
            }

            // Fall-through: single-state (no ValueDescriptions); send the absolute target.
            simConnect.SendPMDGEvent(eventName, eventId, target);
            return true;
        }

        // ------------------------------------------------------------------
        // Default: not handled — delegate to MainForm's generic fallback.
        // ------------------------------------------------------------------
        return false;
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Generic handling first (altitude thousand-foot crossings, etc.).
        if (base.ProcessSimVarUpdate(varName, value, announcer))
            return true;

        // Swallow momentary button chatter — buttons briefly go to 1 then back
        // to 0. The actual effect is reflected through annunciator lights,
        // which are excluded from SuppressedButtonKeys and have explicit cases
        // below.
        if (SuppressedButtonKeys.Contains(varName))
            return true;

        switch (varName)
        {
            // -------------------------------------------------------------
            // Transponder mode: swallow the per-detent callouts produced by a
            // transponder click-walk — walking STBY→TA/RA passes ALT RPTG
            // OFF/XPNDR/TA and each detent would otherwise announce, when the
            // walk's initiator already spoke (the screen reader read the combo
            // pick; a First Officer flow announces its own step label).
            // Self-draining COUNT (not a value latch) so it can never
            // permanently mute a later background change — a knob turned in
            // the VC still announces normally. CAS drain so an off-UI-thread
            // walk start can't race a decrement.
            // -------------------------------------------------------------
            case "XPDR_ModeSel":
                return SimConnect.PMDGNG3DataManager.AnyWalkInProgress;

            // -------------------------------------------------------------
            // Stabilizer trim, in units (~0–17). Sourced from the PMDG L-var
            // ElevTrimTT (see GetPMDGVariables) because the NG3 does not drive
            // the stock ELEVATOR TRIM POSITION SimVar. Gated by the shared
            // Shift+T trim-announcement toggle; baseline absorbed silently on
            // connect; debounced so sub-0.1-unit electric-trim jitter is quiet.
            // -------------------------------------------------------------
            case "MON_PMDG737_StabTrim":
            {
                if (!_trimAnnouncementsEnabled)
                    return true;
                double rounded = Math.Round(value, 1);
                if (double.IsNaN(_lastAnnouncedStabTrim))
                {
                    _lastAnnouncedStabTrim = rounded;
                    return true;
                }
                if (Math.Abs(rounded - _lastAnnouncedStabTrim) < 0.05)
                    return true;
                _lastAnnouncedStabTrim = rounded;
                announcer.Announce($"Trim {rounded:F1}");
                return true;
            }

            // -------------------------------------------------------------
            // Pressurization FLT ALT / LAND ALT — auto-announce on change from
            // any source. NO NaN-seed-and-swallow here: the connect baseline is
            // absorbed upstream by MainForm.OnSimVarUpdated's initial-snapshot
            // early-return (it caches the value and returns BEFORE calling
            // ProcessSimVarUpdate), so this case only ever sees genuine changes —
            // including the FIRST one, which must announce. _last* starts NaN and
            // `value == NaN` is false, so the first change falls through and
            // announces; a panel-initiated set pre-loads _last* (see
            // HandleUIVariableSet) so this does not double-announce it. Do NOT
            // re-add a `double.IsNaN(_last) -> seed; return` guard: with the
            // baseline already absorbed it would silently eat the FIRST real
            // change — and these are rare, discrete values, so that is the only
            // callout the pilot gets.
            // -------------------------------------------------------------
            case "AIR_FltAltWindow":
            {
                if (value == _lastFltAltWindow) return true;
                _lastFltAltWindow = value;
                announcer.Announce($"Flight altitude {(int)Math.Round(value)} feet");
                return true;
            }
            case "AIR_LandAltWindow":
            {
                if (value == _lastLandAltWindow) return true;
                _lastLandAltWindow = value;
                announcer.Announce($"Landing altitude {(int)Math.Round(value)} feet");
                return true;
            }

            // -------------------------------------------------------------
            // Altimeter setting (inHg). 29.92 = "Altimeter standard".
            // Otherwise dual-unit "Altimeter: <hpa>, <inhg>" — same wording
            // as the ReadAltimeter hotkey (B) so set and read sound alike.
            // Switch must key off the variable KEY (ALTIMETER_SETTING), not
            // the SimConnect Name ("KOHLSMAN SETTING HG"); MainForm dispatches
            // on the dictionary key, so a Name-based case never matches and
            // the announcement fell through to the generic FormatVariableValue
            // path ("Altimeter Setting: 29.92" — inches only).
            // -------------------------------------------------------------
            case "ALTIMETER_SETTING":
            {
                if (double.IsNaN(_lastAnnouncedAltimeter))
                {
                    _lastAnnouncedAltimeter = value;
                    return true;
                }
                if (Math.Abs(value - _lastAnnouncedAltimeter) < 0.005)
                    return true;
                _lastAnnouncedAltimeter = value;
                if (Math.Abs(value - 29.92) < 0.005)
                    announcer.Announce("Altimeter standard");
                else
                {
                    int hpa = (int)Math.Round(value * 33.8639);
                    announcer.Announce($"Altimeter: {hpa}, {value:0.00}");
                }
                return true;
            }

            // -------------------------------------------------------------
            // Speed-brake lever position, from the PMDG L-var switch_679_73X
            // (see MON_PMDG737_SpeedBrake in GetPMDGVariables) — the NG3 SDK
            // exposes no lever-position field and PMDG doesn't drive the stock
            // SPOILERS HANDLE POSITION SimVar, so this is the only source for
            // the full position (down / armed / 50% / flight / fully deployed).
            // The L-var sweeps as the lever animates; the resting detent is
            // announced via the trailing-edge settle timer (OnSpeedBrakeSettle).
            // Replaces the old 3-state annunciator-derived callout.
            // -------------------------------------------------------------
            case "MON_PMDG737_SpeedBrake":
            {
                // The L-var sweeps as the lever animates; defer the callout to a
                // trailing-edge settle timer (restarted on every change) so we
                // announce only the resting detent, not values swept through.
                lock (_speedBrakeLock)
                {
                    _speedBrakeLatestValue = value;
                    _speedBrakeAnnouncer = announcer;
                }
                (_speedBrakeSettleTimer ??= new System.Threading.Timer(OnSpeedBrakeSettle))
                    .Change(SpeedBrakeSettleMs, System.Threading.Timeout.Infinite);
                return true;
            }

            // Speed-brake position is now announced from the L-var above;
            // suppress the redundant ARMED / EXTENDED annunciator-light callouts
            // (the separate DO NOT ARM caution light still announces normally).
            case "MAIN_annunSPEEDBRAKE_ARMED":
            case "MAIN_annunSPEEDBRAKE_EXTENDED":
                return true;

            // -------------------------------------------------------------
            // MCP display values. IASBlank/VertSpeedBlank are flags that
            // other handlers consult; we absorb their own announcements
            // silently here. MCP_IASMach below 10 is Mach, otherwise knots.
            // -------------------------------------------------------------
            case "MCP_IASMach":
                if (value < 10)
                    announcer.Announce($"MCP Mach {value:F2}");
                else
                    announcer.Announce($"MCP speed {(int)Math.Round(value)} knots");
                return true;

            case "MCP_Heading":
                announcer.Announce($"MCP heading {(int)Math.Round(value)}");
                return true;

            case "MCP_Altitude":
                announcer.Announce($"MCP altitude {(int)Math.Round(value)} feet");
                return true;

            case "MCP_VertSpeed":
                announcer.Announce($"VS {(int)Math.Round(value)} feet per minute");
                return true;

            case "MCP_IASBlank":
            case "MCP_VertSpeedBlank":
                // Flags consumed by other handlers — absorb silently.
                return true;

            // -------------------------------------------------------------
            // MCP overspeed / underspeed flash and power indicator. Rising
            // edge speaks; falling edge is silent (no point repeating "no
            // longer overspeed" — the pilot just slowed down).
            // -------------------------------------------------------------
            case "MCP_IASOverspeedFlash":
                if (value > 0.5) announcer.AnnounceImmediate("MCP overspeed");
                return true;
            case "MCP_IASUnderspeedFlash":
                if (value > 0.5) announcer.AnnounceImmediate("MCP underspeed");
                return true;
            case "MCP_indication_powered":
                announcer.Announce(value > 0.5 ? "MCP powered" : "MCP unpowered");
                return true;

            // -------------------------------------------------------------
            // MCP mode annunciators.
            // -------------------------------------------------------------
            case "MCP_annunVNAV":
                announcer.Announce(value > 0 ? "VNAV engaged" : "VNAV disengaged");
                return true;
            case "MCP_annunLVL_CHG":
                announcer.Announce(value > 0 ? "Level Change engaged" : "Level Change disengaged");
                return true;
            case "MCP_annunHDG_SEL":
                announcer.Announce(value > 0 ? "Heading Select engaged" : "Heading Select disengaged");
                return true;
            case "MCP_annunLNAV":
                announcer.Announce(value > 0 ? "LNAV engaged" : "LNAV disengaged");
                return true;
            case "MCP_annunVOR_LOC":
                announcer.Announce(value > 0 ? "VOR/LOC armed" : "VOR/LOC disengaged");
                return true;
            case "MCP_annunAPP":
                announcer.Announce(value > 0 ? "Approach armed" : "Approach disengaged");
                return true;
            case "MCP_annunALT_HOLD":
                announcer.Announce(value > 0 ? "Altitude Hold engaged" : "Altitude Hold disengaged");
                return true;
            case "MCP_annunVS":
                announcer.Announce(value > 0 ? "Vertical Speed engaged" : "Vertical Speed disengaged");
                return true;
            case "MCP_annunCMD_A":
                announcer.Announce(value > 0 ? "Command A engaged" : "Command A disengaged");
                return true;
            case "MCP_annunCMD_B":
                announcer.Announce(value > 0 ? "Command B engaged" : "Command B disengaged");
                return true;
            case "MCP_annunCWS_A":
                announcer.Announce(value > 0 ? "CWS A engaged" : "CWS A disengaged");
                return true;
            case "MCP_annunCWS_B":
                announcer.Announce(value > 0 ? "CWS B engaged" : "CWS B disengaged");
                return true;
            case "MCP_annunN1":
                announcer.Announce(value > 0 ? "N1 armed" : "N1 disengaged");
                return true;
            case "MCP_annunSPEED":
                announcer.Announce(value > 0 ? "Speed armed" : "Speed disengaged");
                return true;
            case "MCP_annunATArm":
                announcer.Announce(value > 0 ? "A/T armed" : "A/T disengaged");
                return true;
            case "MCP_annunFD_0":
                announcer.Announce(value > 0 ? "FD Captain engaged" : "FD Captain disengaged");
                return true;
            case "MCP_annunFD_1":
                announcer.Announce(value > 0 ? "FD First Officer engaged" : "FD First Officer disengaged");
                return true;

            // -------------------------------------------------------------
            // Parking brake (control-stand annunciator). Every other 737
            // annunciator has an explicit case; this one was missing, so a
            // toggle produced no callout. Announce both edges. The field is
            // confirmed to track in-sim (false<->true with brakes applied).
            // -------------------------------------------------------------
            case "PED_annunParkingBrake":
                announcer.AnnounceImmediate(value > 0.5 ? "Parking brake on" : "Parking brake off");
                return true;

            // -------------------------------------------------------------
            // Flap position (trailing-edge needle, degrees). No commanded-
            // detent field exists on the 737, so snap the analog needle to the
            // nearest detent and announce on a settled change. The candidate
            // must be confirmed on two consecutive settled samples to debounce
            // the brief sweep past intermediate detents during a multi-step
            // selection. IsAnnounced=false on the var keeps the generic path
            // from reading the raw float.
            // -------------------------------------------------------------
            case "MAIN_TEFlapsNeedle_0":
            {
                int detent = SettledFlapDetent(value);
                if (detent < 0) { _flapDetentCandidate = int.MinValue; return true; }
                if (detent == _lastFlapDetentAnnounced) return true;
                if (detent == _flapDetentCandidate)
                {
                    bool first = _lastFlapDetentAnnounced == int.MinValue;
                    _lastFlapDetentAnnounced = detent;
                    if (!first)
                        announcer.Announce(detent == 0 ? "Flaps up" : $"Flaps {detent}");
                }
                else
                {
                    _flapDetentCandidate = detent;
                }
                return true;
            }

            // -------------------------------------------------------------
            // Fire warnings + master caution — these are time-critical and
            // must interrupt queued speech. Only announce on the rising
            // edge (value > 0).
            // -------------------------------------------------------------
            case "WARN_annunFIRE_WARN_0":
                if (value > 0.5) announcer.AnnounceImmediate("Fire warning Captain");
                return true;
            case "WARN_annunFIRE_WARN_1":
                if (value > 0.5) announcer.AnnounceImmediate("Fire warning First Officer");
                return true;
            case "WARN_annunMASTER_CAUTION_0":
            case "WARN_annunMASTER_CAUTION_1":
                if (value > 0.5) announcer.AnnounceImmediate("Master Caution");
                return true;

            // -------------------------------------------------------------
            // System cautions — rising-edge announcements (no off-state).
            // -------------------------------------------------------------
            case "WARN_annunFLT_CONT":
                if (value > 0.5) announcer.Announce("Flight Controls caution");
                return true;
            case "WARN_annunIRS":
                if (value > 0.5) announcer.Announce("IRS caution");
                return true;
            case "WARN_annunFUEL":
                if (value > 0.5) announcer.Announce("Fuel caution");
                return true;
            case "WARN_annunELEC":
                if (value > 0.5) announcer.Announce("Electrical caution");
                return true;
            case "WARN_annunAPU":
                if (value > 0.5) announcer.Announce("APU caution");
                return true;
            case "WARN_annunOVHT_DET":
                if (value > 0.5) announcer.Announce("Overheat/Detection caution");
                return true;
            case "WARN_annunANTI_ICE":
                if (value > 0.5) announcer.Announce("Anti-Ice caution");
                return true;
            case "WARN_annunHYD":
                if (value > 0.5) announcer.Announce("Hydraulics caution");
                return true;
            case "WARN_annunDOORS":
                if (value > 0.5) announcer.Announce("Doors caution");
                return true;
            case "WARN_annunENG":
                if (value > 0.5) announcer.Announce("Engine caution");
                return true;
            case "WARN_annunOVERHEAD":
                if (value > 0.5) announcer.Announce("Overhead caution");
                return true;
            case "WARN_annunAIR_COND":
                if (value > 0.5) announcer.Announce("Air-conditioning caution");
                return true;

            // -------------------------------------------------------------
            // FMC speeds and cruise altitude — suppress when 0 (not set).
            // -------------------------------------------------------------
            case "FMC_V1":
                if (value > 0) announcer.Announce($"V1 {(int)Math.Round(value)}");
                return true;
            case "FMC_VR":
                if (value > 0) announcer.Announce($"VR {(int)Math.Round(value)}");
                return true;
            case "FMC_V2":
                if (value > 0) announcer.Announce($"V2 {(int)Math.Round(value)}");
                return true;
            case "FMC_CruiseAlt":
                if (value > 0) announcer.Announce($"Cruise altitude {(int)Math.Round(value)} feet");
                return true;

            // -------------------------------------------------------------
            // APU selector (0=OFF / 1=ON / 2=START). The byte field can
            // briefly carry transient values outside this range during PMDG's
            // spring-back from START; suppress those instead of speaking
            // "APU state unknown", which has no useful semantics for a pilot.
            // -------------------------------------------------------------
            case "APU_Selector":
            {
                int apuPos = (int)value;
                string? apuStr = apuPos switch
                {
                    0 => "APU off",
                    1 => "APU on",
                    2 => "APU starting",
                    _ => null
                };
                if (apuStr != null) announcer.Announce(apuStr);
                return true;
            }

            // -------------------------------------------------------------
            // COM frequencies — suppress the initial baseline value and
            // skip micro-deltas, otherwise announce.
            // -------------------------------------------------------------
            case "COM1_ActiveFreq":
                if (double.IsNaN(_lastCom1Active))
                {
                    _lastCom1Active = value;
                    return true;
                }
                if (Math.Abs(value - _lastCom1Active) < 0.0001) return true;
                _lastCom1Active = value;
                announcer.Announce($"COM 1 active {value:F3}");
                return true;

            case "COM2_ActiveFreq":
                if (double.IsNaN(_lastCom2Active))
                {
                    _lastCom2Active = value;
                    return true;
                }
                if (Math.Abs(value - _lastCom2Active) < 0.0001) return true;
                _lastCom2Active = value;
                announcer.Announce($"COM 2 active {value:F3}");
                return true;

            case "COM_STANDBY_FREQUENCY_SET:1":
                if (double.IsNaN(_lastCom1Standby))
                {
                    _lastCom1Standby = value;
                    return true;
                }
                if (Math.Abs(value - _lastCom1Standby) < 0.0001) return true;
                _lastCom1Standby = value;
                announcer.Announce($"COM 1 standby {value:F3}");
                return true;

            case "COM_STANDBY_FREQUENCY_SET:2":
                if (double.IsNaN(_lastCom2Standby))
                {
                    _lastCom2Standby = value;
                    return true;
                }
                if (Math.Abs(value - _lastCom2Standby) < 0.0001) return true;
                _lastCom2Standby = value;
                announcer.Announce($"COM 2 standby {value:F3}");
                return true;

            // -------------------------------------------------------------
            // Transponder code — BCD16. Suppress initial baseline, then
            // announce changes as a 4-digit string.
            // -------------------------------------------------------------
            case "TRANSPONDER_CODE_SET":
            {
                if (double.IsNaN(_lastSquawkCode))
                {
                    _lastSquawkCode = value;
                    return true;
                }
                if (Math.Abs(value - _lastSquawkCode) < 0.5) return true;
                _lastSquawkCode = value;
                int bcd = (int)value;
                int d1 = (bcd >> 12) & 0xF;
                int d2 = (bcd >> 8) & 0xF;
                int d3 = (bcd >> 4) & 0xF;
                int d4 = bcd & 0xF;
                announcer.Announce($"Squawk {d1}{d2}{d3}{d4}");
                return true;
            }

            // -------------------------------------------------------------
            // Press counters — call buttons. Wrapping byte delta tells us
            // whether the button has been pressed since the last sample.
            // The _last != 0 guard suppresses the initial baseline.
            // -------------------------------------------------------------
            case "COMM_Attend_PressCount":
            {
                byte cur = (byte)(int)value;
                byte delta = (byte)(cur - _lastAttendPressCount);
                bool wasInitialized = _lastAttendPressCount != 0;
                _lastAttendPressCount = cur;
                if (delta > 0 && wasInitialized)
                    announcer.Announce("Attend call");
                return true;
            }
            case "COMM_GrdCall_PressCount":
            {
                byte cur = (byte)(int)value;
                byte delta = (byte)(cur - _lastGrdCallPressCount);
                bool wasInitialized = _lastGrdCallPressCount != 0;
                _lastGrdCallPressCount = cur;
                if (delta > 0 && wasInitialized)
                    announcer.Announce("Ground call");
                return true;
            }

            // -------------------------------------------------------------
            // String displays. The data manager raises VariableChanged with
            // a numeric placeholder when these change; the actual text is
            // not available here because ProcessSimVarUpdate has no
            // SimConnectManager reference. Hooking these up to spoken
            // announcements has to happen at MainForm's
            // OnPMDGVariableChanged layer (TODO — separate task).
            // -------------------------------------------------------------
            case "IRS_DisplayLeft":
            case "IRS_DisplayRight":
            case "ELEC_MeterDisplayTop":
            case "ELEC_MeterDisplayBottom":
            case "FMC_flightNumber":
                return true;

            // -------------------------------------------------------------
            // ACP receiver bitmasks (SDK lines 723 + 610-623). Decode the
            // selected-receiver bitmask into a readable string. Suppress when
            // value is 0 (no receivers selected — usually means "muted"); the
            // panel display still surfaces the raw value for query.
            // -------------------------------------------------------------
            case "COMM_ReceiverSwitches_0":
            case "COMM_ReceiverSwitches_1":
            case "COMM_ReceiverSwitches_2":
            {
                uint bits = (uint)value;
                if (bits == 0) return true;
                string acpLabel = varName switch
                {
                    "COMM_ReceiverSwitches_0" => "Captain receivers",
                    "COMM_ReceiverSwitches_1" => "First officer receivers",
                    _                         => "Overhead receivers"
                };
                announcer.Announce($"{acpLabel}: {DecodeReceiverBitmask(bits)}");
                return true;
            }

            default:
                return false;
        }
    }

    // -------------------------------------------------------------------
    // Decode `COMM_ReceiverSwitches` bitmask into a comma-separated list of
    // selected-receiver labels. Bit definitions per SDK lines 610-623.
    // -------------------------------------------------------------------
    private static string DecodeReceiverBitmask(uint bits)
    {
        var labels = new List<string>();
        if ((bits & 0x0001) != 0) labels.Add("VHF1");
        if ((bits & 0x0002) != 0) labels.Add("VHF2");
        if ((bits & 0x0004) != 0) labels.Add("VHF3");
        if ((bits & 0x0008) != 0) labels.Add("HF1");
        if ((bits & 0x0010) != 0) labels.Add("HF2");
        if ((bits & 0x0020) != 0) labels.Add("FLT");
        if ((bits & 0x0040) != 0) labels.Add("SVC");
        if ((bits & 0x0080) != 0) labels.Add("PA");
        if ((bits & 0x0100) != 0) labels.Add("NAV1");
        if ((bits & 0x0200) != 0) labels.Add("NAV2");
        if ((bits & 0x0400) != 0) labels.Add("ADF1");
        if ((bits & 0x0800) != 0) labels.Add("ADF2");
        if ((bits & 0x1000) != 0) labels.Add("MKR");
        if ((bits & 0x2000) != 0) labels.Add("SPKR");
        return labels.Count == 0 ? "none" : string.Join(", ", labels);
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // ------------------------------------------------------------------
            // MCP Readouts
            // ------------------------------------------------------------------

            case HotkeyAction.ReadHeading:
            {
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;
                int heading = (int)dm.GetFieldValue("MCP_Heading");
                string lateralMode = "";
                if ((int)dm.GetFieldValue("MCP_annunHDG_SEL") > 0) lateralMode = ", HDG SEL";
                else if ((int)dm.GetFieldValue("MCP_annunLNAV") > 0) lateralMode = ", LNAV";
                announcer.AnnounceImmediate($"Heading {heading}{lateralMode}");
                return true;
            }

            case HotkeyAction.ReadSpeed:
            {
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;
                bool isBlank = (int)dm.GetFieldValue("MCP_IASBlank") > 0;
                if (isBlank)
                {
                    announcer.AnnounceImmediate("Speed managed by FMC");
                }
                else
                {
                    float speed = (float)dm.GetFieldValue("MCP_IASMach");
                    string speedText = speed < 10f
                        ? $"M{speed:F2}"
                        : $"{(int)Math.Round(speed)} knots";
                    string speedMode = "";
                    if ((int)dm.GetFieldValue("MCP_annunLVL_CHG") > 0) speedMode = ", LVL CHG";
                    announcer.AnnounceImmediate($"{speedText}{speedMode}");
                }
                return true;
            }

            case HotkeyAction.ReadAltitude:
            {
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;
                int altitude = (int)dm.GetFieldValue("MCP_Altitude");
                string altMode = "";
                if ((int)dm.GetFieldValue("MCP_annunVNAV") > 0) altMode = ", VNAV";
                else if ((int)dm.GetFieldValue("MCP_annunLVL_CHG") > 0) altMode = ", LVL CHG";
                else if ((int)dm.GetFieldValue("MCP_annunALT_HOLD") > 0) altMode = ", ALT HOLD";
                announcer.AnnounceImmediate($"Altitude {altitude}{altMode}");
                return true;
            }

            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                // NG3 has no FPA mode — V/S only.
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;
                bool isBlank = (int)dm.GetFieldValue("MCP_VertSpeedBlank") > 0;
                if (isBlank)
                {
                    announcer.AnnounceImmediate("Vertical Speed window blank");
                }
                else
                {
                    int vs = (int)dm.GetFieldValue("MCP_VertSpeed");
                    string vsEngaged = (int)dm.GetFieldValue("MCP_annunVS") > 0 ? ", engaged" : "";
                    announcer.AnnounceImmediate($"VS {vs} feet per minute{vsEngaged}");
                }
                return true;
            }

            // ------------------------------------------------------------------
            // Fuel Readouts
            // ------------------------------------------------------------------

            case HotkeyAction.ReadFuelInfo:
            {
                // R key — total + per-tank fuel in kilograms (NG3 has no aux tank).
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;
                int leftKg   = (int)Math.Round(dm.GetFieldValue("FUEL_QtyLeft")   * 0.453592);
                int centerKg = (int)Math.Round(dm.GetFieldValue("FUEL_QtyCenter") * 0.453592);
                int rightKg  = (int)Math.Round(dm.GetFieldValue("FUEL_QtyRight")  * 0.453592);
                int totalKg  = leftKg + centerKg + rightKg;
                announcer.AnnounceImmediate(
                    $"Total {totalKg} kilograms, left {leftKg}, center {centerKg}, right {rightKg}");
                return true;
            }

            case HotkeyAction.ReadFuelQuantity:
            {
                // F key — total + per-tank fuel in pounds (NG3 has no aux tank).
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;
                int left   = (int)Math.Round(dm.GetFieldValue("FUEL_QtyLeft"));
                int center = (int)Math.Round(dm.GetFieldValue("FUEL_QtyCenter"));
                int right  = (int)Math.Round(dm.GetFieldValue("FUEL_QtyRight"));
                int total  = left + center + right;
                announcer.AnnounceImmediate(
                    $"Total {total} pounds, left {left}, center {center}, right {right}");
                return true;
            }

            // ------------------------------------------------------------------
            // Flaps and Gear
            // ------------------------------------------------------------------

            case HotkeyAction.ReadFlaps:
            {
                var dm = simConnect.PMDGDataManager;
                if (dm == null) { announcer.AnnounceImmediate("Flaps unavailable."); return true; }
                int detent = NearestFlapDetent(dm.GetFieldValue("MAIN_TEFlapsNeedle_0"));
                announcer.AnnounceImmediate(detent == 0 ? "Flaps up" : $"Flaps {detent}");
                return true;
            }

            case HotkeyAction.ReadGear:
            {
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;

                // NG3 MAIN_GearLever: 0 = UP, 1 = OFF, 2 = DOWN.
                int lever = (int)dm.GetFieldValue("MAIN_GearLever");
                string leverText = lever switch
                {
                    0 => "Gear lever up",
                    1 => "Gear lever off",
                    2 => "Gear lever down",
                    _ => $"Gear lever position {lever}"
                };

                // GEAR_locked[3] indices per the SDK's gear-panel convention:
                // 0 = Left, 1 = Nose, 2 = Right.
                bool leftLocked  = (int)dm.GetFieldValue("MAIN_annunGEAR_locked_0") > 0;
                bool noseLocked  = (int)dm.GetFieldValue("MAIN_annunGEAR_locked_1") > 0;
                bool rightLocked = (int)dm.GetFieldValue("MAIN_annunGEAR_locked_2") > 0;

                string leftText  = leftLocked  ? "left locked"  : "left unlocked";
                string noseText  = noseLocked  ? "nose locked"  : "nose unlocked";
                string rightText = rightLocked ? "right locked" : "right unlocked";

                announcer.AnnounceImmediate($"{leverText}; {leftText}, {noseText}, {rightText}");
                return true;
            }

            // ------------------------------------------------------------------
            // Altimeter (EFIS baro) — PMDG 737 has no SDK control of the baro
            // knob, so we READ the standard MSFS KOHLSMAN simvar (the set dialog
            // rotates the EFIS baro knob, EVT_EFIS_CPT_BARO, to the target). Format
            // mirrors PMDG 777 / Fenix: STD detected at 29.92 inHg, otherwise
            // dual-unit announcement "Altimeter: <hpa>, <inhg>".
            // ------------------------------------------------------------------

            case HotkeyAction.ReadAltimeter:
            {
                double? inHgRaw = simConnect.GetCachedVariableValue("ALTIMETER_SETTING");
                if (inHgRaw == null)
                {
                    announcer.AnnounceImmediate("Altimeter not available");
                    return true;
                }

                double inHg = inHgRaw.Value;

                if (Math.Abs(inHg - 29.92) < 0.005)
                {
                    announcer.AnnounceImmediate("Altimeter standard");
                    return true;
                }

                int hpa = (int)Math.Round(inHg * 33.8639);
                announcer.AnnounceImmediate($"Altimeter: {hpa}, {inHg:0.00}");
                return true;
            }

            case HotkeyAction.FCUSetBaro:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGBaroDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.SetNavRadios:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowNavRadiosDialog(simConnect, announcer, parentForm);
                return true;
            }

            // ------------------------------------------------------------------
            // Distance Readouts
            // ------------------------------------------------------------------

            case HotkeyAction.ReadDistanceToTOD:
            {
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;
                AnnounceTODFromSDK(simConnect, dm, announcer);
                return true;
            }

            case HotkeyAction.ReadDistanceToDest:
            {
                var dm = simConnect.PMDGDataManager;
                if (dm == null) return false;
                AnnounceDestFromSDK(simConnect, dm, announcer);
                return true;
            }

            // ------------------------------------------------------------------
            // Gross Weight Readouts (W key + Shift+W key, both via SimVar)
            // ------------------------------------------------------------------

            case HotkeyAction.ReadWaypointInfo:
            {
                // W key — PMDG 737 repurposes waypoint-info key for gross weight (lbs).
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
                return true;
            }

            case HotkeyAction.ReadGrossWeightKg:
            {
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;
            }

            // ------------------------------------------------------------------
            // MCP Direct-Set Input Dialogs
            // ------------------------------------------------------------------

            case HotkeyAction.FCUSetHeading:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGHeadingDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetSpeed:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGSpeedDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetAltitude:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGAltitudeDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetVS:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGVSDialog(simConnect, announcer, parentForm);
                return true;
            }

            // ------------------------------------------------------------------
            // CDU/EFB form dispatch — MainForm handles these by AircraftCode.
            // ------------------------------------------------------------------

            case HotkeyAction.ShowFenixMCDU:
                return false;

            case HotkeyAction.ShowPMDGEFB:
            case HotkeyAction.ShowPMDGEFBFirstOfficer:
                return false;

            // ------------------------------------------------------------------
            // Announcement Monitor — shared dialog with the 777.
            // ------------------------------------------------------------------

            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                if (parentForm is MainForm pf)
                {
                    pf.ShowPMDGAnnouncementMonitorDialog();
                }
                return true;

            // ------------------------------------------------------------------
            // Gemini display capture — Alt+P / Shift+N / Alt+I / Alt+E
            // ------------------------------------------------------------------

            case HotkeyAction.ReadDisplayPFD:
                ReadDisplay(Services.GeminiService.DisplayType.PFD737, "PFD", announcer, parentForm);
                return true;

            case HotkeyAction.ReadDisplayND:
                ReadDisplay(Services.GeminiService.DisplayType.ND737, "ND", announcer, parentForm);
                return true;

            case HotkeyAction.ReadDisplayISIS:
                ReadDisplay(Services.GeminiService.DisplayType.ISFD737, "ISFD", announcer, parentForm);
                return true;

            case HotkeyAction.ReadDisplayUpperECAM:
                ReadDisplay(Services.GeminiService.DisplayType.EICAS737, "EICAS", announcer, parentForm);
                return true;

            // Lower System Display (DU4) — out of scope for now (matches 777, which doesn't handle this either)
            case HotkeyAction.ReadDisplayLowerECAM:
                return false;

            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    // =========================================================================
    // FCU Request Override Methods
    // =========================================================================

    public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dm = simConnect.PMDGDataManager;
        if (dm == null) return;
        int heading = (int)dm.GetFieldValue("MCP_Heading");
        announcer.AnnounceImmediate($"Heading {heading}");
    }

    public override void RequestFCUSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dm = simConnect.PMDGDataManager;
        if (dm == null) return;
        if ((int)dm.GetFieldValue("MCP_IASBlank") > 0)
        {
            announcer.AnnounceImmediate("Speed managed by FMC");
            return;
        }
        float speed = (float)dm.GetFieldValue("MCP_IASMach");
        string speedText = speed < 10f
            ? $"M{speed:F2}"
            : $"{(int)Math.Round(speed)} knots";
        announcer.AnnounceImmediate(speedText);
    }

    public override void RequestFCUAltitude(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dm = simConnect.PMDGDataManager;
        if (dm == null) return;
        int altitude = (int)dm.GetFieldValue("MCP_Altitude");
        announcer.AnnounceImmediate($"Altitude {altitude}");
    }

    public override void RequestFCUVerticalSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dm = simConnect.PMDGDataManager;
        if (dm == null) return;
        if ((int)dm.GetFieldValue("MCP_VertSpeedBlank") > 0)
        {
            announcer.AnnounceImmediate("Vertical Speed window blank");
            return;
        }
        int vs = (int)dm.GetFieldValue("MCP_VertSpeed");
        announcer.AnnounceImmediate($"VS {vs} feet per minute");
    }

    // =========================================================================
    // MCP Direct-Set Dialog Helpers
    // =========================================================================

    private void SendPMDGMomentary(SimConnect.SimConnectManager simConnect, string eventName)
    {
        // Momentary MCP buttons commit only via LEFTSINGLE+LEFTRELEASE — a bare CDA
        // param=1 plays the click sound but the state springs back. Verified in-sim
        // 2026-05-25 (FD switch + HDG SEL committed via LEFTSINGLE+RELEASE; CDA did not).
        if (EventIds.TryGetValue(eventName, out int evId))
            _ = simConnect.SendPMDGMomentaryToggle((uint)evId, 1);
    }

    /// <summary>
    /// Format ETA as ": HH:MM:SS" given remaining distance in nautical miles
    /// and current ground speed in knots. Returns empty string at low ground
    /// speed (taxi / ground) where the estimate is meaningless.
    /// </summary>
    private static string FormatEtaFromDistance(double distanceNm, double groundSpeedKnots)
    {
        if (groundSpeedKnots < 30) return "";   // not airborne / too slow
        if (distanceNm <= 0) return "";

        double hours = distanceNm / groundSpeedKnots;
        int totalSeconds = (int)Math.Round(hours * 3600.0);
        int hh = totalSeconds / 3600;
        int mm = (totalSeconds % 3600) / 60;
        int ss = totalSeconds % 60;
        return $": {hh:D2}:{mm:D2}:{ss:D2}";
    }

    /// <summary>
    /// SDK-offset readout for distance to top of descent on the NG3.
    /// </summary>
    private static void AnnounceTODFromSDK(
        SimConnect.SimConnectManager simConnect,
        SimConnect.IPMDGDataManager dm,
        ScreenReaderAnnouncer announcer)
    {
        float dist = (float)dm.GetFieldValue("FMC_DistanceToTOD");
        if (dist < 0)
        {
            announcer.AnnounceImmediate("Top of descent not available");
            return;
        }
        if (dist < 0.1f)
        {
            announcer.AnnounceImmediate("Past top of descent");
            return;
        }
        simConnect.RequestAircraftPositionAsync(position =>
        {
            string eta = FormatEtaFromDistance(dist, position.GroundSpeedKnots);
            announcer.AnnounceImmediate($"{dist:F0} miles to top of descent{eta}");
        });
    }

    /// <summary>
    /// SDK-offset readout for distance to destination on the NG3.
    /// </summary>
    private static void AnnounceDestFromSDK(
        SimConnect.SimConnectManager simConnect,
        SimConnect.IPMDGDataManager dm,
        ScreenReaderAnnouncer announcer)
    {
        float dist = (float)dm.GetFieldValue("FMC_DistanceToDest");
        if (dist < 0)
        {
            announcer.AnnounceImmediate("Distance to destination not available");
            return;
        }
        simConnect.RequestAircraftPositionAsync(position =>
        {
            string eta = FormatEtaFromDistance(dist, position.GroundSpeedKnots);
            announcer.AnnounceImmediate($"{dist:F0} miles to destination{eta}");
        });
    }

    private void ShowPMDGHeadingDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dm = simConnect.PMDGDataManager;

        var toggles = new List<ToggleButtonDef>
        {
            new("&Heading Select", () =>
            {
                if (dm == null) return "?";
                return (int)dm.GetFieldValue("MCP_annunHDG_SEL") > 0 ? "Engaged" : "Off";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_HDG_SEL_SWITCH")),
            new("&LNAV", () =>
            {
                if (dm == null) return "?";
                return (int)dm.GetFieldValue("MCP_annunLNAV") > 0 ? "Engaged" : "Off";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_LNAV_SWITCH")),
        };

        var dialog = new ValueInputForm(
            "MCP Heading", "heading", "0-359", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 0 && val <= 359)
                    return (true, "");
                return (false, "Enter a heading between 0 and 359");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int hdg))
                {
                    if (EventIds.TryGetValue("EVT_MCP_HDG_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_HDG_SET", (uint)evId, hdg);
                }
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowPMDGSpeedDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dm = simConnect.PMDGDataManager;

        // The A/T SPEED-mode button was removed here: it only engages with the
        // autothrottle armed (so it reads "Off" and no-ops on the ground), it
        // duplicated a mode command rather than helping set a speed, and the
        // SPEED-mode state is already announced via the MCP_annunSPEED monitor.
        //
        // Speed Intervene's engaged state IS observable, but only in VNAV: there
        // SPD INTV latches the MCP speed window OPEN (MCP_IASBlank=0 → you've
        // intervened and own the speed) vs BLANK (=1 → the FMC owns the speed) —
        // verified live in the sim. Outside VNAV the window is always open and
        // intervene is not applicable, so we show no state. The Task-3 refresh
        // timer keeps the label live while the dialog is open.
        var toggles = new List<ToggleButtonDef>
        {
            new("Speed &Intervene", () =>
            {
                if (dm == null) return "";
                if ((int)dm.GetFieldValue("MCP_annunVNAV") == 0) return "";  // intervene only applies in VNAV
                return (int)dm.GetFieldValue("MCP_IASBlank") == 0 ? "Engaged" : "Off";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_SPD_INTV_SWITCH")),
        };

        var dialog = new ValueInputForm(
            "MCP Speed", "speed", "IAS: 100-399 / Mach: M0.40-M0.95", announcer,
            input =>
            {
                if (TryParseSpeedInput(input, out bool isMach, out double val))
                {
                    if (isMach && val >= 0.40 && val <= 0.95) return (true, "");
                    if (!isMach && val >= 100 && val <= 399) return (true, "");
                }
                return (false, "Enter knots (100-399) or Mach (M0.40-M0.95)");
            },
            toggles,
            input =>
            {
                if (!TryParseSpeedInput(input, out bool isMach, out double val))
                    return;
                if (isMach)
                {
                    int machVal = (int)Math.Round(val * 100);   // SDK: param = mach * 100
                    if (EventIds.TryGetValue("EVT_MCP_MACH_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_MACH_SET", (uint)evId, machVal);
                }
                else
                {
                    int iasVal = (int)Math.Round(val);
                    if (EventIds.TryGetValue("EVT_MCP_IAS_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_IAS_SET", (uint)evId, iasVal);
                }
            },
            inputEnabledCheck: () => dm == null || (int)dm.GetFieldValue("MCP_IASBlank") == 0);

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// Parse the speed dialog input. Accepts "M0.85" / "m0.85" / ".85" / "0.85"
    /// as Mach, or "250" as IAS. Anything in [0..10) is treated as Mach.
    /// </summary>
    private static bool TryParseSpeedInput(string input, out bool isMach, out double value)
    {
        isMach = false;
        value = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string trimmed = input.Trim();
        bool hasMachPrefix = trimmed.StartsWith("M", StringComparison.OrdinalIgnoreCase);
        if (hasMachPrefix) trimmed = trimmed.Substring(1);

        if (!double.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return false;

        // Either explicit M prefix, or a value < 10 (interpreted as Mach).
        isMach = hasMachPrefix || value < 10.0;
        return true;
    }

    private void ShowPMDGAltitudeDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dm = simConnect.PMDGDataManager;

        var toggles = new List<ToggleButtonDef>
        {
            new("Altitude &Intervene", () => "",
                () => SendPMDGMomentary(simConnect, "EVT_MCP_ALT_INTV_SWITCH")),
            new("Altitude &Hold", () =>
            {
                if (dm == null) return "?";
                return (int)dm.GetFieldValue("MCP_annunALT_HOLD") > 0 ? "Engaged" : "Off";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_ALT_HOLD_SWITCH")),
            new("&VNAV", () =>
            {
                if (dm == null) return "?";
                return (int)dm.GetFieldValue("MCP_annunVNAV") > 0 ? "Engaged" : "Off";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_VNAV_SWITCH")),
            new("&Level Change", () =>
            {
                if (dm == null) return "?";
                return (int)dm.GetFieldValue("MCP_annunLVL_CHG") > 0 ? "Engaged" : "Off";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_LVL_CHG_SWITCH")),
        };

        var dialog = new ValueInputForm(
            "MCP Altitude", "altitude", "0-50000 (100-foot steps)", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 0 && val <= 50000 && val % 100 == 0)
                    return (true, "");
                return (false, "Enter an altitude between 0 and 50000 in 100-foot steps");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int alt))
                {
                    if (EventIds.TryGetValue("EVT_MCP_ALT_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_ALT_SET", (uint)evId, alt);
                }
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowPMDGVSDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        // NG3 has no FPA mode — V/S only.
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dm = simConnect.PMDGDataManager;

        var toggles = new List<ToggleButtonDef>
        {
            new("&Engage V/S", () =>
            {
                if (dm == null) return "?";
                return (int)dm.GetFieldValue("MCP_annunVS") > 0 ? "Engaged" : "Off";
            }, () => SendPMDGMomentary(simConnect, "EVT_MCP_VS_SWITCH")),
        };

        var dialog = new ValueInputForm(
            "MCP Vertical Speed", "V/S", "-9000 to +9000 fpm", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= -9000 && val <= 9000)
                    return (true, "");
                return (false, "Enter V/S between -9000 and 9000 fpm");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int vs))
                {
                    // SDK: parameter = vs + 10000.
                    int encoded = vs + 10000;
                    if (EventIds.TryGetValue("EVT_MCP_VS_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_VS_SET", (uint)evId, encoded);
                }
            },
            inputEnabledCheck: () => dm != null && (int)dm.GetFieldValue("MCP_annunVS") > 0);

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // Re-entrancy guards for the async knob-stepping loops. The baro value
    // dialog and the EFIS-mins panel set both kick off a multi-second knob
    // sweep, and neither input path debounces, so a second submit mid-sweep
    // would launch a competing sweep and overstep the knob. These hold the
    // in-flight Task so a re-submit is ignored until it completes. UI-thread-only
    // access (dialog callbacks / HandleUIVariableSet + WinForms continuations),
    // so no synchronization is needed.
    private Task? _baroKnobOp;
    private Task? _minsKnobOp;

    // Self-draining suppress count for transponder-mode click-walks. Whichever
    // path starts a walk — the panel combo (HandleUIVariableSet 4b) or a First
    // Officer executor's walked-selector dispatch — sets it to the number of
    /// <summary>
    /// PMDG owns the baro and ignores absolute writes (KOHLSMAN_SET event and direct
    /// SimVar writes get re-asserted every frame), so this dialog sets the altimeter by
    /// ROTATING the captain EFIS baro knob (EVT_EFIS_CPT_BARO) to the target. Accepts either hPa
    /// (~900–1060) or inHg (~26.50–31.30); the validator picks the unit based on
    /// magnitude (>=100 = hPa, <100 = inHg) — the two ranges don't overlap so the
    /// branch is unambiguous, and magnitude-based branching avoids the de-DE
    /// locale ambiguity where "1013,5" would otherwise be misrouted to inHg.
    /// </summary>
    private void ShowPMDGBaroDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        // Parse "1013", "1013,5", or "29.92" into a single double (locale-tolerant).
        static bool TryParseBaro(string input, out double value)
        {
            return double.TryParse(
                input.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        var dialog = new ValueInputForm(
            "Altimeter", "altimeter", "900-1060 hPa or 26.50-31.30 inHg", announcer,
            input =>
            {
                if (string.IsNullOrWhiteSpace(input))
                    return (false, "Enter a value in hPa or inHg");

                if (!TryParseBaro(input, out double value))
                    return (false, "Enter a numeric value in hPa or inHg");

                // Branch on magnitude — ranges don't overlap (smallest hPa 900 > largest inHg 31.30).
                if (value >= 100)
                {
                    if (value >= 900 && value <= 1060)
                        return (true, "");
                    return (false, "Enter hPa between 900 and 1060");
                }

                if (value >= 26.50 && value <= 31.30)
                    return (true, "");
                return (false, "Enter inHg between 26.50 and 31.30");
            },
            new List<ToggleButtonDef>
            {
                // Set the altimeter to standard (29.92 inHg / 1013 hPa) via the
                // EFIS baro knob's STD push (EVT_EFIS_CPT_BARO_STD). That event is
                // a QNH<->STD toggle and there is no STD-state readback on the NG3,
                // so we guard on the current setting: only push when the altimeter
                // is meaningfully off standard (which means we're not already in
                // STD, since STD always reads 29.92). This avoids toggling STD off.
                // The push is a momentary button: NG3 commits it only with a
                // LEFTSINGLE+LEFTRELEASE pair (SendPMDGMomentaryToggle). A bare
                // dwData transmit — including 0 — plays no click and never commits.
                // The dialog's button handler announces "Standard" as confirmation.
                new ToggleButtonDef(
                    "&Standard",
                    () => "",
                    () =>
                    {
                        double? curInHg = simConnect.GetCachedVariableValue("ALTIMETER_SETTING");
                        if (curInHg != null && Math.Abs(curInHg.Value - 29.92) < 0.02)
                            return;   // already at standard pressure — don't toggle STD off
                        if (EventIds.TryGetValue("EVT_EFIS_CPT_BARO_STD", out int stdEvId))
                            _ = simConnect.SendPMDGMomentaryToggle((uint)stdEvId, 1);
                    })
            },
            input =>
            {
                if (!TryParseBaro(input, out double value)) return;

                if (_baroKnobOp is { IsCompleted: false })
                {
                    announcer.AnnounceImmediate("Still setting altimeter, please wait.");
                    return;
                }

                // PMDG owns the baro and re-asserts it every frame, so absolute
                // writes (KOHLSMAN_SET event AND a direct SimVar write) are ignored.
                // The EFIS baro is set by ROTATING the captain baro knob
                // (EVT_EFIS_CPT_BARO via TransmitClientEvent). Verified in-sim
                // 2026-05-25: RIGHTSINGLE = up, LEFTSINGLE = down, ~1 hPa per click
                // in hPa mode / 0.01 inHg per click in inHg mode. So read the current
                // baro, compute the difference in the displayed unit, and step the knob.
                double? curInHg = simConnect.GetCachedVariableValue("ALTIMETER_SETTING");
                if (curInHg == null) { announcer.AnnounceImmediate("Altimeter not available"); return; }
                if (!EventIds.TryGetValue("EVT_EFIS_CPT_BARO", out int baroEvId)) return;

                // STD-mode lockout fix: when STD is active, ALTIMETER_SETTING reads
                // ~29.92 and rotate-clicks fire behind the STD mask with no visible
                // effect. Detect "STD likely active" via the 29.92 tolerance, and if
                // the user's target value is meaningfully different from 29.92 inHg,
                // disengage STD first via EVT_EFIS_CPT_BARO_STD, then re-read the
                // baro to compute clicks against the post-STD value.
                double targetInHg = value >= 100 ? value / 33.8639 : value;
                bool likelyStdActive = Math.Abs(curInHg.Value - 29.92) < 0.005;
                bool targetIsAtStd   = Math.Abs(targetInHg - 29.92) < 0.005;
                if (likelyStdActive && !targetIsAtStd)
                {
                    if (EventIds.TryGetValue("EVT_EFIS_CPT_BARO_STD", out int stdEvId))
                    {
                        _baroKnobOp = DisengageStdThenRotateAsync(
                            simConnect, (uint)stdEvId, (uint)baroEvId, targetInHg, announcer);
                        return;
                    }
                }

                var dm = simConnect.PMDGDataManager;
                bool hpaMode = dm != null && (int)dm.GetFieldValue("EFIS_BaroSelHPA_0") == 1;

                int clicks;
                if (hpaMode)
                {
                    int curHpa = (int)Math.Round(curInHg.Value * 33.8639);
                    int tgtHpa = (int)Math.Round(value >= 100 ? value : value * 33.8639);
                    clicks = tgtHpa - curHpa;                                  // 1 hPa per click
                }
                else
                {
                    double tgtInHg = value >= 100 ? value / 33.8639 : value;
                    clicks = (int)Math.Round((tgtInHg - curInHg.Value) / 0.01); // 0.01 inHg per click
                }

                if (clicks == 0) return;
                // RIGHTSINGLE (0x80000000) = up, LEFTSINGLE (0x20000000) = down.
                uint flag = clicks > 0 ? 0x80000000u : 0x20000000u;
                _baroKnobOp = RotateBaroKnobAsync(simConnect, (uint)baroEvId, flag, Math.Min(Math.Abs(clicks), 200));
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // Rotates the captain EFIS baro knob (EVT_EFIS_CPT_BARO) `count` clicks in the
    // given direction (RIGHTSINGLE=up, LEFTSINGLE=down) via TransmitClientEvent.
    // PMDG ignores absolute baro writes, so the set dialog reaches a target by
    // stepping the knob ~1 hPa (or 0.01 inHg) per click. Verified in-sim 2026-05-25.
    private static async Task RotateBaroKnobAsync(
        SimConnect.SimConnectManager simConnect, uint eventId, uint flag, int count)
    {
        for (int i = 0; i < count; i++)
        {
            simConnect.SendPMDGEventViaTransmitWithTarget(eventId, flag);
            if (i < count - 1) await System.Threading.Tasks.Task.Delay(40);
        }
    }

    // STD-mode lockout fix: toggles STD off, awaits sim settle, re-reads the
    // post-STD altimeter, then rotates the baro knob to the user's target.
    // Called only when STD appeared active at submit time and the user's
    // target value is meaningfully different from 29.92 inHg.
    private static async Task DisengageStdThenRotateAsync(
        SimConnect.SimConnectManager simConnect, uint stdEventId, uint baroEventId,
        double targetInHg, ScreenReaderAnnouncer announcer)
    {
        // Press EVT_EFIS_CPT_BARO_STD once to toggle STD off.
        await simConnect.SendPMDGMomentaryToggle(stdEventId, 1);
        await Task.Delay(200);

        // Re-read the now post-STD altimeter setting.
        double? curInHg = simConnect.GetCachedVariableValue("ALTIMETER_SETTING");
        if (curInHg == null) { announcer.AnnounceImmediate("Altimeter not available"); return; }

        var dm = simConnect.PMDGDataManager;
        bool hpaMode = dm != null && (int)dm.GetFieldValue("EFIS_BaroSelHPA_0") == 1;

        int clicks;
        if (hpaMode)
        {
            int curHpa = (int)Math.Round(curInHg.Value * 33.8639);
            int tgtHpa = (int)Math.Round(targetInHg * 33.8639);
            clicks = tgtHpa - curHpa;
        }
        else
        {
            clicks = (int)Math.Round((targetInHg - curInHg.Value) / 0.01);
        }

        if (clicks == 0) return;
        uint flag = clicks > 0 ? 0x80000000u : 0x20000000u;
        await RotateBaroKnobAsync(simConnect, baroEventId, flag, Math.Min(Math.Abs(clicks), 200));
    }

    // Stateless mins dispatch: zero the knob via RST, then rotate up by
    // (target / stepFt) clicks. Always announces the result. Capped at
    // MINS_MAX_CLICKS_PER_SET for safety (~40 s worst case at 40ms/click).
    private static async Task ResetThenRotateMinsAsync(
        SimConnect.SimConnectManager simConnect,
        string rstEventName, uint rstEventId,
        uint rotEventId,
        int targetFt, int stepFt, bool baroMode,
        ScreenReaderAnnouncer announcer)
    {
        // Step 1: Zero the knob via RST.
        simConnect.SendPMDGEvent(rstEventName, rstEventId, 1);
        await Task.Delay(200);   // sim settle

        // Step 2: Rotate up by N clicks.
        int clicks = targetFt > 0
            ? Math.Min(targetFt / stepFt, MINS_MAX_CLICKS_PER_SET)
            : 0;
        const uint MOUSE_FLAG_RIGHTSINGLE = 0x80000000u;
        for (int i = 0; i < clicks; i++)
        {
            simConnect.SendPMDGEventViaTransmitWithTarget(rotEventId, MOUSE_FLAG_RIGHTSINGLE);
            if (i < clicks - 1) await Task.Delay(40);
        }

        // Step 3: Announce result (verbalize the actual stepped-to value, not the
        // raw target — they can differ when target isn't a multiple of stepFt).
        int reached = clicks * stepFt;
        string label = baroMode ? "Decision altitude" : "Minimums";
        announcer.AnnounceImmediate($"{label} set to {reached} feet.");
    }

    /// <summary>
    /// Opens the NAV Radios dialog (Ctrl+N). The PMDG 737 honors the standard
    /// SimConnect radio events, so frequencies/courses are set directly via
    /// NAV1/2_RADIO_SET_HZ (active frequency, Hz) and VOR1/2_SET (course/OBS,
    /// degrees) — verified in-sim. The dialog is pre-filled with the current
    /// values via RequestNavRadioInfo (one-shot); that callback can arrive off
    /// the UI thread, so the form is opened through parentForm.BeginInvoke.
    /// </summary>
    private void ShowNavRadiosDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        simConnect.RequestNavRadioInfo(navData =>
        {
            void Open()
            {
                var form = new NavRadiosForm(
                    announcer,
                    navData.Nav1Freq, (int)Math.Round(navData.Nav1Obs),
                    navData.Nav2Freq, (int)Math.Round(navData.Nav2Obs),
                    settings =>
                    {
                        // Active frequency in whole Hz (the form already snapped to a 50 kHz channel).
                        simConnect.SendEvent("NAV1_RADIO_SET_HZ", (uint)Math.Round(settings.Nav1FreqMHz * 1_000_000.0));
                        simConnect.SendEvent("VOR1_SET", (uint)settings.Nav1Course);
                        simConnect.SendEvent("NAV2_RADIO_SET_HZ", (uint)Math.Round(settings.Nav2FreqMHz * 1_000_000.0));
                        simConnect.SendEvent("VOR2_SET", (uint)settings.Nav2Course);
                        announcer.AnnounceImmediate(
                            $"NAV 1 {settings.Nav1FreqMHz:0.00}, course {settings.Nav1Course}. " +
                            $"NAV 2 {settings.Nav2FreqMHz:0.00}, course {settings.Nav2Course}.");
                    });
                form.Show(parentForm);
            }

            if (parentForm.IsDisposed) return;
            if (parentForm.InvokeRequired) parentForm.BeginInvoke((Action)Open);
            else Open();
        });
    }
}

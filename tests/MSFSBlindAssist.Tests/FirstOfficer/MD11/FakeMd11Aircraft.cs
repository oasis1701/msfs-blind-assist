using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.MD11;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// A fake TFDi MD-11 implementing the event semantics decoded from md11host.wasm: latching
/// buttons toggle their own var on DOWN; single-event switches toggle; stepped controls INC/DEC
/// clamped, with EVAC and GPWS refusing an INC above position 1 while their cover is CLOSED;
/// momentary buttons toggle a hidden system state read through lamps (which read 0 unpowered);
/// packs are inert in Air AUTO; ignition XORs bits; the hydraulic test runs only in AUTO.
/// Time advances only through DelayAsync/HoldAsync.
/// </summary>
internal sealed class FakeMd11Aircraft : IMd11FoTransport, IMd11FoFlightState
{
    public bool Ready { get; set; } = true;
    public bool Powered { get; set; } = true;
    public bool GpuAvailable { get; set; } = true;
    public bool? GroundState { get; set; } = true;
    public double[] N2 { get; } = new double[4];
    public HashSet<int> Inverted { get; } = new();
    public int FlapExtendEvent { get; set; } = 77830;

    /// <summary>Keys whose reads come back null (a delivery that never arrives).</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);
    /// <summary>The DC power gate reads UNPOWERED while the lamps still read their real values (its own lamp not yet delivered).</summary>
    public bool GateUnpowered { get; set; }
    /// <summary>The gear lever's travel speed in units per second; 0 = the click lands at the far end at once.</summary>
    public double GearRatePerSec { get; set; }
    /// <summary>A running hydraulic test was cut short (leaving AUTO, a fuel switch ON, or an AUX pump press).</summary>
    public bool HydTestAborted { get; private set; }
    /// <summary>The clock at the last AUX pump 1 press, or -1.</summary>
    public long AuxPump1PressedAt { get; private set; } = -1;
    /// <summary>AUTO FLIGHT presses change nothing (a press the aircraft refuses).</summary>
    public bool AutoFlightInert { get; set; }

    public Dictionary<string, double> Vars { get; } = new(StringComparer.Ordinal);
    public List<int> Events { get; } = new();
    public List<(int Down, int Up, int Ms)> Holds { get; } = new();
    public List<(string Var, double Value)> ExternalWrites { get; } = new();
    public List<string> Noted { get; } = new();
    public List<int> MuteRequests { get; } = new();
    public Dictionary<string, double> MaxSeen { get; } = new(StringComparer.Ordinal);
    public long Clock { get; private set; }

    // Hidden system state
    private bool _extConnected;
    private bool _auxPump1;
    private int _ignitionBits;
    private bool _apuPowerRequested;
    private long _apuStartedAt = -1;
    private long _hydTestEndsAt = -1;
    private int _flapIndex;
    private double _gearCommanded = 25;
    private static readonly double[] FlapRng = { 0, 20, 46.91, 70, 82, 100 };

    // Readable keys that are NOT lamps and are not in Vars until set are unread (null).
    private static readonly HashSet<string> NeverReadable = new(StringComparer.Ordinal)
    {
        "MD11_OVHD_ELEC_EXT_PWR_BT", "MD11_OVHD_ELEC_APU_PWR_BT", "MD11_PED_WXR_OFF_BT",
        "MD11_OVHD_LTS_NAV_BT", "MD11_OVHD_LTS_BCN_BT", "MD11_OVHD_LTS_HI_INT_BT",
    };

    public FakeMd11Aircraft()
    {
        // A typical "Ready to Start" cockpit, cover-closed guards, systems in AUTO.
        foreach (var k in new[]
        {
            "MD11_OVHD_ELEC_BATT_BT", "MD11_OVHD_ELEC_SYSTEM_SEL_BT", "MD11_OVHD_FUEL_SYSTEM_SEL_BT",
            "MD11_OVHD_HYD_SYSTEM_SEL_BT", "MD11_OVHD_PNEU_SYSTEM_SEL_BT", "MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_BT",
            "MD11_OVHD_PNEU_APU_BLEED_BT", "MD11_OVHD_WNDSHLD_AICE_L_BT", "MD11_OVHD_WNDSHLD_AICE_R_BT",
            "MD11_OVHD_WNDSHLD_AICE_BT", "MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", "MD11_OVHD_ANNUNLT_BRTDIM_BT",
            "MD11_OVHD_IRS_1_KB", "MD11_OVHD_IRS_2_KB", "MD11_OVHD_IRS_3_KB",
            "MD11_THR_L_FUEL_SW", "MD11_THR_C_FUEL_SW", "MD11_THR_R_FUEL_SW",
            "MD11_THR_L_START_SW", "MD11_THR_C_START_SW", "MD11_THR_R_START_SW",
            "MD11_THR_PARK_LVR", "MD11_PED_XPNDR_ALT_RPTG_KB",
            "MD11_OVHD_ELEC_EMER_PWR_KB", "MD11_OVHD_LTS_EMER_SW", "MD11_OVHD_LTS_NO_SMOKE_SW",
            "MD11_OVHD_LTS_SEAT_BELTS_SW", "MD11_OVHD_LTS_LDG_L_SW", "MD11_OVHD_LTS_LDG_R_SW",
            "MD11_OVHD_LTS_NOSE_SW", "MD11_OVHD_PNEU_FWD_CARGO_TEMP", "MD11_OVHD_PNEU_AFT_CARGO_TEMP",
            "MD11_AOVHD_EVAC_SW", "MD11_AOVHD_GPWS_SW", "MD11_CTR_AUTOBRAKE_SW", "MD11_PED_XPNDR_MODE_KB",
            "MD11_AOVHD_EVAC_GRD", "MD11_AOVHD_GPWS_GRD", "MD11_SPDBRK_ARM", "MD11_SPDBRK_HANDLE",
            "MD11_APU_STATE", "MD11_OVHD_PNEU_ECON_OFF_LT", "MD11_AP_STATE", "MD11_ATS_STATE",
            "MD11_OVHD_PNEU_PACK_1_OFF_LT", "MD11_OVHD_PNEU_PACK_2_OFF_LT", "MD11_OVHD_PNEU_PACK_3_OFF_LT",
        })
            Vars[k] = 0;
        Vars["MD11_OVHD_AICE_SYSTEM_SEL_BT"] = 1;   // MANUAL: TFDi forces it without the autoAntiIce option
        Vars["MD11_AOVHD_GPWS_SW"] = 1;             // Normal
        Vars["MD11_CTR_AUTOBRAKE_SW"] = 1;          // Off
        Vars["MD11_MIP_GEAR_SW"] = 25;              // down
        Vars["MD11_FLAP_LATCH"] = 0;
        Vars["MD11_DIALAFLAP_WHEEL_RNG"] = 33;
        Vars["MD11_FO_WXR_OFF"] = 1;
        Vars["MD11_CAP_ALTIMETER"] = 29.92;
        Vars["MD11_FO_ALTIMETER"] = 1013.0;
        Vars["MD11_STBY_ALTIMETER"] = 29.92;
        _ignitionBits = 0;
        RecomputeLamps();
    }

    public double Get(string key) => Vars.TryGetValue(key, out var v) ? v : double.NaN;

    public void Set(string key, double v)
    {
        Vars[key] = v;
        MaxSeen[key] = MaxSeen.TryGetValue(key, out var m) ? Math.Max(m, v) : v;
        RecomputeLamps();
    }

    // ---- IMd11FoFlightState ----
    public double EngineN2(int engine) => engine is >= 1 and <= 3 ? N2[engine] : double.NaN;
    bool? IMd11FoFlightState.OnGround => GroundState;

    // ---- IMd11FoTransport ----
    public int PendingWrites => 0;
    public long NowMs => Clock;
    public bool IsPowered => Powered && !GateUnpowered;

    public bool Fire(int eventId)
    {
        if (!Ready) return false;
        Events.Add(eventId);
        Apply(eventId);
        return true;
    }

    public bool Press(int downId, int upId)
    {
        if (!Ready) return false;
        if (downId > 0) { Events.Add(downId); Apply(downId); }
        if (upId > 0) { Events.Add(upId); }
        return true;
    }

    public Task<bool> HoldAsync(int downId, int upId, int holdMs)
    {
        if (!Ready) return Task.FromResult(false);
        Events.Add(downId);
        Holds.Add((downId, upId, holdMs));
        Clock += holdMs;
        Events.Add(upId);
        return Task.FromResult(true);
    }

    public bool WriteExternal(string var, double value)
    {
        if (!Ready) return false;
        ExternalWrites.Add((var, value));
        switch (var)
        {
            case "MD11_DIALAFLAP_WHEEL_RNG": Set(var, value); break;
            case "MD11_EXTCTL_CAP_BARO": Set("MD11_CAP_ALTIMETER", value); break;
            case "MD11_EXTCTL_FO_BARO": Set("MD11_FO_ALTIMETER", value); break;
            case "MD11_EXTCTL_STBY_BARO": Set("MD11_STBY_ALTIMETER", value); break;
        }
        return true;
    }

    public Task<double?> ReadFreshAsync(string key, int timeoutMs) => Task.FromResult(Read(key));
    public double? ReadCached(string key) => Read(key);

    public void NoteActuation(string nodeId) => Noted.Add(nodeId);
    public void MuteLampSpeech(int ms) => MuteRequests.Add(ms);

    public Task DelayAsync(int ms)
    {
        Clock += ms;
        Tick();
        return Task.CompletedTask;
    }

    // ---- semantics ----
    private double? Read(string key)
    {
        if (NeverReadable.Contains(key) || Unreadable.Contains(key)) return null;
        if (key.EndsWith("_LT", StringComparison.Ordinal) || key == "MD11_LTS_DOME")
            return Powered ? (Vars.TryGetValue(key, out var lamp) ? lamp : 0) : 0;
        return Vars.TryGetValue(key, out var v) ? v : null;
    }

    private void Toggle(string key) => Set(key, Get(key) > 0.5 ? 0 : 1);

    private void Step(string key, int delta, int min, int max, string? guard = null)
    {
        double v = Get(key);
        if (delta > 0 && guard != null && v >= 1 && Get(guard) < 0.5) return;   // INC gated by the closed cover
        Set(key, Math.Clamp(v + delta, min, max));
    }

    private int Dir(int raiseId, int ev) => (ev == raiseId) != Inverted.Contains(raiseId) ? 1 : -1;

    private void Apply(int ev)
    {
        switch (ev)
        {
            // latches
            case 90150: Toggle("MD11_OVHD_ELEC_BATT_BT"); break;
            case 90157: Toggle("MD11_OVHD_ELEC_SYSTEM_SEL_BT"); break;
            case 90212: Toggle("MD11_OVHD_FUEL_SYSTEM_SEL_BT"); break;
            case 90177: Toggle("MD11_OVHD_HYD_SYSTEM_SEL_BT"); if (Get("MD11_OVHD_HYD_SYSTEM_SEL_BT") > 0.5) AbortHydTest(); break;
            case 90295: Toggle("MD11_OVHD_PNEU_SYSTEM_SEL_BT"); break;
            case 90328: Toggle("MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_BT"); break;
            case 90313: Toggle("MD11_OVHD_PNEU_APU_BLEED_BT"); break;
            case 90424: Toggle("MD11_OVHD_WNDSHLD_AICE_L_BT"); break;
            case 90428: Toggle("MD11_OVHD_WNDSHLD_AICE_R_BT"); break;
            case 90426: Toggle("MD11_OVHD_WNDSHLD_AICE_BT"); break;
            case 90430: Toggle("MD11_OVHD_WNDSHLD_AICE_DEFOG_BT"); break;
            case 90406: Toggle("MD11_OVHD_ANNUNLT_BRTDIM_BT"); break;
            // toggles
            case 90112: Toggle("MD11_OVHD_IRS_1_KB"); break;
            case 90114: Toggle("MD11_OVHD_IRS_2_KB"); break;
            case 90116: Toggle("MD11_OVHD_IRS_3_KB"); break;
            case 77834: FuelSwitch("MD11_THR_L_FUEL_SW"); break;
            case 77835: FuelSwitch("MD11_THR_C_FUEL_SW"); break;
            case 77836: FuelSwitch("MD11_THR_R_FUEL_SW"); break;
            case 77837: Toggle("MD11_THR_L_START_SW"); break;
            case 77838: Toggle("MD11_THR_C_START_SW"); break;
            case 77839: Toggle("MD11_THR_R_START_SW"); break;
            case 77848: Toggle("MD11_THR_PARK_LVR"); break;
            case 69854: Toggle("MD11_PED_XPNDR_ALT_RPTG_KB"); break;
            case 94976:
                // LandingGear::MoveHandle toggles the COMMANDED position; the var is the travel.
                if (GearRatePerSec <= 0) { _gearCommanded = Get("MD11_MIP_GEAR_SW") >= 20 ? 0 : 25; Set("MD11_MIP_GEAR_SW", _gearCommanded); }
                else _gearCommanded = _gearCommanded >= 20 ? 0 : 25;
                break;
            // stepped (raise id first)
            case 90160: case 90159: Step("MD11_OVHD_ELEC_EMER_PWR_KB", Dir(90160, ev), 0, 2); break;
            case 90243: case 90242: Step("MD11_OVHD_LTS_EMER_SW", Dir(90243, ev), 0, 2); break;
            case 90247: case 90246: Step("MD11_OVHD_LTS_NO_SMOKE_SW", Dir(90247, ev), 0, 2); break;
            case 90249: case 90248: Step("MD11_OVHD_LTS_SEAT_BELTS_SW", Dir(90249, ev), 0, 2); break;
            case 90258: case 90257: Step("MD11_OVHD_LTS_LDG_L_SW", Dir(90258, ev), 0, 2); break;
            case 90260: case 90259: Step("MD11_OVHD_LTS_LDG_R_SW", Dir(90260, ev), 0, 2); break;
            case 90262: case 90261: Step("MD11_OVHD_LTS_NOSE_SW", Dir(90262, ev), 0, 2); break;
            case 90276: case 90275: Step("MD11_OVHD_PNEU_FWD_CARGO_TEMP", Dir(90276, ev), 0, 2); break;
            case 90278: case 90277: Step("MD11_OVHD_PNEU_AFT_CARGO_TEMP", Dir(90278, ev), 0, 6); break;
            case 73774: case 73773: Step("MD11_AOVHD_EVAC_SW", Dir(73774, ev), 0, 2, "MD11_AOVHD_EVAC_GRD"); break;
            case 73770: case 73769: Step("MD11_AOVHD_GPWS_SW", Dir(73770, ev), 0, 2, "MD11_AOVHD_GPWS_GRD"); break;
            case 82212: case 82211: Step("MD11_CTR_AUTOBRAKE_SW", Dir(82212, ev), 0, 4); break;
            case 69877: case 69876: Step("MD11_PED_XPNDR_MODE_KB", Dir(69877, ev), 0, 3); break;
            // guard covers (the FO must never send these)
            case 73775: Toggle("MD11_AOVHD_EVAC_GRD"); break;
            case 73771: Toggle("MD11_AOVHD_GPWS_GRD"); break;
            // momentary system toggles
            case 90142: if (GpuAvailable || _extConnected) { _extConnected = !_extConnected; Powered = Powered || _extConnected; } RecomputeLamps(); break;
            case 90144:
                if (Get("MD11_APU_STATE") < 0.5) { Set("MD11_APU_STATE", 1); _apuStartedAt = Clock; _apuPowerRequested = true; }
                else { _apuPowerRequested = !_apuPowerRequested; if (!_apuPowerRequested) Set("MD11_APU_STATE", 3); }
                RecomputeLamps();
                break;
            case 90173: AbortHydTest(); AuxPump1PressedAt = Clock; _auxPump1 = !_auxPump1; RecomputeLamps(); break;
            case 90350: _ignitionBits ^= 1; RecomputeLamps(); break;
            case 90352: _ignitionBits ^= 2; RecomputeLamps(); break;
            case 90354: _ignitionBits ^= 4; RecomputeLamps(); break;
            case 90289: if (Get("MD11_OVHD_PNEU_SYSTEM_SEL_BT") > 0.5) ToggleLamp("MD11_OVHD_PNEU_PACK_1_OFF_LT"); break;
            case 90291: if (Get("MD11_OVHD_PNEU_SYSTEM_SEL_BT") > 0.5) ToggleLamp("MD11_OVHD_PNEU_PACK_2_OFF_LT"); break;
            case 90293: if (Get("MD11_OVHD_PNEU_SYSTEM_SEL_BT") > 0.5) ToggleLamp("MD11_OVHD_PNEU_PACK_3_OFF_LT"); break;
            case 90297: ToggleLamp("MD11_OVHD_PNEU_ECON_OFF_LT"); break;
            case 90267: ToggleLamp("MD11_OVHD_LTS_NAV_LT"); break;
            case 90271: ToggleLamp("MD11_OVHD_LTS_BCN_LT"); break;
            case 90273: ToggleLamp("MD11_OVHD_LTS_HI_INT_LT"); break;
            case 90269: ToggleLamp("MD11_OVHD_LTS_LOGO_ON_LT"); break;
            case 90263: ToggleLamp("MD11_OVHD_LTS_RWY_TURNOFF_L_LT"); break;
            case 90265: ToggleLamp("MD11_OVHD_LTS_RWY_TURNOFF_R_LT"); break;
            case 90236: ToggleLamp("MD11_LTS_DOME"); break;
            case 90414: AntiIce("MD11_OVHD_AICE_ENG1_ON_LT"); break;
            case 90416: AntiIce("MD11_OVHD_AICE_ENG2_ON_LT"); break;
            case 90418: AntiIce("MD11_OVHD_AICE_ENG3_ON_LT"); break;
            case 90420: AntiIce("MD11_OVHD_AICE_WING_ON_LT"); break;
            case 90422: AntiIce("MD11_OVHD_AICE_TAIL_ON_LT"); break;
            // one-shot presses
            case 90191:
                if (Get("MD11_OVHD_HYD_SYSTEM_SEL_BT") < 0.5 && N2[1] < 5 && N2[2] < 5 && N2[3] < 5)
                { Set("MD11_OVHD_HYD_TEST_LT", 1); _hydTestEndsAt = Clock + 100_000; }
                break;
            case 69885: Set("MD11_FO_WXR_OFF", 0); break;   // WXR TEST selected
            case 69883: Set("MD11_FO_WXR_OFF", 1); break;   // WXR OFF selected
            // AUTO FLIGHT engages "both ATs and one AP"; the autopilot is refused below 100 ft, so on
            // the ground only the autothrottle comes on. With an autopilot on it swaps AP 1 and AP 2.
            case 86094:
                if (AutoFlightInert) break;
                Set("MD11_ATS_STATE", 1);
                if (GroundState != true) Set("MD11_AP_STATE", Get("MD11_AP_STATE") switch { 1 => 2, 2 => 1, 3 => 3, _ => 1 });
                break;
            // NAV and PROF arm a mode the aircraft never exports; a second press leaves it armed.
            case 86090: case 86096: break;
            // Spoiler lever click — FlightControls::Create's lambda: SetSpoilerArm(pull == 0).
            // Keys follow the app: MD11_SPDBRK_ARM is the pull, MD11_SPDBRK_HANDLE the travel.
            case 77829:
            {
                double pull = Get("MD11_SPDBRK_ARM"), travel = Get("MD11_SPDBRK_HANDLE");
                if (pull == 0) { if (travel == 0) Set("MD11_SPDBRK_ARM", 1); }   // arm: RET only
                else if (pull == 2) Set("MD11_SPDBRK_ARM", 1);                   // the spring does the rest
                else if (travel == 0) Set("MD11_SPDBRK_ARM", 0);                 // disarm
                break;
            }
            // flap handle wheel
            case 77830: case 77831:
                _flapIndex = Math.Clamp(_flapIndex + (ev == FlapExtendEvent ? 1 : -1), 0, 5);
                Set("MD11_FLAP_LATCH", FlapRng[_flapIndex]);
                break;
        }
    }

    private void ToggleLamp(string lamp) => Set(lamp, Get(lamp) > 0.5 ? 0 : 1);

    /// <summary>IceProtection: a press toggles the system only in MANUAL; in AUTO it flashes MANUAL and does nothing.</summary>
    private void AntiIce(string lamp)
    {
        if (Get("MD11_OVHD_AICE_SYSTEM_SEL_BT") > 0.5) ToggleLamp(lamp);
    }

    private void FuelSwitch(string key)
    {
        Toggle(key);
        if (Get(key) > 0.5) AbortHydTest();                 // a fuel switch ON ends the hydraulic test
    }

    private void AbortHydTest()
    {
        if (_hydTestEndsAt < 0) return;
        _hydTestEndsAt = -1;
        HydTestAborted = true;
        Set("MD11_OVHD_HYD_TEST_LT", 0);
    }

    private long _lastTick;

    private void Tick()
    {
        long dt = Clock - _lastTick;
        _lastTick = Clock;
        // Aircraft::PreUpdate's lever spring: with no hand on the lever, a pull below 2 and the
        // travel above RET, the travel runs back at 50 units/s; arriving at RET zeroes the pull.
        double pull = Get("MD11_SPDBRK_ARM"), travel = Get("MD11_SPDBRK_HANDLE");
        if (pull < 1.5 && travel > 0)
        {
            double next = Math.Max(0, travel - 50.0 * dt / 1000.0);
            Set("MD11_SPDBRK_HANDLE", next);
            if (next == 0 && pull != 0) Set("MD11_SPDBRK_ARM", 0);
        }
        if (GearRatePerSec > 0)
        {
            double g = Get("MD11_MIP_GEAR_SW");
            if (g != _gearCommanded)
            {
                double step = GearRatePerSec * dt / 1000.0;
                Set("MD11_MIP_GEAR_SW", g < _gearCommanded ? Math.Min(_gearCommanded, g + step) : Math.Max(_gearCommanded, g - step));
            }
        }
        if (_apuStartedAt >= 0 && Get("MD11_APU_STATE") is 1 && Clock - _apuStartedAt >= 40_000)
            Set("MD11_APU_STATE", 2);
        if (_hydTestEndsAt >= 0 && Clock >= _hydTestEndsAt)
        { Set("MD11_OVHD_HYD_TEST_LT", 0); _hydTestEndsAt = -1; }
        if (Get("MD11_APU_STATE") is 3) Set("MD11_APU_STATE", 0);
        RecomputeLamps();
    }

    private void RecomputeLamps()
    {
        Vars["MD11_OVHD_ELEC_EXT_PWR_ON_LT"] = _extConnected ? 1 : 0;
        Vars["MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT"] = GpuAvailable && !_extConnected ? 1 : 0;
        Vars["MD11_OVHD_ELEC_APU_PWR_ON_LT"] = Get("MD11_APU_STATE") is 2 && _apuPowerRequested ? 1 : 0;
        Vars["MD11_OVHD_HYD_AUX_PUMP_1_ON_LT"] = _auxPump1 ? 1 : 0;
        Vars["MD11_OVHD_ENG_A_LT"] = (_ignitionBits & 1) != 0 ? 1 : 0;
        Vars["MD11_OVHD_ENG_B_LT"] = (_ignitionBits & 2) != 0 ? 2 : 0;
        Vars["MD11_OVHD_ENG_IGN_OVRD_LT"] = (_ignitionBits & 4) != 0 ? 4 : 0;
        Vars["MD11_OVHD_ENG_IGN_OFF_LT"] = _ignitionBits == 0 ? 1 : 0;
    }

    // Convenience for tests: put the hidden state somewhere.
    public void ConnectExternalPower() { _extConnected = true; Powered = true; RecomputeLamps(); }
    public void StartApuRunningWithPower() { Set("MD11_APU_STATE", 2); _apuPowerRequested = true; RecomputeLamps(); }
    public void SelectIgnition(int bits) { _ignitionBits = bits; RecomputeLamps(); }
    /// <summary>The pilot's own click: the lever starts travelling toward <paramref name="down"/> on the next tick.</summary>
    public void PilotCommandsGear(bool down) => _gearCommanded = down ? 25 : 0;
    /// <summary>After touchdown: the pull locked aft at 2 and the lever at the ground-spoiler travel.</summary>
    public void DeployGroundSpoilers() { Set("MD11_SPDBRK_ARM", 2); Set("MD11_SPDBRK_HANDLE", 50); }
    public int FlapIndex { get => _flapIndex; set { _flapIndex = value; Set("MD11_FLAP_LATCH", FlapRng[value]); } }
}

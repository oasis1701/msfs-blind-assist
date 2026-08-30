using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.HWA330;

/// <summary>
/// Drives Headwind A330 controls for the First Officer by delegating to
/// <see cref="HeadwindA330Definition.ApplyUIVariable"/> — the panels' verified
/// write path — plus a small set of pseudo-keys for non-combo actions (FCU
/// managed pushes, baro STD/QNH, AP engage, cabin call, ECAM page select). A
/// suppressed announcer keeps the FO's own step callouts the single voice.
/// </summary>
public sealed class HwA330ActionExecutor : IFoActionExecutor
{
    private const int WriteSpacingMs = 200;

    // Dotted FBW input events this executor fires through FireFCUButton. The definition's
    // registration is what maps a dotted name onto a transport, so an event the definition
    // does not register cannot reach the aircraft by any path — the sim swallows it silently.
    // These are constants rather than inline literals so FiredEventNames cannot drift out of
    // sync with the dispatch switch below. Pinned by FoFbwEventContractTests.
    private const string EvtSpdPush = "A32NX.FCU_SPD_PUSH";
    private const string EvtHdgPush = "A32NX.FCU_HDG_PUSH";
    private const string EvtAltPush = "A32NX.FCU_ALT_PUSH";
    private const string EvtAp1Push = "A32NX.FCU_AP_1_PUSH";
    private const string EvtBaroLPull = "A32NX.FCU_EFIS_L_BARO_PULL";
    private const string EvtBaroLPush = "A32NX.FCU_EFIS_L_BARO_PUSH";
    private const string EvtBaroRPull = "A32NX.FCU_EFIS_R_BARO_PULL";
    private const string EvtBaroRPush = "A32NX.FCU_EFIS_R_BARO_PUSH";

    /// <summary>Every dotted FBW input event this executor can fire.</summary>
    public static readonly string[] FiredEventNames =
    {
        EvtSpdPush, EvtHdgPush, EvtAltPush, EvtAp1Push,
        EvtBaroLPull, EvtBaroLPush, EvtBaroRPull, EvtBaroRPush,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SimConnectManager? _sc;
    private HeadwindA330Definition? _def;
    private ScreenReaderAnnouncer? _announcer;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;
    public void SetDefinition(HeadwindA330Definition def) => _def = def;
    // The REAL app announcer (ScreenReaderAnnouncer is heavyweight — inits NVDA/Tolk —
    // and has no parameterless ctor; NEVER construct a second instance). Writes wrap
    // ApplyUIVariable in a Suppressed toggle (the HS787/monitor pattern) so any
    // set-path Announce() the def makes is dropped and the FO's step callouts stay
    // the single voice.
    public void SetAnnouncer(ScreenReaderAnnouncer a) => _announcer = a;

    public bool IsAvailable => _sc is { IsConnected: true } && _def != null && _announcer != null;

    /// <summary>
    /// IFoActionExecutor — acquire+release the serialize gate. SemaphoreSlim async
    /// waiters queue FIFO, so by the time this acquires, every dispatch queued before
    /// the call has fully completed (ChecklistManager's post-tick grace re-stamp).
    /// </summary>
    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                // Held self-completing fire tests (Fenix/A380 FIRE_TEST_* precedent) — must not
                // reach the generic dispatch (its SetLVar fallback would write a bogus L:var).
                if (step.EventName == "FIRE_TEST_APU" || step.EventName == "FIRE_TEST_ENG1"
                    || step.EventName == "FIRE_TEST_ENG2")
                    return await FireTestAsync(step.EventName);
                // Held self-completing recorder/config tests — same rationale as the fire
                // tests above (must not fall through to the generic SetLVar dispatch).
                if (step.EventName == "CVR_TEST") return await CvrTestAsync();
                if (step.EventName == "TO_CONFIG_TEST") return await TakeoffConfigTestAsync();
                return await DispatchAsync(step.EventName, step.TargetValue);

            case FlowStepActionType.SetSwitchMultiple:
                await _gate.WaitAsync();
                try
                {
                    bool ok = true;
                    foreach (var (ev, tv) in step.MultiActions)
                        ok &= await DispatchCoreAsync(ev, tv);
                    return ok;
                }
                finally { _gate.Release(); }

            default:
                return false;
        }
    }

    private async Task<bool> DispatchAsync(string name, int? target)
    {
        await _gate.WaitAsync();
        try { return await DispatchCoreAsync(name, target); }
        finally { _gate.Release(); }
    }

    // Must be called inside _gate.
    private async Task<bool> DispatchCoreAsync(string name, int? target)
    {
        if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
        await PaceAsync();
        bool ok = name switch
        {
            "BARO_STD"          => FireBaro(std: true),   // A320: PULL = STD
            "BARO_QNH"          => FireBaro(std: false),  // A320: PUSH = QNH
            "FCU_PUSH_SPEED"    => FireFcu(EvtSpdPush),
            "FCU_PUSH_HEADING"  => FireFcu(EvtHdgPush),
            "FCU_PUSH_ALT"      => FireFcu(EvtAltPush),
            "AP1_ENGAGE"        => FireFcu(EvtAp1Push),
            "CABIN_CALL_ALL"    => await FireCabinCallAsync(),
            // Seat-belt sign. A pseudo-key, NOT the stock CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE
            // event: that key has no HandleUIVariableSet branch, so it would fall through to
            // ApplySilent's SetLVar fallback, write a bogus L:var of that name and report
            // success. See SetSeatbeltSignCoreAsync for why a bare toggle cannot work here.
            SeatbeltSignKey     => await SetSeatbeltSignCoreAsync((target ?? 1) != 0),
            _ when name.StartsWith("ECAM_PAGE_") => await FireEcamPageAsync(name),
            _                   => ApplySilent(name, target ?? 1),
        };
        _lastWriteUtc = DateTime.UtcNow;
        return ok;
    }

    // ApplyUIVariable under a Suppressed wrap: the def's internal Announce() calls are
    // dropped; prior Suppressed state restored (don't clobber the startup grace period).
    private bool ApplySilent(string varKey, double value)
    {
        bool prior = _announcer!.Suppressed;
        _announcer.Suppressed = true;
        try
        {
            if (_def!.ApplyUIVariable(varKey, value, _sc!, _announcer)) return true;
            // Mirror MainForm's combo-set fallback: a key HandleUIVariableSet doesn't
            // claim (returns false) is written as a plain L:var via SetLVar (calc-path
            // routed when MobiFlight is connected). Without this, a key with no def
            // branch and no catch-all prefix would silently no-op through ApplyUIVariable alone.
            _sc!.SetLVar(varKey, value);
            return true;
        }
        finally { _announcer.Suppressed = prior; }
    }

    private bool FireBaro(bool std)
    {
        // A320 PULL = STD, PUSH = QNH (opposite the A380). The _EIS_BARO_IS_STD L:vars are dead.
        string l = std ? EvtBaroLPull : EvtBaroLPush;
        string r = std ? EvtBaroRPull : EvtBaroRPush;
        _def!.FireFCUButton(l, _sc!, _announcer!, readback: false);
        _def!.FireFCUButton(r, _sc!, _announcer!, readback: false);
        return true;
    }

    private bool FireFcu(string evt)
    {
        _def!.FireFCUButton(evt, _sc!, _announcer!, readback: false);
        return true;
    }

    // Cabin call: release pulse. A stuck 1 = endless horn; write 1, delay, write 0
    // as two separate writes. The release is in a finally so an interrupted hold
    // (exception during the delay) can NEVER leave the call horn latched on.
    private async Task<bool> FireCabinCallAsync()
    {
        try
        {
            ApplySilent("PUSH_OVHD_CALLS_ALL", 1);
            await Task.Delay(400);
            return true;
        }
        finally
        {
            if (_sc is { IsConnected: true } && _def != null && _announcer != null)
                ApplySilent("PUSH_OVHD_CALLS_ALL", 0);
        }
    }

    /// <summary>
    /// A339X system-display page indices. NOT the A32NX's: the A330 splits ELEC into
    /// ElecAC(3)/ElecDC(4) and inserts Fuel at 11, so everything above Fctl shifts.
    /// Measured from the A339X SD bundle. The A32NX table maps STS=12, which renders
    /// the CRUISE page here. Exposed for HwA330DivergenceTests.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> EcamPageIndexMap =
        new Dictionary<string, int>
        {
            ["ECAM_PAGE_ENG"]=0, ["ECAM_PAGE_BLEED"]=1, ["ECAM_PAGE_PRESS"]=2,
            ["ECAM_PAGE_ELEC"]=3, ["ECAM_PAGE_HYD"]=5, ["ECAM_PAGE_FUEL"]=11,
            ["ECAM_PAGE_APU"]=6, ["ECAM_PAGE_COND"]=7, ["ECAM_PAGE_DOOR"]=8,
            ["ECAM_PAGE_WHEEL"]=9, ["ECAM_PAGE_FCTL"]=10, ["ECAM_PAGE_STS"]=13,
        };

    // Direct SD page-index write — held-safe (the earlier ECP press/release pulse could
    // leave a button stuck). PagesContainer reads A32NX_ECAM_SD_CURRENT_PAGE_INDEX as the
    // displayed page; auto-SD logic overrides a manual index on the next auto event (fine
    // for a checklist callout, same as the A380's real page-index write).
    private Task<bool> FireEcamPageAsync(string pseudoKey)
    {
        if (!EcamPageIndexMap.TryGetValue(pseudoKey, out int idx)) return Task.FromResult(false);
        return Task.FromResult(ApplySilent("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", idx));
    }

    public enum CockpitLightScene { DayPrep, DimFlight, ParkingBright, Off }

    /// <summary>
    /// The ordered (key, value) writes the A330 lighting scene makes — the SINGLE source
    /// of truth for this scene. <see cref="SetCockpitLighting"/> writes exactly this and
    /// nothing else, and <see cref="CockpitLightingKeys"/> is derived from it, so a pot
    /// added here cannot escape the tests that read that list.
    /// <para>
    /// Deliberately EXCLUDES BRIGHT_GLARESHIELD_CAPT_SET (pot 10) and
    /// BRIGHT_GLARESHIELD_FO_SET (pot 11): on the A339X those are the Captain's CEILING
    /// and MAP lights (A330_NEO_INTERIOR.xml:271-283), not glareshield floods, and both
    /// are binary click-toggles paired with L:A339X_CEILING_LIGHT_CAPTAIN /
    /// L:A339X_MAP_LIGHT_CAPTAIN. Writing the scene's intermediate value would light an
    /// unrelated lamp and leave it desynced from its own state var. The A330 has no
    /// glareshield flood knobs at all.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Key, int Value)> CockpitLightingPlan(CockpitLightScene scene)
    {
        (int ann, int dome, int compass, int integ, int flood) = scene switch
        {
            CockpitLightScene.DayPrep       => (1, 100, 1, 100, 50),
            CockpitLightScene.DimFlight     => (2, 20,  1, 50,  30),
            CockpitLightScene.ParkingBright => (1, 100, 1, 100, 50),
            CockpitLightScene.Off           => (1, 0,   0, 0,   0),
            _                               => (1, 100, 1, 100, 50),
        };
        return new (string Key, int Value)[]
        {
            ("A32NX_OVHD_INTLT_ANN", ann),
            ("A32NX_OVHD_INTLT_DOME", dome),
            ("A32NX_STBY_COMPASS_LIGHT_TOGGLE", compass),
            ("BRIGHT_GLARESHIELD_INTEG_SET", integ),
            ("BRIGHT_OVERHEAD_INTEG_SET", integ),
            ("BRIGHT_MAINPANEL_SET", flood),
            ("BRIGHT_PEDESTAL_SET", flood),
        };
    }

    /// <summary>
    /// The lighting keys the scene writes, DERIVED from <see cref="CockpitLightingPlan"/>
    /// across every scene — never hand-written. It was hand-written once, and because
    /// nothing in production read it the glareshield exclusion above could be reverted
    /// with both of its tests still green. Exposed for HwA330DivergenceTests.
    /// </summary>
    public static readonly IReadOnlyList<string> CockpitLightingKeys = BuildCockpitLightingKeys();

    private static IReadOnlyList<string> BuildCockpitLightingKeys()
    {
        var keys = new List<string>();
        foreach (CockpitLightScene scene in Enum.GetValues<CockpitLightScene>())
            foreach (var (key, _) in CockpitLightingPlan(scene))
                if (!keys.Contains(key)) keys.Add(key);
        return keys;
    }

    /// <summary>Batched, spaced cockpit-lighting scene write (per spec §4.1). Values tunable
    /// in-sim on <see cref="CockpitLightingPlan"/>, which is the only thing this writes —
    /// keep it that way, or the plan stops describing what the aircraft is sent.</summary>
    public async Task<bool> SetCockpitLighting(CockpitLightScene scene)
    {
        foreach (var (key, val) in CockpitLightingPlan(scene))
            await Set(key, val);
        return true;
    }

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(WriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    // ---- Public write used by checklist CheckActions + convenience methods ----
    /// <summary>Write one control by its A320 varKey (checklist CheckAction path).</summary>
    public Task<bool> Set(string varKey, int value) => DispatchAsync(varKey, value);

    /// <summary>Hold time for the per-source fire TEST L:vars (user-set 1.5 s).</summary>
    public const int FireTestHoldMs = 1500;

    /// <summary>Held fire test: write the per-source A32NX_FIRE_TEST_{APU,ENG1,ENG2} L:var
    /// 1 → hold → 0 through the def's dedicated branch (UiVariableSet.cs), which on the
    /// 0-write also pulses the MASTERAWARN acknowledge so the continuous repetitive chime
    /// cancels. Master warning + CRC while held are the blind-pilot verification. Holds the
    /// gate for the whole test so WaitForDispatchDrainAsync covers the checklist grace.</summary>
    public async Task<bool> FireTestAsync(string source)
    {
        string varKey = source switch
        {
            "FIRE_TEST_APU"  => "A32NX_FIRE_TEST_APU",
            "FIRE_TEST_ENG1" => "A32NX_FIRE_TEST_ENG1",
            "FIRE_TEST_ENG2" => "A32NX_FIRE_TEST_ENG2",
            _                => source,
        };
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            try
            {
                ApplySilent(varKey, 1);
                _lastWriteUtc = DateTime.UtcNow;
                await Task.Delay(FireTestHoldMs);
                return true;
            }
            finally
            {
                // ALWAYS release the held fire-test PB, even on an interrupted hold — a stuck 1
                // leaves the fire test active (continuous test bell). Guarded for a dropped sim.
                if (_sc is { IsConnected: true } && _def != null && _announcer != null) ApplySilent(varKey, 0);
                _lastWriteUtc = DateTime.UtcNow;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Fenix/A380-parity hold for the CVR test button.</summary>
    public const int CvrTestHoldMs = 3000;

    /// <summary>Held CVR test: A32NX_RCDR_TEST 1 → hold → 0 (needs
    /// A32NX_RCDR_GROUND_CONTROL_ON on to actually sound the test tone). Holds the gate
    /// for the whole test so WaitForDispatchDrainAsync covers the checklist grace.</summary>
    public async Task<bool> CvrTestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            try
            {
                ApplySilent("A32NX_RCDR_TEST", 1);
                _lastWriteUtc = DateTime.UtcNow;
                await Task.Delay(CvrTestHoldMs);
                return true;
            }
            finally
            {
                // ALWAYS release the held CVR test button, even on an interrupted hold. Guarded
                // for a dropped sim.
                if (_sc is { IsConnected: true } && _def != null && _announcer != null) ApplySilent("A32NX_RCDR_TEST", 0);
                _lastWriteUtc = DateTime.UtcNow;
            }
        }
        finally { _gate.Release(); }
    }
    public Task<bool> CvrTest() => CvrTestAsync();

    /// <summary>FWC latch hold for the ECP TO CONFIG TEST button.</summary>
    public const int ToConfigHoldMs = 2000;

    /// <summary>TO CONFIG test: fire the ECP TO_CONF_TEST press H-event, hold, release
    /// (FWC latches the test result at ≥1.5s held). No direct L:var exists for this
    /// button — it is genuinely an ECP momentary, unlike the SD page keys above.</summary>
    public async Task<bool> TakeoffConfigTestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            try
            {
                _sc.SendHVar("A32NX_ECP_TO_CONF_TEST_PRESSED");
                _lastWriteUtc = DateTime.UtcNow;
                await Task.Delay(ToConfigHoldMs);
                return true;
            }
            finally
            {
                // ALWAYS send the release H-event, even on an interrupted hold — a missed release
                // leaves the ECP TO CONFIG button held down. Guarded for a dropped sim.
                if (_sc is { IsConnected: true }) _sc.SendHVar("A32NX_ECP_TO_CONF_TEST_RELEASED");
                _lastWriteUtc = DateTime.UtcNow;
            }
        }
        finally { _gate.Release(); }
    }
    public Task<bool> TakeoffConfigTest() => TakeoffConfigTestAsync();

    // ---- Convenience methods for the auto-manager / phase monitor ----
    // GEAR_HANDLE_POSITION is a read-only stock SimVar (no HandleUIVariableSet write branch) —
    // set the gear via the stock GEAR_SET K-event instead (0=up, 1=down), the same pattern
    // HorizonSim787Definition.UiAndHotkeys.cs uses. Bypasses the dispatch gate/ApplySilent
    // (whose SetLVar fallback would write a nonexistent L:var and silently no-op).
    public Task<bool> SetGear(bool down)
    {
        _sc?.SendEvent("GEAR_SET", (uint)(down ? 1 : 0));
        return Task.FromResult(true);
    }
    public Task<bool> EngageAp1()              => DispatchAsync("AP1_ENGAGE", 1);
    public Task<bool> SetBaroStd(bool std)     => DispatchAsync(std ? "BARO_STD" : "BARO_QNH", 1);
    /// <summary>All-landing-lights buttons: On extends+illuminates both LAND lights, Off
    /// retracts them (LANDING_LIGHTS_ON_THIRD_PARTY/OFF_THIRD_PARTY are RenderAsButton events —
    /// any nonzero value fires the press; the nose light is independent, see SetNoseLight).</summary>
    public Task<bool> SetLandingLights(int on) =>
        DispatchAsync(on != 0 ? "LANDING_LIGHTS_ON_THIRD_PARTY" : "LANDING_LIGHTS_OFF_THIRD_PARTY", 1);
    /// <summary>Nose light (LIGHTING_LANDING_1): 0=T.O., 1=Taxi, 2=Off.</summary>
    public Task<bool> SetNoseLight(int pos)    => DispatchAsync("LIGHTING_LANDING_1", pos);
    public Task<bool> CabinCall()              => DispatchAsync("CABIN_CALL_ALL", 1);

    /// <summary>A339X seat-belt switch positions. Three-position 0=On / 1=Auto / 2=Off
    /// (A330_NEO_INTERIOR.xml:1817-1823) — the OPPOSITE of the A32NX's two-position
    /// 1=On / 0=Off. Exposed for HwA330DivergenceTests.</summary>
    public const int SeatbeltPositionOn  = 0;
    public const int SeatbeltPositionOff = 2;

    /// <summary>
    /// The dispatch pseudo-key flow steps use for the seat-belt sign, so flow, checklist
    /// and phase monitor all converge on <see cref="SetSeatbeltSign"/> — the ONE write path
    /// that moves the switch out of AUTO. Exposed for HwA330DivergenceTests.
    /// </summary>
    public const string SeatbeltSignKey = "SEATBELT_SIGN";

    /// <summary>
    /// Pure: the switch-POSITION write a seat-belt sign command makes — ON selects
    /// position 0, OFF selects position 2. This, not the stock toggle event, is the
    /// A330's actual seat-belt write; the A32NX has no analogue because its switch has
    /// no AUTO position to be moved out of. Exposed for HwA330DivergenceTests.
    /// </summary>
    public static (string VarKey, int Value) SeatbeltWritePlan(bool on) =>
        ("SEATBELT_SIGN_POSITION", on ? SeatbeltPositionOn : SeatbeltPositionOff);

    /// <summary>
    /// Seat-belt sign, in two steps and in this order.
    ///
    /// (1) Write the switch POSITION. This moves the physical switch AND takes the
    ///     airframe out of AUTO — whose CODE_POS_1 block re-drives the stock simvar
    ///     every 500 ms from engines-running AND (slats out OR a main gear downlocked),
    ///     so a bare stock toggle is fought back within half a second while the switch
    ///     sits there. The A32NX has no AUTO position and needs none of this.
    ///
    /// (2) Reconcile the sign lamp with the guarded stock toggle. Whether CODE_POS_0 /
    ///     CODE_POS_2 fire on an external L:var write, or only on a cockpit click,
    ///     cannot be settled by reading the template — so this is belt-and-braces: a
    ///     no-op if they fire, and what actually lights the sign if they do not.
    ///     See the L1 item in docs/headwind-a330-first-officer-test-plan.md.
    ///
    /// Detection stays on the sign LAMP, never the switch position — the A380 invariant.
    /// </summary>
    public Task<bool> SetSeatbeltSign(bool on) => DispatchAsync(SeatbeltSignKey, on ? 1 : 0);

    // Must be called inside _gate (dispatched from DispatchCoreAsync's SeatbeltSignKey arm,
    // so the checklist CheckAction, the flow step and the phase monitor share one path).
    // SemaphoreSlim is not reentrant, so this calls DispatchCoreAsync directly rather than
    // Set(), which would re-enter the gate and deadlock.
    private async Task<bool> SetSeatbeltSignCoreAsync(bool on)
    {
        if (_sc == null) return false;

        var (varKey, position) = SeatbeltWritePlan(on);
        await DispatchCoreAsync(varKey, position);

        bool currentOn = (_sc.GetCachedVariableValue("CABIN SEATBELTS ALERT SWITCH") ?? (on ? 0.0 : 1.0)) > 0.5;
        if (currentOn != on) _sc.SendEvent("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE");
        return true;
    }
}

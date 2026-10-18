using System.Threading;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Sends PMDG 777 switch events on behalf of the flow engine.
/// Uses the same PMDG event IDs and parameter rules as the rest of BA Assist.
///
/// THREADING/PACING (2026-07-02, mirrors the 737 executor): PMDG polls the control
/// CDA once per frame, so two writes in the same frame COALESCE — only the last
/// survives. Every dispatch goes through a serializing gate that frame-spaces each
/// write from the previous one. Before this, every Multi() flow step (bus ties,
/// engine anti-ice, demand pumps, fuel pumps, …) and every multi-write convenience
/// method fired its events in ONE frame, silently applying only the LAST switch.
/// </summary>
public class AircraftActionExecutor : IFoActionExecutor
{
    private SimConnectManager? _simConnect;

    // MOUSE_FLAG_LEFTSINGLE — required for FD/AT Arm switches and the per-detent
    // flap-lever click events (see CLAUDE.md).
    private const int MouseFlagLeftSingle = 0x20000000;

    // Held FIRE/OVHT TEST button: transmit press/release with the hold between. The 777
    // CDA doctrine has NO release semantics (param 0 also registers as a press — see
    // docs/pmdg-777.md), so the held test uses the '#id' transmit path like the VC click.
    private const uint MouseFlagLeftSingleU  = 0x20000000u;
    private const uint MouseFlagLeftReleaseU = 0x00020000u;
    public const int FireOvhtTestHoldMs = 3000;   // Fenix-parity hold

    /// <summary>System-test timings (live-verified on the 777 2026-07-13, user-tunable).
    /// The 777 TCAS TEST commits ONLY via the transmit press/release — the def's usual CDA
    /// param-1 momentary was SILENT for it (same finding family as EVT_OH_FIRE_OVHT_TEST).</summary>
    public const int QuickTestHoldMs = 150;
    public const int WxrOverlaySettleMs = 4000;
    public const int WxrTestWaitMs = 20000;

    public void SetSimConnect(SimConnectManager? sc) => _simConnect = sc;

    public bool IsAvailable => _simConnect is { IsConnected: true };

    // The presses held right now (HeldTransmitTestAsync) and the weather radar test's restore,
    // sent at once when the window closes.
    private readonly HeldPresses _held = new();

    /// <summary>The window is closing: a held test is released, and a running weather radar test
    /// put back to WX with its overlay off, now, while the sim still listens (on app exit MainForm
    /// disconnects right after).</summary>
    public void ReleaseHeldPresses() => _held.ReleaseAll();

    // -----------------------------------------------------------------------
    // Core dispatch — serialized + frame-paced (see class doc)
    // -----------------------------------------------------------------------

    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private DateTime _lastWriteUtc = DateTime.MinValue;

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(CdaWriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    // Live center fuel quantity (lbs) for the RULING-A gate. Pre-snapshot → 0.0 (interface
    // contract) → gate BLOCKS center-ON (fail-safe; self-heals on the next actuation). Never
    // return a large sentinel here — that would fail OPEN into the dry-run hazard.
    private double CenterQty() => _simConnect?.PMDGDataManager?.GetFieldValue("FUEL_QtyCenter") ?? 0.0;

    /// <summary>
    /// IFoActionExecutor — acquire+release the dispatch gate. SemaphoreSlim async
    /// waiters queue FIFO, so by the time this acquires, every dispatch queued before
    /// the call has fully completed (ChecklistManager's post-tick grace re-stamp).
    /// </summary>
    public async Task WaitForDispatchDrainAsync()
    {
        await _dispatchGate.WaitAsync();
        _dispatchGate.Release();
    }

    /// <summary>Interface implementation: dispatches an <see cref="IFlowStepDispatch"/>,
    /// awaited by the flow engine so steps stay ordered and paced.</summary>
    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (!IsAvailable) return Task.FromResult(false);
        // Pseudo-keys: held self-completing system tests (Fenix FIRE_TEST_* precedent).
        if (step.ActionType == Models.FlowStepActionType.SetSwitch)
        {
            switch (step.EventName)
            {
                case "FIRE_OVHT_TEST": return FireOvhtTestAsync();
                case "TCAS_TEST":      return TcasTestAsync();
                case "WXR_TEST":       return WxrTestAsync();
                case "OXY_TEST_CAPT":  return OxygenTestCaptAsync();
                case "OXY_TEST_FO":    return OxygenTestFOAsync();
                case SpeedbrakeLeverState.ArmPseudoKey: return ArmSpeedbrakeAsync();
                // Guarded switch — the guard and the settle gap can't be expressed as
                // a plain SetSwitch dispatch. See SetEmerExitLightsAsync.
                case "EMER_EXIT_LIGHTS":
                    return SetEmerExitLightsAsync(step.TargetValue ?? EmerExitLightSequence.Armed);
                // The loaded variant's own switches (Pmdg777RecircFans).
                case Pmdg777RecircFans.PseudoKey:
                    return SetRecircFansAsync(step.TargetValue ?? 1);
            }
        }
        return step.ActionType switch
        {
            Models.FlowStepActionType.SetSwitch =>
                DispatchAsync(step.EventName!, step.TargetValue, step.UsesMouseFlag, step.IsMomentary),
            Models.FlowStepActionType.SetSwitchMultiple => MultiAsync(step.MultiActions),
            _ => Task.FromResult(false),
        };
    }

    private async Task<bool> MultiAsync(IReadOnlyList<(string EventName, int? TargetValue)> actions)
    {
        // Hold the gate across the whole sequence so a concurrent dispatch can't slip a
        // write into one of our frame gaps; inter-write spacing comes from PaceAsync.
        await _dispatchGate.WaitAsync();
        try
        {
            bool ok = true;
            foreach (var (ev, tv) in actions)
                ok &= await DispatchCoreAsync(ev, tv, false, false);
            return ok;
        }
        finally { _dispatchGate.Release(); }
    }

    private async Task<bool> DispatchAsync(string eventName, int? target, bool usesMouseFlag, bool isMomentary)
    {
        await _dispatchGate.WaitAsync();
        try { return await DispatchCoreAsync(eventName, target, usesMouseFlag, isMomentary); }
        finally { _dispatchGate.Release(); }
    }

    private async Task<bool> DispatchCoreAsync(string eventName, int? targetValue, bool usesMouseFlag, bool isMomentary)
    {
        if (_simConnect == null || !PMDG777Definition.EventIds.TryGetValue(eventName, out int evId)) return false;
        uint eventId = (uint)evId;

        // Per-detent flap-lever AND speed-brake-lever events are SDK mouse-click events
        // that ONLY commit with MOUSE_FLAG_LEFTSINGLE (the panel's proven FCTL_Flaps
        // convention; the speed-brake detents are the same sub-detent click family —
        // live-verified on the 737 2026-07-03, where CDA+LEFTSINGLE on _ARM lit the
        // ARMED annunciator while the bare param was a silent no-op). Force the flag
        // here so EVERY caller shape is corrected — the flows' flap steps and the
        // "Speedbrake: ARMED"/"DOWN" steps were built as IsMomentary (param 1) and
        // silently did nothing. (The trailing underscore keeps the bare
        // EVT_CONTROL_STAND_SPEED_BRAKE_LEVER drag-axis event out of this.)
        if (eventName.StartsWith("EVT_CONTROL_STAND_FLAPS_LEVER_", StringComparison.Ordinal) ||
            eventName.StartsWith("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_", StringComparison.Ordinal))
        {
            usesMouseFlag = true;
            isMomentary = false;
        }

        // Never click ARM over a speed brake that is not known to be DOWN: over a deployed one
        // the ARM detent RETRACTS it, and an unread lever may be deployed. An armed one needs
        // no click. This guards every caller (the verified arm, the checklist tick).
        if (eventName == SpeedbrakeArmEvent)
        {
            var decision = SpeedbrakeArmDecisionNow();
            if (decision == SpeedbrakeArmDecision.AlreadyArmed) return true;
            if (decision != SpeedbrakeArmDecision.Arm)
            {
                Log.Debug("FirstOfficer", $"777 speedbrake ARM not sent: {decision}.");
                return false;
            }
        }

        // Never move the gear lever UP unless the aircraft is READ as airborne (the MD-11's rule):
        // the After Takeoff flow run by mistake, or its "Gear: UP" line ticked, on the ground. The
        // flow step and the line say why first (AircraftStateEvaluator.GearUpLeaveAloneText); this
        // guards every caller.
        if (eventName == GearLeverEvent && targetValue == 0 && !IsDefinitelyAirborneNow())
        {
            Log.Debug("FirstOfficer", "777 gear UP not sent: the aircraft is not read as airborne.");
            return false;
        }

        // RULING A (§6): suppress a center-pump ON write on an empty tank — BEFORE PaceAsync,
        // returning true so a Multi's ok-conjunction stays intact (the flow step completes,
        // only the center half suppressed). Param 0 (OFF) is never gated.
        if (CenterPumpGate.ShouldSuppressCenterOn(eventName, targetValue == 1, CenterQty()))
            return true;

        await PaceAsync();
        // Read again after the pacing wait: a closed First Officer window (an aircraft switch) or a
        // disconnect detaches the executor meanwhile, and the write must not reach the next aircraft.
        var sc = _simConnect;
        if (sc == null) return false;
        try
        {
            if (usesMouseFlag)
                sc.SendPMDGEvent(eventName, eventId, MouseFlagLeftSingle);
            else if (isMomentary)
                sc.SendPMDGEvent(eventName, eventId, 1);
            else if (targetValue.HasValue)
                sc.SendPMDGEvent(eventName, eventId, targetValue.Value);
            else
                sc.SendPMDGEvent(eventName, eventId);
            return true;
        }
        finally { _lastWriteUtc = DateTime.UtcNow; }
    }

    // Synchronous entry used by the ~60 convenience methods below (checklist
    // CheckAction lambdas are synchronous). Queues a gated, paced write and returns:
    // sequential calls queue in order (SemaphoreSlim async waiters are FIFO), so a
    // lambda firing several switches gets them written one frame apart.
    private bool ExecuteSingle(string eventName, int? targetValue, bool usesMouseFlag, bool isMomentary)
    {
        if (!PMDG777Definition.EventIds.ContainsKey(eventName)) return false;
        _ = DispatchAsync(eventName, targetValue, usesMouseFlag, isMomentary);
        return true;
    }

    // -----------------------------------------------------------------------
    // Named convenience actions (used by flow definitions)
    // -----------------------------------------------------------------------

    // Battery
    public bool SetBattery(int position)
        => ExecuteSingle("EVT_OH_ELEC_BATTERY_SWITCH", position, false, false);

    // APU selector: 0=OFF, 1=ON, 2=START
    public bool SetApuSelector(int position)
        => ExecuteSingle("EVT_OH_ELEC_APU_SEL_SWITCH", position, false, false);

    // Ground power (momentary). Named for the ANNUNCIATOR each one moves, because PMDG's
    // event names are reversed against the annunciator array — see
    // GroundPowerGate.EventForAnnunciatorIndex. Every caller pairs "Primary" with
    // IsGpuPower1On()/ELEC_annunExtPowr_ON_0, so these keep their names and route through
    // the gate rather than naming the event directly.
    public bool PushGroundPowerPrimary()
        => ExecuteSingle(GroundPowerGate.EventForAnnunciatorIndex(0), null, false, true);
    public bool PushGroundPowerSecondary()
        => ExecuteSingle(GroundPowerGate.EventForAnnunciatorIndex(1), null, false, true);

    // Minimum gap between CDA writes (same-frame writes coalesce — see class doc).
    private const int CdaWriteSpacingMs = 350;
    // APU selector ON → START needs the longer gap the flow uses (BS_APU_ON_WAIT = 2 s)
    // so the model registers ON before START.
    private const int ApuOnToStartMs = 2000;

    /// <summary>Push BOTH ground-power buttons — the paced gate frame-spaces them.</summary>
    public async Task PushBothGroundPowerAsync()
    {
        PushGroundPowerPrimary();
        await Task.Delay(CdaWriteSpacingMs);
        PushGroundPowerSecondary();
    }

    /// <summary>Held transmit press/release for buttons with no CDA release semantics
    /// (param 0 also registers as a press on the 777 — docs/pmdg-777.md).</summary>
    public async Task<bool> HeldTransmitTestAsync(string eventName, int holdMs)
    {
        if (_simConnect == null || !PMDG777Definition.EventIds.TryGetValue(eventName, out int evId))
            return false;
        await _dispatchGate.WaitAsync();
        try
        {
            await PaceAsync();
            // Read after the gate and the pacing wait, as DispatchCoreAsync does: a window closed
            // meanwhile (an aircraft switch) detaches the executor, and a press queued behind the
            // gate must not reach the next aircraft. The RELEASE of a press already sent still goes
            // out on this same reference, so a press is never left held.
            var sc = _simConnect;
            if (sc == null) return false;
            // The release goes out at once if the window closes during the hold (_held): on app
            // exit MainForm disconnects right after, and a release on the hold's timer went nowhere.
            long hold = _held.Begin(() =>
            {
                if (sc.IsConnected) sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftReleaseU);
            });
            try
            {
                sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftSingleU);
                _lastWriteUtc = DateTime.UtcNow;
                await Task.Delay(holdMs);
                return true;
            }
            finally
            {
                _held.End(hold);
                _lastWriteUtc = DateTime.UtcNow;
            }
        }
        finally { _dispatchGate.Release(); }
    }

    /// <summary>FIRE/OVHT TEST button, HELD: transmit LEFTSINGLE, hold, LEFTRELEASE —
    /// fire bell + fire warning lights while held, self-releasing. NOT the panel's
    /// momentary CDA param-1 press (that is a bare blip; the test wants a real hold,
    /// and CDA has no release). In-sim verification: 737 test plan Part U (the 777
    /// shares that plan); fallback if transmit proves inert on this button is the
    /// CDA param-1 press.</summary>
    public Task<bool> FireOvhtTestAsync() =>
        HeldTransmitTestAsync("EVT_OH_FIRE_OVHT_TEST", FireOvhtTestHoldMs);

    /// <summary>TCAS self-test — quick press; "TCAS TEST" → ~8 s → "TCAS TEST PASS".</summary>
    public Task<bool> TcasTestAsync() => HeldTransmitTestAsync("EVT_TCAS_TEST", QuickTestHoldMs);

    /// <summary>Crew oxygen TEST/RESET switch — one quick transmit press/release per
    /// side (the panel OXY_TestReset_L/R momentary mechanism; audible oxygen-flow
    /// sound is the verification — the 737 sibling was live-confirmed 2026-08-16).</summary>
    public Task<bool> OxygenTestCaptAsync() =>
        HeldTransmitTestAsync("EVT_OXY_TEST_RESET_SWITCH_L", QuickTestHoldMs);

    /// <summary>First Officer side of the crew oxygen test — see OxygenTestCaptAsync.</summary>
    public Task<bool> OxygenTestFOAsync() =>
        HeldTransmitTestAsync("EVT_OXY_TEST_RESET_SWITCH_R", QuickTestHoldMs);

    // (No both-sides helper: each side is its own flow step and its own Preflight item, so
    // each mask can be tested alone. Firing the pair from one tick put the two presses ~350 ms
    // apart, which read as a single sound — user report 2026-08-16.)

    /// <summary>WXR/PWS test — same managed sequence as the 737 (see that executor's doc):
    /// EFIS WXR overlay on (blind toggle, assumed OFF) → settle → TEST → callout wait →
    /// WX mode (never leave TEST latched) → overlay off. Live-verified on the 777 2026-07-13.
    /// The restore is owed from the overlay press on (<see cref="_held"/>): the window's close sends
    /// it at once, before the detach (<see cref="ClickNow"/>). Left to this sequence it was skipped,
    /// since every press after the detach is refused, and the radar stayed in TEST, overlay on.</summary>
    public async Task<bool> WxrTestAsync()
    {
        // Read before the overlay press: a press that went out went out through this manager (a
        // detach in between makes HeldTransmitTestAsync press nothing and return false).
        var sc = _simConnect;
        // Overlay on first; if that fails (no SimConnect) nothing was touched — bail
        // before the try, no restore owed.
        if (sc == null || !await HeldTransmitTestAsync("EVT_EFIS_CPT_WXR", QuickTestHoldMs)) return false;
        // The overlay first: the window's close sends the latest hold first, so WX mode, then the
        // overlay off, the sequence's own order.
        long overlayOwed = _held.Begin(() => ClickNow(sc, "EVT_EFIS_CPT_WXR", pauseAfter: false));
        long wxOwed = _held.Begin(() => ClickNow(sc, "EVT_WXR_L_WX", pauseAfter: true));
        try
        {
            await Task.Delay(WxrOverlaySettleMs);
            return await HeldTransmitTestAsync("EVT_WXR_TEST", QuickTestHoldMs)
                && await DelayThenTrueAsync(WxrTestWaitMs);
        }
        finally
        {
            // Restore UNCONDITIONALLY once the overlay is on — never leave the radar
            // latched in TEST or the overlay stuck on, even if the TEST press failed. Each part
            // unless the window's close already sent it.
            if (_held.TakeOver(wxOwed)) await HeldTransmitTestAsync("EVT_WXR_L_WX", QuickTestHoldMs);
            if (_held.TakeOver(overlayOwed)) await HeldTransmitTestAsync("EVT_EFIS_CPT_WXR", QuickTestHoldMs);
        }
    }

    /// <summary>
    /// One click sent now, its hold slept out on the calling thread: only for a restore the
    /// window's close sends (<see cref="ReleaseHeldPresses"/>, the UI thread), where MainForm may
    /// disconnect right after and an awaited click would go nowhere. <paramref name="pauseAfter"/>
    /// spaces it from a click that follows, as the paced sequence does. At most about 0.65 s, and
    /// only when the window closes during the weather radar test.
    /// </summary>
    private static void ClickNow(SimConnectManager sc, string eventName, bool pauseAfter)
    {
        if (!sc.IsConnected || !PMDG777Definition.EventIds.TryGetValue(eventName, out int evId)) return;
        sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftSingleU);
        Thread.Sleep(QuickTestHoldMs);
        sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftReleaseU);
        if (pauseAfter) Thread.Sleep(CdaWriteSpacingMs);
    }

    private static async Task<bool> DelayThenTrueAsync(int ms)
    {
        await Task.Delay(ms);
        return true;
    }

    /// <summary>APU start sequence: selector ON, wait, then START (springs back to ON).</summary>
    public async Task StartApuAsync()
    {
        SetApuSelector(1);                  // ON
        await Task.Delay(ApuOnToStartMs);
        SetApuSelector(2);                  // START
    }

    // APU Generator
    public bool SetApuGenerator(int position)
        => ExecuteSingle("EVT_OH_ELEC_APU_GEN_SWITCH", position, false, false);

    // Bus Ties
    public bool SetBusTies(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ELEC_BUS_TIE1_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ELEC_BUS_TIE2_SWITCH", position, false, false);
        return ok;
    }

    // Generators
    public bool SetGenerators(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ELEC_GEN1_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ELEC_GEN2_SWITCH", position, false, false);
        return ok;
    }

    // Backup Generators
    public bool SetBackupGenerators(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ELEC_BACKUP_GEN1_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ELEC_BACKUP_GEN2_SWITCH", position, false, false);
        return ok;
    }

    // IFE / Cabin
    public bool SetIFEPassSeats(int position) => ExecuteSingle("EVT_OH_ELEC_IFE", position, false, false);
    public bool SetCabinUtility(int position)  => ExecuteSingle("EVT_OH_ELEC_CAB_UTIL", position, false, false);

    // Hydraulic engine pumps
    public bool SetEngPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_HYD_ENG1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_ENG2", position, false, false);
        return ok;
    }

    // Hydraulic electric pumps
    public bool SetElecPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_HYD_ELEC1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_ELEC2", position, false, false);
        return ok;
    }

    // Hydraulic demand pumps (electric + air)
    public bool SetDemandPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_HYD_DEMAND_ELEC1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_DEMAND_ELEC2", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_AIR1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_AIR2", position, false, false);
        return ok;
    }

    // Fuel pumps (all wing pumps)
    public bool SetWingFuelPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_FUEL_PUMP_1_FORWARD", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_PUMP_2_FORWARD", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_PUMP_1_AFT", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_PUMP_2_AFT", position, false, false);
        return ok;
    }

    // Center fuel pumps
    public bool SetCenterFuelPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_FUEL_PUMP_L_CENTER", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_PUMP_R_CENTER", position, false, false);
        return ok;
    }

    // Fuel control levers (PMDG inverted: 1=Cutoff, 0=Run in the event param)
    // Here we take logical values: 0=Cutoff, 1=Run
    public bool SetFuelControlLevers(int logicalPosition)
    {
        int pmdgParam = logicalPosition == 1 ? 0 : 1; // invert
        bool ok = ExecuteSingle("EVT_CONTROL_STAND_ENG1_START_LEVER", pmdgParam, false, false);
        ok &= ExecuteSingle("EVT_CONTROL_STAND_ENG2_START_LEVER", pmdgParam, false, false);
        return ok;
    }

    public bool SetFuelControl1(int logicalPosition)
    {
        int pmdgParam = logicalPosition == 1 ? 0 : 1;
        return ExecuteSingle("EVT_CONTROL_STAND_ENG1_START_LEVER", pmdgParam, false, false);
    }

    public bool SetFuelControl2(int logicalPosition)
    {
        int pmdgParam = logicalPosition == 1 ? 0 : 1;
        return ExecuteSingle("EVT_CONTROL_STAND_ENG2_START_LEVER", pmdgParam, false, false);
    }

    // Engine start selectors
    public bool SetEngStartSelector1(int position) => ExecuteSingle("EVT_OH_ENGINE_L_START", position, false, false);
    public bool SetEngStartSelector2(int position) => ExecuteSingle("EVT_OH_ENGINE_R_START", position, false, false);

    // EEC Mode
    public bool SetEECMode(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_EEC_L_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_EEC_R_SWITCH", position, false, false);
        return ok;
    }

    // Autostart
    public bool SetAutoStart(int position) => ExecuteSingle("EVT_OH_ENGINE_AUTOSTART", position, false, false);

    // Anti-ice
    public bool SetWindowHeat(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ICE_WINDOW_HEAT_1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ICE_WINDOW_HEAT_2", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ICE_WINDOW_HEAT_3", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ICE_WINDOW_HEAT_4", position, false, false);
        return ok;
    }

    public bool SetWingAntiIce(int position) => ExecuteSingle("EVT_OH_ICE_WING_ANTIICE", position, false, false);
    public bool SetEngAntiIce(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ICE_ENGINE_ANTIICE_1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ICE_ENGINE_ANTIICE_2", position, false, false);
        return ok;
    }

    // Packs
    public bool SetPacks(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_AIRCOND_PACK_SWITCH_L", position, false, false);
        ok &= ExecuteSingle("EVT_OH_AIRCOND_PACK_SWITCH_R", position, false, false);
        return ok;
    }

    // Bleed
    public bool SetEngBleeds(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_BLEED_ENG_1_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_BLEED_ENG_2_SWITCH", position, false, false);
        return ok;
    }
    public bool SetApuBleed(int position) => ExecuteSingle("EVT_OH_BLEED_APU_SWITCH", position, false, false);
    public bool SetBleedIsolationValves(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_L", position, false, false);
        ok &= ExecuteSingle("EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_C", position, false, false);
        ok &= ExecuteSingle("EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_R", position, false, false);
        return ok;
    }

    // Pressurization
    public bool SetOutflowValves(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_PRESS_VALVE_SWITCH_1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_PRESS_VALVE_SWITCH_2", position, false, false);
        return ok;
    }

    // Trim air
    public bool SetTrimAir(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_L", position, false, false);
        ok &= ExecuteSingle("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_R", position, false, false);
        return ok;
    }

    // Equip cooling / Gasper / Recirc fans
    public bool SetEquipCooling(int position) => ExecuteSingle("EVT_OH_AIRCOND_EQUIP_COOLING_SWITCH", position, false, false);
    public bool SetGasper(int position)       => ExecuteSingle("EVT_OH_AIRCOND_GASPER_SWITCH", position, false, false);

    // Recirculation fans through the loaded variant's own switches (Pmdg777RecircFans): UPPER and
    // LOWER on a passenger 777, the single RECIRC FANS switch on the 777F. This wrote the freighter's
    // switch on every variant. Before the CDA names the variant nothing is written (false).
    public bool SetRecircFans(int position)
    {
        var writes = Pmdg777RecircFans.Writes(AircraftModel(), position);
        if (writes.Count == 0) return false;
        _ = MultiAsync(writes);
        return true;
    }

    /// <summary>The flow's recirculation-fan step (<see cref="Pmdg777RecircFans.PseudoKey"/>): false,
    /// nothing written, while the variant is not known.</summary>
    public Task<bool> SetRecircFansAsync(int position)
    {
        var writes = Pmdg777RecircFans.Writes(AircraftModel(), position);
        if (writes.Count == 0)
        {
            Log.Debug("FirstOfficer", "777 recirculation fans not set: the variant is not known yet.");
            return Task.FromResult(false);
        }
        return MultiAsync(writes);
    }

    // The CDA's AircraftModel (1 -200 ... 5 777F, 6 -300ER); NaN before the first snapshot.
    private double AircraftModel() =>
        _simConnect?.PMDGDataManager is { IsReady: true } dm
            ? dm.GetFieldValue(Pmdg777RecircFans.ModelField) : double.NaN;

    // Lights
    public bool SetBeacon(int position)         => ExecuteSingle("EVT_OH_LIGHTS_BEACON", position, false, false);
    public bool SetNavLights(int position)      => ExecuteSingle("EVT_OH_LIGHTS_NAV", position, false, false);
    public bool SetStrobeLights(int position)   => ExecuteSingle("EVT_OH_LIGHTS_STROBE", position, false, false);
    public bool SetLogoLights(int position)     => ExecuteSingle("EVT_OH_LIGHTS_LOGO", position, false, false);
    public bool SetWingLights(int position)     => ExecuteSingle("EVT_OH_LIGHTS_WING", position, false, false);
    public bool SetStormLights(int position)    => ExecuteSingle("EVT_OH_LIGHTS_STORM", position, false, false);
    public bool SetTaxiLights(int position)     => ExecuteSingle("EVT_OH_LIGHTS_TAXI", position, false, false);
    // All THREE landing lights via the individual per-switch events the panel's proven
    // controls use (EVT_OH_LIGHTS_LANDING_L/NOSE/R) — the ganged LANDING_LNR event was
    // never live-verified. Paced by the dispatch gate.
    public bool SetLandingLights(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_LIGHTS_LANDING_L", position, false, false);
        ok &= ExecuteSingle("EVT_OH_LIGHTS_LANDING_NOSE", position, false, false);
        ok &= ExecuteSingle("EVT_OH_LIGHTS_LANDING_R", position, false, false);
        return ok;
    }
    // Both runway turnoff lights through the per-side events the panel's proven controls use
    // (EVT_OH_LIGHTS_L/R_TURNOFF), as the landing lights above: the ganged LR_TURNOFF (+1201) was
    // never live-verified. Paced by the dispatch gate.
    public bool SetRunwayTurnoff(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_LIGHTS_L_TURNOFF", position, false, false);
        ok &= ExecuteSingle("EVT_OH_LIGHTS_R_TURNOFF", position, false, false);
        return ok;
    }
    public bool SetMasterLights(int position)   => ExecuteSingle("EVT_OH_LIGHTS_IND_LTS_SWITCH", position, false, false);

    // Signs
    public bool SetSeatBelts(int position)      => ExecuteSingle("EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", position, false, false);

    /// <summary>Seat-belt sign: ON = position 2, OFF = position 0 (0=OFF,1=AUTO,2=ON).</summary>
    public bool SetSeatbeltSign(bool on) => SetSeatBelts(on ? 2 : 0);
    public bool SetNoSmoking(int position)      => ExecuteSingle("EVT_OH_NO_SMOKING_LIGHT_SWITCH", position, false, false);

    // Emergency exit lights — a GUARDED switch. Ordering and polarity come from
    // EmerExitLightSequence (0 = guard CLOSED, 1 = guard OPEN; ARMED is the normal
    // guard-closed position, OFF and ON sit outside it), which mirrors
    // PMDG777Definition's LTS_EmerLights block — both are the same user-verified
    // model (MobiFlight against the real switch, 2026-08-01). An earlier version of
    // this file had the polarity INVERTED and named the open-guard write
    // "CloseEmerExitLightGuard"; EmerExitLightSequenceTests pins it now.
    //
    // Two things make this unlike every other switch here:
    //  1. The guard and the switch travel on DIFFERENT transports — the guard is a
    //     CDA write, the switch an absolute-target TransmitClientEvent. The CDA
    //     selector only ever steps one detent UPWARD, which is why Armed -> Off is
    //     unreachable through it (the bug this replaces: the FO dispatched the
    //     switch over CDA and could never actually reach OFF).
    //  2. Because the transports differ, the two writes are not guaranteed to arrive
    //     in the order they were issued — a settle gap orders them. Fired back to
    //     back, the switch can reach PMDG before the guard has lifted and the write
    //     is refused.
    //
    // The whole sequence is awaited INSIDE _dispatchGate so WaitForDispatchDrainAsync
    // still covers it and ChecklistManager's revert grace cannot fire mid-gap. It also takes
    // the panel's own sequence gate (PMDG777Definition.EmerLightsSequenceGate, P777-13), so a
    // pick on the Emergency Lights combo never lands between this guard write and its switch
    // write, nor this one inside the panel's. Order: _dispatchGate first, then the panel's;
    // the panel never takes _dispatchGate, so the two cannot wait on each other.
    private const int EmerLightsGuardSettleMs = 80;

    /// <summary>Drive the guarded emergency-exit light switch to <paramref name="target"/>
    /// (0 = Off, 1 = Armed, 2 = On), guard included.</summary>
    public async Task<bool> SetEmerExitLightsAsync(int target)
    {
        if (_simConnect == null) return false;
        if (!PMDG777Definition.EventIds.TryGetValue("EVT_OH_EMER_EXIT_LIGHT_SWITCH", out int switchEventId))
            return false;
        bool haveGuard = PMDG777Definition.EventIds.TryGetValue("EVT_OH_EMER_EXIT_LIGHT_GUARD", out int guardEventId);

        await _dispatchGate.WaitAsync();
        try
        {
            var panelGate = PMDG777Definition.EmerLightsSequenceGate;
            await panelGate.WaitAsync();
            try
            {
                await PaceAsync();
                // Everything is read only now, inside both gates and after the pacing wait: a
                // window closed meanwhile detaches the executor (nothing may reach the next
                // aircraft), and the plan must start from the position left by any sequence that
                // ran first, never from one read before the wait.
                var sc = _simConnect;
                if (sc == null) return false;

                // Reading the live position needs a ready CDA snapshot — before the first one
                // GetFieldValue returns 0.0 for every field, which would read as "already Off"
                // and silently skip a real write.
                var dm = sc.PMDGDataManager;
                if (dm == null || !dm.IsReady) return false;
                int current = (int)Math.Round(dm.GetFieldValue("LTS_EmerLightsSelector"));

                var steps = EmerExitLightSequence.Plan(current, target, haveGuard);
                for (int i = 0; i < steps.Count; i++)
                {
                    if (i > 0) await Task.Delay(EmerLightsGuardSettleMs);
                    var step = steps[i];
                    if (step.Kind == EmerExitStepKind.Guard)
                        sc.SendPMDGEvent("EVT_OH_EMER_EXIT_LIGHT_GUARD", (uint)guardEventId, step.Value);
                    else
                        sc.SendPMDGEventViaTransmitWithTarget((uint)switchEventId, (uint)step.Value);
                }
                return true;
            }
            catch (Exception ex)
            {
                // The synchronous entry below is fire-and-forget, so an escaped exception
                // would vanish as an unobserved task fault with nothing in the log.
                Log.Debug("PMDG", $"FO emergency exit lights set to {target} failed: " +
                                  $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally
            {
                _lastWriteUtc = DateTime.UtcNow;
                panelGate.Release();
            }
        }
        finally { _dispatchGate.Release(); }
    }

    /// <summary>Synchronous entry for checklist CheckAction lambdas (queues the
    /// gated sequence; sequential calls still queue FIFO behind it).</summary>
    public bool SetEmerExitLights(int target)
    {
        _ = SetEmerExitLightsAsync(target);
        return true;
    }

    // ADIRU
    public bool SetAdiru(int position) => ExecuteSingle("EVT_OH_ADIRU_SWITCH", position, false, false);

    // Thrust Asym Comp
    public bool SetThrustAsymComp(int position) => ExecuteSingle("EVT_OH_THRUST_ASYM_COMP", position, false, false);

    // CVR test (momentary)
    public bool PushCvrTest() => ExecuteSingle("EVT_OH_CVR_TEST", null, false, true);

    // Cargo fire arm (both)
    public bool SetCargoFireArm(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_FIRE_CARGO_ARM_FWD", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FIRE_CARGO_ARM_AFT", position, false, false);
        return ok;
    }

    // MCP — Flight Directors (require mouse flag)
    public bool SetFDLeft(int targetOn, AircraftStateEvaluator state)
        => ToggleTo("MCP_FD_Sw_On_0", targetOn, state, "EVT_MCP_FD_SWITCH_L");
    public bool SetFDRight(int targetOn, AircraftStateEvaluator state)
        => ToggleTo("MCP_FD_Sw_On_1", targetOn, state, "EVT_MCP_FD_SWITCH_R");

    // MCP — AT Arm (require mouse flag)
    public bool SetATArmLeft(int targetOn, AircraftStateEvaluator state)
        => ToggleTo("MCP_ATArm_Sw_On_0", targetOn, state, "EVT_MCP_AT_ARM_SWITCH_L");
    public bool SetATArmRight(int targetOn, AircraftStateEvaluator state)
        => ToggleTo("MCP_ATArm_Sw_On_1", targetOn, state, "EVT_MCP_AT_ARM_SWITCH_R");

    // The FD and A/T ARM switches are mouse-flag TOGGLES: pressed only on a READ state that
    // differs from the target. Unread (NaN, before the first CDA snapshot) once read as "off",
    // so a tick pressed an FD that was already on, and switched it off. Unread does nothing and
    // returns false; the checklist's grace then reports the line if the state never agrees.
    private bool ToggleTo(string field, int targetOn, AircraftStateEvaluator state, string eventName)
    {
        double v = state.GetValue(field);
        if (double.IsNaN(v)) return false;
        if ((v > 0.5 ? 1 : 0) == targetOn) return true;
        return ExecuteSingle(eventName, null, true, false);
    }

    // MCP — LNAV/VNAV (momentary push)
    public bool PushLNAV() => ExecuteSingle("EVT_MCP_LNAV_SWITCH", null, false, true);
    public bool PushVNAV() => ExecuteSingle("EVT_MCP_VNAV_SWITCH", null, false, true);

    // MCP — Autopilot CMD (left seat — momentary push)
    public bool PushAPCmd() => ExecuteSingle("EVT_MCP_AP_L_SWITCH", null, false, true);

    // EFIS mode / range
    public bool SetEFISModeCapt(int position) => ExecuteSingle("EVT_EFIS_CPT_MODE", position, false, false);
    public bool SetEFISModeFO(int position)   => ExecuteSingle("EVT_EFIS_FO_MODE", position, false, false);
    public bool SetEFISRangeCapt(int position) => ExecuteSingle("EVT_EFIS_CPT_RANGE", position, false, false);
    public bool SetEFISRangeFO(int position)  => ExecuteSingle("EVT_EFIS_FO_RANGE", position, false, false);

    // EFIS Baro STD toggle (momentary push-button, toggles between STD and QNH). Pushed only through
    // Pmdg777TransitionStd's decision: 29.92 alone cannot tell STD from a QNH of 29.92.
    public bool PushBaroSTDCapt() => ExecuteSingle("EVT_EFIS_CPT_BARO_STD", null, false, true);
    public bool PushBaroSTDFO()   => ExecuteSingle("EVT_EFIS_FO_BARO_STD",  null, false, true);

    // Autobrake
    // 0=RTO, 1=Off, 2=1, 3=2, 4=3, 5=4, 6=Auto
    public bool SetAutobrake(int position) => ExecuteSingle("EVT_ABS_AUTOBRAKE_SELECTOR", position, false, false);

    // Gear lever
    // 0=Up, 1=Down. UP is sent only when airborne (DispatchCoreAsync).
    public bool SetGearLever(int position) => ExecuteSingle(GearLeverEvent, position, false, false);

    private const string GearLeverEvent = "EVT_GEAR_LEVER";

    // SIM ON GROUND READ as airborne; unread (no cache yet) is not airborne.
    private bool IsDefinitelyAirborneNow() =>
        _simConnect?.GetCachedVariableValue(AircraftStateEvaluator.OnGroundKey) is double v
        && !double.IsNaN(v) && v < 0.5;

    // Flap lever (use position-specific events). The per-detent
    // EVT_CONTROL_STAND_FLAPS_LEVER_<deg> events are SDK mouse-click events that
    // expect MOUSE_FLAG_LEFTSINGLE as the CDA parameter — the same convention the
    // panel's proven FCTL_Flaps dispatch uses (PMDG777Definition.HandleUIVariableSet
    // 2f: "they expect MOUSE_FLAG_LEFTSINGLE ... matching the SDK's mouse-click
    // convention"). The old isMomentary param=1 shape was silently ignored by the
    // SDK, which is why FO auto-flaps never moved the lever.
    public bool SetFlapsPosition(int position)
    {
        string? eventName = position switch
        {
            0 => "EVT_CONTROL_STAND_FLAPS_LEVER_0",
            1 => "EVT_CONTROL_STAND_FLAPS_LEVER_1",
            2 => "EVT_CONTROL_STAND_FLAPS_LEVER_5",
            3 => "EVT_CONTROL_STAND_FLAPS_LEVER_15",
            4 => "EVT_CONTROL_STAND_FLAPS_LEVER_20",
            5 => "EVT_CONTROL_STAND_FLAPS_LEVER_25",
            6 => "EVT_CONTROL_STAND_FLAPS_LEVER_30",
            _ => null
        };
        if (eventName == null) return false;
        return ExecuteSingle(eventName, null, usesMouseFlag: true, isMomentary: false);
    }

    // Speedbrake lever
    public bool SetSpeedbrakeDown()  => ExecuteSingle("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", null, false, true);
    public bool SetSpeedbrakeArmed() => ExecuteSingle(SpeedbrakeArmEvent, null, false, true);

    private const string SpeedbrakeArmEvent = "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM";

    /// <summary>The verified arm waits this long for the lever to reach ARM: it takes about five
    /// seconds from DOWN (ten end to end, measured 2026-09-30) and rides the 1 Hz L-var batch.
    /// A flow step's own verify reads only 600 ms after dispatch, which is why this waits.</summary>
    public const int SpeedbrakeArmVerifyMs = 8000;
    private const int SpeedbrakeArmPollMs = 250;

    private double SpeedbrakeLever() =>
        _simConnect?.GetCachedVariableValue(SpeedbrakeLeverState.Pmdg777.LeverKey) ?? double.NaN;

    // The 777 SDK has no speed-brake ARMED or EXTENDED light: the lever alone decides.
    private SpeedbrakeArmDecision SpeedbrakeArmDecisionNow() =>
        SpeedbrakeLeverState.DecideArm(Pmdg777SpeedbrakeLever.Position(SpeedbrakeLever()),
            armedLightLit: true, extendedLightLit: false);

    /// <summary>
    /// Speed brake to ARM, verified: an armed lever is left as it is (true), a deployed or unread
    /// one is never clicked (false — the flow's leave-alone rule has normally spoken already),
    /// otherwise ARM is clicked and the lever must reach ARM within
    /// <see cref="SpeedbrakeArmVerifyMs"/>. The wait runs outside the dispatch gate so a hand-tick
    /// elsewhere is not held behind it.
    /// </summary>
    public async Task<bool> ArmSpeedbrakeAsync()
    {
        if (!IsAvailable) return false;
        var decision = SpeedbrakeArmDecisionNow();
        if (decision == SpeedbrakeArmDecision.AlreadyArmed) return true;
        if (decision != SpeedbrakeArmDecision.Arm)
        {
            Log.Debug("FirstOfficer", $"777 speedbrake arm not attempted: {decision}.");
            return false;
        }
        if (!await DispatchAsync(SpeedbrakeArmEvent, null, false, true)) return false;

        var deadline = DateTime.UtcNow.AddMilliseconds(SpeedbrakeArmVerifyMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Pmdg777SpeedbrakeLever.IsArmed(SpeedbrakeLever())) return true;
            await Task.Delay(SpeedbrakeArmPollMs);
        }
        bool armed = Pmdg777SpeedbrakeLever.IsArmed(SpeedbrakeLever());
        if (!armed) Log.Debug("FirstOfficer", $"777 speedbrake arm: the lever did not reach ARM ({SpeedbrakeLever()}).");
        return armed;
    }

    // Parking brake
    public bool SetParkingBrake(int position) => ExecuteSingle("EVT_CONTROL_STAND_PARK_BRAKE_LEVER", position, false, false);

    // Transponder mode via the PMDG TCAS-mode event (NOT the stock XPNDR_SET, which
    // sets the squawk CODE — mode 2 there wrote squawk 0002). Values:
    // 0=STBY, 1=ALT-OFF, 2=XPNDR, 3=TA, 4=TA/RA
    public bool SetTransponderMode(int mode)
        => ExecuteSingle("EVT_TCAS_MODE", mode, false, false);

    // Wipers
    public bool SetWipers(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_WIPER_LEFT_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_WIPER_RIGHT_SWITCH", position, false, false);
        return ok;
    }

    // Alternate flaps selector — FCTL_AltnFlaps_Control_Sw: 0 RET, 1 OFF, 2 EXT (PMDG_777X_SDK.h).
    public const int AltFlapsOff = 1;
    public bool SetAltFlaps(int position)
        => ExecuteSingle("EVT_ALTN_FLAPS_POS", position, false, false);

    // Display select panel — only the buttons BA Assist can reliably control
    // CANC/RCL (useful for flow completion check)
    public bool PushCancelRecall() => ExecuteSingle("EVT_DSP_CANC_RCL_SWITCH", null, false, true);

    // Fuel jettison — nozzles + arm OFF (mirrors the Cockpit Prep flow's CP_JETT
    // steps; the paced gate frame-spaces the three writes).
    public bool SetFuelJettisonOff()
    {
        bool ok = ExecuteSingle("EVT_OH_FUEL_JETTISON_NOZZLE_L", 0, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_JETTISON_NOZZLE_R", 0, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_JETTISON_ARM", 0, false, false);
        return ok;
    }

    // Fuel crossfeed valves (forward + aft)
    public bool SetCrossfeeds(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_FUEL_CROSSFEED_FORWARD", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_CROSSFEED_AFT", position, false, false);
        return ok;
    }

    /// <summary>Map SimBrief takeoff-flap DEGREES (1/5/15/20/25) to the 777 lever
    /// position index SetFlapsPosition takes (0=UP,1=1,2=5,3=15,4=20,5=25,6=30).</summary>
    public static int FlapDegreesToPosition(int degrees) => degrees switch
    {
        1 => 1, 5 => 2, 15 => 3, 20 => 4, 25 => 5, 30 => 6,
        _ => 2, // default: flaps 5 — the common 777 takeoff setting
    };
}

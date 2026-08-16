using System.Diagnostics;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

/// <summary>
/// iFly 737 MAX8 First Officer automation. Same 737 SOP behaviours as
/// <see cref="MSFSBlindAssist.FirstOfficer.PMDG737.FOAutoManager"/> — that class is the
/// template this one ports; only the transport differs (a polled SDK snapshot read through
/// <see cref="IFly737StateEvaluator"/> instead of a PMDG CDA broadcast). What this class does:
/// the 737-specific LNAV/VNAV push at a FIXED 400 ft AGL (annunciator-guarded, gated on
/// FOAutoApEnabled — gear and the generic AP-engage are the universal
/// <see cref="MSFSBlindAssist.Automation.UniversalAutomationService"/>'s job, not this class's),
/// and Boeing SOP center-tank pump management (opt-in via FOAutoCenterPumpsEnabled) through the
/// shared <see cref="CenterFuelPumpAutomation"/> policy. There is no 737 auto-flap schedule
/// (removed 2026-07-08, user decision — do not reintroduce), so AutoFlapsEnabled is stored but
/// never acted on, matching the PMDG template exactly.
///
/// Update() is called only on the FO background-timer thread (not thread-safe in general — the
/// center-pump policy holds interdependent, non-atomic latch/accumulator state).
/// </summary>
public class IFly737FOAutoManager : IFoAutoManager
{
    private readonly IFly737ActionExecutor _executor;
    private readonly IFly737StateEvaluator _state;
    private readonly ScreenReaderAnnouncer _announcer;

    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on (no 737 auto-flap schedule)

    private bool _lnavVnavEngagedThisLeg; // one-shot: LNAV/VNAV pushes at 400 ft AGL
    private bool _wasOnGround = true;
    private readonly CenterFuelPumpAutomation _centerPumps = new();
    private readonly CenterPumpDiagnostics    _centerPumpLog = new("IFLY737");

    // Wall-clock elapsed-time measurement for the center-pump policy's wall-clock windows —
    // Update() is driven by the FO background timer, a variable-rate feed, not a fixed
    // per-frame tick.
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastCenterPumpsMs;
    private bool   _centerPumpsClockPrimed;

    public IFly737FOAutoManager(
        IFly737ActionExecutor executor,
        IFly737StateEvaluator state,
        ScreenReaderAnnouncer announcer)
    {
        _executor  = executor;
        _state     = state;
        _announcer = announcer;
    }

    public void Reset()
    {
        _lnavVnavEngagedThisLeg = false;
        _wasOnGround            = true;
        _centerPumps.Reset();
        _centerPumpsClockPrimed = false;
    }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        UpdateCenterPumps(onGround);

        // --- Ground-to-air transition resets ---
        if (onGround)
        {
            if (!_wasOnGround)
                _lnavVnavEngagedThisLeg = false;   // touchdown: re-arm for the next takeoff
            _wasOnGround = true;
            return;
        }
        _wasOnGround = false;

        // LNAV/VNAV follows the AP-engage opt-in (the generic AP-engage itself is the
        // universal service's job; this is the 737-specific SOP mode selection on top).
        if (SettingsManager.Current.FOAutoApEnabled)
            CheckLnavVnav(altitudeAgl, verticalSpeedFpm > 200);
    }

    // 737 SOP: select LNAV/VNAV at 400 ft AGL (fixed height, deliberately independent of the
    // configurable AP-engage altitude). The MCP mode buttons are TOGGLES on this airframe too
    // (LNAV_Switch_Status / VNAV_Switch_Status — a 0-5 switch+light composite) — press only a
    // mode whose annunciator is DEFINITIVELY unlit; see ShouldPushMcpMode for the NaN guard.
    private void CheckLnavVnav(double agl, bool climbing)
    {
        if (double.IsNaN(agl)) return;   // no AGL response yet — NaN < 400 is false, but state this positively
        if (_lnavVnavEngagedThisLeg || !climbing || agl < 400) return;

        double lnavRead = _state.GetValue("LNAV_Switch_Status");
        double vnavRead = _state.GetValue("VNAV_Switch_Status");
        bool pushLnav = ShouldPushMcpMode(lnavRead);
        bool pushVnav = ShouldPushMcpMode(vnavRead);

        if (pushLnav) _ = _executor.PushLNAV();
        if (pushVnav) _ = _executor.PushVNAV();

        if (pushLnav || pushVnav)
        {
            string modes = pushLnav && pushVnav ? "LNAV and VNAV"
                         : pushLnav             ? "LNAV"
                         :                        "VNAV";
            _announcer.AnnounceImmediate($"400 feet. {modes} engaged.");
        }

        // Only spend the one-shot when the decision was actually informed — an unreadable
        // snapshot (both reads NaN) must never burn the leg's only chance at LNAV/VNAV with no
        // announcement telling the pilot why. See ShouldBurnLnavVnavLatch.
        if (ShouldBurnLnavVnavLatch(pushLnav, pushVnav, lnavRead, vnavRead))
            _lnavVnavEngagedThisLeg = true;
    }

    /// <summary>Pure latch-burn guard for the LNAV/VNAV one-shot: burn the leg's single 400 ft
    /// AGL attempt only when this tick's decision was actually informed — either a push fired
    /// (definitively unlit, now pressed), or BOTH annunciators came back genuinely known (never
    /// NaN) and were simply already lit, so there was nothing left to do this leg. If EITHER
    /// read is still unknown and no push fired, the tick decided nothing — leave the latch
    /// unset so the next tick can retry once the snapshot becomes readable. Internal and static
    /// for the same construction reason as <see cref="ShouldPushMcpMode"/> — pure function of
    /// the tick's already-computed values, no instance needed.</summary>
    internal static bool ShouldBurnLnavVnavLatch(bool pushedLnav, bool pushedVnav, double lnavRead, double vnavRead) =>
        pushedLnav || pushedVnav || (!double.IsNaN(lnavRead) && !double.IsNaN(vnavRead));

    /// <summary>Pure MCP-mode push guard: press only when the annunciator reads DEFINITIVELY
    /// unlit. NaN (no SDK snapshot yet / field unreadable) must SKIP the push, never be treated
    /// the same as "unlit" — <see cref="IFly737FoComposition.Lit"/> already classifies NaN as
    /// not-lit (it returns false for NaN), so a bare <c>!evaluator.IsLit(field)</c> cannot tell
    /// "confirmed off" apart from "unknown"; this checks the raw value for NaN FIRST, before
    /// asking whether it is lit. Internal and static (a pure function of the raw value, no
    /// instance needed) so <c>IFly737AutoManagerTests</c> can pin the NaN/lit/unlit truth table
    /// without constructing this class — <see cref="ScreenReaderAnnouncer"/> has no parameterless
    /// constructor and must never be instantiated a second time.</summary>
    internal static bool ShouldPushMcpMode(double rawAnnunciatorValue) =>
        !double.IsNaN(rawAnnunciatorValue) && !IFly737FoComposition.Lit(rawAnnunciatorValue);

    // Boeing SOP center-tank pump management (opt-in). Arms ON during ground setup with center
    // fuel loaded (wing pumps already on); switches OFF when the center low-press lights latch
    // dry (M-2/M-3/M-4 — see FuelSystemLogic: the center annunciator is gated on its own switch,
    // the wing annunciator tracks output pressure, and no reasoning transfers between the two
    // families).
    private void UpdateCenterPumps(bool onGround)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        double elapsedMs = _centerPumpsClockPrimed ? now - _lastCenterPumpsMs : 0;
        _lastCenterPumpsMs = now;
        _centerPumpsClockPrimed = true;

        bool enabled   = SettingsManager.Current.FOAutoCenterPumpsEnabled;
        bool dataReady = _state.IsDataReady;
        var (qty, pumpsOn, dry, credible, wingOn) = ReadCenterPumpInputs(_state);

        var action = _centerPumps.Update(
            enabled:       enabled,
            dataReady:     dataReady,
            onGround:      onGround,
            centerQtyLbs:  qty,
            centerPumpsOn: pumpsOn,
            centerTankDry: dry,
            systemCredible:credible,
            wingPumpsOn:   wingOn,
            rawElapsedMs:  elapsedMs);

        _centerPumpLog.Record(enabled, dataReady, onGround, qty, pumpsOn, dry, credible,
                              wingOn, elapsedMs, action, _centerPumps.Diagnostics);

        switch (action)
        {
            case CenterFuelPumpAutomation.Action.TurnOn:
                _ = _executor.SetCenterFuelPumps(1);
                _announcer.AnnounceImmediate("Center fuel pumps on.");
                break;
            case CenterFuelPumpAutomation.Action.TurnOff:
                _ = _executor.SetCenterFuelPumps(0);
                _announcer.AnnounceImmediate("Center tank low. Center fuel pumps off.");
                break;
        }
    }

    /// <summary>Composes the shared center-pump policy's five inputs from the live evaluator:
    /// centre quantity (lb), whether either centre pump is switched on, the CenterTankDry
    /// composite (M-2), the FuelSystemCredible composite (M-3), and whether all four wing pumps
    /// are on. Field names verified against <c>IFlySdkFields.cs</c> (Fuel_CENTER_L/R_Switch_Status,
    /// LOW_PRESSURE_CENTER_L/R_Light_Status, Fuel_L/R_FWD/AFT_Switch_Status,
    /// LOW_PRESSURE_L/R_FWD/AFT_Light_Status). Internal and static (takes the evaluator rather
    /// than reading <c>this</c>) so <c>IFly737AutoManagerTests</c> can drive it from a
    /// snapshot-seeded <see cref="IFly737StateEvaluator"/> with no live SDK client and without
    /// constructing this class.</summary>
    internal static (double qty, bool pumpsOn, bool dry, bool credible, bool wingOn)
        ReadCenterPumpInputs(IFly737StateEvaluator state)
    {
        double qty = state.CenterQtyLbs();

        bool ctrL = state.IsOn("Fuel_CENTER_L_Switch_Status");
        bool ctrR = state.IsOn("Fuel_CENTER_R_Switch_Status");
        bool pumpsOn = ctrL || ctrR;
        bool dry = FuelSystemLogic.CenterTankDry(ctrL, ctrR,
            state.IsOn("LOW_PRESSURE_CENTER_L_Light_Status"),
            state.IsOn("LOW_PRESSURE_CENTER_R_Light_Status"));

        bool lFwd = state.IsOn("Fuel_L_FWD_Switch_Status");
        bool rFwd = state.IsOn("Fuel_R_FWD_Switch_Status");
        bool lAft = state.IsOn("Fuel_L_AFT_Switch_Status");
        bool rAft = state.IsOn("Fuel_R_AFT_Switch_Status");
        bool credible = FuelSystemLogic.FuelSystemCredible(
            lFwd, state.IsOn("LOW_PRESSURE_L_FWD_Light_Status"),
            rFwd, state.IsOn("LOW_PRESSURE_R_FWD_Light_Status"),
            lAft, state.IsOn("LOW_PRESSURE_L_AFT_Light_Status"),
            rAft, state.IsOn("LOW_PRESSURE_R_AFT_Light_Status"));
        // Delegate to the evaluator's own predicate (widened to internal for this call) rather
        // than re-deriving "all four wing pumps on" here — one definition of the predicate,
        // matching the PMDG737 adapter's FOAutoManager, which calls AircraftStateEvaluator's
        // public AreWingFuelPumpsOn() the same way.
        bool wingOn = state.AreWingFuelPumpsOn();

        return (qty, pumpsOn, dry, credible, wingOn);
    }
}

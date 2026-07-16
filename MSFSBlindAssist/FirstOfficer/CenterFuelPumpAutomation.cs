namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Shared, sim-agnostic decision logic for automatic Boeing center-tank fuel pump
/// management (PMDG 737 + 777). Stateful across calls (low-press debounce + per-leg
/// dry-off latch). The per-aircraft FOAutoManager gathers raw inputs from its evaluator,
/// calls <see cref="Update"/> on each position-update tick (~1-2.7 Hz, driven by
/// AircraftPositionReceived — NOT a fixed per-frame rate), and actuates on the returned
/// action.
///
/// SOP: arm the center pumps ON during ground setup when center fuel is present — gated on
/// the wing pumps already being ON so it never fires cold-and-dark. Switch them OFF when
/// the center-pump LOW PRESSURE light latches on (tank dry). Once switched off dry, they
/// stay off for the rest of the leg; a turnaround refuel (center quantity rising back above
/// the arm threshold while on the ground) re-arms. All thresholds are tune-in-sim consts;
/// the timing windows are wall-clock SECONDS (not ticks) so they hold steady regardless of
/// how often the caller's position feed happens to fire.
/// </summary>
public sealed class CenterFuelPumpAutomation
{
    public enum Action { None, TurnOn, TurnOff }

    /// <summary>Center fuel (lbs) above which the tank holds usable fuel worth running the
    /// pumps. Comfortably above unusable residual.</summary>
    public const double ArmThresholdLbs = 500;
    /// <summary>Consecutive lit low-press wall-clock seconds required before OFF triggers
    /// (rejects a single-tick annunciator flicker). Wall-clock, tune-in-sim.</summary>
    public const double LowPressConfirmSeconds = 3.0;
    /// <summary>After the center pumps are observed switching on (automated or manual),
    /// skip OFF-detection for this many wall-clock seconds so the brief low-press transient
    /// while pump pressure builds cannot immediately switch off. Wall-clock, tune-in-sim.</summary>
    public const double SettleSecondsAfterOn = 10.0;

    /// <summary>Ground quantity uplift (lbs) required, above the quantity recorded at the
    /// moment the dry-off latch was set, before a reading counts as a genuine refuel and
    /// clears the latch. Comfortably above sensor/float jitter, far below any real
    /// center-tank uplift (a real center refuel is thousands of lbs).</summary>
    public const double RefuelMarginLbs = 250;

    /// <summary>Defensive per-call clamp on the caller-measured elapsed time. Rejects a
    /// first-call/sim-pause/long-hitch spike from instantly satisfying a window, while never
    /// clamping a normal ~370-1000 ms tick at the feed's real rate.</summary>
    private const double MaxElapsedMs = 2000;

    private bool   _switchedOffThisLeg;   // dry-off latch: no re-arm until refuel
    private double _qtyAtDryOff = double.NaN; // center qty recorded at the moment of dry-off; NaN = not set
    private bool   _prevPumpsOn;          // previous tick's observed centerPumpsOn (edge detect)
    private double _lowPressMs;           // consecutive lit low-press wall-clock ms
    private double _settleMs;             // remaining post-switch-on settle wall-clock ms

    public void Reset()
    {
        _switchedOffThisLeg = false;
        _qtyAtDryOff        = double.NaN;
        _prevPumpsOn        = false;
        _lowPressMs         = 0;
        _settleMs           = 0;
    }

    public Action Update(
        bool enabled, bool onGround, double centerQtyLbs,
        bool centerPumpsOn, bool centerLowPressRaw, bool wingPumpsOn, double elapsedMs)
    {
        if (!enabled)
        {
            _lowPressMs  = 0;             // don't carry stale debounce across an enable toggle
            _prevPumpsOn = centerPumpsOn; // so re-enabling sees no phantom edge
            return Action.None;
        }

        elapsedMs = System.Math.Clamp(elapsedMs, 0, MaxElapsedMs);

        // Refuel detection: clear the dry-off latch only on a GENUINE ground uplift above the
        // quantity recorded when the latch was set. Inferring a refuel from "qty is now above the
        // arm threshold" is unsound — after a dry-off the tank stops draining, so the quantity
        // freezes at whatever lit the annunciator and the test would pass vacuously, oscillating
        // the pumps on/off forever.
        if (onGround && _switchedOffThisLeg && !double.IsNaN(_qtyAtDryOff)
            && centerQtyLbs > _qtyAtDryOff + RefuelMarginLbs)
        {
            _switchedOffThisLeg = false;
            _qtyAtDryOff        = double.NaN;
        }

        // Decay the settle window first, THEN check for a rising edge — a fresh edge this
        // tick must arm the FULL window, not the window minus one tick's decay.
        _settleMs = System.Math.Max(0, _settleMs - elapsedMs);

        // Rising edge on the OBSERVED switch state (automation-driven or manual) starts the
        // settle window uniformly — the spin-up transient is identical regardless of who
        // threw the switch.
        if (centerPumpsOn && !_prevPumpsOn)
        {
            _settleMs = SettleSecondsAfterOn * 1000;
        }
        _prevPumpsOn = centerPumpsOn;

        _lowPressMs = centerLowPressRaw ? _lowPressMs + elapsedMs : 0;
        bool lowPressLatched = _lowPressMs >= LowPressConfirmSeconds * 1000;

        // OFF: pumps running and the tank has latched dry (any phase). Suppressed during
        // the post-switch-on settle window.
        if (centerPumpsOn && lowPressLatched && _settleMs <= 0)
        {
            _switchedOffThisLeg = true;
            _qtyAtDryOff        = centerQtyLbs;
            _lowPressMs         = 0;
            return Action.TurnOff;
        }

        // ON: ground setup only, fuel present, pumps off, not already dry-latched, and the
        // fuel-panel setup has begun (wing pumps on). No settle-window assignment here — the
        // rising-edge detector above starts the window once the readback confirms the pumps
        // are actually on.
        if (onGround && !centerPumpsOn && !_switchedOffThisLeg
            && centerQtyLbs > ArmThresholdLbs && wingPumpsOn)
        {
            return Action.TurnOn;
        }

        return Action.None;
    }
}

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Shared, sim-agnostic decision logic for automatic Boeing center-tank fuel pump
/// management (PMDG 737 + 777). Stateful across calls (low-press debounce + per-leg
/// dry-off latch). The per-aircraft FOAutoManager gathers raw inputs from its evaluator,
/// calls <see cref="Update"/> each frame, and actuates on the returned action.
///
/// SOP: arm the center pumps ON during ground setup when center fuel is present — gated on
/// the wing pumps already being ON so it never fires cold-and-dark. Switch them OFF when
/// the center-pump LOW PRESSURE light latches on (tank dry). Once switched off dry, they
/// stay off for the rest of the leg; a turnaround refuel (center quantity rising back above
/// the arm threshold while on the ground) re-arms. All thresholds are tune-in-sim consts.
/// </summary>
public sealed class CenterFuelPumpAutomation
{
    public enum Action { None, TurnOn, TurnOff }

    /// <summary>Center fuel (lbs) above which the tank holds usable fuel worth running the
    /// pumps. Comfortably above unusable residual.</summary>
    public const double ArmThresholdLbs = 500;
    /// <summary>Consecutive lit low-press ticks required before OFF triggers (rejects a
    /// single-frame annunciator flicker). Tick-based ≈ a few FO Update calls.</summary>
    public const int LowPressConfirmTicks = 3;
    /// <summary>After arming ON, skip OFF-detection for this many ticks so the brief
    /// low-press transient while pump pressure builds cannot immediately switch off.</summary>
    public const int SettleTicksAfterOn = 10;

    private bool _switchedOffThisLeg;   // dry-off latch: no re-arm until refuel
    private bool _belowArmSeen;         // center qty has dropped below the arm threshold
    private int  _lowPressTicks;        // consecutive lit low-press ticks
    private int  _settleTicks;          // remaining post-arm settle ticks

    public void Reset()
    {
        _switchedOffThisLeg = false;
        _belowArmSeen       = false;
        _lowPressTicks      = 0;
        _settleTicks        = 0;
    }

    public Action Update(
        bool enabled, bool onGround, double centerQtyLbs,
        bool centerPumpsOn, bool centerLowPressRaw, bool wingPumpsOn)
    {
        if (!enabled)
        {
            _lowPressTicks = 0;   // don't carry stale debounce across an enable toggle
            return Action.None;
        }

        // Refuel tracking: once center fuel drops below the arm threshold, a later rise back
        // above it while on the ground is a turnaround refuel → clear the dry-off latch.
        if (centerQtyLbs < ArmThresholdLbs)
        {
            _belowArmSeen = true;
        }
        else if (onGround && _belowArmSeen && _switchedOffThisLeg)
        {
            _switchedOffThisLeg = false;
            _belowArmSeen       = false;
        }

        _lowPressTicks = centerLowPressRaw ? _lowPressTicks + 1 : 0;
        bool lowPressLatched = _lowPressTicks >= LowPressConfirmTicks;

        if (_settleTicks > 0) _settleTicks--;

        // OFF: pumps running and the tank has latched dry (any phase). Suppressed during
        // the post-arm settle window.
        if (centerPumpsOn && lowPressLatched && _settleTicks == 0)
        {
            _switchedOffThisLeg = true;
            _lowPressTicks      = 0;
            return Action.TurnOff;
        }

        // ON: ground setup only, fuel present, pumps off, not already dry-latched, and the
        // fuel-panel setup has begun (wing pumps on).
        if (onGround && !centerPumpsOn && !_switchedOffThisLeg
            && centerQtyLbs > ArmThresholdLbs && wingPumpsOn)
        {
            _settleTicks = SettleTicksAfterOn;
            return Action.TurnOn;
        }

        return Action.None;
    }
}

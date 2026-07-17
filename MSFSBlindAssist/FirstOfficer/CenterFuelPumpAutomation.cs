using System;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Shared, sim-agnostic decision logic for automatic Boeing center-tank fuel pump management
/// (PMDG 737 + 777). PURE (no SimConnect / aircraft deps). Stateful across calls: a debounced
/// low-press dry-off, a settle window on the observed switch-on edge, a per-leg dry-off latch,
/// a user-intent (manual-off) latch, a monotonic refuel floor, and a pending-command latch that
/// stops re-issuing a write until its readback lands (or a 30 s failure-path timeout). Timing
/// windows are wall-clock SECONDS. See docs/superpowers/specs/2026-07-15-center-pump-corrective-redesign.md.
///
/// SOP: arm the center pumps ON during ground setup when center fuel is present, gated on the
/// wing pumps already being ON (so it never fires cold-and-dark and never in preflight). Switch
/// them OFF when the center tank latches dry (low-press confirmed while the wing pumps prove the
/// system is credible). Once off dry, they stay off for the leg; a genuine ground refuel above
/// the recorded floor + margin re-arms them, as does anyone switching the pumps back on.
/// </summary>
public sealed class CenterFuelPumpAutomation
{
    public enum Action { None, TurnOn, TurnOff }
    private enum Pending { None, On, Off }

    /// <summary>Center fuel (lbs) above which the tank holds usable fuel worth running the
    /// pumps. Also the shared executor ON-gate threshold (§6).</summary>
    public const double ArmThresholdLbs = 500;
    /// <summary>Consecutive confirmed-dry wall-clock seconds required before OFF triggers.</summary>
    public const double LowPressConfirmSeconds = 3.0;
    /// <summary>Skip OFF-detection for this many seconds after an observed switch-on rising edge
    /// (spin-up transient). Empirical (≈6× over M-6's 1.74 s engines-off lower bound); tune-in-sim.</summary>
    public const double SettleSecondsAfterOn = 10.0;
    /// <summary>Ground uplift (lbs) above the recorded floor before a reading counts as a refuel.</summary>
    public const double RefuelMarginLbs = 250;

    /// <summary>Per-tick clamp on caller elapsed time (rejects a first-call/pause/hitch spike).
    /// Bounds a single tick's contribution to a window.</summary>
    private const double MaxElapsedMs = 2000;
    /// <summary>Elapsed above which observation continuity is declared broken (M-4/R-M4).
    /// DELIBERATELY ≠ MaxElapsedMs: keying the gap on the clamp would mark every sustained
    /// sub-0.5 Hz tick a gap → phantom rising edge every tick → OFF could never fire.</summary>
    private const double ObservationGapMs = 5000;
    /// <summary>Un-sticks a pending command on a lost readback. Failure-path bound only,
    /// sized for the 20 s+ dispatch-gate tail.</summary>
    private const double CommandConfirmSeconds = 30.0;

    // Observation state — physical reality; runs regardless of `enabled`; NOT touched by ClearPolicyLatches().
    private bool   _prevPumpsOn;
    private double _settleMs;
    private double _lowPressMs;
    private bool   _lastCommandedOff;   // edge attribution (M3): set by TurnOff; cleared by any rising edge.

    // Policy state — decisions; cleared by ClearPolicyLatches().
    private bool    _switchedOffThisLeg;
    private double  _qtyFloor = double.NaN;   // refuel reference; NaN iff no latch is set (enforced, step 9).
    private bool    _manualOffLatch;
    private Pending _pendingCommand = Pending.None;
    private double  _pendingMs;

    // Edge tracking.
    private bool _prevEnabled;

    /// <summary>Full reset (aircraft switch / adapter Reset). No production call site otherwise.</summary>
    public void Reset()
    {
        _prevPumpsOn      = false;
        _settleMs         = 0;
        _lowPressMs       = 0;
        _lastCommandedOff = false;
        _prevEnabled      = false;
        ClearPolicyLatches();
    }

    // Clears ONLY the policy group (the enable-edge + refuel clear + the OFF/ON decisions use it).
    private void ClearPolicyLatches()
    {
        _switchedOffThisLeg = false;
        _qtyFloor           = double.NaN;
        _manualOffLatch     = false;
        _pendingCommand     = Pending.None;
        _pendingMs          = 0;
    }

    // Idempotent floor seed; first latch wins. Guards int.MinValue / negatives (F13 defence-in-depth).
    private void SeedFloor(double q)
    {
        if (double.IsNaN(_qtyFloor) && !double.IsNaN(q) && !double.IsInfinity(q) && q >= 0)
            _qtyFloor = q;
    }

    public Action Update(
        bool enabled, bool dataReady, bool onGround, double centerQtyLbs,
        bool centerPumpsOn, bool centerTankDry, bool systemCredible, bool wingPumpsOn,
        double rawElapsedMs)
    {
        // 0. gap + clamp
        bool gap = rawElapsedMs > ObservationGapMs;
        double elapsedMs = Math.Clamp(rawElapsedMs, 0, MaxElapsedMs);

        // 1. enable-edge clear — BEFORE any decision; needs no data; runs unconditionally.
        if (enabled && !_prevEnabled) ClearPolicyLatches();
        _prevEnabled = enabled;

        // 2. settle decays — physics passes whether observed or not.
        _settleMs = Math.Max(0, _settleMs - elapsedMs);

        // 3. cannot observe → touch NO latch, NO pending; force a fresh settle when data returns.
        if (!dataReady)
        {
            _lowPressMs  = 0;
            _prevPumpsOn = false;
            return Action.None;   // _pendingMs is NOT accrued (I2)
        }

        // 4. pending accrual — AFTER the !dataReady return (I2).
        if (_pendingCommand != Pending.None) _pendingMs += elapsedMs;

        // 5. a gap breaks the debounce's "unbroken run" contract — BEFORE edge detect.
        if (gap) { _prevPumpsOn = false; _lowPressMs = 0; }

        // 6. edges.
        bool rising  =  centerPumpsOn && !_prevPumpsOn;
        bool falling = !centerPumpsOn &&  _prevPumpsOn;
        if (rising)
        {
            _settleMs         = SettleSecondsAfterOn * 1000;
            _lastCommandedOff = false;   // pumps are back on; the old Off is history
            _manualOffLatch   = false;   // C-A: someone re-armed by hand; the old off-intent is stale
        }
        _prevPumpsOn = centerPumpsOn;
        _lowPressMs  = (centerTankDry && systemCredible) ? _lowPressMs + elapsedMs : 0;
        bool dryLatched = _lowPressMs >= LowPressConfirmSeconds * 1000;

        // 7. pending resolution.
        if (_pendingCommand == Pending.On && centerPumpsOn) _pendingCommand = Pending.None;
        else if (_pendingCommand == Pending.Off && !centerPumpsOn) _pendingCommand = Pending.None;
        else if (_pendingCommand != Pending.None && _pendingMs >= CommandConfirmSeconds * 1000)
            _pendingCommand = Pending.None;

        // 8. user-intent latch — a falling edge we did not command, while the wing pumps are on.
        if (falling && !_lastCommandedOff && wingPumpsOn)
        {
            _manualOffLatch = true;
            SeedFloor(centerQtyLbs);
        }

        // 9. refuel floor ratchet + latch clear (inert unless a latch is set); then structural
        //    NaN-iff-no-latch so a rising-edge latch clear can't leave a stale floor behind.
        bool anyLatch = _switchedOffThisLeg || _manualOffLatch;
        if (anyLatch && !double.IsNaN(_qtyFloor) && !double.IsNaN(centerQtyLbs)
            && centerQtyLbs >= 0 && centerQtyLbs < _qtyFloor)
            _qtyFloor = centerQtyLbs;                                   // ratchet monotonically DOWN
        if (onGround && anyLatch && !double.IsNaN(_qtyFloor)
            && centerQtyLbs > _qtyFloor + RefuelMarginLbs)
            ClearPolicyLatches();                                       // a refuel tick can now arm below
        if (!(_switchedOffThisLeg || _manualOffLatch))
            _qtyFloor = double.NaN;

        // 10. decision gate (enabled + pending).
        if (!enabled) return Action.None;
        if (_pendingCommand != Pending.None) return Action.None;

        // 11. OFF — pumps running and confirmed dry, ANY phase, no onGround gate.
        if (centerPumpsOn && dryLatched && _settleMs <= 0)
        {
            _switchedOffThisLeg = true;
            SeedFloor(centerQtyLbs);
            _lowPressMs         = 0;
            _lastCommandedOff   = true;
            _pendingCommand     = Pending.Off; _pendingMs = 0;
            return Action.TurnOff;
        }

        // 12. ON — ground setup only.
        if (onGround && !centerPumpsOn && !_switchedOffThisLeg && !_manualOffLatch
            && centerQtyLbs > ArmThresholdLbs && wingPumpsOn)
        {
            _lastCommandedOff = false;
            _pendingCommand   = Pending.On; _pendingMs = 0;
            return Action.TurnOn;
        }

        // 13.
        return Action.None;
    }
}

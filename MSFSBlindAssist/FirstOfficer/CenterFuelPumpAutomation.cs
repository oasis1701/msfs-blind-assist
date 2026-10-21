using System;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Shared, sim-agnostic decision logic for automatic Boeing center-tank fuel pump management
/// (PMDG 737 + 777 + iFly MAX8). PURE (no SimConnect / aircraft deps). Stateful across calls:
/// a short continuous below-threshold confirm, a per-leg dry-off latch, a user-intent
/// (manual-off) latch, a monotonic refuel floor, and a pending-command latch that stops
/// re-issuing a write until its readback lands (or a 30 s failure-path timeout). Timing
/// windows are wall-clock SECONDS. See docs/superpowers/specs/2026-08-16-center-pump-quantity-off-design.md.
///
/// The OFF trigger is QUANTITY-BASED. It replaced the low-pressure-annunciator detection
/// after a second field failure (2026-08-16 log: quantity fell 922→304→0 lbs with the pumps
/// running and the debounced dry signal never accrued a second of evidence — the PMDG
/// annunciators are not reliably observable at the ~1 Hz sample rate). FUEL quantity from the
/// aircraft's own data (CDA / iFly SDK) is monotone and reliable in the same log; do not
/// reintroduce an annunciator term.
///
/// SOP: arm the center pumps ON during ground setup when the center tank holds meaningfully
/// more than the empty threshold (ArmThresholdLbs, gated on the wing pumps already being ON so
/// it never fires cold-and-dark). Switch them OFF — any phase — once quantity is confirmed
/// below OffThresholdLbs. Once off, they stay off for the leg; a genuine ground refuel above
/// the recorded floor + margin re-arms them, as does anyone switching the pumps back on.
/// </summary>
public sealed class CenterFuelPumpAutomation
{
    public enum Action { None, TurnOn, TurnOff }
    private enum Pending { None, On, Off }

    /// <summary>Center fuel (lbs) below which the tank is effectively EMPTY: the automation
    /// switches running pumps OFF under it, and no path switches them ON under it
    /// (CenterPumpGate + the Before-Start checklist synthetics share this const).</summary>
    public const double OffThresholdLbs = 1000;
    /// <summary>Auto-arm (ON) gate — deliberately above OffThresholdLbs so the automation can
    /// never arm pumps it would immediately switch off (the 2026-08-16 defect log shows an arm
    /// at 922 lbs). The 500 lb gap is the hysteresis.</summary>
    public const double ArmThresholdLbs = 1500;
    /// <summary>Continuous wall-clock seconds below OffThresholdLbs required before OFF fires.
    /// Quantity is stable and monotone (unlike the retired flickering annunciator), so a short
    /// CONTINUOUS confirm — reset the moment quantity reads back above — is correct; it exists
    /// only to ride out a single anomalous tick.</summary>
    public const double QtyOffConfirmSeconds = 2.0;
    /// <summary>Ground uplift (lbs) above the recorded floor before a reading counts as a refuel.</summary>
    public const double RefuelMarginLbs = 250;

    /// <summary>Per-tick clamp on caller elapsed time (rejects a first-call/pause/hitch spike).
    /// Bounds a single tick's contribution to a window. A clamped maximum-length tick equals the
    /// whole QtyOffConfirmSeconds (2 s) confirm window, so a single below-threshold sample after
    /// a &gt;=2 s hitch satisfies the confirm on that one tick — accepted design: quantity is
    /// stable, so the confirm only needs to ride out a single anomalous tick, not guard against
    /// a genuinely fast crossing.</summary>
    private const double MaxElapsedMs = 2000;
    /// <summary>Un-sticks a pending command on a lost readback. Failure-path bound only,
    /// sized for the 20 s+ dispatch-gate tail.</summary>
    private const double CommandConfirmSeconds = 30.0;

    // Observation state — physical reality; runs regardless of `enabled`; NOT touched by ClearPolicyLatches().
    private bool   _prevPumpsOn;
    private double _belowMs;            // continuous below-OffThreshold run; reset when above/invalid.
    private bool   _lastCommandedOff;   // edge attribution (M3): set by TurnOff; cleared by any rising edge.

    // Policy state — decisions; cleared by ClearPolicyLatches().
    private bool    _switchedOffThisLeg;
    private double  _qtyFloor = double.NaN;   // refuel reference; NaN iff no latch is set (enforced below).
    private bool    _manualOffLatch;
    private Pending _pendingCommand = Pending.None;
    private double  _pendingMs;

    // Edge tracking.
    private bool _prevEnabled;

    /// <summary>Internal decision state, for the `center_pumps` diagnostic log ONLY.
    /// Never branch on this string.</summary>
    public string Diagnostics =>
        $"belowMs={_belowMs:F0} "
        + $"dryOffLatch={(_switchedOffThisLeg ? 1 : 0)} manualOffLatch={(_manualOffLatch ? 1 : 0)} "
        + $"floor={(double.IsNaN(_qtyFloor) ? "-" : _qtyFloor.ToString("F0"))} pending={_pendingCommand}";

    /// <summary>Full reset (aircraft switch / adapter Reset). No production call site otherwise.</summary>
    public void Reset()
    {
        _prevPumpsOn      = false;
        _belowMs          = 0;
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
        bool centerPumpsOn, bool wingPumpsOn, double rawElapsedMs)
    {
        // 0. clamp
        double elapsedMs = Math.Clamp(rawElapsedMs, 0, MaxElapsedMs);

        // 1. enable-edge clear — BEFORE any decision; needs no data; runs unconditionally.
        if (enabled && !_prevEnabled) ClearPolicyLatches();
        _prevEnabled = enabled;

        // 2. cannot observe → touch NO latch, NO pending; confirm restarts when data returns.
        if (!dataReady)
        {
            _belowMs     = 0;
            _prevPumpsOn = false;
            return Action.None;   // _pendingMs is NOT accrued (I2)
        }

        // 3. pending accrual — AFTER the !dataReady return (I2).
        if (_pendingCommand != Pending.None) _pendingMs += elapsedMs;

        // 4. edges.
        bool rising  =  centerPumpsOn && !_prevPumpsOn;
        bool falling = !centerPumpsOn &&  _prevPumpsOn;
        if (rising)
        {
            _lastCommandedOff = false;   // pumps are back on; the old Off is history
            _manualOffLatch   = false;   // C-A: someone re-armed by hand; the old off-intent is stale
            _belowMs          = 0;       // fresh observation epoch
        }
        _prevPumpsOn = centerPumpsOn;

        // 5. below-threshold confirm — CONTINUOUS: any valid reading at/above the threshold
        //    (or an invalid one) resets it. Quantity does not flicker; this only rides out a
        //    single anomalous tick.
        //    qtyValid also rejects NaN/negative/Infinity. The PMDG adapters route NaN through
        //    SafeRoundToInt -> 0 before it reaches here, so this guard mainly protects
        //    non-PMDG callers (iFly passes NaN through unconverted).
        bool qtyValid = !double.IsNaN(centerQtyLbs) && !double.IsInfinity(centerQtyLbs) && centerQtyLbs >= 0;
        if (qtyValid && centerQtyLbs < OffThresholdLbs)
            _belowMs = Math.Min(_belowMs + elapsedMs, QtyOffConfirmSeconds * 1000);
        else
            _belowMs = 0;
        bool lowLatched = qtyValid && _belowMs >= QtyOffConfirmSeconds * 1000;

        // 6. pending resolution.
        if (_pendingCommand == Pending.On && centerPumpsOn) _pendingCommand = Pending.None;
        else if (_pendingCommand == Pending.Off && !centerPumpsOn) _pendingCommand = Pending.None;
        else if (_pendingCommand != Pending.None && _pendingMs >= CommandConfirmSeconds * 1000)
            _pendingCommand = Pending.None;

        // 7. user-intent latch — a falling edge we did not command, while the wing pumps are on.
        if (falling && !_lastCommandedOff && wingPumpsOn)
        {
            _manualOffLatch = true;
            SeedFloor(centerQtyLbs);
        }

        // 8. refuel floor ratchet + latch clear (inert unless a latch is set); then structural
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

        // 9. decision gate (enabled + pending).
        if (!enabled) return Action.None;
        if (_pendingCommand != Pending.None) return Action.None;

        // 10. OFF — pumps running and quantity confirmed below the empty threshold, ANY phase.
        //     (Deliberately does NOT read _switchedOffThisLeg — the documented trap.)
        if (centerPumpsOn && lowLatched)
        {
            _switchedOffThisLeg = true;
            SeedFloor(centerQtyLbs);
            _belowMs            = 0;
            _lastCommandedOff   = true;
            _pendingCommand     = Pending.Off; _pendingMs = 0;
            return Action.TurnOff;
        }

        // 11. ON — ground setup only, and only meaningfully above the empty threshold.
        if (onGround && !centerPumpsOn && !_switchedOffThisLeg && !_manualOffLatch
            && centerQtyLbs > ArmThresholdLbs && wingPumpsOn)
        {
            _lastCommandedOff = false;
            _pendingCommand   = Pending.On; _pendingMs = 0;
            return Action.TurnOn;
        }

        // 12.
        return Action.None;
    }
}

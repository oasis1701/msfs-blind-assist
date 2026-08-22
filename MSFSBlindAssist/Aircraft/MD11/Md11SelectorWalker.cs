using System.Collections.Concurrent;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Drives a detented MD-11 control to an absolute target position.
///
/// WHY THIS EXISTS. Every multi-position control on the MD-11 is actuated by RELATIVE step
/// events — a knob exposes WHEEL_UP/WHEEL_DOWN, a rotary switch exposes LEFT_BUTTON_DOWN /
/// RIGHT_BUTTON_DOWN (click left side / right side). None of them accept "go to position 3".
/// But MSFSBA's panels are combo boxes: the user picks a target and expects the aircraft to get
/// there. Something has to close that loop, and this is it — read state, step once, re-read,
/// repeat. It is the same shape as <c>PMDGNG3DataManager.WalkSelectorClosedLoop</c>, which
/// exists for the same reason on the PMDG NG3's detented rotaries.
///
/// WHY IT SELF-CALIBRATES. Which direction WHEEL_UP turns a given knob is not stated anywhere
/// in TFDi's exported behaviours or docs — the ModelBehaviorDefs give us the event ids and
/// nothing about their sign. Guessing costs a control that walks confidently the wrong way to
/// its end stop, silently, on an aircraft nobody can see. So the walker treats polarity as
/// UNKNOWN, assumes the conventional mapping (WHEEL_UP / left-click = increase), and watches
/// what the state var actually does after the first step. If the value moved away from the
/// target, it flips the sign and remembers that for the rest of the session, keyed on the node
/// id. Costs at most one wasted step, once per control — and removes an unverifiable guess from
/// ~200 controls.
///
/// The walk is bounded and always terminates: <see cref="MaxSteps"/> caps the step count, and a
/// step that produces no movement at all (a control that is inhibited, unpowered, or simply
/// won't budge) breaks out rather than hammering CEVENT — which TFDi explicitly asks us not to
/// overuse.
/// </summary>
public static class Md11SelectorWalker
{
    /// <summary>
    /// Step budget. The widest control in the map is well under a dozen detents, so this is
    /// generous headroom; it exists to guarantee termination when a control refuses to move
    /// (unpowered bus, guarded switch, hydraulics off), never as a normal operating limit.
    /// </summary>
    private const int MaxSteps = 24;

    /// <summary>How long to wait after a step before believing the read-back.</summary>
    private const int SettleMs = 90;

    /// <summary>
    /// Learned step polarity per node id: true = the conventional mapping (WHEEL_UP / left-click
    /// increases the state value), false = inverted. Session-scoped; a control is calibrated on
    /// its first walk and stays calibrated. Concurrent because a walk runs off the UI thread.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> Polarity = new();

    /// <summary>Values are floats from the sim; compare with a tolerance, never with ==.</summary>
    private const double Epsilon = 0.5;

    /// <summary>
    /// Walks <paramref name="control"/> to <paramref name="targetValue"/>.
    /// </summary>
    /// <param name="varKey">The MSFSBA variable key the control's state is cached under (the node id).</param>
    /// <returns>True if the control landed on the target; false on budget exhaustion or a stuck control.</returns>
    public static async Task<bool> WalkAsync(
        Md11Control control,
        double targetValue,
        string varKey,
        SimConnectManager sim,
        Md11EventBus bus,
        CancellationToken ct = default)
    {
        var (incEvent, decEvent) = StepEvents(control);
        if (incEvent == null || decEvent == null)
        {
            Log.Debug("MD11", $"{control.NodeId}: no step events — cannot walk.");
            return false;
        }

        var ordered = OrderedValues(control);
        if (ordered.Count == 0) return false;

        var targetIdx = PositionIndex(control, ordered, targetValue);

        for (var step = 0; step < MaxSteps; step++)
        {
            // Superseded by a newer selection (the user arrowed on in the combo) — stop now rather
            // than keep force-requesting the state var, which is what floods SimConnect when several
            // walks on one control run at once. Throws so SafeWalk swallows it silently: a cancelled
            // walk is not a failure to announce.
            ct.ThrowIfCancellationRequested();

            var current = await ReadAsync(varKey, sim, ct).ConfigureAwait(false);
            if (current == null)
            {
                Log.Debug("MD11", $"{control.NodeId}: state var {control.StateVar} unreadable — aborting walk.");
                return false;
            }

            var currentIdx = PositionIndex(control, ordered, current.Value);
            if (currentIdx == targetIdx) return true;

            var wantUp = targetIdx > currentIdx;
            var conventional = Polarity.GetOrAdd(control.NodeId, true);
            // conventional: inc event raises the value. Inverted: inc event lowers it.
            var eventId = (wantUp == conventional) ? incEvent.Value : decEvent.Value;

            bus.Fire(eventId);
            await Task.Delay(SettleMs, ct).ConfigureAwait(false);

            var after = await ReadAsync(varKey, sim, ct).ConfigureAwait(false);
            if (after == null) return false;

            var afterIdx = PositionIndex(control, ordered, after.Value);

            if (afterIdx == currentIdx)
            {
                // No movement. Either the control is inhibited, or it is already at an end stop
                // in the direction we asked for. Both mean stepping again is pointless.
                Log.Debug("MD11",
                    $"{control.NodeId}: step produced no movement at value {current.Value} " +
                    $"(target {targetValue}) — control may be inhibited or at an end stop.");
                return false;
            }

            var movedTowardTarget = Math.Abs(afterIdx - targetIdx) < Math.Abs(currentIdx - targetIdx);
            if (!movedTowardTarget)
            {
                // First real evidence of this control's sign, and it contradicts the assumption.
                // Flip it once and keep going; the next iteration steps the other way.
                var flipped = !conventional;
                Polarity[control.NodeId] = flipped;
                Log.Info("MD11",
                    $"{control.NodeId}: step polarity calibrated to {(flipped ? "INVERTED" : "conventional")} " +
                    $"(value went {current.Value} -> {after.Value} while walking toward {targetValue}).");
            }
        }

        Log.Debug("MD11", $"{control.NodeId}: walk to {targetValue} exhausted {MaxSteps} steps.");
        return false;
    }

    /// <summary>
    /// The (increase, decrease) CEVENT pair, under the CONVENTIONAL assumption that WHEEL_UP and
    /// a left-click step "up". <see cref="WalkAsync"/> verifies that against the aircraft and
    /// inverts if wrong — nothing here is trusted as fact.
    /// </summary>
    private static (int? inc, int? dec) StepEvents(Md11Control c)
    {
        var wheelUp = c.Event("WHEEL_UP");
        var wheelDown = c.Event("WHEEL_DOWN");
        if (wheelUp != null && wheelDown != null) return (wheelUp, wheelDown);

        // Detented rotaries and multi-position switches: clicking the left vs right half of the
        // knob steps in opposite directions.
        var left = c.Event("LEFT_BUTTON_DOWN");
        var right = c.Event("RIGHT_BUTTON_DOWN");
        if (left != null && right != null) return (left, right);

        return (null, null);
    }

    /// <summary>
    /// The control's positions, ascending. Prefers curated detents (which carry the real ordering
    /// including range-valued positions the tooltip parser cannot see) over the tooltip value map.
    /// </summary>
    public static List<double> OrderedValues(Md11Control c)
    {
        if (c.Detents is { Count: > 0 })
            return c.Detents.Select(d => d.Value).OrderBy(v => v).ToList();

        var vals = new List<double>();
        foreach (var k in c.ValueMap.Keys)
            if (double.TryParse(k, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                vals.Add(v);
        vals.Sort();
        return vals;
    }

    /// <summary>
    /// Index of the position <paramref name="value"/> currently sits in.
    ///
    /// Prefers the curated detent test over nearest-value, because nearest-value is WRONG for a
    /// range-valued detent. The flap lever is the case in point: its Dial-A-Flap detent spans
    /// FLAP_RNG 38–65 but is represented by the single value 50, so nearest-value puts everything
    /// from 60 upward (the midpoint of 50 and the next detent, 70) into "Flap 28" — i.e. a handle
    /// sitting in Dial-A-Flap with the thumbwheel toward 25° would read out, and be walked from,
    /// as though it were at 28. The range test resolves it correctly; nearest-value remains the
    /// fallback for controls with no curated detents and for a lever caught mid-travel.
    /// </summary>
    public static int PositionIndex(Md11Control control, List<double> ordered, double value)
    {
        if (control.Detents is { Count: > 0 })
        {
            var hit = control.Detents.FirstOrDefault(d => d.Matches(value));
            if (hit != null)
            {
                var idx = ordered.FindIndex(v => Near(v, hit.Value));
                if (idx >= 0) return idx;
            }
        }
        return NearestIndex(ordered, value);
    }

    /// <summary>
    /// Index of the position nearest <paramref name="value"/>. Nearest rather than exact because
    /// a lever caught mid-travel sits between detents. Prefer <see cref="PositionIndex"/>, which
    /// honours range-valued detents first.
    /// </summary>
    public static int NearestIndex(List<double> ordered, double value)
    {
        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < ordered.Count; i++)
        {
            var d = Math.Abs(ordered[i] - value);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Walks a CONTINUOUS (non-detented) control to a raw target value — the Dial-A-Flap
    /// thumbwheel being the motivating case.
    ///
    /// Detent-walking one step at a time does not work here. The thumbwheel's raw range is
    /// 0–100 spanning 10°–25°, and nothing tells us how far one wheel click moves it: if a click
    /// is one raw unit, crossing the full range is 100 clicks — four times
    /// <see cref="MaxSteps"/>, and 100 CEVENT writes at a channel TFDi asks us not to overuse.
    ///
    /// So measure instead of assume. One probe click yields a SIGNED delta, which gives both the
    /// step size and the polarity in a single observation; from there the remaining distance is
    /// arithmetic. Fire that many clicks, verify, and allow a couple of correction rounds for
    /// rounding and for clicks the aircraft dropped. Typically ~3 rounds and well under 20 writes
    /// even for a full-range move.
    /// </summary>
    /// <param name="tolerance">Raw units within which the target counts as reached.</param>
    public static async Task<bool> WalkAnalogAsync(
        Md11Control control,
        double targetRaw,
        string varKey,
        SimConnectManager sim,
        Md11EventBus bus,
        double tolerance,
        int maxRounds = 8,
        CancellationToken ct = default)
    {
        var (incEvent, decEvent) = StepEvents(control);
        if (incEvent == null || decEvent == null) return false;

        Log.Info("MD11", $"{control.NodeId}: analog walk START target={targetRaw:0.##} tol={tolerance:0.##}.");

        for (var round = 0; round < maxRounds; round++)
        {
            if (ct.IsCancellationRequested) return false;   // superseded by a newer target
            var current = await ReadAsync(varKey, sim).ConfigureAwait(false);
            if (current == null) return false;
            if (Math.Abs(current.Value - targetRaw) <= tolerance)
            {
                Log.Info("MD11", $"{control.NodeId}: reached {current.Value:0.##} (target {targetRaw:0.##}) in {round} rounds.");
                return true;
            }

            // Probe: one click in the direction we believe is "toward target", then measure what
            // actually happened. This single observation carries BOTH unknowns — how big a click
            // is, and which way it goes.
            var conventional = Polarity.GetOrAdd(control.NodeId, true);
            var wantUp = targetRaw > current.Value;
            bus.Fire((wantUp == conventional) ? incEvent.Value : decEvent.Value);
            await Task.Delay(SettleMs).ConfigureAwait(false);

            var probed = await ReadAsync(varKey, sim).ConfigureAwait(false);
            if (probed == null) return false;

            var delta = probed.Value - current.Value;
            if (Math.Abs(delta) < 1e-6)
            {
                Log.Debug("MD11", $"{control.NodeId}: analog probe produced no movement at {current.Value} — end stop or inhibited.");
                return Math.Abs(probed.Value - targetRaw) <= tolerance;
            }

            // Movement away from the target means our polarity assumption was wrong. Record it;
            // the next round probes the other way.
            var movingUp = delta > 0;
            if (movingUp != wantUp)
            {
                Polarity[control.NodeId] = !conventional;
                Log.Info("MD11", $"{control.NodeId}: analog step polarity calibrated to {(!conventional ? "INVERTED" : "conventional")}.");
                continue;
            }

            if (Math.Abs(probed.Value - targetRaw) <= tolerance) return true;

            // Remaining distance / measured step size = clicks to go.
            var clicks = (int)Math.Round((targetRaw - probed.Value) / delta);
            Log.Info("MD11", $"{control.NodeId}: round {round} current={current.Value:0.##} probed={probed.Value:0.##} " +
                $"delta/click={delta:0.###} target={targetRaw:0.##} → {clicks} clicks.");
            if (clicks <= 0) continue;

            clicks = Math.Min(clicks, MaxSteps * 4);   // hard bound; the loop re-verifies anyway
            var eventId = (targetRaw > probed.Value) == movingUp ? incEvent.Value : decEvent.Value;
            for (var i = 0; i < clicks && !ct.IsCancellationRequested; i++) bus.Fire(eventId);
            if (ct.IsCancellationRequested) return false;

            // Wait for the whole burst to LAND before re-reading, or the re-read counts a
            // half-finished walk and the next round's click math is wrong. The bus paces each
            // CEVENT write at its MinGapMs (60 ms); this per-click budget must exceed that with
            // margin. (It was 35 ms — fine when the pump paced at 30 ms, too short once the
            // press-release gap was raised to 60 ms, which is what made the walk undershoot.)
            await Task.Delay(SettleMs + clicks * 80).ConfigureAwait(false);
        }

        var final = await ReadAsync(varKey, sim).ConfigureAwait(false);
        var ok = final != null && Math.Abs(final.Value - targetRaw) <= tolerance;
        Log.Info("MD11", $"{control.NodeId}: analog walk END final={final?.ToString("0.##") ?? "null"} " +
            $"target={targetRaw:0.##} converged={ok} after {maxRounds} rounds.");
        return ok;
    }

    /// <summary>
    /// Reads the state var AFTER it settles, not the instant a step fires.
    ///
    /// This is the crux of why walks got "stuck". MD-11 state vars are ANIMATED — the cockpit XML
    /// gives them ANIM_LAG up to 1000 ms — so for up to a second after a step the value is still
    /// travelling toward its new position. A single fast read catches it mid-flight (often still
    /// reading the OLD value), the walk concludes "no movement" and bails one click in; on a
    /// direction change it can even mis-learn polarity and jam the control against an end stop.
    /// So poll until two consecutive reads agree (the animation has stopped) or a cap elapses.
    /// </summary>
    private static async Task<double?> ReadAsync(string varKey, SimConnectManager sim, CancellationToken ct = default)
    {
        double? prev = null;
        for (var waited = 0; waited < SettleCapMs; waited += SettlePollMs)
        {
            ct.ThrowIfCancellationRequested();
            sim.RequestVariable(varKey, forceUpdate: true);
            await Task.Delay(SettlePollMs, ct).ConfigureAwait(false);
            var v = sim.GetCachedVariableValue(varKey);
            if (v != null && prev != null && Math.Abs(v.Value - prev.Value) < 0.05)
                return v;   // two reads agree → the value has settled
            prev = v;
        }
        return prev;
    }

    /// <summary>Poll interval and total cap for <see cref="ReadAsync"/>'s settle wait. The cap
    /// comfortably exceeds the aircraft's largest ANIM_LAG (1000 ms) so a fully-lagged var still
    /// settles before we give up; a snappy var returns after the first two agreeing reads.</summary>
    private const int SettlePollMs = 150;
    private const int SettleCapMs = 1650;

    /// <summary>Test seam: clears learned polarity so a test can assert calibration from scratch.</summary>
    internal static void ResetPolarity() => Polarity.Clear();

    /// <summary>Test seam.</summary>
    internal static bool? PolarityFor(string nodeId) => Polarity.TryGetValue(nodeId, out var v) ? v : null;

    /// <summary>Approximate equality against a detent value.</summary>
    public static bool Near(double a, double b) => Math.Abs(a - b) < Epsilon;
}

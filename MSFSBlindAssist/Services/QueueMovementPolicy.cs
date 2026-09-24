namespace MSFSBlindAssist.Services;

/// <summary>Where one aircraft stands in the "…ahead is moving." life cycle.</summary>
public enum QueueMoverPhase { None, StoppedAhead, Departed, Announced }

/// <summary>Per-aircraft state. <see cref="StoppedGapFt"/> is the closest the pilot has been to it while it sat stopped ahead (NaN when unknown).</summary>
public readonly record struct QueueMoverState(QueueMoverPhase Phase, double StoppedGapFt)
{
    public static readonly QueueMoverState Initial = new(QueueMoverPhase.None, double.NaN);
}

/// <summary>One evaluation's view of one aircraft: in the queue cone (directly ahead, close), its distance and speed.</summary>
public readonly record struct QueueMoverObservation(bool InQueueCone, double DistFt, double GsKts);

/// <summary>The "Move up" nudge: armed when "…ahead is moving." was spoken.</summary>
public readonly record struct NudgeState(bool Armed, DateTime LastSpokenUtc, int Count)
{
    public static readonly NudgeState Disarmed = new(false, DateTime.MinValue, 0);
    public static NudgeState ArmedAt(DateTime nowUtc) => new(true, nowUtc, 0);
}

public enum NudgeAction { None, Disarm, Speak }

public readonly record struct NudgeDecision(NudgeAction Action, string Text);

/// <summary>
/// "Delta A320 ahead is moving." and the "Move up" prompt that may follow it, as pure rules.
///
/// <para>The departure is a LATCHED state, not a one-sample speed edge (PR #247 review R15): once the
/// aircraft that sat stopped ahead is seen rolling (≥ 2 kt) or opening the gap (≥ 60 ft at ≥ 1 kt),
/// it stays <see cref="QueueMoverPhase.Departed"/> until the call is spoken or it leaves the cone —
/// being outranked by another callout, or another component's sweep moving the previous speed on, can
/// no longer lose it. The pilot taxiing resets everything (L2).</para>
///
/// <para>"Move up" is an instruction ATC has not given, so it is tightly gated (owner decision,
/// review R5): only where the context allows a queue prompt (taxiing on a joined route, not on a
/// runway, not at a hold), only while the pilot is stopped, never with another aircraft — anything
/// other than the one whose departure armed it (<see cref="NearestOtherAheadFt"/>) — within
/// <see cref="NudgeMinGapFt"/> ahead, every <see cref="NudgeIntervalMs"/>, at most
/// <see cref="NudgeMax"/> times, and it disarms the moment the pilot rolls.</para>
///
/// <para>That exemption is for the DISARM rule only (PR #247 B5 follow-up K1): the spoken text names
/// whichever aircraft is nearest ahead, the departed leader included, because a leader that stopped
/// again a short way ahead genuinely IS the traffic the pilot will close on next. Conflating the two —
/// judging both the disarm and the text off the leader-excluded distance — made a leader stopped nearby
/// with nothing else around announce "The traffic ahead has taxied on." instead of naming it.</para>
/// </summary>
public static class QueueMovementPolicy
{
    /// <summary>At or below this own ground speed the pilot is in the queue (not taxiing).</summary>
    public const double OwnQueueGsKts = 5.0;
    public const int NudgeIntervalMs = 20000;
    public const int NudgeMax = 3;
    /// <summary>Closer than this to the nearest aircraft ahead (other than the one that left), there is nothing to move up into.</summary>
    public const double NudgeMinGapFt = 250.0;
    /// <summary>The pilot rolling at this speed has moved up: disarm.</summary>
    public const double NudgeResetOwnGsKts = 2.0;
    /// <summary>The nudge only speaks to a pilot below this speed (stopped).</summary>
    public const double NudgeStoppedOwnGsKts = 1.0;

    /// <summary>Advance one aircraft's state by one evaluation.</summary>
    public static QueueMoverState Step(QueueMoverState state, QueueMoverObservation obs, double ownGsKts)
    {
        if (ownGsKts > OwnQueueGsKts || !obs.InQueueCone) return QueueMoverState.Initial;

        if (obs.GsKts <= GroundTrafficLogic.QueueStoppedGs)
        {
            double gap = state.Phase == QueueMoverPhase.StoppedAhead && !double.IsNaN(state.StoppedGapFt)
                ? Math.Min(state.StoppedGapFt, obs.DistFt)
                : obs.DistFt;
            return new QueueMoverState(QueueMoverPhase.StoppedAhead, gap);
        }

        // Moving. Only an aircraft seen stopped ahead can depart; previousGs = 0 because being in
        // StoppedAhead means the last observation of it was stopped.
        if (state.Phase == QueueMoverPhase.StoppedAhead
            && GroundTrafficLogic.QueueDeparted(0.0, obs.GsKts, obs.DistFt, state.StoppedGapFt))
            return new QueueMoverState(QueueMoverPhase.Departed, state.StoppedGapFt);

        return state;   // None stays None; a creeper stays StoppedAhead; Departed / Announced latch
    }

    /// <summary>The call was spoken.</summary>
    public static QueueMoverState MarkAnnounced(QueueMoverState state)
        => new(QueueMoverPhase.Announced, double.NaN);

    /// <summary>
    /// Arm "Move up" only when the aircraft moved off ALONG the pilot's route
    /// (<paramref name="movingAlongRoute"/>, null when there is no route to judge by — then the gap
    /// must have grown by more than 10 ft).
    /// </summary>
    public static bool ShouldArmNudge(bool? movingAlongRoute, double distFt, double stoppedGapFt)
        => movingAlongRoute ?? (!double.IsNaN(stoppedGapFt) && distFt > stoppedGapFt + 10.0);

    /// <summary>
    /// The nearest aircraft directly ahead OTHER than the one whose departure armed the nudge — the
    /// owner's rule is "never with anything ELSE within 250 ft ahead": the leader pulling away is the
    /// reason to move up, not a reason to stay put. Null when there is none.
    /// </summary>
    public static double? NearestOtherAheadFt(IEnumerable<(uint Id, double DistFt)> directlyAhead, uint? leaderId)
    {
        double? best = null;
        foreach (var (id, dist) in directlyAhead)
            if (id != leaderId && (best is null || dist < best)) best = dist;
        return best;
    }

    /// <summary>
    /// What the nudge does this evaluation. <paramref name="nearestOtherAheadFt"/> — the nearest
    /// aircraft directly ahead OTHER than the one whose departure armed the nudge
    /// (<see cref="NearestOtherAheadFt"/>), null when there is none in range — decides ONLY the
    /// <see cref="NudgeMinGapFt"/> disarm rule: something else closing the gap is a reason to hold,
    /// the departed leader stopping again nearby is not. <paramref name="nearestAheadFt"/> — the
    /// nearest aircraft directly ahead, the leader included, null when nothing is ahead at all — is
    /// what the spoken text names, since whichever aircraft that is genuinely is the traffic the pilot
    /// will close on next (PR #247 B5 follow-up K1).
    /// </summary>
    public static NudgeDecision EvaluateNudge(NudgeState state, bool contextAllowsPrompt, double ownGsKts,
        double? nearestOtherAheadFt, double? nearestAheadFt, DateTime nowUtc, Func<double, string> formatDistance)
    {
        if (!state.Armed) return new NudgeDecision(NudgeAction.None, "");
        if (!contextAllowsPrompt || ownGsKts >= NudgeResetOwnGsKts) return new NudgeDecision(NudgeAction.Disarm, "");
        if (nearestOtherAheadFt is double gap && gap < NudgeMinGapFt) return new NudgeDecision(NudgeAction.Disarm, "");
        if (state.Count >= NudgeMax) return new NudgeDecision(NudgeAction.Disarm, "");
        if (ownGsKts >= NudgeStoppedOwnGsKts) return new NudgeDecision(NudgeAction.None, "");
        if ((nowUtc - state.LastSpokenUtc).TotalMilliseconds < NudgeIntervalMs) return new NudgeDecision(NudgeAction.None, "");
        string text = nearestAheadFt is double d
            ? $"Move up. {formatDistance(d)} to the traffic ahead."
            : "Move up. The traffic ahead has taxied on.";
        return new NudgeDecision(NudgeAction.Speak, text);
    }

    /// <summary>The prompt was spoken.</summary>
    public static NudgeState AfterSpoken(NudgeState state, DateTime nowUtc)
        => state with { LastSpokenUtc = nowUtc, Count = state.Count + 1 };
}

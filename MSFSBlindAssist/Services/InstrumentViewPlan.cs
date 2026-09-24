namespace MSFSBlindAssist.Services;

/// <summary>
/// One reading of the simulator camera: <c>CAMERA STATE</c>, <c>CAMERA VIEW TYPE AND INDEX:0</c>
/// (the view type) and <c>:1</c> (the index), as integers. State 2 is a cockpit camera; view type
/// 1 is a pilot view, 2 an instrument view, 3 a quickview; the index is 0-based within its type,
/// so the sim's own "instrument view 1" (Ctrl+1 on MSFS 2020, Shift+1 on MSFS 2024) is index 0.
/// Live-verified on MSFS 2024, 2026-09-08 (docs/md11.md, "AI display reading").
///
/// A user-saved custom cockpit camera also reads as type 1, at an index past the count
/// <c>CAMERA VIEW TYPE AND INDEX MAX:1</c> advertises. That index is still both readable and
/// writable: on the iFly 737 MAX8, MAX:1 reads 6 while the camera sits on index 7, and writing
/// 1/7 back restores the pilot's own view (measured 2026-09-18, MSFS 2024 1.8.16.0). So MAX
/// does NOT bound what the write path accepts and must never gate a restore — see
/// <see cref="InstrumentViewPlan.RestoreWrites"/>.
///
/// A NON-COCKPIT camera reads view type 0, index 0 — measured on the live PMDG 737-800
/// (2026-09-20, MSFS 2024) at <c>CAMERA STATE</c> 3 (external) and 6 (environment) — and a write
/// to the view TYPE register while non-cockpit is REFUSED (sent, register unchanged). Both
/// matter for <see cref="IsAt"/> below: a cockpit reading is type 1, 2 or 3, so a non-cockpit
/// reading can never satisfy a restore's test, and a restore cannot drag a pilot who deliberately
/// took an external view back into the cockpit — it fails and says so, which is correct. That is
/// why <see cref="IsAt"/> not consulting the state is safe rather than a gap.
/// </summary>
public readonly record struct CameraViewReading(int State, int ViewType, int ViewIndex)
{
    /// <summary>
    /// True when this reading is at the given view (type and index; the state is not consulted —
    /// see the measurement above for why that cannot let a non-cockpit camera pass).
    /// </summary>
    public bool IsAt(int viewType, int viewIndex) => ViewType == viewType && ViewIndex == viewIndex;
}

/// <summary>What <see cref="InstrumentViewPlan.For"/> decided about the camera.</summary>
public enum InstrumentViewOutcome
{
    /// <summary>Not a cockpit camera (external, drone, showcase). Nothing is written; the read is refused.</summary>
    NotInCockpit,
    /// <summary>Already on the wanted instrument view. Nothing to write.</summary>
    AlreadyThere,
    /// <summary>In the cockpit on some other view: write the instrument view.</summary>
    Switch,
    /// <summary>The camera could not be read: write the instrument view anyway.</summary>
    Unknown,
}

/// <summary>
/// The pure half of moving the simulator camera to an instrument view for an AI display read:
/// given the camera as it is and the view the display needs, whether to refuse, and what to
/// write. The async half (the write, read-back verification, settle) is
/// <see cref="InstrumentViewSwitcher"/>. The two simulator constants live here and nowhere else.
///
/// The camera IS put back after the capture, by <see cref="RestoreWrites"/> — written, then
/// verified by read-back, never assumed. A restore was removed on 2026-09-09 on the reasoning
/// that a custom camera's index is one the write path refuses; that reasoning is DISPROVEN (see
/// <see cref="CameraViewReading"/>). Why the MD-11 restore failed that day was ANSWERED on
/// 2026-09-20 and was never MD-11-specific: the removed code wrote both camera registers on ONE
/// frame, so the TYPE write was refused while the INDEX write applied. Spaced across frames the
/// same pair restores correctly — see <c>InstrumentViewSwitcher.RestoreAsync</c>, which carries
/// the measurement.
///
/// The verification is what makes reinstating it safe: an out-of-range index is CLAMPED rather
/// than ignored and the read-back reports the clamped value (a write of 20 landed on 8, read back
/// as 8), so a restore that did not take is detectable, and the caller says so out loud.
/// </summary>
public sealed record InstrumentViewPlan(
    InstrumentViewOutcome Outcome,
    (int Type, int Index)? Writes)
{
    /// <summary><c>CAMERA STATE</c> for any cockpit camera.</summary>
    public const int CockpitState = 2;

    /// <summary><c>CAMERA VIEW TYPE AND INDEX:0</c> for the instrument views defined in the aircraft's cameras.cfg.</summary>
    public const int InstrumentViewType = 2;

    public static InstrumentViewPlan For(CameraViewReading? current, int wantedIndex)
    {
        if (current is not { } camera)
            return new InstrumentViewPlan(InstrumentViewOutcome.Unknown, (InstrumentViewType, wantedIndex));

        if (camera.State != CockpitState)
            return new InstrumentViewPlan(InstrumentViewOutcome.NotInCockpit, null);

        if (IsOn(camera, wantedIndex))
            return new InstrumentViewPlan(InstrumentViewOutcome.AlreadyThere, null);

        return new InstrumentViewPlan(InstrumentViewOutcome.Switch, (InstrumentViewType, wantedIndex));
    }

    /// <summary>True when the reading is the wanted instrument view (type and index; the state is not consulted).</summary>
    public static bool IsOn(CameraViewReading reading, int wantedIndex)
        => reading.IsAt(InstrumentViewType, wantedIndex);

    /// <summary>
    /// What to write to put the pilot's camera back after the capture, or null when there is
    /// nothing to put back.
    ///
    /// <para>
    /// The aim is a TARGET, not "the reading this read happened to take". Usually they are the
    /// same thing, but a home owed by an earlier FAILED restore outlives the read that recorded
    /// it (<see cref="CameraHomePlan"/>) — so a read that moved nothing at all can still be the
    /// one that gets the pilot home. Keying this on <see cref="InstrumentViewOutcome.Switch"/>,
    /// as it once did, is what made a failed restore permanent: the next read saw the instrument
    /// view it had been stranded on, called it AlreadyThere, wrote nothing and reported success.
    /// </para>
    ///
    /// <para>
    /// Two cases write nothing. <see cref="InstrumentViewOutcome.NotInCockpit"/> wrote nothing on
    /// the way in and the read is refused, so there is nothing to undo. And a target that IS the
    /// instrument view this read wanted is already where it belongs — writing it back would be a
    /// no-op the restore then has to spend a poll and a confirm verifying.
    /// </para>
    /// </summary>
    public static (int Type, int Index)? RestoreWrites(
        CameraViewReading? target, int wantedIndex, InstrumentViewOutcome outcome)
        => outcome == InstrumentViewOutcome.NotInCockpit || target is not { } home || IsOn(home, wantedIndex)
            ? null
            : (home.ViewType, home.ViewIndex);

    /// <summary>
    /// True when the app moved the camera off wherever the pilot had it and has nothing to send
    /// it back to — the one case where a display read must confess rather than report success.
    ///
    /// <para>
    /// Both halves are load-bearing. <paramref name="target"/> null means no reading to aim at:
    /// the camera could not be read on the way in and no earlier read left a home owed. And
    /// <paramref name="moved"/> is a FACT — a write that would move the camera was dispatched to
    /// the simulator — not an inference.
    /// </para>
    ///
    /// <para>
    /// ⚠️ This used to read the entry's own <c>Verified</c> flag as a proxy for "the write
    /// landed", and it was wrong in BOTH directions. Verified is FALSE for a write that landed
    /// while every read-back timed out — the camera registration can fail on its own
    /// (<c>SimConnectManager.RegisterCameraViewDefinition</c> has its own catch) while writes keep
    /// landing, because <c>SetSimVar</c> builds its own temporary data definition and never
    /// touches the camera one. That combination moved the camera on every display read of the
    /// session and never restored it, in silence, which is the exact failure this feature exists
    /// to prevent. And Verified is TRUE for a camera that was already on the wanted view, where
    /// nothing moved at all. Dispatch is the fact both of those need; never go back to the proxy.
    /// </para>
    ///
    /// <para>
    /// Residual, stated rather than hidden: when the camera cannot be read AND it happened to be
    /// on the wanted view already, a dispatched write moved nothing and this still returns true.
    /// <see cref="InstrumentViewSwitcher"/> retries the entry read once, which turns almost every
    /// such case into a real outcome; what is left needs the pilot to be sitting on the very
    /// instrument view the read wants while two reads in a row time out.
    /// </para>
    /// </summary>
    public static bool MovedWithNoWayBack(CameraViewReading? target, bool moved)
        => target is null && moved;
}

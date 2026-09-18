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
/// </summary>
public readonly record struct CameraViewReading(int State, int ViewType, int ViewIndex);

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
/// <see cref="CameraViewReading"/>), and why the MD-11 restore failed that day is unknown. The
/// verification is what makes reinstating it safe: an out-of-range index is CLAMPED rather than
/// ignored and the read-back reports the clamped value (a write of 20 landed on 8, read back as
/// 8), so a restore that did not take is detectable, and the caller says so out loud.
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
        => reading.ViewType == InstrumentViewType && reading.ViewIndex == wantedIndex;

    /// <summary>
    /// What to write to put the pilot's camera back after the capture, or null when there is
    /// nothing to put back. Only a <see cref="InstrumentViewOutcome.Switch"/> moved the camera
    /// away from a reading we hold: <see cref="InstrumentViewOutcome.AlreadyThere"/> never moved
    /// it, <see cref="InstrumentViewOutcome.NotInCockpit"/> wrote nothing, and
    /// <see cref="InstrumentViewOutcome.Unknown"/> could not read a camera to remember.
    ///
    /// The entry's own verification is deliberately NOT consulted: Switch always attempted the
    /// write, so the camera may have moved whether or not the read-back confirmed it.
    /// </summary>
    public static (int Type, int Index)? RestoreWrites(InstrumentViewOutcome outcome, CameraViewReading? before)
        => outcome == InstrumentViewOutcome.Switch && before is { } camera
            ? (camera.ViewType, camera.ViewIndex)
            : null;
}

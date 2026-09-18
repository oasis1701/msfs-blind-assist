namespace MSFSBlindAssist.Services;

/// <summary>
/// One reading of the simulator camera: <c>CAMERA STATE</c>, <c>CAMERA VIEW TYPE AND INDEX:0</c>
/// (the view type) and <c>:1</c> (the index), as integers. State 2 is a cockpit camera; view type
/// 1 is a pilot view, 2 an instrument view, 3 a quickview; the index is 0-based within its type,
/// so the sim's own "instrument view 1" (Ctrl+1 on MSFS 2020, Shift+1 on MSFS 2024) is index 0.
/// Live-verified on MSFS 2024, 2026-09-08 (docs/md11.md, "AI display reading").
///
/// A user-saved custom cockpit camera also reads as type 1 — with an index the WRITE path does
/// not accept back (measured 2026-09-09: a custom cabin view read as type 1 index 7 while the sim
/// advertised 6 pilot views, and writing the pair back left the pilot in the instrument view).
/// That is why nothing here remembers a previous view: a read is a plan input, never a way back.
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
/// The camera is deliberately NOT put back afterwards. The pilot's previous view is often a
/// user-saved custom camera (a cabin or wing view, chosen for what it sounds like), and the sim
/// reports those as pilot-view indices it refuses on the way back — so a "restore" silently did
/// nothing for exactly the pilots who cared. The pilot returns with their own view key, which is
/// the one thing that works for every camera set.
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
}

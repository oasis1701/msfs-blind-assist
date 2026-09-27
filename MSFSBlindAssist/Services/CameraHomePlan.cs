namespace MSFSBlindAssist.Services;

/// <summary>
/// What <see cref="CameraHomePlan.For"/> decided: the view a restore should write back, and
/// whether the owed home has been settled and should be forgotten.
/// </summary>
public readonly record struct CameraHomeDecision(CameraViewReading? RestoreTarget, bool ClearOwedHome);

/// <summary>
/// Where a display read's restore should aim, once a previous restore has FAILED.
///
/// <para>
/// A failed restore leaves the camera on the instrument view the read used. Without a memory of
/// what it could not reach, the NEXT read takes that instrument view as "where the pilot was" —
/// so it faithfully puts them back on it and reports success. The warning is spoken once and then
/// never again, and the app can never get them home. That is the whole reason this type exists;
/// it is not an optimisation.
/// </para>
///
/// <para>
/// The OWED HOME is the reading a failed restore could not reach. It outlives the read that
/// recorded it and is settled two ways: a later restore reaches it, or the pilot moves the camera
/// somewhere of their own — any reading that is not one of our instrument views. A pilot who has
/// taken their own view has got themselves out, and their new view is what the next read owes
/// them; continuing to drag them back to a stale home would be the same defect pointed the other
/// way.
/// </para>
///
/// <para>
/// An unreadable camera keeps the debt: it is no evidence that the pilot got home, and dropping
/// the home on a timed-out read would silently reinstate the strand.
/// </para>
/// </summary>
public static class CameraHomePlan
{
    /// <param name="owedHome">The view a previous restore failed to reach, or null when nothing is owed.</param>
    /// <param name="before">The camera as it reads at the start of this read, or null when it could not be read.</param>
    public static CameraHomeDecision For(CameraViewReading? owedHome, CameraViewReading? before)
    {
        if (owedHome is not { } home)
            return new CameraHomeDecision(before, ClearOwedHome: false);

        // An instrument view is where a failed restore leaves them, so it is not evidence the
        // pilot has moved. A camera that will not read is not evidence either.
        if (before is not { } reading || reading.ViewType == InstrumentViewPlan.InstrumentViewType)
            return new CameraHomeDecision(home, ClearOwedHome: false);

        return new CameraHomeDecision(reading, ClearOwedHome: true);
    }
}

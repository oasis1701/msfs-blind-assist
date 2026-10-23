namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The pilot's "Automatically set altimeters at transition altitude and level" setting
/// (UserSettings.FOAutoAltimetersEnabled, FO-23), shared by every First Officer phase monitor.
///
/// On, each monitor sets the altimeters at the crossing as it always has. Off, no monitor
/// touches an altimeter: the crossing is spoken as a reminder instead, because a screen-reader
/// pilot has no other cue that the transition has come. The crossing detector keeps running
/// either way, so turning the setting back on mid-flight never acts on a crossing already passed.
///
/// Pure (no sim or UI dependencies) so it is unit-testable; the monitors need a
/// ScreenReaderAnnouncer, which has no test constructor.
/// </summary>
public static class TransitionAltimeters
{
    public const string AltitudeReminder = "Transition altitude. Set standard altimeters.";
    public const string LevelReminder = "Transition level. Set local altimeter pressure now.";

    private const string NoTransitionLoaded =
        "Passing one eight thousand. No transition altitude loaded — set standard altimeters as required.";
    private const string LoadSimBriefHint =
        " Load SimBrief in the First Officer window for automatic altimeter changes.";

    /// <summary>
    /// What to speak INSTEAD of setting the altimeters at this crossing, or null when the
    /// monitor may set them (setting on) or there is no crossing.
    /// </summary>
    public static string? ReminderInstead(TransitionCrossingDetector.Crossing crossing, bool autoAltimetersEnabled)
    {
        if (autoAltimetersEnabled) return null;
        return crossing switch
        {
            TransitionCrossingDetector.Crossing.ClimbToStd => AltitudeReminder,
            TransitionCrossingDetector.Crossing.DescendToQnh => LevelReminder,
            _ => null,
        };
    }

    /// <summary>
    /// The one-shot reminder passing 18,000 ft with no SimBrief transition altitude loaded.
    /// It points at SimBrief only while the setting is on: with it off, loading a plan would
    /// change nothing, so the promise of automatic changes would mislead.
    /// </summary>
    public static string NoTransitionLoadedReminder(bool autoAltimetersEnabled)
        => autoAltimetersEnabled ? NoTransitionLoaded + LoadSimBriefHint : NoTransitionLoaded;
}

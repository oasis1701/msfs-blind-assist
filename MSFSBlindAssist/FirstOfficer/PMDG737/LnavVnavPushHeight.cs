namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// The 737 First Officer's 400 ft AGL LNAV/VNAV push (the PMDG 737's <see cref="FOAutoManager"/>
/// and the iFly 737 MAX8's port of it), as pure rules so they are unit-testable: the managers
/// themselves need a ScreenReaderAnnouncer.
/// </summary>
public static class LnavVnavPushHeight
{
    /// <summary>The fixed push height, feet AGL (FO-16: deliberately not the AP-engage setting).</summary>
    public const double Feet = 400;

    /// <summary>
    /// Whether the push is armed after one AIRBORNE sample (the managers arm it on the ground
    /// themselves). A sample below the push height arms it, so only a crossing the manager
    /// watched can fire it; passing above the height with the setting off disarms it, so turning
    /// the setting on later in the climb never fires it there; an unknown AGL changes nothing.
    /// The push used to fire on the first climbing sample above 400 ft wherever it came: with
    /// the window first opened (Shift+C), the app restarted or the setting switched on at FL150
    /// it pressed LNAV/VNAV there and said "400 feet. LNAV and VNAV engaged."
    /// </summary>
    public static bool Armed(bool wasArmed, double aglFt, bool settingOn)
    {
        if (double.IsNaN(aglFt)) return wasArmed;
        if (aglFt < Feet) return true;
        return wasArmed && settingOn;
    }

    /// <summary>FOB-21's latch-burn rule, for both 737s: spend the leg's single attempt only when
    /// this sample's decision was informed — a push fired, or BOTH annunciators read (never NaN)
    /// and were already lit. Either one unread with nothing pushed decided nothing, so the next
    /// sample retries.</summary>
    public static bool ShouldBurnLatch(bool pushedLnav, bool pushedVnav, double lnavRead, double vnavRead) =>
        pushedLnav || pushedVnav || (!double.IsNaN(lnavRead) && !double.IsNaN(vnavRead));
}

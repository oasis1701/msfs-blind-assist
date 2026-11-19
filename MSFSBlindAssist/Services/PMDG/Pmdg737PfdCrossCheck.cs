namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The simulator's indicated airspeed and altitude (AIRSPEED INDICATED, INDICATED ALTITUDE), each null
/// until the simulator has delivered it; never 0 for "not delivered".</summary>
public readonly record struct PfdSimSnapshot(double? IasKnots, double? AltitudeFt);

/// <summary>Where the Captain's PFD is: the glass PFD page on a unit drawing the Captain's side, only a classic EFIS
/// page with its attitude indicator there, or on no display.</summary>
public enum PfdShown { Shown, NotOnAnyDisplay, Classic }

/// <summary>
/// The PFD gate's checks against what the app already trusts. The speed and altitude: one memory sample from the
/// last <see cref="WindowMs"/> must hold both PMDG's Captain IAS and altitude within tolerance of the simulator's
/// (the simulator lags; PMDG smooths its values and keeps its own baro). A value whose instrument flag is up (a
/// message that replaces the instrument in the speed or altitude tape's place: SPD, ALT) is not compared, since
/// PMDG's value then leaves the simulator's on purpose. The speed tape bottoms out at its floor (45 kt live at the
/// gate, 2026-10-30) while the simulator reads about 0: both at or below a tape's floor count as within (a floor of
/// 0 is none). Both tolerances are measured, the altitude at the second ground session, the IAS on the flight. The displays: lit
/// while the panel is powered and some display unit shows a page other than off or blank, the ND's rule.
/// </summary>
public static class Pmdg737PfdCrossCheck
{
    public const long WindowMs = 3_000;

    /// <summary>Measured on the flight (2026-10-30, 713 check lines from the takeoff roll to the approach): above the tape's
    /// floor PMDG's IAS and the simulator's differed by at most 6.9 kt, on the takeoff roll (PMDG 54.2, the simulator 47.3,
    /// accelerating), and within about 1 kt otherwise; 3 × 6.9, rounded up. The landing roll is not yet measured.</summary>
    public const double IasToleranceKt = 21;

    /// <summary>Measured at the second ground session (2026-10-30): settled, PMDG's altitude and the simulator's differed
    /// by at most 0.6 ft at three baro settings (the simulator's follows PMDG's baro); 3 × that is under this floor. A
    /// baro turn opens a gap of up to about 250 ft for about a second (the simulator's altitude updates about once a
    /// second), which the 3 s window absorbs.</summary>
    public const double AltitudeToleranceFt = 100;

    public static bool Passes(PfdSimSnapshot sim, IEnumerable<(long AtMs, PfdSample Sample)> history, long nowMs, Pmdg737PfdLayout l)
    {
        if (sim.IasKnots is not { } ias || sim.AltitudeFt is not { } altitude) return false;
        var messages = l.AllMessages();
        var speedFlags = FlagsIn(messages, PfdPlace.SpeedTape);
        var altitudeFlags = FlagsIn(messages, PfdPlace.AltitudeTape);
        return history.Any(h => nowMs - h.AtMs <= WindowMs
            && (AnyUp(h.Sample, speedFlags) || IasWithin(h.Sample.Ias, ias, IasToleranceKt, l))
            && (AnyUp(h.Sample, altitudeFlags) || AltitudeWithin(h.Sample.Altitude, altitude, AltitudeToleranceFt, l)));
    }

    /// <summary>Whether the speed tape's flag (SPD) is up in the sample.</summary>
    internal static bool SpeedFlagged(PfdSample s, Pmdg737PfdLayout l) => AnyUp(s, FlagsIn(l.AllMessages(), PfdPlace.SpeedTape));

    /// <summary>Whether the altitude tape's flag (ALT) is up in the sample.</summary>
    internal static bool AltitudeFlagged(PfdSample s, Pmdg737PfdLayout l) => AnyUp(s, FlagsIn(l.AllMessages(), PfdPlace.AltitudeTape));

    /// <summary>PMDG's IAS within <paramref name="tolerance"/> of the simulator's, or both at or below the tape's floor.</summary>
    internal static bool IasWithin(double pmdg, double sim, double tolerance, Pmdg737PfdLayout l) => Within(pmdg, sim, tolerance, l.Speed.Geometry.Floor);

    internal static bool AltitudeWithin(double pmdg, double sim, double tolerance, Pmdg737PfdLayout l) => Within(pmdg, sim, tolerance, l.Altitude.Geometry.Floor);

    public static bool DisplaysLit(PfdSample s, Pmdg737PfdLayout l) =>
        s.Panel.Powered && s.UnitPages.Any(page => !l.Units.DarkPages.Contains(page));

    /// <summary>The glass PFD when a unit drawing the Captain's side (PMDG's display unit rule, from the layout) shows
    /// the PFD page, else classic when one shows a classic EFIS page, else on no display.</summary>
    public static PfdShown CaptainPfd(PfdSample s, PfdUnitFields u)
    {
        var shown = u.CaptainUnits
            .Where(c => c.Unit < s.UnitPages.Count && s.UnitPages[c.Unit] == c.Page
                && (c.WhenSelector is not { } selector || s.LowerSelector == selector))
            .Select(c => c.Page)
            .ToList();
        if (shown.Contains(u.PfdPage)) return PfdShown.Shown;
        return shown.Any(u.ClassicPages.Contains) ? PfdShown.Classic : PfdShown.NotOnAnyDisplay;
    }

    /// <summary>The indexes (in <see cref="Pmdg737PfdLayout.AllMessages"/> order) of the flags that replace the
    /// instrument in <paramref name="place"/>.</summary>
    private static List<int> FlagsIn(IReadOnlyList<PfdMessage> messages, PfdPlace place) =>
        Enumerable.Range(0, messages.Count).Where(k => messages[k].ReplacesInstrument && messages[k].Place == place).ToList();

    private static bool AnyUp(PfdSample s, List<int> flags) => flags.Any(k => k < s.MessagesUp.Count && s.MessagesUp[k]);

    private static bool Within(double pmdg, double sim, double tolerance, double floor) =>
        Math.Abs(pmdg - sim) <= tolerance || (floor > 0 && pmdg <= floor && sim <= floor);
}

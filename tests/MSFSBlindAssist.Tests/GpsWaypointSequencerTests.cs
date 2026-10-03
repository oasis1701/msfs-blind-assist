using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The waypoint-passing call. Every test here is about the ONE question that decides whether
/// this feature helps or misleads: is this frame a passing, or merely a change?
///
/// A pilot acts on the call. Announcing a passing for a Direct-To or a plan activation would
/// have them report passing a fix they had only re-routed to, which is worse than the silence
/// this replaces.
/// </summary>
public class GpsWaypointSequencerTests
{
    private static SimConnectManager.GpsWaypointData Frame(
        string next, string prev, bool prevValid = true, double distanceMetres = 18520,
        double bearing = 87, double ete = 360, bool plan = true, bool directTo = false)
        => new()
        {
            NextId = next,
            PrevId = prev,
            DistanceMeters = distanceMetres,
            BearingDegrees = bearing,
            EteSeconds = ete,
            IsActiveFlightPlan = plan ? 1 : 0,
            IsDirectTo = directTo ? 1 : 0,
            PrevValid = prevValid ? 1 : 0
        };

    [Fact]
    public void TheFirstFrameIsABaselineAndNeverAPassing()
    {
        // Connecting to an aeroplane already established on a leg. The passing, if there was
        // one, happened before MSFSBA was watching.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM"), previousNextId: null);
        Assert.Equal("", r.PassedId);
        Assert.Equal("VEKIN", r.NextId);
    }

    [Fact]
    public void SequencingOntoTheNextLegIsAPassing()
    {
        // The waypoint we were flying TO has become the waypoint we are flying FROM.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM"), previousNextId: "SOXOM");
        Assert.Equal("SOXOM", r.PassedId);
    }

    [Fact]
    public void AnUnchangedFrameIsNotAPassing()
    {
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "TOPGI"), previousNextId: "SOXOM");
        Assert.Equal("", r.PassedId);
    }

    [Fact]
    public void ADirectToIsNotAPassing()
    {
        // ⚠️ THE WHOLE POINT. A Direct-To changes the TO-waypoint exactly like a sequence does,
        // so "the ident changed" would announce one. It is not a passing: the fix we were
        // flying to has NOT become the fix we are flying from — it has simply been abandoned.
        var r = GpsWaypointSequencer.Read(Frame("KUGEL", "TOPGI", directTo: true), previousNextId: "SOXOM");
        Assert.Equal("", r.PassedId);
    }

    [Fact]
    public void ActivatingAPlanIsNotAPassing()
    {
        // Empty to something. Nothing was passed; a route was loaded.
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", ""), previousNextId: "");
        Assert.Equal("", r.PassedId);
    }

    [Fact]
    public void ADiscontinuityIsNotAPassing()
    {
        // The navigator clears PREV VALID across a discontinuity, so the previous leg is not a
        // fix that was overflown and must not be reported as one.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM", prevValid: false), previousNextId: "SOXOM");
        Assert.Equal("", r.PassedId);
    }

    [Fact]
    public void ThePassingCallNamesBothTheFixPassedAndTheOneNowActive()
    {
        // Two halves of one position report. Asking for the second on a separate keypress
        // wastes the moment the call has to be made in.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM", distanceMetres: 25928), previousNextId: "SOXOM");
        string said = GpsWaypointSequencer.ComposePassing(r);
        Assert.Contains("Passing S O X O M", said);
        Assert.Contains("Next V E K I N", said);
        Assert.Contains("14 miles", said);
    }

    [Fact]
    public void ANonPassingComposesNothingAtAll()
    {
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "TOPGI"), previousNextId: "SOXOM");
        Assert.Equal("", GpsWaypointSequencer.ComposePassing(r));
    }

    [Fact]
    public void TheReadoutCarriesFixDistanceBearingAndTime()
    {
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "TOPGI", distanceMetres: 25928, bearing: 87, ete: 360),
                                          previousNextId: "SOXOM");
        string said = GpsWaypointSequencer.ComposeReadout(r);
        Assert.Contains("S O X O M", said);
        Assert.Contains("14 miles", said);
        Assert.Contains("bearing 087", said);   // padded — "bearing 87" reads as a different number
        Assert.Contains("6 minutes", said);
    }

    [Fact]
    public void TheReadoutSaysSoWhenThereIsNoWaypoint()
    {
        var r = GpsWaypointSequencer.Read(Frame("", "", plan: false), previousNextId: null);
        Assert.Equal("No active flight plan.", GpsWaypointSequencer.ComposeReadout(r));
    }

    [Fact]
    public void TimeIsOmittedWhenTheNavigatorPublishesZero()
    {
        // Stopped on the ground the navigator writes ETE 0 (it divides by ground speed and
        // refuses below a knot). "0 minutes" would be a reading; it is an absence.
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "", ete: 0), previousNextId: "SOXOM");
        string said = GpsWaypointSequencer.ComposeReadout(r);
        Assert.DoesNotContain("minute", said);
    }

    [Fact]
    public void AnIdentIsSpeltButProseIsNot()
    {
        // "SOXOM" run together through a synthesiser is a noise; a generated leg name is a
        // phrase and must survive intact.
        var fix = GpsWaypointSequencer.Read(Frame("SOXOM", ""), previousNextId: "SOXOM");
        Assert.Contains("S O X O M", GpsWaypointSequencer.ComposeReadout(fix));

        var leg = GpsWaypointSequencer.Read(Frame("HDG 071", ""), previousNextId: "HDG 071");
        Assert.Contains("HDG 071", GpsWaypointSequencer.ComposeReadout(leg));
    }

    [Fact]
    public void AnUncomputedLegNamesTheFixAndRefusesToInventTheNumbers()
    {
        // ⚠️ FOUND BY PRESSING THE KEY, NOT BY READING THE CODE. On the ground at VCBI with the
        // whole route loaded and the TO waypoint correctly reading LIKRA, every geometry field
        // the navigator publishes was exactly zero - distance, bearing, desired track,
        // cross-track and ETE - because the G1000's LNAV does not begin computing until it is
        // tracking a leg. The readout said "LIKRA, 0.0 miles, bearing 000", which a pilot hears
        // as being ON TOP OF THE FIX: the opposite of the truth, in the voice of a measurement.
        var r = GpsWaypointSequencer.Read(Frame("LIKRA", "VCBI", distanceMetres: 0, bearing: 0, ete: 0),
                                          previousNextId: "LIKRA");
        string said = GpsWaypointSequencer.ComposeReadout(r);

        Assert.Equal("L I K R A, distance not computed.", said);
        Assert.DoesNotContain("miles", said);
        Assert.DoesNotContain("bearing", said);
    }

    [Fact]
    public void APassingOntoAnUncomputedLegNamesTheNextFixWithoutADistance()
    {
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM", distanceMetres: 0),
                                          previousNextId: "SOXOM");
        string said = GpsWaypointSequencer.ComposePassing(r);

        Assert.Equal("Passing S O X O M. Next V E K I N.", said);
        Assert.DoesNotContain("miles", said);
    }

    [Fact]
    public void BearingZeroIsStillARealBearingWhenThereIsADistanceBehindIt()
    {
        // Distance is the discriminator and bearing must never be: due north is 000, and
        // suppressing a reading because of it would lose a legitimate one.
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "", distanceMetres: 9260, bearing: 0),
                                          previousNextId: "SOXOM");
        Assert.Contains("bearing 000", GpsWaypointSequencer.ComposeReadout(r));
    }

    // ---- D: distance and time to the destination -------------------------------------

    private static SimConnectManager.GpsWaypointData Route(double ete, double gs, bool plan = true)
        => new() { NextId = "LIKRA", PrevId = "VCBI", IsActiveFlightPlan = plan ? 1 : 0,
                   RouteEteSeconds = ete, GroundSpeedKnots = gs, PrevValid = 1 };

    [Fact]
    public void TheDestinationDistanceIsRecoveredExactlyFromTimeAndGroundSpeed()
    {
        // The navigator never publishes route distance; it publishes ETE computed as
        // 3600 * distance / groundSpeed. Inverting returns the SAME number, not an estimate:
        // 130 nm at 120 kt is 3900 s, and 3900 s at 120 kt must come back as 130 nm.
        string said = GpsWaypointSequencer.ComposeDestination(Route(ete: 3900, gs: 120));
        Assert.Contains("130 miles", said);
        Assert.Contains("1 hour 5 minutes", said);
    }

    [Fact]
    public void TheDestinationIsNotComputedWhenStopped()
    {
        // ⚠️ The same method writes ETE as a flat 0 at or below 1 knot, so on the ground there
        // is nothing to invert. Saying "0 miles" there is the waypoint bug all over again.
        Assert.Equal("Distance to destination not computed.",
                     GpsWaypointSequencer.ComposeDestination(Route(ete: 0, gs: 0)));
    }

    [Fact]
    public void TheDestinationSaysSoWithNoFlightPlan()
    {
        Assert.Equal("No active flight plan.",
                     GpsWaypointSequencer.ComposeDestination(Route(ete: 3900, gs: 120, plan: false)));
    }

    // ---- Shift+D: top of descent ------------------------------------------------------

    [Fact]
    public void NoVerticalPathIsSaidPlainlyRatherThanAsZeroMiles()
    {
        // Without a computed path there is no top of descent at all, and the distance reads 0
        // in that case exactly as it does when sitting on top of one. The flag separates them.
        Assert.Equal("No vertical path computed.",
                     GpsWaypointSequencer.ComposeTopOfDescent(pathAvailable: false, todMetres: 0));
    }

    [Fact]
    public void ATopOfDescentAheadIsGivenInMiles()
    {
        // 46,300 m = 25 nm.
        Assert.Equal("Top of descent in 25 miles.",
                     GpsWaypointSequencer.ComposeTopOfDescent(pathAvailable: true, todMetres: 46300));
    }

    [Fact]
    public void PastTheTopOfDescentSaysThatRatherThanZero()
    {
        Assert.Equal("Already past top of descent.",
                     GpsWaypointSequencer.ComposeTopOfDescent(pathAvailable: true, todMetres: 0));
    }

    [Fact]
    public void AnUnnamedLegGivesTheGeometryRatherThanClaimingThereIsNoWaypoint()
    {
        // ⚠️ Measured live at VCBI on the ANUT1D departure: the active leg was DER22 - an
        // ARINC path/terminator leg with NO FIX - so the navigator published an empty ident
        // while LNAV computed a perfectly good 1.0 miles. "No active waypoint" there says the
        // flight plan has run out, which is a lie a pilot would act on. Fix-less legs are most
        // of a departure.
        var r = GpsWaypointSequencer.Read(Frame("", "", distanceMetres: 1852, bearing: 220),
                                          previousNextId: "");
        string said = GpsWaypointSequencer.ComposeReadout(r);

        Assert.Equal("Unnamed leg, 1.0 miles, bearing 220.", said);
        Assert.DoesNotContain("No active waypoint", said);
    }

    [Fact]
    public void AnUnnamedLegWithNoGeometryStillSaysThereIsNoWaypoint()
    {
        // Nothing named AND nothing computed is genuinely nothing to report.
        var r = GpsWaypointSequencer.Read(Frame("", "", distanceMetres: 0), previousNextId: "");
        Assert.Equal("No active waypoint.", GpsWaypointSequencer.ComposeReadout(r));
    }

    [Fact]
    public void NoFlightPlanAtAllIsNamedAsSuch()
    {
        var r = GpsWaypointSequencer.Read(Frame("", "", plan: false), previousNextId: null);
        Assert.Equal("No active flight plan.", GpsWaypointSequencer.ComposeReadout(r));
    }

    // ---- the flight plan is the ident source on a procedure -------------------------

    [Fact]
    public void ThePlansLegNameIsUsedWhenTheSimVarIsEmpty()
    {
        // ⚠️ THE CASE THAT MATTERS ON EVERY IFR FLIGHT. Measured live on a hand-built ANUT1D
        // departure: GPS WP NEXT ID and GPS WP PREV ID were BOTH empty strings while the
        // flight plan's own getLeg(activeLateralLeg).name returned "BI583". The navigator
        // writes those SimVars off a plan-change event that does not fire as a procedure
        // sequences, so on a SID, STAR or approach they are the only thing missing.
        var r = GpsWaypointSequencer.Read(Frame("", "", distanceMetres: 25928),
                                          previousNextId: "BI582",
                                          legNext: "BI583", legPrev: "BI582");

        Assert.Equal("BI583", r.NextId);
        Assert.Equal("BI582", r.PassedId);   // the passing the aeroplane made in silence
    }

    [Fact]
    public void ThePlanWinsOverAStaleSimVarIdent()
    {
        var r = GpsWaypointSequencer.Read(Frame("OLD", "OLDER"),
                                          previousNextId: "BI582",
                                          legNext: "BI583", legPrev: "BI582");
        Assert.Equal("BI583", r.NextId);
    }

    [Fact]
    public void APlanSourcedPassingDoesNotNeedThePrevValidFlag()
    {
        // ⚠️ PREV VALID guards the SIMVAR's previous ident. Requiring it for a plan-sourced
        // name would keep the call silent on exactly the procedures it was built for.
        var r = GpsWaypointSequencer.Read(Frame("", "", prevValid: false),
                                          previousNextId: "BI582",
                                          legNext: "BI583", legPrev: "BI582");
        Assert.Equal("BI582", r.PassedId);
    }

    [Fact]
    public void TheSimVarIsStillUsedWhenThePlanHasNoName()
    {
        // A Direct-To DOES populate the SimVars, and a fix-less leg has no name in either -
        // so the fallback has to keep working rather than being replaced.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM"), previousNextId: "SOXOM",
                                          legNext: "", legPrev: "");
        Assert.Equal("VEKIN", r.NextId);
        Assert.Equal("SOXOM", r.PassedId);
    }

    [Fact]
    public void DistanceGainsADecimalOnlyWhenItIsClose()
    {
        var near = GpsWaypointSequencer.Read(Frame("SOXOM", "", distanceMetres: 5556), previousNextId: "SOXOM");
        Assert.Contains("3.0 miles", GpsWaypointSequencer.ComposeReadout(near));

        var far = GpsWaypointSequencer.Read(Frame("SOXOM", "", distanceMetres: 185200), previousNextId: "SOXOM");
        Assert.Contains("100 miles", GpsWaypointSequencer.ComposeReadout(far));
    }
}

// The PFD gate's checks against what the app already trusts: PMDG's Captain IAS and altitude must match the
// simulator's (AIRSPEED INDICATED, INDICATED ALTITUDE) in one sample of the last 3 s; a value whose instrument
// flag is up (SPD, ALT) skips the check, since PMDG's value then leaves the simulator's on purpose; the parked
// speed tape bottoms out at its floor (45 kt live, 2026-10-30) while the simulator reads about 0. The tolerances
// are provisional until the second ground session measures them. Nothing counts while the displays are dark,
// and the Captain's PFD is on a screen when a unit drawing the Captain's side shows the PFD page.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.SimConnect;
using Xunit;
using M = MSFSBlindAssist.Tests.Pmdg737PfdTestMemory;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdCrossCheckTests
{
    private static readonly Pmdg737PfdLayout L = M.Layout;

    /// <summary>The test memory's reading: IAS 160.2, altitude 5000, the PFD on the Captain's first unit, powered.</summary>
    private static PfdSample Live() => new M().Sample();

    private static int MessageIndex(Func<PfdMessage, bool> which) =>
        L.AllMessages().Select((m, k) => (m, k)).First(x => which(x.m)).k;

    private static int SpdFlag => MessageIndex(m => m.ReplacesInstrument && m.Place == PfdPlace.SpeedTape);

    private static int AltFlag => MessageIndex(m => m.ReplacesInstrument && m.Place == PfdPlace.AltitudeTape);

    private static PfdSample WithMessage(PfdSample s, int index) =>
        s with { MessagesUp = s.MessagesUp.Select((up, k) => up || k == index).ToList() };

    private static bool Passes(double? ias, double? altitude, params (long AtMs, PfdSample Sample)[] history) =>
        Pmdg737PfdCrossCheck.Passes(new PfdSimSnapshot(ias, altitude), history, 10_000, L);

    [Fact]
    public void Speed_and_altitude_match_in_the_window()
    {
        var s = Live();
        Assert.Equal((160.2, 5000.0), (s.Ias, s.Altitude));
        Assert.True(Passes(165, 5050, (9_900L, s)));
    }

    [Fact]
    public void A_lagging_simulator_matches_an_older_sample()
    {
        var older = Live();
        var newer = older with { Ias = 190, Altitude = 5600 };

        Assert.True(Passes(161, 5010, (7_500L, older), (9_900L, newer)));
        Assert.False(Passes(161, 5010, (10_000L - Pmdg737PfdCrossCheck.WindowMs - 1, older), (9_900L, newer)));
    }

    [Fact]
    public void Speed_tolerance_is_three_times_the_flights_largest_difference()
    {
        // The flight (2026-10-30) measured at most 6.9 kt between PMDG's IAS and the simulator's above the tape's floor,
        // on the takeoff roll (PMDG 54.2, the simulator 47.3, accelerating); 3 × 6.9 = 20.7 passes, rounded up to 21.
        var s = Live();
        Assert.True(Passes(160.2 + 20.7, 5000, (9_900L, s)));
        Assert.True(Passes(160.2 - 20.7, 5000, (9_900L, s)));
        Assert.False(Passes(160.2 + 21.5, 5000, (9_900L, s)));
    }

    [Fact]
    public void Speed_off_by_more_than_the_tolerance_fails()
    {
        var s = Live();
        Assert.True(Passes(160.2 + Pmdg737PfdCrossCheck.IasToleranceKt, 5000, (9_900L, s)));
        Assert.False(Passes(160.2 + Pmdg737PfdCrossCheck.IasToleranceKt + 0.5, 5000, (9_900L, s)));
        Assert.False(Passes(160.2 - Pmdg737PfdCrossCheck.IasToleranceKt - 0.5, 5000, (9_900L, s)));
    }

    [Fact]
    public void Altitude_off_by_more_than_the_tolerance_fails()
    {
        var s = Live();
        Assert.True(Passes(160.2, 5000 - Pmdg737PfdCrossCheck.AltitudeToleranceFt, (9_900L, s)));
        Assert.False(Passes(160.2, 5000 + Pmdg737PfdCrossCheck.AltitudeToleranceFt + 1, (9_900L, s)));
    }

    [Fact]
    public void Speed_and_altitude_must_match_in_the_same_sample()
    {
        var speedOnly = Live() with { Altitude = 7000 };
        var altitudeOnly = Live() with { Ias = 250 };
        Assert.False(Passes(160.2, 5000, (9_000L, speedOnly), (9_900L, altitudeOnly)));
    }

    [Fact]
    public void A_flagged_speed_skips_the_check()
    {
        // The SPD flag (the tape not drawn), raised by the sampler from PMDG's own condition; PMDG's IAS left behind.
        var m = new M();
        m.Set(M.Only(L.Speed.TapeShown), 0);
        m.Set((PfdField)L.Speed.Ias, 0);
        var flagged = m.Sample();
        Assert.True(flagged.MessagesUp[SpdFlag]);

        Assert.True(Passes(250, 5000, (9_900L, flagged)));
        // The altitude is still checked.
        Assert.False(Passes(250, 6000, (9_900L, flagged)));
        // A message that only prints text (not in the tape's place) never excuses the speed.
        int printed = MessageIndex(msg => !msg.ReplacesInstrument);
        Assert.False(Passes(250, 5000, (9_900L, WithMessage(Live(), printed))));
        // Nor does the altitude tape's flag.
        Assert.False(Passes(250, 5000, (9_900L, WithMessage(Live(), AltFlag))));
    }

    [Fact]
    public void A_flagged_altitude_skips_the_check()
    {
        // The ALT flag, raised by the sampler from PMDG's own condition; PMDG's altitude leaves the simulator's.
        var m = new M();
        m.Set(M.Only(L.Altitude.TapeShown), 1);
        m.Set((PfdField)L.Altitude.Altitude, 0);
        var flagged = m.Sample();
        Assert.True(flagged.MessagesUp[AltFlag]);

        Assert.True(Passes(160.2, 5000, (9_900L, flagged)));
        // The speed is still checked.
        Assert.False(Passes(250, 5000, (9_900L, flagged)));
    }

    [Fact]
    public void Parked_tape_floor_with_simulator_zero_passes()
    {
        // Live at the gate (2026-10-30): PMDG's IAS 45, the speed tape's floor, while the simulator reads about 0.
        double floor = L.Speed.Geometry.Floor;
        var parked = Live() with { Ias = floor, Altitude = 704 };
        Assert.True(Passes(0, 710, (9_900L, parked)));
        Assert.True(Passes(floor, 710, (9_900L, parked)));
        // Above the floor the tolerance holds again.
        Assert.False(Passes(0, 710, (9_900L, parked with { Ias = floor + Pmdg737PfdCrossCheck.IasToleranceKt + 1 })));
        // The altitude tape has no floor (0 stands for none): two values below 0 are still compared.
        Assert.Equal(0.0, L.Altitude.Geometry.Floor);
        Assert.False(Passes(0, -400, (9_900L, parked with { Altitude = -200 })));
    }

    [Fact]
    public void Missing_simulator_value_never_passes()
    {
        var s = Live();
        Assert.False(Passes(null, 5000, (9_900L, s)));
        Assert.False(Passes(160.2, null, (9_900L, s)));
        Assert.False(Passes(null, 5000, (9_900L, WithMessage(s, SpdFlag))));
        Assert.False(Passes(160.2, 5000));
    }

    [Fact]
    public void Dark_displays_are_not_lit()
    {
        var s = Live();
        var dark = s with { UnitPages = s.UnitPages.Select(_ => L.Units.DarkPages[0]).ToList() };
        Assert.False(Pmdg737PfdCrossCheck.DisplaysLit(dark, L));
        var unpowered = s with { Panel = s.Panel with { Powered = false } };
        Assert.False(Pmdg737PfdCrossCheck.DisplaysLit(unpowered, L));
    }

    [Fact]
    public void Lit_when_a_unit_shows_a_page()
    {
        Assert.True(Pmdg737PfdCrossCheck.DisplaysLit(Live(), L));
        // Ground power live (2026-10-30): PFD, ND, engines, lower, ND, PFD on the six units.
        Assert.True(Pmdg737PfdCrossCheck.DisplaysLit(Live() with { UnitPages = new[] { 2, 3, 5, 6, 3, 2 } }, L));
    }

    [Fact]
    public void Pfd_on_the_outboard_unit_is_shown()
    {
        var s = Live() with { UnitPages = new[] { 2, 3, 5, 6, 3, 2 } };
        Assert.Equal(PfdShown.Shown, Pmdg737PfdCrossCheck.CaptainPfd(s, L.Units));
    }

    [Fact]
    public void Pfd_on_the_inboard_unit_with_the_selector_is_shown()
    {
        // MAIN PANEL DUs at INBD PFD: the outboard unit blank, the inboard one on the PFD page.
        var s = Live() with { UnitPages = new[] { 1, L.Units.PfdPage, 5, 6, 3, 2 } };
        Assert.Equal(PfdShown.Shown, Pmdg737PfdCrossCheck.CaptainPfd(s, L.Units));
    }

    [Fact]
    public void A_unit_waiting_on_the_lower_selector_shows_the_pfd_only_with_it()
    {
        var units = L.Units with { CaptainUnits = new[] { new PfdCaptainUnit(3, L.Units.PfdPage, 1) } };
        var s = Live() with { UnitPages = new[] { 1, 3, 5, L.Units.PfdPage, 3, 2 }, LowerSelector = 1 };
        Assert.Equal(PfdShown.Shown, Pmdg737PfdCrossCheck.CaptainPfd(s, units));
        Assert.Equal(PfdShown.NotOnAnyDisplay, Pmdg737PfdCrossCheck.CaptainPfd(s with { LowerSelector = 0 }, units));
    }

    [Fact]
    public void Classic_page_on_the_captain_unit_is_classic()
    {
        int classic = L.Units.ClassicPages[0];
        var s = Live() with { UnitPages = new[] { classic, 3, 5, 6, 3, 2 } };
        Assert.Equal(PfdShown.Classic, Pmdg737PfdCrossCheck.CaptainPfd(s, L.Units));
        // The glass PFD on another Captain unit wins.
        Assert.Equal(PfdShown.Shown, Pmdg737PfdCrossCheck.CaptainPfd(s with { UnitPages = new[] { classic, 2, 5, 6, 3, 2 } }, L.Units));
    }

    [Fact]
    public void No_captain_unit_on_the_pfd_page_is_not_on_any_display()
    {
        // Only the First Officer's outboard unit (5) shows the PFD page.
        var s = Live() with { UnitPages = new[] { 1, 3, 5, 6, 3, 2 } };
        Assert.Equal(PfdShown.NotOnAnyDisplay, Pmdg737PfdCrossCheck.CaptainPfd(s, L.Units));
        Assert.Equal(PfdShown.NotOnAnyDisplay, Pmdg737PfdCrossCheck.CaptainPfd(s with { UnitPages = new[] { 2 } }, L.Units with
        {
            CaptainUnits = new[] { new PfdCaptainUnit(4, L.Units.PfdPage, null) },
        }));
    }

    [Fact]
    public void Pfd_check_ias_is_cached_and_hidden_from_ctrl_m()
    {
        var def = new PMDG737Definition();
        var vars = def.GetVariables();
        var ias = vars[PMDG737Definition.PfdCheckIasKey];
        Assert.Equal("MSFSBA_PMDG737_PFD_CHECK_IAS", PMDG737Definition.PfdCheckIasKey);
        Assert.Equal("AIRSPEED INDICATED", ias.Name);
        Assert.Equal("knots", ias.Units);
        Assert.Equal(SimVarType.SimVar, ias.Type);
        Assert.Equal(UpdateFrequency.Continuous, ias.UpdateFrequency);
        Assert.True(ias.IsAnnounced);                 // cached for the check
        Assert.True(ias.ExcludeFromMonitorManager);   // VAR-9: consumed silently, so no Ctrl+M row
        // VAR-7: the only continuous var of that name on the 737.
        Assert.Single(vars.Values, v => v.Name == "AIRSPEED INDICATED" && v.UpdateFrequency == UpdateFrequency.Continuous);
        // The altitude comes from the base's cached INDICATED_ALTITUDE.
        Assert.Equal(UpdateFrequency.Continuous, vars["INDICATED_ALTITUDE"].UpdateFrequency);
        Assert.True(vars["INDICATED_ALTITUDE"].IsAnnounced);

        var speech = new SpeechCapture();
        Assert.True(def.ProcessSimVarUpdate(PMDG737Definition.PfdCheckIasKey, 160, speech));
        Assert.Empty(speech.All);
    }
}

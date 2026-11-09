// Where things sit on the PFD, worked out from the layout's derived scales: a tape shows the current value
// ± HalfHeight / PixelsPerUnit (the real speed tape 6 px a knot over 360 px: ±60 kt; the altitude tape 0.9 px a
// foot: ±400 ft), its ends drawn; a bug PMDG parks sits at the edge when out of view. Offsets, headings and
// degrees are rounded as the rows read them; PMDG's bank is positive left (clockwise on the screen, confirmed in
// flight 2026-10-30), the roll bar positive right; the roll bar reads in degrees of bank difference (PMDG's step is
// 2.5°), "at the edge" where PMDG clamps it.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using Xunit;
using G = MSFSBlindAssist.Services.PMDG.Pmdg737PfdGeometry;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdGeometryTests
{
    private static readonly PfdTapeGeometry Speed = Pmdg737PfdTestMemory.Layout.Speed.Geometry;
    private static readonly PfdTapeGeometry Altitude = Pmdg737PfdTestMemory.Layout.Altitude.Geometry;
    private static readonly PfdFlightDirectorFields Fd = Pmdg737PfdTestMemory.Layout.Attitude.FlightDirector;

    private static string Show(double v) => v.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void The_fixture_has_the_real_scales()
    {
        Assert.Equal(new PfdTapeGeometry(6, 360, 45), Speed);
        Assert.Equal((360.0, 0.0), (Altitude.HalfHeightPixels, Altitude.Floor));
        Assert.Equal((10.5, 84.0), (Fd.RollBarPixelsPerUnit, Fd.RollBarFullScalePixels));
    }

    [Fact]
    public void Bug_on_the_strip_has_its_offset()
    {
        Assert.Equal(PfdStripPlace.On, G.Place(208, 190, Speed, parksAtEdge: false));
        Assert.Equal(18, G.Offset(208, 190));
        Assert.Equal(-18, G.Offset(172, 190));
        Assert.Equal(-1, G.Offset(4999.4, 5000.6));
    }

    [Fact]
    public void Bug_out_of_the_slice_is_off()
    {
        Assert.Equal(PfdStripPlace.Off, G.Place(260, 190, Speed, parksAtEdge: false));
        Assert.Equal(PfdStripPlace.Off, G.Place(120, 190, Speed, parksAtEdge: false));
    }

    [Fact]
    public void Parking_bug_out_of_the_slice_is_at_the_edge()
    {
        Assert.Equal(PfdStripPlace.TopEdge, G.Place(260, 190, Speed, parksAtEdge: true));
        Assert.Equal(PfdStripPlace.BottomEdge, G.Place(120, 190, Speed, parksAtEdge: true));
        Assert.Equal(PfdStripPlace.On, G.Place(208, 190, Speed, parksAtEdge: true));
        // Ground session 1: the selected altitude 10000 parked at the top of a tape around 711.
        Assert.Equal(PfdStripPlace.TopEdge, G.Place(10000, 711.65, Altitude, parksAtEdge: true));
    }

    [Fact]
    public void Bug_exactly_at_the_slice_end_is_on()
    {
        Assert.Equal((130.0, 250.0), G.Slice(190, Speed));
        Assert.Equal(PfdStripPlace.On, G.Place(250, 190, Speed, parksAtEdge: false));
        Assert.Equal(PfdStripPlace.On, G.Place(130, 190, Speed, parksAtEdge: false));
        Assert.Equal(PfdStripPlace.Off, G.Place(250.01, 190, Speed, parksAtEdge: false));
        var (low, high) = G.Slice(5000, Altitude);   // PMDG's 0.9f: 400 ft each way, to a few hundred-thousandths
        Assert.Equal(4600, low, 3);
        Assert.Equal(5400, high, 3);
    }

    [Fact]
    public void Band_starting_inside_the_slice_is_on_the_strip()
    {
        Assert.True(G.BandOnStrip(240, null, upward: true, 190, Speed));     // the overspeed band from 240 up
        Assert.True(G.BandOnStrip(140, null, upward: false, 190, Speed));    // the stall band from 140 down
        Assert.True(G.BandOnStrip(200, 220, upward: true, 190, Speed));      // an amber bar from 200 to 220
    }

    [Fact]
    public void Band_starting_beyond_the_slice_is_off()
    {
        Assert.False(G.BandOnStrip(260, null, upward: true, 190, Speed));
        Assert.False(G.BandOnStrip(120, null, upward: false, 190, Speed));
        // A bar ending before the slice begins (its other end known) is off too.
        Assert.False(G.BandOnStrip(100, 120, upward: true, 190, Speed));
        Assert.False(G.BandOnStrip(270, 255, upward: false, 190, Speed));
    }

    [Fact]
    public void Band_starting_past_the_slice_and_running_across_it_is_on_the_strip()
    {
        Assert.True(G.BandOnStrip(300, null, upward: false, 190, Speed));
        Assert.True(G.BandOnStrip(100, null, upward: true, 190, Speed));
        Assert.True(G.BandOnStrip(100, 300, upward: true, 190, Speed));
    }

    [Fact]
    public void Heading_offset_wraps_across_north()
    {
        Assert.Equal(10, G.HeadingOffset(5, 355));
        Assert.Equal(-10, G.HeadingOffset(355, 5));
        Assert.Equal(0, G.HeadingOffset(360, 0));
    }

    [Fact]
    public void Heading_offset_180_is_right()
    {
        Assert.Equal(180, G.HeadingOffset(180, 0));
        Assert.Equal(180, G.HeadingOffset(0, 180));
        Assert.Equal(-179, G.HeadingOffset(181, 0));
        // Live on the runway: the selected heading 080 with the heading 095.5.
        Assert.Equal(-15, G.HeadingOffset(80, 95.46));
    }

    [Fact]
    public void Pitch_rounds_to_half_degrees()
    {
        Assert.Equal(2.0, G.HalfDegrees(2.24));
        Assert.Equal(2.5, G.HalfDegrees(2.26));
        Assert.Equal("0", Show(G.HalfDegrees(-0.24)));   // never "-0"
        Assert.Equal(9.0, G.HalfDegrees(8.97431));       // live in the climb
        Assert.Equal(-1.0, G.HalfDegrees(-1.0454));      // live at the gate
    }

    [Fact]
    public void Bank_rounds_to_whole_degrees()
    {
        Assert.Equal(24, G.WholeDegrees(24.02279));      // live, a left turn (positive is left)
        Assert.Equal(10, G.WholeDegrees(9.5));
        Assert.Equal(-10, G.WholeDegrees(-9.5));
        Assert.Equal(0, G.WholeDegrees(-0.4));
    }

    [Fact]
    public void Pointer_in_tenths_of_a_dot()
    {
        Assert.Equal(0.1, G.TenthsOfDot(0.14));
        Assert.Equal(-1.3, G.TenthsOfDot(-1.26));
        Assert.Equal("0", Show(G.TenthsOfDot(-0.04)));
        // Live at the gate: the localizer 5.75 dots right, drawn at the scale's edge (172.8 px).
        Assert.True(G.AtEdge(172.8, 172.8));
        Assert.True(G.AtEdge(-172.8000030517578, 172.8000030517578));
        Assert.False(G.AtEdge(100, 172.8));
    }

    [Fact]
    public void Fd_pitch_bar_in_degrees()
    {
        Assert.Equal(-8.5, G.FdPitchDegrees(-8.4915));     // live, TO/GA on the runway: 8.5° below
        Assert.Equal("0", Show(G.FdPitchDegrees(0.14197))); // live in the climb: centred
        Assert.Equal(2.5, G.FdPitchDegrees(2.6));
    }

    [Fact]
    public void Fd_roll_bar_in_degrees_of_bank_difference()
    {
        // Live in the climb: banked 24.0 left against a command of 22.6 left, the bar 0.56 steps right.
        Assert.Equal(new PfdFdRoll(1, false), G.FdRoll(0.56427, 24.02279, 22.61212, Fd));
        // Ground session 1: the heading bug 10° right, the command 5.9° right (negative), the bar 2.39 steps right.
        Assert.Equal(new PfdFdRoll(6, false), G.FdRoll(2.39472, 0.04193, -5.94487, Fd));
        // Centred (TO/GA on the runway).
        Assert.Equal(new PfdFdRoll(0, false), G.FdRoll(0.01827, 0.04567, 0, Fd));
        // PMDG holds the bar at 8 steps (84 px): at the edge, the side from the bank difference.
        Assert.Equal(new PfdFdRoll(30, true), G.FdRoll(8, 10, -20, Fd));
        Assert.Equal(new PfdFdRoll(-25, true), G.FdRoll(-8, -5, 20, Fd));
    }
}

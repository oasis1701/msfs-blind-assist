// Tests for the output+I wind readout's gust handling (both halves of the announcement):
//
// 1. MainForm.FormatActiveSkyWind (MainForm.Announcers.cs) — the CURRENT-position wind
//    under ActiveSky. AS's /GetWeatherAreaJson returns two independent wind groups:
//    Ambient* (at aircraft altitude) and Surface* (ground level below the aircraft).
//    The #129 implementation appended SurfaceGustSpeed to the ambient wind
//    unconditionally, so a pilot at FL360 heard their cruise wind "gusting 21" — the
//    ground gust below them glued onto an at-altitude wind. The gust is only a
//    coherent part of the readout when the aircraft is ON the surface, so the
//    formatter takes the on-ground state and gates the suffix on it.
//
// 2. VATSIMService.ParseMETARWind / FormatWind — the DESTINATION wind. The METAR
//    wind-group regex matched the gust (27010G20KT) but discarded it in a
//    non-capturing group, so destination gusts — the ones that matter on approach —
//    were never spoken.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class WindReadoutGustTests
{
    // --- FormatActiveSkyWind: surface gust gated on on-ground -------------------------

    private static ActiveSkyClient.Conditions Conditions(
        double ambientDir, double ambientSpeed, double surfaceGust) => new()
    {
        AmbientWindDirection = ambientDir,
        AmbientWindSpeed = ambientSpeed,
        SurfaceGustSpeed = surfaceGust,
    };

    [Fact]
    public void Airborne_surface_gust_is_not_spoken()
        => Assert.Equal("061 at 11",
            MainForm.FormatActiveSkyWind(Conditions(61, 11, 21), onGround: false));

    [Fact]
    public void OnGround_surface_gust_is_spoken()
        => Assert.Equal("061 at 11, gusting 21",
            MainForm.FormatActiveSkyWind(Conditions(61, 11, 21), onGround: true));

    [Fact]
    public void OnGround_without_gust_has_no_suffix()
        => Assert.Equal("190 at 15",
            MainForm.FormatActiveSkyWind(Conditions(190, 15, 0), onGround: true));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Calm_wind_reads_calm_regardless_of_ground_state(bool onGround)
        => Assert.Equal("calm",
            MainForm.FormatActiveSkyWind(Conditions(0, 0, 0), onGround));

    [Fact]
    public void Direction_and_speeds_round_to_whole_numbers()
        => Assert.Equal("061 at 11, gusting 21",
            MainForm.FormatActiveSkyWind(Conditions(60.6, 11.3, 21.4), onGround: true));

    // --- ParseMETARWind: gust captured from the METAR wind group ----------------------

    [Theory]
    [InlineData("KJFK 071751Z 27010G20KT 10SM FEW020 22/18 A3000", 270, 10, 20)]
    [InlineData("KJFK 071751Z 27010KT 10SM FEW020 22/18 A3000", 270, 10, 0)]
    [InlineData("EGLL 071750Z VRB03KT 9999 SCT030 18/12 Q1015", 0, 3, 0)]
    [InlineData("EGLL 071750Z VRB03G15KT 9999 SCT030 18/12 Q1015", 0, 3, 15)]
    [InlineData("KJFK 071751Z 00000KT 10SM CLR 22/18 A3000", 0, 0, 0)]
    [InlineData("TNCM 071800Z 240100G110KT 1/4SM +TSRA OVC005 26/25 A2910", 240, 100, 110)]
    public void ParseMETARWind_captures_direction_speed_and_gust(
        string metar, int direction, int speed, int gust)
    {
        var wind = VATSIMService.ParseMETARWind(metar);
        Assert.NotNull(wind);
        Assert.Equal(direction, wind!.Value.Direction);
        Assert.Equal(speed, wind.Value.Speed);
        Assert.Equal(gust, wind.Value.Gust);
    }

    [Fact]
    public void ParseMETARWind_returns_null_when_no_wind_group()
        => Assert.Null(VATSIMService.ParseMETARWind("KJFK 071751Z 10SM FEW020 22/18 A3000"));

    // --- FormatWind: destination gust spoken -------------------------------------------

    [Fact]
    public void FormatWind_speaks_gust()
        => Assert.Equal("270 at 10, gusting 20",
            VATSIMService.FormatWind(new VATSIMService.WindData { Direction = 270, Speed = 10, Gust = 20 }));

    [Fact]
    public void FormatWind_without_gust_is_unchanged()
        => Assert.Equal("270 at 10",
            VATSIMService.FormatWind(new VATSIMService.WindData { Direction = 270, Speed = 10 }));

    [Fact]
    public void FormatWind_variable_wind_speaks_gust()
        => Assert.Equal("variable at 3, gusting 15",
            VATSIMService.FormatWind(new VATSIMService.WindData { Direction = 0, Speed = 3, Gust = 15 }));

    [Fact]
    public void FormatWind_calm_and_unavailable_are_unchanged()
    {
        Assert.Equal("calm", VATSIMService.FormatWind(new VATSIMService.WindData()));
        Assert.Equal("unavailable", VATSIMService.FormatWind(null));
    }

    // --- End-to-end: METAR string through parse + format -------------------------------

    [Fact]
    public void Destination_metar_with_gust_reads_direction_speed_and_gust()
        => Assert.Equal("180 at 10, gusting 25",
            VATSIMService.FormatWind(VATSIMService.ParseMETARWind(
                "KJFK 071751Z 18010G25KT 10SM -RA FEW020 22/18 A3000")));

    // --- MPS units and unmeasurable gusts -----------------------------------------------

    [Fact]
    public void ParseMETARWind_converts_mps_to_knots()
    {
        var w = VATSIMService.ParseMETARWind("UUEE 131930Z 24004MPS 9999 BKN020 12/08 Q1013");
        Assert.NotNull(w);
        Assert.Equal(240, w!.Value.Direction);
        Assert.Equal(8, w.Value.Speed);      // 4 m/s × 1.94384 ≈ 7.78 → 8 kt
        Assert.Equal(0, w.Value.Gust);
    }

    [Fact]
    public void ParseMETARWind_converts_mps_gust_to_knots()
    {
        var w = VATSIMService.ParseMETARWind("UUEE 131930Z 24007G12MPS 9999 BKN020 12/08 Q1013");
        Assert.Equal(14, w!.Value.Speed);    // 7 m/s → 13.6 → 14 kt
        Assert.Equal(23, w.Value.Gust);      // 12 m/s → 23.3 → 23 kt
    }

    [Fact]
    public void ParseMETARWind_unmeasurable_gust_keeps_direction_and_speed()
    {
        var w = VATSIMService.ParseMETARWind("EGLL 131920Z 27010G//KT 9999 FEW030 18/09 Q1021");
        Assert.NotNull(w);                   // G// must not fail the whole match
        Assert.Equal(270, w!.Value.Direction);
        Assert.Equal(10, w.Value.Speed);
        Assert.Equal(0, w.Value.Gust);
    }
}

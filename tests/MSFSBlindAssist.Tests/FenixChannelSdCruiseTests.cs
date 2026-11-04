// Characterization tests for the Fenix SD's CRUISE page (FenixChannelSdPages.CruiseRows, seam ComposeCruise). The pushes are
// built with SdPush (FenixChannelSdWindowTests.cs) and the composer is called directly, so every field number is written
// out here, never read from FenixChannelFields: a wrong number in the app fails a test.
//
// The page reads the engines 129.1 and 129.2 (OilQuantity 23, VibrationN1 11, VibrationN2 12), the fuel used 134.13
// (Engine1 1, Engine2 2, Total 3), the weight unit 154 (left out is LBS), the cabin zones 151.7 (1 cockpit, 2 forward, 3
// aft), the flags 151.9 (ManualLandingElev 16) and the pressurization data 151.10 (ΔP 1, V/S 2, CAB ALT 3, LDG ELEV 7).
// A message left out stops the page ("no field 129"), never its defaults.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdCruiseTests
{
    private static FenixChannelProtoWriter Engine(double vibN1, double vibN2, double oil)
        => new FenixChannelProtoWriter().Double(11, vibN1).Double(12, vibN2).Double(23, oil);

    private static FenixChannelProtoWriter Used(double first, double second, double total)
        => new FenixChannelProtoWriter().Double(1, first).Double(2, second).Double(3, total);

    // The cruise push of 2026-11-03 (A321, LBS). Parts named in omit are left out of the push.
    private static SdPush CruisePush(bool kg = false, string omit = "", double vs = -0.1665, double deltaP = 7.983)
    {
        var pressure = new FenixChannelProtoWriter().Double(1, deltaP).Double(2, vs).Double(3, 6399.74).Varint(4, 1)
            .Varint(5, 1).Varint(9, 1).Double(6, 0.0842).Double(7, 550);
        var zone = new FenixChannelProtoWriter().Double(1, 23.99).Double(2, 23.99).Double(3, 23.99);
        var pneumatic = new FenixChannelProtoWriter();
        if (omit != "151.7") pneumatic.Message(7, zone);
        if (omit != "151.9") pneumatic.Message(9, new FenixChannelProtoWriter().Bool(15, true));
        if (omit != "151.10") pneumatic.Message(10, pressure);

        var engines = new FenixChannelProtoWriter();
        if (omit != "129.1") engines.Message(1, Engine(0.4827, 0.911, 12.79));
        if (omit != "129.2") engines.Message(2, Engine(0.4471, 0.9402, 12.9838));

        return new SdPush { Page = 5, Kg = kg }.With(remote =>
        {
            if (omit != "129") remote.Message(129, engines);
            if (omit != "134") remote.Message(134, new FenixChannelProtoWriter().Message(13, Used(2770, 2770, 5540)));
            if (omit != "151") remote.Message(151, pneumatic);
        });
    }

    private static IReadOnlyList<string> CruiseRowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelSdPages.CruiseRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    [Fact]
    public void The_cruise_page_reads_engines_and_air()
    {
        Assert.Equal(
            new[]
            {
                "F.USED LBS: 2770, 2770", "F.USED 1+2: 5540 LBS", "OIL QT: 12.8, 13.0", "VIB N1: 0.5, 0.4", "VIB N2: 0.9, 0.9",
                "LDG ELEV AUTO: 550 FT", "ΔP: 8.0 PSI", "CAB V/S: 0 FT/MIN", "CAB ALT: 6400 FT", "CKPT: 24 °C", "FWD: 24 °C",
                "AFT: 24 °C"
            },
            CruiseRowsOf(CruisePush()));
    }

    [Fact]
    public void Cruise_fuel_used_follows_the_weight_unit()
    {
        IReadOnlyList<string> rows = CruiseRowsOf(CruisePush(kg: true));

        Assert.Equal("F.USED KG: 2770, 2770", rows[0]);
        Assert.Equal("F.USED 1+2: 5540 KG", rows[1]);
    }

    [Fact]
    public void Cruise_ldg_elev_reads_man_while_manual()
    {
        var pneumatic = new FenixChannelProtoWriter().Message(7, new FenixChannelProtoWriter())
            .Message(9, new FenixChannelProtoWriter().Bool(16, true))
            .Message(10, new FenixChannelProtoWriter().Double(7, 1250));
        SdPush push = CruisePush(omit: "151").With(remote => remote.Message(151, pneumatic));

        Assert.Equal("LDG ELEV MAN: 1250 FT", CruiseRowsOf(push)[5]);
    }

    [Theory]
    [InlineData("129", "no field 129")]
    [InlineData("129.1", "no field 129.1")]
    [InlineData("129.2", "no field 129.2")]
    [InlineData("134", "no field 134")]
    [InlineData("151", "no field 151")]
    [InlineData("151.7", "no field 151.7")]
    [InlineData("151.9", "no field 151.9")]
    [InlineData("151.10", "no field 151.10")]
    public void A_missing_engine_message_is_not_available(string omit, string problem)
    {
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.CruiseRows(CruisePush(omit: omit).Context(reported.Add));

        Assert.Equal(problem, rows.Problem);
        Assert.Null(rows.Rows);
        Assert.Empty(reported);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Cruise_rows_are_the_same_in_every_culture(string culture)
    {
        IReadOnlyList<string> rows = TestCulture.Run(culture, () => CruiseRowsOf(CruisePush(vs: -0.4, deltaP: 7.86)));

        Assert.Contains("CAB V/S: 0 FT/MIN", rows);
        Assert.Contains("ΔP: 7.9 PSI", rows);
        Assert.Contains("OIL QT: 12.8, 13.0", rows);
    }
}

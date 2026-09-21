using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The text of a Status Display row (MainForm's read-only list, Ctrl+3): a lamp reads the same
/// composed state it speaks, an exported number reads with its unit, an AFS window reads the
/// composer's panel wording. Rows render as "{DisplayName}: {text}", so the text never repeats
/// the name.
/// </summary>
public class Md11StatusRowTests
{
    private static Md11StateSpec Lamp(string var, string lit, string? dark) => new()
    {
        Lamps = new List<Md11StateLamp> { new() { Var = var, Legend = lit.ToUpperInvariant(), Lit = lit } },
        Dark = dark,
    };

    private static double? None(string _) => null;

    [Fact]
    public void Lamp_Lit_SpeaksItsLegend()
        => Assert.Equal("Off", Md11StatusRow.Lamp(Lamp("MD11_OVHD_ELEC_AC1_OFF_LT", "Off", "Powered"), "MD11_OVHD_ELEC_AC1_OFF_LT", 1, None, powered: true));

    [Fact]
    public void Lamp_DarkAndPowered_SpeaksTheDarkMeaning()
        => Assert.Equal("Powered", Md11StatusRow.Lamp(Lamp("MD11_OVHD_ELEC_AC1_OFF_LT", "Off", "Powered"), "MD11_OVHD_ELEC_AC1_OFF_LT", 0, None, powered: true));

    [Fact]
    public void Lamp_DarkAndUnpowered_SaysUnpowered()
        => Assert.Equal(Md11ControlState.Unpowered, Md11StatusRow.Lamp(Lamp("X_LT", "On", null), "X_LT", 0, None, powered: false));

    [Fact]
    public void Lamp_DarkWithNoDarkMeaning_LeavesTheFallbackToTheCaller()
        => Assert.Null(Md11StatusRow.Lamp(Lamp("X_LT", "On", null), "X_LT", 0, None, powered: true));

    [Fact]
    public void Lamp_UsesTheDeliveredValue_NotTheCache()
    {
        // The cache still says dark; the value just delivered says lit — the row follows the delivery.
        Assert.Equal("On", Md11StatusRow.Lamp(Lamp("X_LT", "On", "Off"), "X_LT", 1, _ => 0, powered: true));
    }

    [Theory]
    [InlineData("MD11_V1", 0, "not set")]
    [InlineData("MD11_V1", 152, "152 knots")]
    [InlineData("MD11_VFR", 210, "210 knots")]
    [InlineData("MD11_CAP_MINIMUMS", 0, "not set")]
    [InlineData("MD11_CAP_MINIMUMS", 200, "200 feet")]
    [InlineData("MD11_CAP_ALTIMETER", 29.92, "standard")]
    [InlineData("MD11_FO_ALTIMETER", 30.12, "1020, 30.12")]
    [InlineData("MD11_STBY_ALTIMETER", 995, "995, 29.38")]
    [InlineData("MD11_CAP_ALTIMETER", 0, "not available")]      // not yet delivered: never "0, 0.00"
    [InlineData("MD11_FO_ALTIMETER", 0, "not available")]
    [InlineData("MD11_STBY_ALTIMETER", -1, "not available")]
    [InlineData("MD11_ATS_STATE", 1, "on")]
    [InlineData("MD11_ATS_STATE", 0, "off")]
    [InlineData("MD11_APU_N1", 25.4, "25 percent")]
    [InlineData("MD11_ENG1_N1", 25.396, "25.4 percent")]
    [InlineData("MD11_ENG2_N1", 0, "0.0 percent")]
    [InlineData("MD11_ENG3_N1", 101.26, "101.3 percent")]
    [InlineData("MD11_OVHD_TANK_1_VAL", 12000, "12000 pounds")]
    [InlineData("MD11_OVHD_TANK_TAIL_VAL", 0, "0 pounds")]
    [InlineData("MD11_AFS_ALT", 36000, "36000 feet")]
    public void Readout_FormatsWithItsUnit(string key, double value, string expected)
        => Assert.Equal(expected, Md11StatusRow.Readout(key, value, None));

    [Fact]
    public void Readout_AfsRows_CarryTheModeWordOnlyWhenNotTheDefault()
    {
        static double? Track(string k) => k == Md11Fcp.ModeHeadingIsTrack ? 1 : 0;
        static double? Fpa(string k) => k == Md11Fcp.ModeVerticalIsFpa ? 1 : 0;
        static double? Mach(string k) => k == Md11Fcp.ModeSpeedIsMach ? 1 : 0;
        Assert.Equal("123", Md11StatusRow.Readout(Md11Fcp.ReadHeading, 123, None));
        Assert.Equal("track 123", Md11StatusRow.Readout(Md11Fcp.ReadHeading, 123, Track));
        Assert.Equal("dashed, NAV engaged", Md11StatusRow.Readout(Md11Fcp.ReadHeading, -999, None));
        Assert.Equal("FPA -3.0 degrees", Md11StatusRow.Readout(Md11Fcp.ReadVerticalSpeed, -3, Fpa));
        Assert.Equal("-1500 feet per minute", Md11StatusRow.Readout(Md11Fcp.ReadVerticalSpeed, -1500, None));
        Assert.Equal("Mach 0.820", Md11StatusRow.Readout(Md11Fcp.ReadSpeed, 0.82, Mach));
        Assert.Equal("dashed, FMS speed engaged", Md11StatusRow.Readout(Md11Fcp.ReadSpeed, -999, None));
    }

    [Fact]
    public void Readout_LeavesOtherKeysToTheGenericPath()
    {
        Assert.Null(Md11StatusRow.Readout("MD11_AP_STATE", 1, None));   // ValueDescriptions: "AP 1"
        Assert.Null(Md11StatusRow.Readout("COM_ACTIVE_FREQUENCY:1", 135500, None));   // its own override
    }

    [Fact]
    public void Readout_MinimumsRows_SayWhichModeTheyShow()
    {
        static double? Radio(string k) => k == "MD11_CAP_MINIMUMS_MODE" ? 0 : null;
        static double? Baro(string k) => k == "MD11_FO_MINIMUMS_MODE" ? 1 : null;
        Assert.Equal("200 feet, radio", Md11StatusRow.Readout("MD11_CAP_MINIMUMS", 200, Radio));
        Assert.Equal("500 feet, baro", Md11StatusRow.Readout("MD11_FO_MINIMUMS", 500, Baro));
        Assert.Equal("200 feet", Md11StatusRow.Readout("MD11_CAP_MINIMUMS", 200, None));   // mode never read
        Assert.Equal("not set", Md11StatusRow.Readout("MD11_FO_MINIMUMS", 0, Baro));
    }
}

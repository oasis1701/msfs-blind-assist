// The Fenix options window shows the Fenix EFB's own words for each option, so a pilot reads what a
// sighted pilot reads in the EFB. These pin the snapshot of those words (EFB bundle 3.0.0.7426): the
// right count, labels scoped to their own option, descriptions whole and unescaped, and the control hints.
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixOptionLabelsTests
{
    [Fact]
    public void Table_holds_every_option_the_EFB_labels()
    {
        Assert.Equal(125, FenixOptionLabels.All.Count);
        Assert.Equal(34, FenixOptionLabels.All.Values.Sum(t => t.ValueLabels.Count));
    }

    [Theory]
    [InlineData("encoder_acceleration_enabled", "Encoder Acceleration")]
    [InlineData("efis_baro_linking_type", "EFIS Linking: Baro")]
    [InlineData("fd_ls_sync_enabled", "EFIS Linking: FD/LS")]
    [InlineData("channel_spacing_type", "Channel Spacing")]
    [InlineData("tcas_ap_enabled", "AP/FD TCAS")]
    public void Labels_are_the_EFB_own(string key, string label) => Assert.Equal(label, FenixOptionLabels.All[key].Label);

    [Fact]
    public void Value_labels_are_scoped_to_their_option()
    {
        Assert.Equal("Realistic (16 Kg/s)", FenixOptionLabels.All["refuel_rate_type"].ValueLabels["Realistic"]);
        Assert.Equal("Realistic (1x)", FenixOptionLabels.All["failure_rate"].ValueLabels["Realistic"]);
        Assert.Equal("8.33 kHz", FenixOptionLabels.All["channel_spacing_type"].ValueLabels["Khz8d33"]);
        Assert.Equal("Linked Left+Right+Standby", FenixOptionLabels.All["efis_baro_linking_type"].ValueLabels["LinkedWithStandby"]);
    }

    [Fact]
    public void Descriptions_are_whole_and_unescaped()
    {
        Assert.EndsWith("For GSX, doors will always be handled automatically.", FenixOptionLabels.All["door_automation_enabled"].Description);
        Assert.StartsWith("New style FCU readouts", FenixOptionLabels.All["fcu_readouts_type"].Description);
        Assert.Equal("FWC \"Ten\" callout.", FenixOptionLabels.All["gpws10"].Description);
        Assert.Equal("Boarding → Fuel", FenixOptionLabels.All["gsx_boarding_service_sequence_type"].ValueLabels["BoardingThenFuel"]);
    }

    [Fact]
    public void Control_hints_are_kept()
    {
        Assert.Equal("password", FenixOptionLabels.All["hoppie_key"].ControlType);
        Assert.Equal("action-button", FenixOptionLabels.All["navigraph_refresh_token"].ControlType);
        Assert.Null(FenixOptionLabels.All["weight_unit"].ControlType);
    }

    [Fact]
    public void Parse_reads_option_and_value_rows()
    {
        var t = FenixOptionLabels.Parse("k||Key label|Key text|\nk|V1|Value one||\n");
        Assert.Equal("Key label", t["k"].Label);
        Assert.Equal("Value one", t["k"].ValueLabels["V1"]);
    }

    [Theory]
    [InlineData("k|Key")]                 // not five fields
    [InlineData("k|V1|Value one||\n")]    // a value row before its option row
    public void Parse_refuses_a_malformed_table(string table) => Assert.Throws<FormatException>(() => FenixOptionLabels.Parse(table));
}

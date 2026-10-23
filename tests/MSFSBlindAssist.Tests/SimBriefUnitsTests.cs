using System.Xml;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

// The SimBrief planner labels every fuel and weight with the plan's own unit. SimBrief publishes it
// as params/units ("kgs" or "lbs"); the general object has none, so reading it there labelled every
// kilogram plan "lbs". The TLR's QNH follows each airport's unit, so one plan can mix hPa and inHg.
public class SimBriefUnitsTests
{
    private static SimBriefOFP Parse(string paramsXml, string generalXml = "") =>
        new SimBriefService().ParseFullOFP(Load($"""
            <OFP>
              {paramsXml}
              <general><route>DCT</route>{generalXml}</general>
              <api_params><minfob_units>wgt</minfob_units></api_params>
              <origin><icao_code>YPDN</icao_code></origin>
              <destination><icao_code>AYPY</icao_code></destination>
            </OFP>
            """));

    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    [Fact]
    public void A_kilogram_plan_reads_as_kgs_from_params()
    {
        Assert.Equal("kgs", Parse("<params><request_id>1</request_id><units>kgs</units></params>").Units);
    }

    [Fact]
    public void A_pound_plan_reads_as_lbs_from_params()
    {
        Assert.Equal("lbs", Parse("<params><request_id>1</request_id><units>lbs</units></params>").Units);
    }

    [Fact]
    public void A_plan_without_params_units_falls_back_to_lbs()
    {
        Assert.Equal("lbs", Parse("<params><request_id>1</request_id></params>").Units);
    }

    [Fact]
    public void A_units_field_under_general_is_not_the_source()
    {
        // params says kgs; a stray general/units must not override it.
        Assert.Equal("kgs", Parse("<params><units>kgs</units></params>", "<units>lbs</units>").Units);
    }

    [Theory]
    [InlineData("1011", "1011 hPa")]
    [InlineData("29.88", "29.88 inHg")]
    [InlineData("1013.2", "1013.2 hPa")]
    [InlineData("----", "----")]
    [InlineData("", "")]
    public void Tlr_qnh_takes_the_unit_its_value_is_written_in(string raw, string expected)
    {
        Assert.Equal(expected, TestCulture.Run("de-DE", () => SimBriefPlannerForm.TlrFmtQnh(raw)));
    }
}

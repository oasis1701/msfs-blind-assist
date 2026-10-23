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

    // Trimmed from a real YPDN-AYPY plan's OFP/tlr.
    private const string TlrXml = """
        <tlr>
          <takeoff>
            <runway><identifier>29</identifier><length_tora>11004</length_tora></runway>
            <runway><identifier>18</identifier><length_tora>5000</length_tora></runway>
          </takeoff>
          <landing>
            <distance_dry><actual_distance>3775</actual_distance><factored_distance>5028</factored_distance></distance_dry>
            <distance_wet><actual_distance>4764</actual_distance><factored_distance>6166</factored_distance></distance_wet>
            <runway><identifier>14L</identifier><length_lda>9022</length_lda></runway>
            <runway><identifier>14R</identifier><length_lda>4265</length_lda></runway>
          </landing>
        </tlr>
        """;

    [Fact]
    public void Tlr_runway_lengths_and_landing_distances_are_read_from_the_ofp_fields()
    {
        var ofp = Parse("<params><units>kgs</units></params>" + TlrXml);

        Assert.Equal("11004", ofp.TlrTakeoffToraFt["29"]);
        Assert.Equal("4265", ofp.TlrLandingLdaFt["14r"]);
        Assert.Equal("3775", ofp.TlrLdgActualDryFt);
        Assert.Equal("4764", ofp.TlrLdgActualWetFt);
        Assert.Equal("5028", ofp.TlrLdgFactoredDryFt);
        Assert.Equal("6166", ofp.TlrLdgFactoredWetFt);
    }

    [Fact]
    public void A_plan_without_tlr_fields_leaves_them_empty()
    {
        var ofp = Parse("<params><units>kgs</units></params>");

        Assert.Empty(ofp.TlrTakeoffToraFt);
        Assert.Equal("", ofp.TlrLdgActualDryFt);
    }

    [Theory]
    [InlineData("3775", "3,775 ft")]   // was shown as 12,385 ft
    [InlineData("4265", "4,265 ft")]   // was shown as 13,993 ft
    [InlineData("11004", "11,004 ft")]
    [InlineData("", "")]
    public void Tlr_distances_are_feet_even_when_short(string raw, string expected)
    {
        Assert.Equal(expected, TestCulture.Run("en-US", () => SimBriefPlannerForm.TlrFmtFt(raw)));
    }

    [Theory]
    [InlineData("3775", "9999", "3775")]
    [InlineData("", "3775", "3775")]
    [InlineData(null, "3775", "3775")]
    public void The_ofp_field_wins_over_the_tlr_text(string? structured, string text, string expected)
    {
        Assert.Equal(expected, SimBriefPlannerForm.TlrPreferStructured(structured, text));
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

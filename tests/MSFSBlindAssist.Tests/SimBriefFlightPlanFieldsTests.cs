using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

// The route briefing classifies the aircraft from the SimBrief OFP only (owner's choice), so the
// four fields it needs must survive the parse. ParseSimBriefXML is internal for this test.
public class SimBriefFlightPlanFieldsTests
{
    private const string Ofp = """
        <OFP>
          <general><icao_airline>UPS</icao_airline><route>DCT</route></general>
          <aircraft><icaocode>MD1F</icaocode><name>MD-11F</name><max_passengers>0</max_passengers></aircraft>
          <origin><icao_code>KSDF</icao_code><plan_rwy>17R</plan_rwy></origin>
          <destination><icao_code>KLAX</icao_code><plan_rwy>25L</plan_rwy></destination>
          <navlog></navlog>
        </OFP>
        """;

    [Fact]
    public void Aircraft_and_airline_fields_are_read_from_the_ofp()
    {
        var plan = new SimBriefService().ParseSimBriefXML(Ofp, "user");

        Assert.Equal("MD1F", plan.AircraftTypeIcao);
        Assert.Equal("MD-11F", plan.AircraftName);
        Assert.Equal(0, plan.AircraftMaxPassengers);
        Assert.Equal("UPS", plan.AirlineIcao);
        Assert.Equal("KSDF", plan.DepartureICAO);
        Assert.Equal("25L", plan.ArrivalRunway);
    }

    [Fact]
    public void Missing_max_passengers_reads_as_null_and_missing_aircraft_as_empty()
    {
        const string ofp = """
            <OFP>
              <general></general>
              <origin><icao_code>EGLL</icao_code></origin>
              <destination><icao_code>KJFK</icao_code></destination>
            </OFP>
            """;
        var plan = new SimBriefService().ParseSimBriefXML(ofp, "user");

        Assert.Null(plan.AircraftMaxPassengers);
        Assert.Equal("", plan.AircraftTypeIcao);
        Assert.Equal("", plan.AircraftName);
        Assert.Equal("", plan.AirlineIcao);
    }
}

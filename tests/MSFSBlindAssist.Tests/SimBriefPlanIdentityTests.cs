using System.Xml;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

// The First Officer window forgets a plan once its flight has landed, and the next automatic fetch
// refuses the plan just flown until SimBrief has a new one (PR 160 second review). That compares the
// plans' IdentityKey: SimBrief's request id, else when it was generated, else the route itself.
public class SimBriefPlanIdentityTests
{
    private static SimBriefOFP Parse(string paramsXml) =>
        new SimBriefService().ParseFullOFP(Load($"""
            <OFP>
              {paramsXml}
              <general><flight_number>123</flight_number><route>DCT</route><initial_altitude>35000</initial_altitude></general>
              <origin><icao_code>EGLL</icao_code></origin>
              <destination><icao_code>KJFK</icao_code></destination>
            </OFP>
            """));

    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    [Fact]
    public void The_request_id_is_the_plans_identity()
    {
        var ofp = Parse("<params><request_id>98765</request_id><time_generated>1760000000</time_generated></params>");

        Assert.Equal("98765", ofp.PlanId);
        Assert.Equal("98765", ofp.IdentityKey);
    }

    [Fact]
    public void Without_a_request_id_the_time_generated_is_the_identity()
    {
        var ofp = Parse("<params><time_generated>1760000000</time_generated></params>");

        Assert.Equal("1760000000", ofp.IdentityKey);
    }

    // A plan regenerated for the same route is a new plan: a new request id, so a new identity.
    [Fact]
    public void A_regenerated_plan_for_the_same_route_is_a_new_plan()
        => Assert.NotEqual(
            Parse("<params><request_id>1</request_id></params>").IdentityKey,
            Parse("<params><request_id>2</request_id></params>").IdentityKey);

    [Fact]
    public void Without_params_the_route_is_the_identity()
    {
        var ofp = Parse("");

        Assert.Equal("", ofp.PlanId);
        Assert.Equal("EGLL|KJFK|123|35000|DCT", ofp.IdentityKey);
    }
}

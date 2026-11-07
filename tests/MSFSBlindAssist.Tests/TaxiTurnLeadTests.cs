// The taxi-turn leads and their ceiling are per-aircraft (IAircraftDefinition.TaxiTurnLeadSeconds,
// TaxiSharpTurnLeadSeconds, TaxiTurnLeadMaxDegrees). The Fenix's values were measured on Fenix v3
// on 2026-10-23 (docs/taxi-guidance.md, the lead table). Every aircraft that sets neither keeps a
// constant lead under a 30-degree ceiling, so tuning one aircraft changes no other.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiTurnLeadTests
{
    [Fact]
    public void Fenix_leads_and_ceiling_are_the_measured_v3_values()
    {
        var fenix = new FenixA320Definition();
        Assert.Equal(1.3, fenix.TaxiTurnLeadSeconds);
        Assert.Equal(1.7, fenix.TaxiSharpTurnLeadSeconds);
        Assert.Equal(45.0, fenix.TaxiTurnLeadMaxDegrees);
    }

    [Theory]
    [InlineData(nameof(IAircraftDefinition.TaxiSharpTurnLeadSeconds))]
    [InlineData(nameof(IAircraftDefinition.TaxiTurnLeadMaxDegrees))]
    public void Only_the_Fenix_sets_its_own_sharp_turn_lead_or_ceiling(string property)
    {
        var overriders = typeof(BaseAircraftDefinition).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(BaseAircraftDefinition)) && !t.IsAbstract)
            .Where(t => t.GetProperty(property)!.DeclaringType != typeof(BaseAircraftDefinition))
            .Select(t => t.Name)
            .ToList();

        // A new entry here needs its own measured turn rollouts, as the Fenix's have.
        Assert.Equal(new[] { nameof(FenixA320Definition) }, overriders);
    }

    [Fact]
    public void An_aircraft_that_sets_neither_keeps_a_constant_lead_under_30_degrees()
    {
        // The same type from another maker: its measured 1.6 s in every turn, ceiling 30.
        var fbw = new FlyByWireA320Definition();
        Assert.Equal(fbw.TaxiTurnLeadSeconds, fbw.TaxiSharpTurnLeadSeconds);
        Assert.Equal(30.0, fbw.TaxiTurnLeadMaxDegrees);
    }

    [Fact]
    public void Fenix_lead_grows_through_a_sharp_turn_and_its_ceiling_lets_it()
    {
        var fenix = new FenixA320Definition();
        double gentle = fenix.TaxiTurnLeadSeconds, sharp = fenix.TaxiSharpTurnLeadSeconds;

        Assert.Equal(1.3, GuidanceGeometry.TurnLeadSeconds(20.0, gentle, sharp), 3);
        Assert.Equal(1.5, GuidanceGeometry.TurnLeadSeconds(45.0, gentle, sharp), 3);
        Assert.Equal(1.7, GuidanceGeometry.TurnLeadSeconds(75.0, gentle, sharp), 3);

        // 22 deg/s, the KATL 89-degree turn's peak: 1.7 s asks for 37.4 deg of anticipation.
        // The base ceiling stops it at 30; the Fenix's 45 passes it whole.
        Assert.Equal(-30.0, GuidanceGeometry.ProjectHeadingError(0.0, 22.0, sharp, 30.0), 0.01);
        Assert.Equal(-37.4, GuidanceGeometry.ProjectHeadingError(0.0, 22.0, sharp, fenix.TaxiTurnLeadMaxDegrees), 0.01);
    }
}

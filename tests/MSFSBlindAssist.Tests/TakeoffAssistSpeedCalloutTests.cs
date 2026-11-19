using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Takeoff Assist's own "80 knots" and "100 knots" calls, and the aircraft-definition opt-out of "80 knots" an
/// airframe that plays its own speed callouts takes (<see cref="IAircraftDefinition.PlaysOwnTakeoffSpeedCallouts"/>).
/// The calls are shared code: every aircraft but the one opting out keeps them unchanged.
/// </summary>
public class TakeoffAssistSpeedCalloutTests
{
    /// <summary>Legacy mode: no tone generator, so the manager runs headless.</summary>
    private static (TakeoffAssistManager Manager, SpeechCapture Speech) ActiveManager()
    {
        var speech = new SpeechCapture();
        var manager = new TakeoffAssistManager(speech, useLegacyMode: true, useEnableCallouts: true);
        manager.Toggle(47.45, -122.31, 160, 15);
        speech.All.Clear();
        return (manager, speech);
    }

    [Fact]
    public void Takeoff_assist_calls_80_and_100_knots_by_default()
    {
        var (manager, speech) = ActiveManager();
        using (manager)
        {
            manager.ProcessSpeedUpdate(85);
            manager.ProcessSpeedUpdate(105);
        }

        Assert.Equal(new[] { "80 knots", "100 knots" }, speech.All);
    }

    [Fact]
    public void An_aircraft_that_calls_its_own_speeds_hears_only_100_knots_from_takeoff_assist()
    {
        // The 757 calls 80 knots itself (so never twice), but no 100 knots: Takeoff Assist keeps that one.
        var (manager, speech) = ActiveManager();
        using (manager)
        {
            manager.ProcessSpeedUpdate(85, aircraftCallsOwnSpeeds: true);
            manager.ProcessSpeedUpdate(105, aircraftCallsOwnSpeeds: true);
            manager.ProcessSpeedUpdate(140, aircraftCallsOwnSpeeds: true);
        }

        Assert.Equal(new[] { "100 knots" }, speech.All);
    }

    // #242 met #358 (2026-11-19): the calls are configurable per aircraft (the DA40's "Rotate"), and the opt-out acts on the
    // configured LOW call, whatever it says, never the high one (an automatic merge had silenced both).
    [Theory]
    [InlineData(false, new[] { "Rotate", "Positive" })]
    [InlineData(true, new[] { "Positive" })]
    public void The_opt_out_acts_on_the_configured_low_call_only(bool callsOwnSpeeds, string[] expected)
    {
        var (manager, speech) = ActiveManager();
        using (manager)
        {
            manager.ConfigureSpeedCallouts(67, "Rotate", 90, "Positive");
            manager.ProcessSpeedUpdate(70, aircraftCallsOwnSpeeds: callsOwnSpeeds);
            manager.ProcessSpeedUpdate(95, aircraftCallsOwnSpeeds: callsOwnSpeeds);
        }

        Assert.Equal(expected, speech.All);
    }

    [Fact]
    public void The_opt_out_leaves_the_rest_of_takeoff_assist_speaking()
    {
        var (manager, speech) = ActiveManager();
        using (manager)
        {
            manager.ProcessSpeedUpdate(85, aircraftCallsOwnSpeeds: true);
            manager.ProcessPitchUpdate(8);   // the pitch call is takeoff assist's, not a speed callout
        }

        Assert.Equal(new[] { "+8" }, speech.All);
    }

    /// <summary>A definition built with no arguments, its optional parameters at their defaults: the COWS DA40's one parameter is its
    /// airframe (NG unless told), and <see cref="Activator.CreateInstance(Type)"/> fills no optional parameter (#242 met #358).</summary>
    private static IAircraftDefinition Create(Type type)
    {
        var ctor = type.GetConstructors().Where(c => c.GetParameters().All(p => p.IsOptional))
            .OrderBy(c => c.GetParameters().Length).First();
        return (IAircraftDefinition)ctor.Invoke(ctor.GetParameters().Select(p => p.DefaultValue).ToArray());
    }

    /// <summary>Every shipped aircraft definition, found by reflection so a new one is never left out.</summary>
    public static IEnumerable<object[]> EveryShippedAircraft()
    {
        foreach (var type in typeof(BaseAircraftDefinition).Assembly.GetTypes()
                     .Where(t => !t.IsAbstract && t.IsClass && typeof(IAircraftDefinition).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
            yield return new object[] { type };
    }

    [Theory]
    [MemberData(nameof(EveryShippedAircraft))]
    public void Only_the_bluebird_757_opts_out_of_takeoff_assist_s_speed_callouts(Type type)
    {
        var aircraft = Create(type);
        try
        {
            // The BlueBird 757 plays its own 80 knots, V1 and rotate (docs/b757.md, "Take-off callouts").
            Assert.Equal(aircraft.AircraftCode == "BBS_757", aircraft.PlaysOwnTakeoffSpeedCallouts);
        }
        finally
        {
            (aircraft as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void The_fleet_reflection_finds_every_listed_aircraft()
    {
        var found = EveryShippedAircraft().Select(row => (Type)row[0]).ToHashSet();
        foreach (var row in ComboLabelCollapseTests.AllAircraft())
            Assert.Contains(row[0].GetType(), found);
    }
}

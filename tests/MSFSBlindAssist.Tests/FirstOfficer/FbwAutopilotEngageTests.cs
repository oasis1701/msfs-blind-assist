using System;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-21: the FlyByWire family's universal auto-engage. FlyByWire captures the stock
/// AUTOPILOT_ON into a flag nothing reads, so before 2026-10-09 the app announced
/// "Autopilot engaged" over an autopilot that never engaged. The engage is now the cockpit's
/// AP 1 push — a TOGGLE — sent only while no autopilot reads engaged, verified on AP 1 / AP 2
/// active, and spaced 3 s apart. Every other aircraft keeps the every-tick retry.
/// </summary>
public class FbwAutopilotEngageTests
{
    [Theory]
    [InlineData(typeof(PMDG777Definition))]
    [InlineData(typeof(PMDG737Definition))]
    [InlineData(typeof(IFly737MAXDefinition))]
    [InlineData(typeof(FenixA320Definition))]
    [InlineData(typeof(TFDiMD11Definition))]
    [InlineData(typeof(HorizonSim787Definition))]
    public void Other_aircraft_keep_the_every_tick_retry(Type defType)
    {
        var def = (IAircraftDefinition)Activator.CreateInstance(defType)!;
        try { Assert.Equal(0, def.AutopilotEngageRetryIntervalMs); }
        finally { (def as IDisposable)?.Dispose(); }
    }

    private static Func<string, double?> Read(double? ap1, double? ap2) => key => key switch
    {
        FbwAutopilotEngage.Ap1ActiveVar => ap1,
        FbwAutopilotEngage.Ap2ActiveVar => ap2,
        _ => null,
    };

    [Theory]
    [InlineData(1.0, 0.0, true)]
    [InlineData(0.0, 1.0, true)]
    [InlineData(1.0, null, true)]      // one engaged autopilot is enough to know
    [InlineData(0.0, 0.0, false)]
    [InlineData(0.0, null, null)]      // unknown is never "off" (FO-18): the push is a toggle
    [InlineData(null, null, null)]
    public void IsEngaged_is_ap1_or_ap2_and_unknown_until_both_are_read(double? ap1, double? ap2, bool? expected)
        => Assert.Equal(expected, FbwAutopilotEngage.IsEngaged(Read(ap1, ap2)));

    [Fact]
    public void An_unread_nan_value_is_unknown()
        => Assert.Null(FbwAutopilotEngage.IsEngaged(Read(double.NaN, 0.0)));

    [Theory]
    [InlineData(0.0, 0.0, "A32NX.FCU_AP_1_PUSH")]
    [InlineData(null, null, "A32NX.FCU_AP_1_PUSH")]   // unknown: press; the service announces on the press
    [InlineData(1.0, 0.0, null)]                       // AP 1 engaged: a press would switch it off
    [InlineData(0.0, 1.0, null)]                       // AP 2 engaged: already flying, leave it
    public void PressFor_never_presses_over_an_engaged_autopilot(double? ap1, double? ap2, string? expected)
        => Assert.Equal(expected, FbwAutopilotEngage.PressFor(Read(ap1, ap2)));

    [Fact]
    public void The_press_is_the_cockpit_ap1_button_event()
        => Assert.Equal("A32NX.FCU_AP_1_PUSH", FbwAutopilotEngage.Ap1PushEvent);

    [Theory]
    [InlineData(typeof(FlyByWireA320Definition))]
    [InlineData(typeof(FlyByWireA380Definition))]
    [InlineData(typeof(HeadwindA330Definition))]
    public void Fbw_definitions_engage_through_ap1_and_verify_on_the_ap_state(Type defType)
    {
        var def = (IAircraftDefinition)Activator.CreateInstance(defType)!;

        // Never the base's stock AUTOPILOT_ON (FO-21); the A330 inherits the A32NX's override.
        Assert.NotEqual(typeof(BaseAircraftDefinition),
            defType.GetMethod(nameof(IAircraftDefinition.EngageAutopilot))!.DeclaringType);
        Assert.Equal(FbwAutopilotEngage.RetryIntervalMs, def.AutopilotEngageRetryIntervalMs);

        Assert.Null(def.IsAutopilotEngaged(SeededSimConnectCache.With()));
        Assert.True(def.IsAutopilotEngaged(SeededSimConnectCache.With(
            (FbwAutopilotEngage.Ap1ActiveVar, 1), (FbwAutopilotEngage.Ap2ActiveVar, 0))));
        Assert.False(def.IsAutopilotEngaged(SeededSimConnectCache.With(
            (FbwAutopilotEngage.Ap1ActiveVar, 0), (FbwAutopilotEngage.Ap2ActiveVar, 0))));
    }
}

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
    [InlineData(0.0, 0.0, false, "A32NX.FCU_AP_1_PUSH")]
    [InlineData(null, null, false, "A32NX.FCU_AP_1_PUSH")]   // unknown: press; the service announces on the press
    [InlineData(1.0, 0.0, false, null)]                       // AP 1 engaged: a press would switch it off
    [InlineData(1.0, 0.0, true, null)]                        // ... and a press that would wait is no different
    [InlineData(0.0, 1.0, false, null)]                       // AP 2 engaged: already flying, leave it
    [InlineData(0.0, 1.0, true, null)]
    public void PressFor_never_presses_over_an_engaged_autopilot(
        double? ap1, double? ap2, bool pressWouldWait, string? expected)
        => Assert.Equal(expected, FbwAutopilotEngage.PressFor(Read(ap1, ap2), pressWouldWait));

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

    // SIM-13: SendEvent holds a dotted event in its calc-path probe queue while the MobiFlight
    // module is there and the probe has neither verified nor concluded, then sends the whole queue
    // at once. The three inputs are the ones the engage reads off SimConnectManager.
    [Theory]
    [InlineData(true, false, false, true)]     // module there, probe still running: SendEvent would queue the press
    [InlineData(true, false, true, false)]     // probe gave up: the legacy transmit goes out at once
    [InlineData(true, true, false, false)]     // verified (never seen without concluded): the calc path fires at once
    [InlineData(true, true, true, false)]      // verified: the calc path fires at once
    [InlineData(false, false, false, false)]   // no module (or not connected): nothing waits in the queue
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    public void PressWouldWait_only_while_the_module_is_there_and_the_probe_is_still_running(
        bool canExecuteCalculatorCode, bool calcPathVerified, bool calcPathProbeConcluded, bool expected)
        => Assert.Equal(expected,
            FbwAutopilotEngage.PressWouldWait(canExecuteCalculatorCode, calcPathVerified, calcPathProbeConcluded));

    [Fact]
    public void PressFor_withholds_a_waiting_press_while_both_autopilots_read_off()
    {
        // Both autopilots read definitively off, so the press is otherwise due and the service
        // retries from this state. A withheld press is tried again after the retry interval; it is
        // never queued to arrive in a burst when the probe ends.
        var bothOff = Read(0.0, 0.0);

        Assert.Null(FbwAutopilotEngage.PressFor(bothOff, pressWouldWait: true));
        Assert.Equal(FbwAutopilotEngage.Ap1PushEvent, FbwAutopilotEngage.PressFor(bothOff, pressWouldWait: false));
    }

    // An unknown state (an AP var not in the cache yet, or NaN) still pushes when the press would
    // wait: from there the service presses once and never retries, so the one queued push cannot
    // pile up, and when the probe ends it goes out and engages AP 1. Withheld, it would never be
    // retried and the service would announce "Autopilot engaged" over nothing.
    [Theory]
    [InlineData(null, null)]          // neither AP var read yet
    [InlineData(0.0, null)]           // one unread: still unknown (FO-18)
    [InlineData(null, 0.0)]
    [InlineData(double.NaN, 0.0)]     // a NaN read is unknown as well
    [InlineData(0.0, double.NaN)]
    public void PressFor_still_sends_the_single_press_while_the_state_is_unknown(double? ap1, double? ap2)
    {
        var read = Read(ap1, ap2);

        Assert.Equal(FbwAutopilotEngage.Ap1PushEvent, FbwAutopilotEngage.PressFor(read, pressWouldWait: true));
        Assert.Equal(FbwAutopilotEngage.Ap1PushEvent, FbwAutopilotEngage.PressFor(read, pressWouldWait: false));
    }

    // The definitions theory above compares their interval with the constant itself, so the
    // 3 s value is pinned here.
    [Fact]
    public void The_retry_interval_is_three_seconds()
        => Assert.Equal(3000, FbwAutopilotEngage.RetryIntervalMs);
}

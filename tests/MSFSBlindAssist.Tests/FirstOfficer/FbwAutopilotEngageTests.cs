using System;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-21: the FlyByWire family's universal auto-engage. FlyByWire captures the stock
/// AUTOPILOT_ON into a flag nothing reads, so before 2026-10-09 the app announced
/// "Autopilot engaged" over an autopilot that never engaged. The engage is now the cockpit's
/// AP 1 push — a TOGGLE — sent only once both autopilots read definitively off (never on an
/// unknown state: the autopilot may already be on), verified on AP 1 / AP 2 active, and spaced
/// 3 s apart. Since 2026-10-18 the other toggle engages (the PMDG 737 and 777, the iFly, the MD-11
/// and the Fenix) are spaced and wait the same way: at 0 a PMDG retry pressed CMD A again before
/// the 1 Hz snapshot showed the first press, and switched it off. Only an aircraft whose engage is
/// the idempotent stock AUTOPILOT_ON keeps the every-tick retry and the open-loop press.
/// </summary>
public class FbwAutopilotEngageTests
{
    // The other aircraft whose engage is a toggle of their own, outside the FlyByWire family.
    public static TheoryData<Type> OtherToggleAircraft => new()
    {
        typeof(PMDG777Definition),
        typeof(PMDG737Definition),
        typeof(IFly737MAXDefinition),
        typeof(FenixA320Definition),
        typeof(TFDiMD11Definition),
    };

    // The aircraft that keep the stock AUTOPILOT_ON: no engaged state is read, so every pin says
    // they stay as they were.
    public static TheoryData<Type> OtherAircraft => new()
    {
        typeof(HorizonSim787Definition),
    };

    [Theory]
    [MemberData(nameof(OtherToggleAircraft))]
    public void Other_toggle_aircraft_press_three_seconds_apart_and_wait_for_a_reading(Type defType)
    {
        var def = (IAircraftDefinition)Activator.CreateInstance(defType)!;
        try
        {
            Assert.Equal(3000, def.AutopilotEngageRetryIntervalMs);
            Assert.True(def.AutopilotEngageWaitsForState);
        }
        finally { (def as IDisposable)?.Dispose(); }
    }

    // The Fenix's engage presses its FCU AP1 pushbutton and reads both FCU AP lights: it inherited
    // the stock AUTOPILOT_ON and a null state until 2026-10-18, so "Autopilot engaged" was never read back.
    [Fact]
    public void Fenix_reads_both_fcu_ap_lights_and_unknown_until_both_are_read()
    {
        var def = new FenixA320Definition();
        Assert.NotEqual(typeof(BaseAircraftDefinition),
            typeof(FenixA320Definition).GetMethod(nameof(IAircraftDefinition.EngageAutopilot))!.DeclaringType);

        Assert.Null(def.IsAutopilotEngaged(SeededSimConnectCache.With()));
        Assert.Null(def.IsAutopilotEngaged(SeededSimConnectCache.With(("I_FCU_AP1", 0))));
        Assert.True(def.IsAutopilotEngaged(SeededSimConnectCache.With(("I_FCU_AP1", 1))));
        Assert.True(def.IsAutopilotEngaged(SeededSimConnectCache.With(("I_FCU_AP1", 0), ("I_FCU_AP2", 1))));
        Assert.False(def.IsAutopilotEngaged(SeededSimConnectCache.With(("I_FCU_AP1", 0), ("I_FCU_AP2", 0))));
    }

    // The FlyByWire family; the Headwind A330 inherits the A32NX's overrides.
    public static TheoryData<Type> FbwAircraft => new()
    {
        typeof(FlyByWireA320Definition),
        typeof(FlyByWireA380Definition),
        typeof(HeadwindA330Definition),
    };

    [Theory]
    [MemberData(nameof(OtherAircraft))]
    public void Other_aircraft_keep_the_every_tick_retry(Type defType)
    {
        var def = (IAircraftDefinition)Activator.CreateInstance(defType)!;
        try { Assert.Equal(0, def.AutopilotEngageRetryIntervalMs); }
        finally { (def as IDisposable)?.Dispose(); }
    }

    // A null readback keeps its open-loop press only where the engage is the stock event.
    [Theory]
    [MemberData(nameof(OtherAircraft))]
    public void Other_aircraft_do_not_wait_for_a_definite_autopilot_state(Type defType)
    {
        var def = (IAircraftDefinition)Activator.CreateInstance(defType)!;
        try { Assert.False(def.AutopilotEngageWaitsForState); }
        finally { (def as IDisposable)?.Dispose(); }
    }

    // On the FlyByWire family a null readback only means "not read yet" (the AP vars arrive with
    // the 1 Hz batch), and the AP 1 push is a toggle: the service waits for a definite reading
    // instead of pressing blind over an autopilot that may already be on.
    [Theory]
    [MemberData(nameof(FbwAircraft))]
    public void Fbw_definitions_wait_for_a_definite_autopilot_state(Type defType)
    {
        var def = (IAircraftDefinition)Activator.CreateInstance(defType)!;
        Assert.True(def.AutopilotEngageWaitsForState);
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
    [InlineData(0.0, 0.0, false, "A32NX.FCU_AP_1_PUSH")]      // both off and nothing in the way: the one state that presses
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
    [MemberData(nameof(FbwAircraft))]
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
    // bridge object exists and the probe has neither verified nor concluded, then sends the whole
    // queue at once. The bridge object exists on every connection, whether or not the WASM module
    // is installed (canExecuteCalculatorCode is true either way), so with no module the probe cannot
    // verify and the press waits its full ~60 s. The three inputs are the ones the engage reads off
    // SimConnectManager.
    [Theory]
    [InlineData(true, false, false, true)]     // connected, probe still running (no module: its full ~60 s): SendEvent would queue the press
    [InlineData(true, false, true, false)]     // probe gave up (no module, or it never answered): the legacy transmit goes out at once
    [InlineData(true, true, false, false)]     // verified (never seen without concluded): the calc path fires at once
    [InlineData(true, true, true, false)]      // verified: the calc path fires at once
    [InlineData(false, false, false, false)]   // not connected, no bridge object: SendEvent sends or drops the press, it never queues it
    [InlineData(false, false, true, false)]    // ... whatever the probe flags say
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    public void PressWouldWait_only_while_the_bridge_object_exists_and_the_probe_is_still_running(
        bool canExecuteCalculatorCode, bool calcPathVerified, bool calcPathProbeConcluded, bool expected)
        => Assert.Equal(expected,
            FbwAutopilotEngage.PressWouldWait(canExecuteCalculatorCode, calcPathVerified, calcPathProbeConcluded));

    [Fact]
    public void PressFor_withholds_a_waiting_press_while_both_autopilots_read_off()
    {
        // Both autopilots read definitively off, so the press is otherwise due and the service
        // retries from this state. A withheld press is tried again after the retry interval; it is
        // never queued to arrive up to a minute late, when the probe ends, over an autopilot the
        // Captain may have engaged since.
        var bothOff = Read(0.0, 0.0);

        Assert.Null(FbwAutopilotEngage.PressFor(bothOff, pressWouldWait: true));
        Assert.Equal(FbwAutopilotEngage.Ap1PushEvent, FbwAutopilotEngage.PressFor(bothOff, pressWouldWait: false));
    }

    // An unknown state (an AP var not in the cache yet, or NaN) never pushes, whether or not the
    // press would wait: the push is a toggle, so a blind press could disconnect an autopilot that
    // is already on. The service waits for a definite reading instead (WaitForApState, FO-21).
    [Theory]
    [InlineData(null, null)]          // neither AP var read yet
    [InlineData(0.0, null)]           // one unread: still unknown (FO-18)
    [InlineData(null, 0.0)]
    [InlineData(double.NaN, 0.0)]     // a NaN read is unknown as well
    [InlineData(0.0, double.NaN)]
    public void PressFor_never_pushes_while_the_state_is_unknown(double? ap1, double? ap2)
    {
        var read = Read(ap1, ap2);

        Assert.Null(FbwAutopilotEngage.PressFor(read, pressWouldWait: true));
        Assert.Null(FbwAutopilotEngage.PressFor(read, pressWouldWait: false));
    }

    // The definitions theory above compares their interval with the constant itself, so the
    // 3 s value is pinned here.
    [Fact]
    public void The_retry_interval_is_three_seconds()
        => Assert.Equal(3000, FbwAutopilotEngage.RetryIntervalMs);
}

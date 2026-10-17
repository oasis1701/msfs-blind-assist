using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300AutopilotStatusTests
{
    [Fact]
    public void Flying_in_altitude_hold_reads_the_windows_the_fma_and_the_autopilot()
    {
        var values = new Dictionary<string, double>
        {
            [A300Readouts.SpeedKey] = 250, [A300Readouts.HeadingKey] = 270,
            [A300Readouts.AltitudeKey] = 12000, [A300Readouts.VerticalSpeedKey] = -1500,
            ["A300_FMA_IRS1_ALIGNED"] = 1, ["A300_PITCH_TRIM_2"] = 1,
            [A300FmaSources.PitchModeKey] = 6, [A300FmaSources.RollModeKey] = 3, [A300FmaSources.ThrustModeKey] = 1,
            ["A300_FCU_LT_ATHR"] = 1, ["A300_ATS_1"] = 1, ["A300_AP_SWITCH_1"] = 1,
        };
        Assert.Equal(new[]
        {
            "Speed 250 knots", "Heading 270", "Altitude 12,000 feet", "Vertical speed -1,500 feet per minute",
            "Thrust mode: Speed", "Pitch mode: Altitude", "Roll mode: Heading hold", "Armed modes: none",
            "Autopilot: CMD 1",
        }, A300AutopilotStatus.Lines(k => values.TryGetValue(k, out var v) ? v : null));
    }

    [Fact]
    public void Cold_and_dark_reads_unread_windows_and_no_fma()
    {
        Assert.Equal(new[]
        {
            "Speed window not read yet", "Heading window not read yet", "Altitude window not read yet",
            "Vertical speed window not read yet", "Flight mode annunciator not shown", "Autopilot: off",
        }, A300AutopilotStatus.Lines(_ => null));
    }

    [Fact]
    public void The_speed_window_reads_mach_in_mach_mode()
    {
        var values = new Dictionary<string, double> { [A300Readouts.SpeedKey] = 0.78, [A300FcuState.SpeedMachLightKey] = 1 };
        Assert.Equal("Mach 0.78", A300AutopilotStatus.Lines(k => values.TryGetValue(k, out var v) ? v : null)[0]);
    }

    [Fact]
    public void Only_the_on_request_windows_are_asked_for() =>
        Assert.Equal(new[] { A300Readouts.SpeedKey, A300Readouts.HeadingKey, A300Readouts.VerticalSpeedKey },
            A300AutopilotStatus.RequestedKeys);
}

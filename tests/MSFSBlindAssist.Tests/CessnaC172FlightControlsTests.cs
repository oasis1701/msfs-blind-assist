// The C172's elevator trim and throttle, measured 2026-10-18 (C172F-1 to C172F-3): the trim written with
// ELEVATOR_TRIM_SET, 22 degrees per 16383 both ways, travel 22 up to 19 down; the disconnect blocking every
// trim; the throttle combo written with THROTTLE1_SET, never its fraction-scaled input event.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class CessnaC172FlightControlsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(11, 8192)]
    [InlineData(22, 16383)]
    [InlineData(30, 16383)]     // past the stop: the travel's end
    [InlineData(-11, -8192)]    // nose down, as its 32-bit two's complement
    [InlineData(-19, -14149)]   // 19/22 of 16383, the nose-down stop
    [InlineData(-25, -14149)]
    public void The_trim_is_sent_at_22_degrees_per_16383_both_ways(double degrees, int expected)
    {
        Assert.Equal(unchecked((uint)expected), CessnaC172G1000Definition.TrimSetValue(degrees));
    }

    [Theory]
    [InlineData(-19, true)]
    [InlineData(22, true)]
    [InlineData(3.5, true)]
    [InlineData(-19.1, false)]
    [InlineData(22.1, false)]
    [InlineData(double.NaN, false)]
    public void A_typed_trim_is_taken_only_inside_the_travel(double degrees, bool taken)
    {
        Assert.Equal(taken, CessnaC172G1000Definition.TrimRefusal(degrees) == null);
    }

    [Theory]
    [InlineData(1.7, 1, 2.2, false)]
    [InlineData(1.7, -1, 1.2, false)]
    [InlineData(21.8, 1, 22, false)]     // the step stops at the limit
    [InlineData(22, 1, 22, true)]        // already there: nothing is sent
    [InlineData(-18.8, -1, -19, false)]
    [InlineData(-19, -1, -19, true)]
    public void A_step_is_half_a_degree_and_stops_at_the_travels_end(double now, int direction, double target, bool atLimit)
    {
        var (t, limit) = CessnaC172G1000Definition.SteppedTrim(now, direction);
        Assert.Equal(target, t, 6);
        Assert.Equal(atLimit, limit);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_trim_and_throttle_read_the_same_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            // Every number with its unit (the owner's rule for the C172's readouts).
            Assert.Equal("3.5 degrees nose up", CessnaC172G1000Definition.DescribeTrimPosition(3.47));
            Assert.Equal("2.5 degrees nose down", CessnaC172G1000Definition.DescribeTrimPosition(-2.5));
            // A runway start's trim, 2026-10-18: the take-off mark, named as the wheel shows it.
            Assert.Equal("3.0 degrees nose up, takeoff position", CessnaC172G1000Definition.DescribeTrimPosition(2.97));
            Assert.Equal("3.0 degrees nose down", CessnaC172G1000Definition.DescribeTrimPosition(-3));
            Assert.Equal("neutral", CessnaC172G1000Definition.DescribeTrimPosition(0.02));
            Assert.Equal("Trim at the nose up limit, 22.0 degrees", CessnaC172G1000Definition.DescribeTrimLimit(22));
            Assert.Equal("Trim at the nose down limit, 19.0 degrees", CessnaC172G1000Definition.DescribeTrimLimit(-19));

            Assert.Equal("idle", CessnaC172BaseDefinition.DescribeThrottle(0));
            Assert.Equal("full", CessnaC172BaseDefinition.DescribeThrottle(100));
            Assert.Equal("65 percent", CessnaC172BaseDefinition.DescribeThrottle(64.6));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_throttle_combo_runs_idle_to_full_and_is_written_with_throttle1_set()
    {
        var positions = CessnaC172BaseDefinition.ThrottlePositions();
        Assert.Equal(101, positions.Count);
        Assert.Equal("Idle", positions[0]);
        Assert.Equal("1 percent", positions[1]);
        Assert.Equal("99 percent", positions[99]);
        Assert.Equal("Full", positions[100]);
        Assert.Equal(0, positions.Keys.First());   // the parked position on top

        // Measured: 1638 gave 10.0 percent.
        Assert.Equal(1638u, CessnaC172BaseDefinition.ThrottleEventValue(10));
        Assert.Equal(16383u, CessnaC172BaseDefinition.ThrottleEventValue(100));
        Assert.Equal(0u, CessnaC172BaseDefinition.ThrottleEventValue(-5));

        var throttle = new CessnaC172G1000Definition().GetVariables()["C172_THROTTLE"];
        Assert.Equal(65, throttle.ValueToDescriptionKey!(64.6));
    }

    [Fact]
    public void The_elevator_trim_panel_has_the_typed_trim_the_steps_and_the_disconnect()
    {
        var def = new CessnaC172G1000Definition();
        Assert.Contains("Elevator Trim", def.GetPanelStructure()["Flight Controls"]);
        Assert.Equal(new[]
            {
                CessnaC172G1000Definition.TrimSetKey, CessnaC172G1000Definition.TrimNoseUpKey,
                CessnaC172G1000Definition.TrimNoseDownKey, CessnaC172G1000Definition.TrimTakeoffKey,
                CessnaC172G1000Definition.TrimDisconnectKey,
            },
            def.GetPanelControls()["Elevator Trim"]);
        Assert.Equal(new[] { "MON_ElevatorTrim" }, def.GetPanelDisplayVariables()["Elevator Trim"]);

        // The disconnect is a switch: written through its input event, read from ELEVATOR TRIM DISABLED,
        // announced with a Ctrl+M row (C172-20).
        Assert.Equal("HANDLING_TRIM_DISCONNECT",
            CessnaC172G1000Definition.SwitchInputEvents[CessnaC172G1000Definition.TrimDisconnectKey]);
        var disconnect = def.GetVariables()[CessnaC172G1000Definition.TrimDisconnectKey];
        Assert.Equal("ELEVATOR TRIM DISABLED", disconnect.Name);
        Assert.Equal("Connected", disconnect.ValueDescriptions[0]);
        Assert.Equal("Disconnected", disconnect.ValueDescriptions[1]);
        Assert.Contains(CessnaC172G1000Definition.TrimDisconnectKey,
            MonitorRowBuilder.Build(def.GetVariables()).Select(r => r.Key));

        Assert.True(def.TryGetDisplayOverride("MON_ElevatorTrim", 1.67, out string row));
        Assert.Equal("1.7 degrees nose up", row);
    }

    [Fact]
    public void The_throttle_is_the_last_engine_control_right_before_the_status_display()
    {
        var engine = new CessnaC172G1000Definition().GetPanelControls()["Engine Controls"];
        Assert.Equal("C172_THROTTLE", engine[^1]);
    }
}

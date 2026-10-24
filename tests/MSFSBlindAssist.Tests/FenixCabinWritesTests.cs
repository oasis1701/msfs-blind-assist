// Characterization tests for FenixCabinWrites, the data-name writes behind the Fenix Cabin window:
// the cabin ready and lights writes, the FAP temperature steps and reset, the water preselect step,
// the FAP Set-Up page's loudspeaker volume step, and the EFB's door toggles. The names, kinds and
// limits are the FAP and EFB bundles' own. The HTTP side runs against a fake
// HttpMessageHandler, never the network.

using System.Net;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinWritesTests
{
    private static void AssertWrite(FenixCabinWrite? write, FenixCabinWriteKind kind, string name, object value)
    {
        Assert.NotNull(write);
        Assert.Equal(kind, write.Kind);
        Assert.Equal(name, write.Name);
        Assert.Equal(value.GetType(), write.Value.GetType());
        Assert.Equal(value, write.Value);
    }

    // A fraction written by a division (0.95 and the like): compare with a tolerance, as a double.
    private static void AssertFloatWrite(FenixCabinWrite? write, string name, double value)
    {
        Assert.NotNull(write);
        Assert.Equal(FenixCabinWriteKind.Float, write.Kind);
        Assert.Equal(name, write.Name);
        var written = Assert.IsType<double>(write.Value);
        Assert.Equal(value, written, 1e-9);
    }

    // ---- Cabin ready and cabin lights -----------------------------------------------------------

    [Fact]
    public void Cabin_ready_and_lights_are_the_FAP_writes()
    {
        AssertWrite(FenixCabinWrites.CabinReady(true), FenixCabinWriteKind.Bool, "aircraft.communication.cids.cabinReady", true);
        AssertWrite(FenixCabinWrites.CabinReady(false), FenixCabinWriteKind.Bool, "aircraft.communication.cids.cabinReady", false);
        AssertWrite(FenixCabinWrites.CabinLights(FenixCabinLightLevel.Dim1), FenixCabinWriteKind.String, "aircraft.communication.cids.cabinLights", "Dim1");
        AssertWrite(FenixCabinWrites.CabinLights(FenixCabinLightLevel.Bright), FenixCabinWriteKind.String, "aircraft.communication.cids.cabinLights", "Bright");
        AssertWrite(FenixCabinWrites.CabinLights(FenixCabinLightLevel.Dim2), FenixCabinWriteKind.String, "aircraft.communication.cids.cabinLights", "Dim2");
        AssertWrite(FenixCabinWrites.CabinLights(FenixCabinLightLevel.Off), FenixCabinWriteKind.String, "aircraft.communication.cids.cabinLights", "Off");
        Assert.Equal(FenixCabinLightLevel.Off, FenixCabinWrites.ParseLight("Off"));
        Assert.Null(FenixCabinWrites.ParseLight("Blue"));
        Assert.Equal("Dim 2", FenixCabinWrites.LightName(FenixCabinLightLevel.Dim2));
    }

    [Theory]
    [InlineData(FenixCabinLightLevel.Bright, "Bright", "Bright")]
    [InlineData(FenixCabinLightLevel.Dim1, "Dim1", "Dim 1")]
    [InlineData(FenixCabinLightLevel.Dim2, "Dim2", "Dim 2")]
    [InlineData(FenixCabinLightLevel.Off, "Off", "Off")]
    public void Every_light_level_has_its_wire_value_and_its_spoken_name(FenixCabinLightLevel level, string wire, string name)
    {
        Assert.Equal(wire, FenixCabinWrites.LightWire(level));
        Assert.Equal(name, FenixCabinWrites.LightName(level));
        Assert.Equal(level, FenixCabinWrites.ParseLight(wire));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("off")]
    [InlineData("BRIGHT")]
    [InlineData("Dim 1")]
    [InlineData("dim2")]
    [InlineData(" Off")]
    public void ParseLight_is_ordinal_and_exact(string? wire)
    {
        Assert.Null(FenixCabinWrites.ParseLight(wire));
    }

    // ---- Temperature ----------------------------------------------------------------------------

    [Fact]
    public void Temperature_steps_follow_the_FAP_limits()
    {
        AssertWrite(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, 0, warmer: true),
            FenixCabinWriteKind.Float, "aircraft.cabin.fwd.temp.offset", 0.5);
        AssertWrite(FenixCabinWrites.TemperatureStep(FenixCabinZone.Aft, 24, 0, warmer: false),
            FenixCabinWriteKind.Float, "aircraft.cabin.aft.temp.offset", -0.5);

        // A step is the offset plus or minus a half degree, from wherever the offset is.
        AssertWrite(FenixCabinWrites.TemperatureStep(FenixCabinZone.Aft, 24, 2, warmer: true),
            FenixCabinWriteKind.Float, "aircraft.cabin.aft.temp.offset", 2.5);
        AssertWrite(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, -2, warmer: false),
            FenixCabinWriteKind.Float, "aircraft.cabin.fwd.temp.offset", -2.5);

        // The FAP greys a button at the offset limit (+/- 2.5) or when the target reaches 30 or 18.
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, 2.5, warmer: true));
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 29.5, 0.5, warmer: true));
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, -2.5, warmer: false));
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 18.5, -0.5, warmer: false));

        // The limit holds one way only: at the warm limit the zone can still cool, and the reverse.
        Assert.NotNull(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, 2.5, warmer: false));
        Assert.NotNull(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 29.5, 0.5, warmer: false));
        Assert.NotNull(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, -2.5, warmer: true));
        Assert.NotNull(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 18.5, -0.5, warmer: true));
    }

    [Fact]
    public void Temperature_target_beyond_the_limit_is_also_refused()
    {
        // The target is the cockpit selection plus the offset; past 30 or under 18 the FAP's button is greyed.
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Aft, 30, 0, warmer: true));
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Aft, 18, 0, warmer: false));
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Aft, 31, 0, warmer: true));
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Aft, 17, 0, warmer: false));
        AssertWrite(FenixCabinWrites.TemperatureStep(FenixCabinZone.Aft, 29, 0.5, warmer: true),
            FenixCabinWriteKind.Float, "aircraft.cabin.aft.temp.offset", 1.0);
    }

    [Fact]
    public void Temperature_step_with_a_value_that_is_not_a_number_writes_nothing()
    {
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, double.NaN, 0, warmer: true));
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, double.NaN, warmer: false));
        Assert.Null(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, double.PositiveInfinity, warmer: false));
    }

    [Fact]
    public void Reset_writes_both_offsets_to_zero()
    {
        var writes = FenixCabinWrites.ResetTemperatures();

        Assert.Equal(2, writes.Count);
        AssertWrite(writes[0], FenixCabinWriteKind.Float, "aircraft.cabin.fwd.temp.offset", 0.0);
        AssertWrite(writes[1], FenixCabinWriteKind.Float, "aircraft.cabin.aft.temp.offset", 0.0);
    }

    // ---- Water preselect ------------------------------------------------------------------------

    [Fact]
    public void Preselect_steps_follow_the_FAP()
    {
        AssertWrite(FenixCabinWrites.PreselectStep(50, up: true),
            FenixCabinWriteKind.Float, "aircraft.communication.cids.waterTank.preselect", 75.0);
        AssertWrite(FenixCabinWrites.PreselectStep(null, up: false),
            FenixCabinWriteKind.Float, "aircraft.communication.cids.waterTank.preselect", 75.0);
        Assert.Null(FenixCabinWrites.PreselectStep(100, up: true));
        Assert.Null(FenixCabinWrites.PreselectStep(25, up: false));
    }

    [Fact]
    public void Preselect_steps_are_quarters_clamped_to_25_and_100()
    {
        AssertWrite(FenixCabinWrites.PreselectStep(75, up: true),
            FenixCabinWriteKind.Float, "aircraft.communication.cids.waterTank.preselect", 100.0);
        AssertWrite(FenixCabinWrites.PreselectStep(50, up: false),
            FenixCabinWriteKind.Float, "aircraft.communication.cids.waterTank.preselect", 25.0);
        // Clamped like the FAP: a value off the quarter grid still lands inside 25 to 100.
        AssertWrite(FenixCabinWrites.PreselectStep(90, up: true),
            FenixCabinWriteKind.Float, "aircraft.communication.cids.waterTank.preselect", 100.0);
        AssertWrite(FenixCabinWrites.PreselectStep(30, up: false),
            FenixCabinWriteKind.Float, "aircraft.communication.cids.waterTank.preselect", 25.0);
        // Unknown counts as 100, and the FAP writes whatever the step gives: up from unknown writes 100,
        // since there is no current value for it to equal.
        AssertWrite(FenixCabinWrites.PreselectStep(null, up: true),
            FenixCabinWriteKind.Float, "aircraft.communication.cids.waterTank.preselect", 100.0);
    }

    // ---- Doors ----------------------------------------------------------------------------------

    [Fact]
    public void Door_toggle_follows_the_EFB()
    {
        AssertWrite(FenixCabinWrites.DoorToggle("d1l", 2), FenixCabinWriteKind.Int, "doors.control.d1l", 1);
        AssertWrite(FenixCabinWrites.DoorToggle("d1l", 0), FenixCabinWriteKind.Int, "doors.control.d1l", 2);
        AssertWrite(FenixCabinWrites.DoorToggle("d1l", null), FenixCabinWriteKind.Int, "doors.control.d1l", 2);

        // The EFB never writes Armed (0): not from any control value.
        foreach (double? control in new double?[] { null, 0, 1, 2, 3, -1 })
        {
            var write = FenixCabinWrites.DoorToggle("d4r", control);
            Assert.Equal("doors.control.d4r", write.Name);
            Assert.NotEqual(0, (int)write.Value);
        }
    }

    [Fact]
    public void Door_toggle_from_disarmed_opens_it()
    {
        AssertWrite(FenixCabinWrites.DoorToggle("d2r", 1), FenixCabinWriteKind.Int, "doors.control.d2r", 2);
    }

    [Fact]
    public void Cargo_doors_are_bool_writes()
    {
        AssertWrite(FenixCabinWrites.CargoDoor(FenixCargoDoor.Bulk, true), FenixCabinWriteKind.Bool, "doors.cargo.bulk", true);
        AssertWrite(FenixCabinWrites.CargoDoor(FenixCargoDoor.Forward, false), FenixCabinWriteKind.Bool, "doors.cargo.forward", false);
        AssertWrite(FenixCabinWrites.CargoDoor(FenixCargoDoor.Aft, true), FenixCabinWriteKind.Bool, "doors.cargo.aft", true);
    }

    // ---- Loudspeaker volume (the FAP Set-Up page) -----------------------------------------------

    [Fact]
    public void Speaker_volume_steps_follow_the_FAP_setup_page()
    {
        const string Name = "aircraft.communication.cids.fap.speakerVolume";

        Assert.Null(FenixCabinWrites.SpeakerVolumeStep(1.0, up: true));
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(1.0, up: false), Name, 0.95);
        Assert.Null(FenixCabinWrites.SpeakerVolumeStep(0.0, up: false));
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(0.0, up: true), Name, 0.05);

        // Unknown counts as the Set-Up store's default of 100 percent.
        Assert.Null(FenixCabinWrites.SpeakerVolumeStep(null, up: true));
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(null, up: false), Name, 0.95);

        // The page shows 5 * round(percent / 5), rounding half up like JavaScript's Math.round.
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(0.83, up: true), Name, 0.90);
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(0.825, up: false), Name, 0.80);
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(0.5, up: true), Name, 0.55);
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(0.5, up: false), Name, 0.45);
    }

    [Fact]
    public void Speaker_volume_step_stays_inside_zero_to_one_hundred_percent()
    {
        const string Name = "aircraft.communication.cids.fap.speakerVolume";

        // A value past either end shows as 100 or 0 percent on the page, so the step is measured from that.
        Assert.Null(FenixCabinWrites.SpeakerVolumeStep(1.2, up: true));
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(1.2, up: false), Name, 0.95);
        Assert.Null(FenixCabinWrites.SpeakerVolumeStep(-0.3, up: false));
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(-0.3, up: true), Name, 0.05);
        // A value that shows as 100 percent (rounds to 100) cannot go up.
        Assert.Null(FenixCabinWrites.SpeakerVolumeStep(0.98, up: true));
        Assert.Null(FenixCabinWrites.SpeakerVolumeStep(0.02, up: false));
    }

    [Fact]
    public void Speaker_volume_step_from_a_value_that_is_not_a_number_counts_as_unknown()
    {
        const string Name = "aircraft.communication.cids.fap.speakerVolume";

        Assert.Null(FenixCabinWrites.SpeakerVolumeStep(double.NaN, up: true));
        AssertFloatWrite(FenixCabinWrites.SpeakerVolumeStep(double.NaN, up: false), Name, 0.95);
    }

    // ---- Limits ---------------------------------------------------------------------------------

    [Fact]
    public void The_limits_are_the_bundles_own()
    {
        Assert.Equal(0.5, FenixCabinWrites.OffsetStep);
        Assert.Equal(-2.5, FenixCabinWrites.MinOffset);
        Assert.Equal(2.5, FenixCabinWrites.MaxOffset);
        Assert.Equal(18, FenixCabinWrites.MinTemperature);
        Assert.Equal(30, FenixCabinWrites.MaxTemperature);
        Assert.Equal(25, FenixCabinWrites.PreselectStepPercent);
        Assert.Equal(25, FenixCabinWrites.MinPreselect);
        Assert.Equal(100, FenixCabinWrites.MaxPreselect);
        Assert.Equal(5, FenixCabinWrites.SpeakerVolumeStepPercent);
        Assert.Equal(0, FenixCabinWrites.MinSpeakerVolumePercent);
        Assert.Equal(100, FenixCabinWrites.MaxSpeakerVolumePercent);
        Assert.Equal(1, FenixCabinWrites.DoorDisarmed);
        Assert.Equal(2, FenixCabinWrites.DoorOpen);
    }

    // ---- SendAsync ------------------------------------------------------------------------------

    private sealed class StubGateway : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _answer;
        public readonly List<string?> Bodies = new();

        public StubGateway(Func<HttpResponseMessage> answer) { _answer = answer; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Bodies) Bodies.Add(body);
            return _answer();
        }
    }

    private static StubGateway Answering(string json)
        => new(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });

    [Fact]
    public void SendAsync_writes_an_invariant_float_under_de_DE()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{\"writeFloat\":true}}}");
        using var service = new FenixDataService(gateway);

        bool ok = TestCulture.Run("de-DE", () =>
            service.SendAsync(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, 0, true)!).GetAwaiter().GetResult());

        Assert.True(ok);
        string body = Assert.Single(gateway.Bodies)!;
        Assert.Contains("writeFloat", body);
        Assert.Contains("aircraft.cabin.fwd.temp.offset", body);
        Assert.Contains("\"value\":0.5", body);
        Assert.DoesNotContain("0,5", body);
    }

    [Fact]
    public void SendAsync_writes_a_speaker_volume_fraction_with_a_dot_under_de_DE()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{\"writeFloat\":true}}}");
        using var service = new FenixDataService(gateway);

        bool ok = TestCulture.Run("de-DE", () =>
            service.SendAsync(FenixCabinWrites.SpeakerVolumeStep(0.83, up: true)!).GetAwaiter().GetResult());

        Assert.True(ok);
        string body = Assert.Single(gateway.Bodies)!;
        Assert.Contains("aircraft.communication.cids.fap.speakerVolume", body);
        Assert.Contains("\"value\":0.9", body);
        Assert.DoesNotContain("0,9", body);
    }

    [Fact]
    public async Task SendAsync_sends_each_kind_as_its_typed_write()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{}}}");
        using var service = new FenixDataService(gateway);

        Assert.True(await service.SendAsync(FenixCabinWrites.DoorToggle("d1l", 0)));
        Assert.True(await service.SendAsync(FenixCabinWrites.CabinReady(true)));
        Assert.True(await service.SendAsync(FenixCabinWrites.PreselectStep(50, up: true)!));
        Assert.True(await service.SendAsync(FenixCabinWrites.CabinLights(FenixCabinLightLevel.Dim2)));

        Assert.Equal(4, gateway.Bodies.Count);
        Assert.Contains("writeInt(name: $name, value: $value)", gateway.Bodies[0]);
        Assert.Contains("\"variables\":{\"name\":\"doors.control.d1l\",\"value\":2}", gateway.Bodies[0]);
        Assert.Contains("writeBool(name: $name, value: $value)", gateway.Bodies[1]);
        Assert.Contains("\"variables\":{\"name\":\"aircraft.communication.cids.cabinReady\",\"value\":true}", gateway.Bodies[1]);
        Assert.Contains("writeFloat(name: $name, value: $value)", gateway.Bodies[2]);
        Assert.Contains("\"variables\":{\"name\":\"aircraft.communication.cids.waterTank.preselect\",\"value\":75.0}", gateway.Bodies[2]);
        Assert.Contains("writeString(name: $name, value: $value)", gateway.Bodies[3]);
        Assert.Contains("\"variables\":{\"name\":\"aircraft.communication.cids.cabinLights\",\"value\":\"Dim2\"}", gateway.Bodies[3]);
    }

    [Fact]
    public async Task SendAsync_is_false_when_the_service_refuses_the_write()
    {
        using var service = new FenixDataService(Answering("{\"data\":{\"dataRef\":{\"writeBool\":false}}}"));

        Assert.False(await service.SendAsync(FenixCabinWrites.CargoDoor(FenixCargoDoor.Aft, true)));
    }

    [Fact]
    public async Task SendAsync_is_false_when_the_service_is_down()
    {
        using var service = new FenixDataService(new StubGateway(() => throw new HttpRequestException("refused")));

        Assert.False(await service.SendAsync(FenixCabinWrites.TemperatureStep(FenixCabinZone.Forward, 24, 0, true)!));
        Assert.False(await service.SendAsync(FenixCabinWrites.CabinReady(true)));
    }

    [Fact]
    public async Task SendAsync_sends_nothing_for_a_write_whose_value_does_not_match_its_kind()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{\"writeInt\":true}}}");
        using var service = new FenixDataService(gateway);

        Assert.False(await service.SendAsync(new FenixCabinWrite(FenixCabinWriteKind.Int, "doors.control.d1l", "two")));
        Assert.False(await service.SendAsync(new FenixCabinWrite(FenixCabinWriteKind.Float, "aircraft.cabin.fwd.temp.offset", 1)));
        Assert.Empty(gateway.Bodies);
    }
}

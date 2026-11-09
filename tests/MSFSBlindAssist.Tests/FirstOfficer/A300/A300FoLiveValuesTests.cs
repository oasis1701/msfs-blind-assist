using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.FirstOfficer.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>The values the First Officer speaks where the card leaves a blank, never in the tree text.</summary>
public class A300FoLiveValuesTests
{
    private readonly Dictionary<string, double> _v = new();
    private double? Read(string key) => _v.TryGetValue(key, out var v) ? v : null;

    [Fact]
    public void Altimeters_say_both_when_they_agree_and_each_side_when_not()
    {
        _v[A300FoKeys.BaroCaptain] = 1013; _v[A300FoKeys.BaroFirstOfficer] = 1013;
        _v[A300Baro.Captain.ModeKey] = 0; _v[A300Baro.FirstOfficer.ModeKey] = 0;
        Assert.Equal("1013 hectopascals, 29.91 inches, both", A300FoLiveValues.Altimeters(Read));
        _v[A300FoKeys.BaroFirstOfficer] = 1012;
        Assert.Equal("captain 1013 hectopascals, 29.91 inches, first officer 1012 hectopascals, 29.88 inches",
            A300FoLiveValues.Altimeters(Read));
        _v[A300Baro.Captain.ModeKey] = 1; _v[A300Baro.FirstOfficer.ModeKey] = 1;
        Assert.Equal("standard, both", A300FoLiveValues.Altimeters(Read));
    }

    [Theory]
    [InlineData(0, "no status messages")] [InlineData(1, "1 status message")] [InlineData(3, "3 status messages")]
    public void Ecam_status_counts_its_messages(double n, string expected)
    {
        _v[A300FoVariables.StatusMessagesKey] = n;
        Assert.Equal(expected, A300FoLiveValues.EcamStatus(Read));
    }

    [Fact]
    public void Speeds_take_v2_only_when_plausible_and_leave_out_what_is_not_set()
    {
        _v[A300TakeoffCallouts.V1Key] = 140; _v[A300TakeoffCallouts.VrKey] = 145; _v[A300TakeoffCallouts.V2Key] = 250;
        Assert.Equal("V1 140, VR 145", A300FoLiveValues.Speeds(Read));
        _v[A300TakeoffCallouts.V2Key] = 150; _v[A300FoKeys.FlexTemperature] = 46;
        Assert.Equal("V1 140, VR 145, V2 150, flex 46 degrees", A300FoLiveValues.Speeds(Read));
    }

    [Fact]
    public void Flaps_transponder_ignition_and_autobrake_read_their_positions()
    {
        _v[A300FoKeys.Flaps] = 2; _v[A300FoKeys.Transponder] = 2; _v[A300FoKeys.Squawk] = 0x2000;
        _v[A300FoKeys.Ignition] = 4; _v[A300FoKeys.AutobrakeLevel] = 1;
        Assert.Equal("15/15", A300FoLiveValues.Flaps(Read));
        Assert.Equal("2000, TA/RA", A300FoLiveValues.Transponder(Read));
        Assert.Equal("continuous relight", A300FoLiveValues.Ignition(Read));
        Assert.Equal("low", A300FoLiveValues.Autobrake(Read));
    }

    [Fact]
    public void An_unread_value_gives_no_live_value()
    {
        Assert.Null(A300FoLiveValues.Altimeters(Read));
        Assert.Null(A300FoLiveValues.Fuel(Read));
        Assert.Null(A300FoLiveValues.Speeds(Read));
    }

    [Fact]
    public void Live_values_ignore_a_comma_decimal_culture()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            _v[A300FoKeys.BaroCaptain] = 1013; _v[A300FoKeys.BaroFirstOfficer] = 1013;
            _v[A300Baro.Captain.ModeKey] = 0; _v[A300Baro.FirstOfficer.ModeKey] = 0;
            _v[A300FoKeys.FuelTotal] = 24300;
            Assert.Equal("1013 hectopascals, 29.91 inches, both", A300FoLiveValues.Altimeters(Read));
            Assert.Equal("24,300 pounds", A300FoLiveValues.Fuel(Read));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void Every_key_a_live_value_reads_is_registered_and_on_request_ones_are_polled()
    {
        var vars = new IniA300Definition().GetVariables();
        var polled = new A300FoStateEvaluator().OnRequestPollFields.ToHashSet();
        foreach (var key in A300FoLiveValues.Keys)
        {
            Assert.True(vars.TryGetValue(key, out var def), key);
            if (def!.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.Contains(key, polled);
        }
    }
}

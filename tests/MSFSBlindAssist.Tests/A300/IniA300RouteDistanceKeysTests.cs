using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// D and Shift+D in output mode: each reads the A300's own VNAV distances fresh, together, and says the route distance to the
/// destination, or to the top of descent, with the time at the ground speed the cache holds. The words are
/// A300RouteDistancesTests'; this pins the wiring: which variables each key asks for fresh, where the ground speed comes
/// from, that the reads are registered on request only, and what a read that does not answer says.
/// </summary>
public class IniA300RouteDistanceKeysTests
{
    private const string GroundSpeedKey = "GROUND_VELOCITY";

    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _fresh = new();   // what a fresh read answers
    private readonly Dictionary<string, double> _cache = new();   // what the 1 Hz cache holds
    private readonly List<string> _asked = new();

    public IniA300RouteDistanceKeysTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            ReadFresh = (_, key, _) =>
            {
                _asked.Add(key);
                return Task.FromResult(_fresh.TryGetValue(key, out var v) ? v : (double?)null);
            },
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
        };
        _def.Attach(_sim);
    }

    /// <summary>The route as the sim reports it in cruise (measured 2026-10-14): 541 miles to go, 443.3 to the top of
    /// descent, at a ground speed of 423 knots (the cache's: the batch carries it).</summary>
    private void Cruise()
    {
        _fresh[A300RouteDistances.DestinationKey] = 541;
        _fresh[A300RouteDistances.TopOfDescentKey] = 443.3;
        _cache[GroundSpeedKey] = 423;
    }

    private string[] Press(HotkeyAction action)
    {
        _speech.All.Clear();
        _asked.Clear();
        Assert.True(_def.HandleHotkeyAction(action, _sim, _speech, null!, null!));
        return _speech.All.ToArray();
    }

    [Fact]
    public void D_says_the_route_distance_to_the_destination_and_the_time()
    {
        Cruise();
        Assert.Equal(new[] { "541 miles to destination: 01:16:44" }, Press(HotkeyAction.ReadDistanceToDest));
    }

    [Fact]
    public void Shift_D_says_the_distance_to_the_top_of_descent_and_the_time()
    {
        Cruise();
        Assert.Equal(new[] { "443 miles to top of descent: 01:02:53" }, Press(HotkeyAction.ReadDistanceToTOD));
    }

    [Fact]
    public void Each_key_reads_only_the_route_l_vars_fresh_together_and_never_asks_for_the_ground_speed()
    {
        // The ground speed rides the 1 Hz batch: a fresh read of it would hold the distance up to a second, and a speed that
        // did not answer would say "unavailable" over a distance that was read.
        Cruise();
        Press(HotkeyAction.ReadDistanceToDest);
        Assert.Equal(new[] { A300RouteDistances.DestinationKey }, _asked);
        Press(HotkeyAction.ReadDistanceToTOD);
        Assert.Equal(new[] { A300RouteDistances.DestinationKey, A300RouteDistances.TopOfDescentKey }, _asked);
    }

    [Fact]
    public void The_time_is_worked_out_at_the_cached_ground_speed()
    {
        Cruise();
        _cache[GroundSpeedKey] = 250;     // not the 423 knots of the cruise
        _fresh[GroundSpeedKey] = 999;     // a fresh read of it would answer this: it is never asked for
        Assert.Equal(new[] { "541 miles to destination: 02:09:50" }, Press(HotkeyAction.ReadDistanceToDest));
        Assert.DoesNotContain(GroundSpeedKey, _asked);
        Assert.Equal(new[] { "443 miles to top of descent: 01:46:24" }, Press(HotkeyAction.ReadDistanceToTOD));
        Assert.DoesNotContain(GroundSpeedKey, _asked);
    }

    [Fact]
    public void The_ground_speed_the_time_uses_rides_the_batch_so_the_cache_holds_it()
    {
        // The base variable, in knots: the A300 carries it once a second for the ground speed call-outs and its status boxes.
        var speed = _def.GetVariables()[GroundSpeedKey];
        Assert.Equal(("GROUND VELOCITY", "knots"), (speed.Name, speed.Units));
        Assert.True(ContinuousBatchLayout.RidesBatch(speed));
    }

    [Fact]
    public void An_unread_ground_speed_speaks_the_miles_without_a_time()
    {
        // Nothing cached yet (just connected, or the batch has not delivered): the distance was read, so it is said.
        Cruise();
        _cache.Remove(GroundSpeedKey);
        Assert.Equal(new[] { "541 miles to destination" }, Press(HotkeyAction.ReadDistanceToDest));
        Assert.Equal(new[] { "443 miles to top of descent" }, Press(HotkeyAction.ReadDistanceToTOD));
    }

    [Fact]
    public void Past_the_top_of_descent_shift_d_says_so()
    {
        _fresh[A300RouteDistances.DestinationKey] = 24.6;
        _fresh[A300RouteDistances.TopOfDescentKey] = -81.2;
        _cache[GroundSpeedKey] = 250;
        Assert.Equal(new[] { "Past top of descent" }, Press(HotkeyAction.ReadDistanceToTOD));
        Assert.Equal(new[] { "25 miles to destination: 00:05:54" }, Press(HotkeyAction.ReadDistanceToDest));
    }

    [Fact]
    public void With_no_top_of_descent_worked_out_shift_d_says_so_and_d_still_reads()
    {
        Cruise();
        _fresh[A300RouteDistances.TopOfDescentKey] = 0;
        Assert.Equal(new[] { "Top of descent not yet computed" }, Press(HotkeyAction.ReadDistanceToTOD));
        Assert.Equal(new[] { "541 miles to destination: 01:16:44" }, Press(HotkeyAction.ReadDistanceToDest));
    }

    [Theory]
    [InlineData(0.0)]    // on the ground
    [InlineData(400.0)]  // flying with no route loaded
    public void With_no_route_every_variable_0_both_keys_say_not_available(double groundSpeed)
    {
        _fresh[A300RouteDistances.DestinationKey] = 0;
        _fresh[A300RouteDistances.TopOfDescentKey] = 0;
        _cache[GroundSpeedKey] = groundSpeed;
        Assert.Equal(new[] { "Distance to destination not available" }, Press(HotkeyAction.ReadDistanceToDest));
        Assert.Equal(new[] { "Top of descent not available" }, Press(HotkeyAction.ReadDistanceToTOD));
    }

    [Fact]
    public void A_read_that_does_not_answer_says_unavailable()
    {
        Cruise();
        _fresh.Remove(A300RouteDistances.DestinationKey);
        Assert.Equal(new[] { "Distance to destination unavailable" }, Press(HotkeyAction.ReadDistanceToDest));
        Assert.Equal(new[] { "Top of descent unavailable" }, Press(HotkeyAction.ReadDistanceToTOD));

        Cruise();
        _fresh.Remove(A300RouteDistances.TopOfDescentKey);
        Assert.Equal(new[] { "Top of descent unavailable" }, Press(HotkeyAction.ReadDistanceToTOD));
        Assert.Equal(new[] { "541 miles to destination: 01:16:44" }, Press(HotkeyAction.ReadDistanceToDest));   // D does not ask for it
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDistanceToDest)]
    [InlineData(HotkeyAction.ReadDistanceToTOD)]
    public void A_read_whose_definition_went_away_says_nothing(HotkeyAction action)
    {
        var read = new TaskCompletionSource<double?>();
        _def.ReadFresh = (_, _, _) => read.Task;
        Assert.True(_def.HandleHotkeyAction(action, _sim, _speech, null!, null!));
        _def.Dispose();
        read.SetResult(null);   // "unavailable" would otherwise be said over the next aircraft
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_two_reads_are_on_request_l_vars_on_no_panel_and_have_no_ctrl_m_row()
    {
        var vars = _def.GetVariables();
        var destination = vars[A300RouteDistances.DestinationKey];
        var topOfDescent = vars[A300RouteDistances.TopOfDescentKey];
        Assert.Equal(("FG_VNAV_DIST_DEST", SimVarType.LVar, "number", UpdateFrequency.OnRequest, false),
            (destination.Name, destination.Type, destination.Units, destination.UpdateFrequency, destination.IsAnnounced));
        Assert.True(topOfDescent.Name == "INI_TD_DIST_TOTAL",
            $"Shift+D's key is registered to read {topOfDescent.Name}. {A300RouteDistancesTests.TopOfDescentTrap}");
        Assert.Equal(("INI_TD_DIST_TOTAL", SimVarType.LVar, "number", UpdateFrequency.OnRequest, false),
            (topOfDescent.Name, topOfDescent.Type, topOfDescent.Units, topOfDescent.UpdateFrequency, topOfDescent.IsAnnounced));

        foreach (var key in new[] { A300RouteDistances.DestinationKey, A300RouteDistances.TopOfDescentKey })
        {
            Assert.False(ContinuousBatchLayout.RidesBatch(vars[key]), key);   // never a new batch name ([A300-9])
            Assert.DoesNotContain(_def.GetPanelControls().Values, keys => keys.Contains(key));
            Assert.DoesNotContain(_def.GetPanelDisplayVariables().Values, keys => keys.Contains(key));
            Assert.DoesNotContain(MonitorRowBuilder.Build(vars), row => row.Key == key);   // nothing for Ctrl+M to mute ([VAR-9])
        }
    }

    [Fact]
    public void A_delivery_of_either_read_is_left_to_the_display_path_and_says_nothing()
    {
        // The answer to a fresh read reaches ProcessSimVarUpdate like any readout's: not a row, not consumed, never spoken.
        Assert.False(_def.ProcessSimVarUpdate(A300RouteDistances.DestinationKey, 541, _speech));
        Assert.False(_def.ProcessSimVarUpdate(A300RouteDistances.TopOfDescentKey, 443.3, _speech));
        Assert.Empty(_speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_keys_speak_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Cruise();
            _fresh[A300RouteDistances.DestinationKey] = 541.4;
            Assert.Equal(new[] { "541 miles to destination: 01:16:48" }, Press(HotkeyAction.ReadDistanceToDest));
            Assert.Equal(new[] { "443 miles to top of descent: 01:02:53" }, Press(HotkeyAction.ReadDistanceToTOD));
            _cache.Remove(GroundSpeedKey);   // an unread ground speed: the miles alone
            Assert.Equal(new[] { "541 miles to destination" }, Press(HotkeyAction.ReadDistanceToDest));
            Assert.Equal(new[] { "443 miles to top of descent" }, Press(HotkeyAction.ReadDistanceToTOD));
            _fresh[A300RouteDistances.TopOfDescentKey] = -81.2;
            Assert.Equal(new[] { "Past top of descent" }, Press(HotkeyAction.ReadDistanceToTOD));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

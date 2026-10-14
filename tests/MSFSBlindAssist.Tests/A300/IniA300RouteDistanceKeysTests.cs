using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// D and Shift+D in output mode: each reads the A300's own VNAV distances fresh, together with the ground speed, and says the
/// route distance to the destination, or to the top of descent. The words are A300RouteDistancesTests'; this pins the wiring:
/// which variables each key asks for, that they are registered on request only, and what a read that does not answer says.
/// </summary>
public class IniA300RouteDistanceKeysTests
{
    private const string GroundSpeedKey = "GROUND_VELOCITY";

    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _fresh = new();
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
        };
        _def.Attach(_sim);
    }

    /// <summary>The route as the sim reports it in cruise (measured 2026-10-14): 541 miles to go, 443.3 to the top of descent.</summary>
    private void Cruise()
    {
        _fresh[A300RouteDistances.DestinationKey] = 541;
        _fresh[A300RouteDistances.TopOfDescentKey] = 443.3;
        _fresh[GroundSpeedKey] = 423;
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
    public void Each_key_reads_its_variables_fresh_together_with_the_ground_speed()
    {
        Cruise();
        Press(HotkeyAction.ReadDistanceToDest);
        Assert.Equal(new[] { A300RouteDistances.DestinationKey, GroundSpeedKey }, _asked);
        Press(HotkeyAction.ReadDistanceToTOD);
        Assert.Equal(new[] { A300RouteDistances.DestinationKey, A300RouteDistances.TopOfDescentKey, GroundSpeedKey }, _asked);
    }

    [Fact]
    public void Past_the_top_of_descent_shift_d_says_so()
    {
        _fresh[A300RouteDistances.DestinationKey] = 24.6;
        _fresh[A300RouteDistances.TopOfDescentKey] = -81.2;
        _fresh[GroundSpeedKey] = 250;
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
        _fresh[GroundSpeedKey] = groundSpeed;
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

        Cruise();
        _fresh.Remove(GroundSpeedKey);
        Assert.Equal(new[] { "Distance to destination unavailable" }, Press(HotkeyAction.ReadDistanceToDest));
        Assert.Equal(new[] { "Top of descent unavailable" }, Press(HotkeyAction.ReadDistanceToTOD));
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
            _fresh[A300RouteDistances.TopOfDescentKey] = -81.2;
            Assert.Equal(new[] { "Past top of descent" }, Press(HotkeyAction.ReadDistanceToTOD));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

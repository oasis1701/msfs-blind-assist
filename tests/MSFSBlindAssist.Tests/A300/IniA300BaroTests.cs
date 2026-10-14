using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>Ctrl+B's STD and QNH: the variables they read, and the press, settle, confirm, write and
/// read-back sequence.</summary>
public class IniA300BaroTests
{
    private const string Standard = "1 16212 (>K:2:KOHLSMAN_SET) 2 16212 (>K:2:KOHLSMAN_SET) 3 16212 (>K:2:KOHLSMAN_SET)";

    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Dictionary<string, double> _cache = new();
    private readonly Dictionary<string, double> _fresh = new();
    private readonly List<string> _sent = new();
    private readonly List<TaskCompletionSource> _waits = new();
    private bool _canLand = true;

    public IniA300BaroTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => _canLand,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            TypedDelay = _ =>
            {
                var wait = new TaskCompletionSource();
                _waits.Add(wait);
                return wait.Task;
            },
            ReadFresh = (_, key, _) => Task.FromResult(_fresh.TryGetValue(key, out var v) ? v : (double?)null),
        };
        _def.Attach(_sim);
    }

    private static string Press(string key) => $"1 (>B:{A300ControlMap.Load().FindByKey(key)!.Event})";

    private void ReadBack(double captain, double firstOfficer, double standby)
    {
        _fresh[A300Readouts.BaroCaptainKey] = captain;
        _fresh[A300Readouts.BaroFirstOfficerKey] = firstOfficer;
        _fresh[A300Readouts.BaroStandbyKey] = standby;
    }

    [Fact]
    public void The_modes_ride_their_own_subscriptions_and_the_saved_settings_are_read_on_request()
    {
        var vars = _def.GetVariables();
        foreach (var side in A300Baro.Sides)
        {
            var mode = vars[side.ModeKey];
            Assert.Equal((side.ModeVar, UpdateFrequency.Continuous, true, true),
                (mode.Name, mode.UpdateFrequency, mode.ExcludeFromBatch, mode.ExcludeFromMonitorManager));
            Assert.False(ContinuousBatchLayout.RidesBatch(mode));
            Assert.Equal((side.SavedVar, UpdateFrequency.OnRequest), (vars[side.SavedKey].Name, vars[side.SavedKey].UpdateFrequency));
        }
    }

    [Fact]
    public void A_mode_change_is_consumed_silently()
    {
        Assert.True(_def.ProcessSimVarUpdate(A300Baro.Captain.ModeKey, 1, _speech));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void One_mode_button_reads_the_reported_modes()
    {
        var mode = Assert.Single(_def.BaroButtons(_sim, _speech));
        Assert.Equal("Altimeter &mode", mode.Label);
        Assert.Equal("", mode.GetCurrentState());   // nothing read yet: the label is "Altimeter mode"
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        Assert.Equal("QNH", mode.GetCurrentState());
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        Assert.Equal("STD", mode.GetCurrentState());
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        Assert.Equal("captain STD, first officer QNH", mode.GetCurrentState());
        Assert.True(mode.SuppressStateAnnounce!());   // the sequence speaks its own read-back
    }

    [Fact]
    public void The_mode_button_in_qnh_runs_the_std_sequence()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _def.BaroButtons(_sim, _speech)[0].OnPressed();
        Assert.Equal(new[] { Press(A300Baro.Captain.PullKey), Press(A300Baro.FirstOfficer.PullKey) }, _sent);
    }

    [Fact]
    public void The_mode_button_in_std_runs_the_qnh_sequence()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _fresh[A300Baro.Captain.SavedKey] = 16399;
        _fresh[A300Baro.FirstOfficer.SavedKey] = 16288;
        _def.BaroButtons(_sim, _speech)[0].OnPressed();
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey), Press(A300Baro.FirstOfficer.PushKey) }, _sent);
    }

    [Fact]
    public void The_mode_button_with_the_sides_differing_takes_both_to_qnh()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _fresh[A300Baro.Captain.SavedKey] = 16399;
        _def.BaroButtons(_sim, _speech)[0].OnPressed();
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey) }, _sent);   // the first officer is already QNH
    }

    [Fact]
    public void The_mode_button_with_a_mode_unread_is_refused()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _def.BaroButtons(_sim, _speech)[0].OnPressed();
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.UnknownModeRefusal }, _speech.All);
    }

    [Fact]
    public void The_value_field_names_both_units()
    {
        using var box = _def.CreateAltimeterBox(_sim, _speech);
        var field = Assert.Single(box.Controls.OfType<System.Windows.Forms.TextBox>());
        Assert.Equal("altimeter setting in inches or hectopascals value", field.AccessibleName);
    }

    private const string Typed1018 = "1 16288 (>K:2:KOHLSMAN_SET) 2 16288 (>K:2:KOHLSMAN_SET) 3 16288 (>K:2:KOHLSMAN_SET)";

    [Fact]
    public void A_typed_value_in_qnh_is_written_and_confirmed_as_before()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetAltimetersAsync(1018, _sim, _speech);
        Assert.Equal(new[] { Typed1018 }, _sent);
        Assert.Equal(new[] { $"Altimeters {A300Readouts.Altimeter(1018)}" }, _speech.All);
        Assert.Empty(_waits);
    }

    [Fact]
    public void A_typed_value_with_a_side_in_std_pushes_it_to_qnh_confirms_then_writes()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetAltimetersAsync(1018, _sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey) }, _sent);   // nothing written while it reads STD
        Assert.Empty(_speech.All);

        _fresh[A300Baro.Captain.ModeKey] = 0;
        _waits.Single().SetResult();
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey), Typed1018 }, _sent);
        Assert.Equal(new[] { $"Altimeters {A300Readouts.Altimeter(1018)}" }, _speech.All);
    }

    [Fact]
    public void A_typed_value_whose_side_stays_in_std_names_it_and_writes_nothing()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _ = _def.SetAltimetersAsync(1018, _sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey), Press(A300Baro.FirstOfficer.PushKey) }, _sent);
        _fresh[A300Baro.Captain.ModeKey] = 0;
        _fresh[A300Baro.FirstOfficer.ModeKey] = 1;
        _waits.Single().SetResult();
        Assert.DoesNotContain(Typed1018, _sent);
        Assert.Equal(new[] { "First officer altimeter did not switch to QNH" }, _speech.All);
    }

    [Fact]
    public void A_typed_value_with_a_mode_unread_is_refused()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _ = _def.SetAltimetersAsync(1018, _sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.UnknownModeRefusal }, _speech.All);
    }

    [Fact]
    public void A_typed_value_while_std_or_qnh_is_switching_is_refused_aloud()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetStandardAsync(_sim, _speech);
        _sent.Clear();
        _ = _def.SetAltimetersAsync(1018, _sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.BusyRefusal }, _speech.All);
    }

    [Fact]
    public void A_typed_value_out_of_range_or_unreachable_says_why()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetAltimetersAsync(1200, _sim, _speech);
        _canLand = false;
        _ = _def.SetAltimetersAsync(1018, _sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { $"Altimeters: {A300TypedValues.AltimeterError}", A300Baro.UnavailableRefusal }, _speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_typed_value_after_a_push_reads_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            _cache[A300Baro.Captain.ModeKey] = 1;
            _cache[A300Baro.FirstOfficer.ModeKey] = 0;
            _ = _def.SetAltimetersAsync(30.06, _sim, _speech);
            _fresh[A300Baro.Captain.ModeKey] = 0;
            _waits.Single().SetResult();
            Assert.Equal("1 16287 (>K:2:KOHLSMAN_SET) 2 16287 (>K:2:KOHLSMAN_SET) 3 16287 (>K:2:KOHLSMAN_SET)", _sent.Last());
            Assert.Equal(new[] { "Altimeters 1018 hectopascals, 30.06 inches" }, _speech.All);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- The Captain Side and First Officer Side panels' "Altimeter setting" boxes: Ctrl+B's sequence for one side ----

    private const string CaptainBox1018 = "1 16288 (>K:2:KOHLSMAN_SET)";
    private const string FirstOfficerBox1018 = "2 16288 (>K:2:KOHLSMAN_SET)";

    /// <summary>A value typed into a panel's altimeter box and set, the way the panel hands it over.</summary>
    private void SetBox(string key, double value) =>
        Assert.True(_def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech));

    [Fact]
    public void A_captain_box_value_with_the_captain_in_std_pushes_confirms_then_writes()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        SetBox(A300TypedValues.BaroCaptainKey, 1018);
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey) }, _sent);   // nothing is written while it reads STD
        Assert.Empty(_speech.All);

        _fresh[A300Baro.Captain.ModeKey] = 0;
        _waits.Single().SetResult();
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey), CaptainBox1018 }, _sent);
        Assert.Equal(new[] { "Captain altimeter 1018 hectopascals, 30.06 inches" }, _speech.All);
    }

    [Fact]
    public void A_captain_box_value_whose_side_stays_in_std_writes_nothing()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        SetBox(A300TypedValues.BaroCaptainKey, 1018);
        _fresh[A300Baro.Captain.ModeKey] = 1;
        _waits.Single().SetResult();
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey) }, _sent);   // the push only: no KOHLSMAN write
        Assert.Equal(new[] { "Captain altimeter did not switch to QNH" }, _speech.All);
        Assert.Equal(_speech.All, _speech.Interrupts);
    }

    [Fact]
    public void A_first_officer_box_value_pushes_only_the_first_officer()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        SetBox(A300TypedValues.BaroFirstOfficerKey, 1018);
        Assert.Equal(new[] { Press(A300Baro.FirstOfficer.PushKey) }, _sent);   // the captain's STD is not this box's

        _fresh[A300Baro.FirstOfficer.ModeKey] = 0;
        _waits.Single().SetResult();
        Assert.Equal(new[] { Press(A300Baro.FirstOfficer.PushKey), FirstOfficerBox1018 }, _sent);
        Assert.Equal(new[] { "First officer altimeter 1018 hectopascals, 30.06 inches" }, _speech.All);
    }

    [Fact]
    public void A_side_box_value_while_std_or_qnh_runs_is_refused_aloud()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetStandardAsync(_sim, _speech);
        _sent.Clear();
        SetBox(A300TypedValues.BaroFirstOfficerKey, 1018);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.BusyRefusal }, _speech.All);
        Assert.Equal(_speech.All, _speech.Interrupts);
    }

    [Fact]
    public void While_a_side_box_waits_for_its_push_the_other_altimeter_entries_are_refused_or_ignored()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        SetBox(A300TypedValues.BaroCaptainKey, 1018);
        _sent.Clear();

        SetBox(A300TypedValues.BaroFirstOfficerKey, 1018);
        _ = _def.SetAltimetersAsync(1018, _sim, _speech);
        _ = _def.SetStandardAsync(_sim, _speech);   // a mode press is ignored: the running push speaks

        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.BusyRefusal, A300Baro.BusyRefusal }, _speech.All);
    }

    [Fact]
    public void A_side_box_value_with_its_mode_unread_is_refused()
    {
        SetBox(A300TypedValues.BaroCaptainKey, 1018);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.UnknownModeRefusal }, _speech.All);
        Assert.Equal(_speech.All, _speech.Interrupts);
    }

    [Fact]
    public void A_side_box_needs_only_its_own_sides_mode()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;   // the first officer's has not been read
        SetBox(A300TypedValues.BaroCaptainKey, 1018);
        SetBox(A300TypedValues.BaroFirstOfficerKey, 1018);
        Assert.Equal(new[] { CaptainBox1018 }, _sent);
        Assert.Equal(new[] { "Captain altimeter 1018 hectopascals, 30.06 inches", A300Baro.UnknownModeRefusal }, _speech.All);
    }

    [Fact]
    public void A_side_box_value_in_qnh_is_written_at_once()
    {
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        SetBox(A300TypedValues.BaroFirstOfficerKey, 1018);
        Assert.Equal(new[] { FirstOfficerBox1018 }, _sent);
        Assert.Equal(new[] { "First officer altimeter 1018 hectopascals, 30.06 inches" }, _speech.All);
        Assert.Empty(_waits);
    }

    [Fact]
    public void A_side_box_value_leaves_the_other_side_in_std_alone()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        SetBox(A300TypedValues.BaroCaptainKey, 1018);
        Assert.Equal(new[] { CaptainBox1018 }, _sent);   // no push, and only the captain's index is written
        Assert.Empty(_waits);
    }

    [Fact]
    public void A_side_box_value_out_of_range_or_unreachable_says_why_and_presses_nothing()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        SetBox(A300TypedValues.BaroCaptainKey, 1200);
        _canLand = false;
        SetBox(A300TypedValues.BaroCaptainKey, 1018);
        Assert.Empty(_sent);
        Assert.Empty(_waits);
        Assert.Equal(new[] { $"Altimeter setting: {A300TypedValues.AltimeterError}", "Altimeter setting unavailable" },
            _speech.All);
    }

    [Fact]
    public void A_side_box_value_that_was_refused_or_failed_leaves_the_guard_clear()
    {
        SetBox(A300TypedValues.BaroCaptainKey, 1018);   // its mode is unread: refused
        _cache[A300Baro.Captain.ModeKey] = 1;
        SetBox(A300TypedValues.BaroCaptainKey, 1018);   // pushed, and the side stays in STD
        _fresh[A300Baro.Captain.ModeKey] = 1;
        _waits[0].SetResult();
        _sent.Clear();

        _fresh[A300Baro.Captain.ModeKey] = 0;
        SetBox(A300TypedValues.BaroCaptainKey, 1018);   // pushed again, and this time it lands
        _waits[1].SetResult();

        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey), CaptainBox1018 }, _sent);
        Assert.Equal(new[]
        {
            A300Baro.UnknownModeRefusal,
            "Captain altimeter did not switch to QNH",
            "Captain altimeter 1018 hectopascals, 30.06 inches",
        }, _speech.All);
    }

    [Fact]
    public void A_side_box_value_for_a_definition_disposed_during_the_settle_writes_nothing()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        SetBox(A300TypedValues.BaroCaptainKey, 1018);
        _def.Dispose();
        _fresh[A300Baro.Captain.ModeKey] = 0;
        _waits.Single().SetResult();
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey) }, _sent);
        Assert.Empty(_speech.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1.0)]
    public void The_standby_box_value_is_written_at_once_whatever_the_sides_read(double? sidesMode)
    {
        if (sidesMode is double mode)
        {
            _cache[A300Baro.Captain.ModeKey] = mode;
            _cache[A300Baro.FirstOfficer.ModeKey] = mode;
        }
        SetBox(A300TypedValues.BaroStandbyKey, 1018);
        Assert.Equal(new[] { "3 16288 (>K:2:KOHLSMAN_SET)" }, _sent);   // the standby has no STD: nothing is pushed
        Assert.Equal(new[] { "Standby altimeter 1018 hectopascals, 30.06 inches" }, _speech.All);
        Assert.Empty(_waits);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_side_box_value_after_a_push_reads_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            _cache[A300Baro.Captain.ModeKey] = 1;
            _cache[A300Baro.FirstOfficer.ModeKey] = 0;
            SetBox(A300TypedValues.BaroCaptainKey, 30.06);
            _fresh[A300Baro.Captain.ModeKey] = 0;
            _waits.Single().SetResult();
            Assert.Equal("1 16287 (>K:2:KOHLSMAN_SET)", _sent.Last());
            Assert.Equal(new[] { "Captain altimeter 1018 hectopascals, 30.06 inches" }, _speech.All);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Standard_pulls_waits_confirms_writes_1013_and_reads_back()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetStandardAsync(_sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PullKey), Press(A300Baro.FirstOfficer.PullKey) }, _sent);

        _fresh[A300Baro.Captain.ModeKey] = 1;
        _fresh[A300Baro.FirstOfficer.ModeKey] = 1;
        _waits[0].SetResult();
        Assert.Equal(Standard, _sent.Last());
        Assert.Empty(_speech.All);

        ReadBack(1013.25, 1013.25, 1013.25);
        _waits[1].SetResult();
        Assert.Equal(new[] { "Altimeters standard, 1013 hectopascals, 29.92 inches" }, _speech.All);
    }

    [Fact]
    public void Standard_stops_and_says_which_side_did_not_switch()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _ = _def.SetStandardAsync(_sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PullKey) }, _sent);
        _fresh[A300Baro.Captain.ModeKey] = 0;
        _waits[0].SetResult();
        Assert.DoesNotContain(Standard, _sent);
        Assert.Equal(new[] { "Captain altimeter did not switch to STD" }, _speech.All);
    }

    [Fact]
    public void Qnh_pushes_restores_the_saved_settings_and_reads_back()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _fresh[A300Baro.Captain.SavedKey] = 16399;
        _fresh[A300Baro.FirstOfficer.SavedKey] = 16288;
        _ = _def.SetQnhAsync(_sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey), Press(A300Baro.FirstOfficer.PushKey) }, _sent);

        _fresh[A300Baro.Captain.ModeKey] = 0;
        _fresh[A300Baro.FirstOfficer.ModeKey] = 0;
        _waits[0].SetResult();
        Assert.Equal("1 16399 (>K:2:KOHLSMAN_SET) 2 16288 (>K:2:KOHLSMAN_SET) 3 16399 (>K:2:KOHLSMAN_SET)", _sent.Last());

        ReadBack(1024.94, 1018, 1024.94);
        _waits[1].SetResult();
        Assert.Equal(new[] { "Altimeters QNH: captain 1025, first officer 1018, standby 1025 hectopascals" }, _speech.All);
    }

    [Fact]
    public void Qnh_warnings_lead_the_one_read_back_so_the_read_back_cannot_cut_them_off()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _fresh[A300Baro.Captain.SavedKey] = 100;   // 6.25 hPa: not a plausible setting
        _fresh[A300Baro.FirstOfficer.SavedKey] = 16288;
        _ = _def.SetQnhAsync(_sim, _speech);

        _fresh[A300Baro.Captain.ModeKey] = 0;
        _fresh[A300Baro.FirstOfficer.ModeKey] = 0;
        _waits[0].SetResult();
        Assert.Equal("2 16288 (>K:2:KOHLSMAN_SET) 3 16288 (>K:2:KOHLSMAN_SET)", _sent.Last());
        Assert.Empty(_speech.All);

        ReadBack(1018, 1018, 1018);
        _waits[1].SetResult();
        Assert.Equal(new[]
        {
            $"Captain saved setting unavailable. Altimeters QNH, {A300Readouts.Altimeter(1018)}",
        }, _speech.All);
        Assert.Equal(_speech.All, _speech.Interrupts);
    }

    [Fact]
    public void Qnh_warnings_also_lead_the_did_not_report_back_line()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _fresh[A300Baro.Captain.SavedKey] = 100;
        _fresh[A300Baro.FirstOfficer.SavedKey] = 16288;
        _ = _def.SetQnhAsync(_sim, _speech);

        _fresh[A300Baro.Captain.ModeKey] = 0;
        _fresh[A300Baro.FirstOfficer.ModeKey] = 0;
        _waits[0].SetResult();
        _waits[1].SetResult();   // no altimeter is delivered
        Assert.Equal(new[]
        {
            "Captain saved setting unavailable. Altimeters QNH; the altimeters did not report back",
        }, _speech.All);
    }

    [Fact]
    public void A_second_standard_press_while_the_first_is_settling_is_ignored()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetStandardAsync(_sim, _speech);
        var sentAfterFirst = _sent.ToArray();

        _ = _def.SetStandardAsync(_sim, _speech);

        Assert.Equal(sentAfterFirst, _sent);
        Assert.Single(_waits);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_new_standard_press_works_once_the_first_has_finished()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetStandardAsync(_sim, _speech);
        _fresh[A300Baro.Captain.ModeKey] = 1;
        _fresh[A300Baro.FirstOfficer.ModeKey] = 1;
        _waits[0].SetResult();
        ReadBack(1013.25, 1013.25, 1013.25);
        _waits[1].SetResult();
        _sent.Clear();

        _ = _def.SetStandardAsync(_sim, _speech);

        Assert.Equal(new[] { Press(A300Baro.Captain.PullKey), Press(A300Baro.FirstOfficer.PullKey) }, _sent);
    }

    [Fact]
    public void A_refused_press_does_not_leave_the_guard_set()
    {
        _ = _def.SetStandardAsync(_sim, _speech);   // modes unread: refused
        Assert.Equal(new[] { A300Baro.UnknownModeRefusal }, _speech.All);
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;

        _ = _def.SetStandardAsync(_sim, _speech);

        Assert.Equal(new[] { Press(A300Baro.Captain.PullKey), Press(A300Baro.FirstOfficer.PullKey) }, _sent);
    }

    [Fact]
    public void A_standard_press_while_qnh_is_running_is_ignored()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _fresh[A300Baro.Captain.SavedKey] = 16399;
        _fresh[A300Baro.FirstOfficer.SavedKey] = 16288;
        _ = _def.SetQnhAsync(_sim, _speech);
        var sentAfterQnh = _sent.ToArray();

        _ = _def.SetStandardAsync(_sim, _speech);

        Assert.Equal(sentAfterQnh, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_definition_disposed_during_the_settle_writes_nothing()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        // The last side's confirmation read is still in flight when the definition goes away. Once it answers,
        // the read helper (ReadAllFreshAsync) finds the definition disposed, so the sequence stops before the write.
        var lastRead = new TaskCompletionSource<double?>();
        _def.ReadFresh = (_, key, _) => key == A300Baro.FirstOfficer.ModeKey
            ? lastRead.Task
            : Task.FromResult<double?>(1);
        _ = _def.SetStandardAsync(_sim, _speech);
        _waits[0].SetResult();
        _def.Dispose();
        lastRead.SetResult(1);

        Assert.DoesNotContain(Standard, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_definition_disposed_while_a_mode_read_fails_says_nothing()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        var reads = new TaskCompletionSource<double?>();
        _def.ReadFresh = (_, _, _) => reads.Task;
        _ = _def.SetStandardAsync(_sim, _speech);
        _waits[0].SetResult();
        _def.Dispose();
        reads.SetResult(null);   // what SimConnectManager's FailAll delivers on a switch or disconnect
        Assert.DoesNotContain(Standard, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Both_sides_modes_are_read_together()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        var asked = new List<string>();
        var reads = new TaskCompletionSource<double?>();
        _def.ReadFresh = (_, key, _) => { asked.Add(key); return reads.Task; };
        _ = _def.SetStandardAsync(_sim, _speech);
        _waits[0].SetResult();
        Assert.Equal(new[] { A300Baro.Captain.ModeKey, A300Baro.FirstOfficer.ModeKey }, asked);   // before either answers
    }

    /// <summary>Runs STD to its final read-back: both sides confirm STD at once, then the three settings are asked
    /// for and left unanswered. Returns the keys asked and the answer to resolve.</summary>
    private (List<string> Asked, TaskCompletionSource<double?> Settings) StartStandardUntilItsReadBackIsOut()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        var asked = new List<string>();
        var settings = new TaskCompletionSource<double?>();
        _def.ReadFresh = (_, key, _) =>
        {
            if (key == A300Baro.Captain.ModeKey || key == A300Baro.FirstOfficer.ModeKey)
                return Task.FromResult<double?>(1);
            asked.Add(key);
            return settings.Task;
        };
        _ = _def.SetStandardAsync(_sim, _speech);
        _waits[0].SetResult();   // the knobs have settled: 1013 is written and the read-back's own wait starts
        _waits[1].SetResult();   // that wait is over: the three settings are asked for
        return (asked, settings);
    }

    [Fact]
    public void The_final_read_back_asks_for_the_three_settings_together()
    {
        var (asked, _) = StartStandardUntilItsReadBackIsOut();
        Assert.Equal(new[] { A300Readouts.BaroCaptainKey, A300Readouts.BaroFirstOfficerKey, A300Readouts.BaroStandbyKey },
            asked);   // before any answers
    }

    [Fact]
    public void A_definition_disposed_while_the_final_read_back_is_out_says_nothing()
    {
        var (_, settings) = StartStandardUntilItsReadBackIsOut();
        _def.Dispose();
        settings.SetResult(null);   // the read-back would otherwise say "did not report back" over the next aircraft
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Qnh_when_already_qnh_says_so_and_sends_nothing()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetQnhAsync(_sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.AlreadyQnh }, _speech.All);
    }

    [Fact]
    public void An_unread_mode_is_refused()
    {
        _ = _def.SetStandardAsync(_sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.UnknownModeRefusal }, _speech.All);
    }

    [Fact]
    public void Altimeters_that_cannot_be_reached_say_so()
    {
        _canLand = false;
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetStandardAsync(_sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Altimeters unavailable" }, _speech.All);
        Assert.Equal("Altimeters unavailable", A300Baro.UnavailableRefusal);
    }

    [Fact]
    public void Qnh_that_cannot_be_reached_says_so_and_leaves_the_guard_clear()
    {
        _canLand = false;
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _ = _def.SetQnhAsync(_sim, _speech);
        Assert.Equal(new[] { A300Baro.UnavailableRefusal }, _speech.All);

        _canLand = true;
        _fresh[A300Baro.Captain.SavedKey] = 16399;
        _fresh[A300Baro.FirstOfficer.SavedKey] = 16288;
        _ = _def.SetQnhAsync(_sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey), Press(A300Baro.FirstOfficer.PushKey) }, _sent);
    }
}

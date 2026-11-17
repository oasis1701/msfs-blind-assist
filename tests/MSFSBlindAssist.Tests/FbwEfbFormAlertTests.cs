using System.Globalization;
using MSFSBlindAssist.Forms.FBWA380;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins how the shared EFB window tells one alert from another and speaks several at once
/// (FbwEfbForm.ApplyElements, through TakeNewAlerts), and where its list mode puts the focus after a
/// rebuild (FocusRowAfterRebuild). Review of PR 328: keyed by the key alone, the BlueBird 757 tablet's
/// one alert, always under the key "alert", spoke only its first text.
/// </summary>
public class FbwEfbFormAlertTests
{
    private static void UnderCulture(string cultureName, Action assertions)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static (string, string?, string) Alert(string? key, string text) => ("alert", key, text);

    [Fact]
    public void A_keyless_alert_is_its_text()
    {
        Assert.Equal("Success: Tablet preferences were updated.", FbwEfbForm.AlertIdentity("", "Success: Tablet preferences were updated."));
        Assert.Equal(FbwEfbForm.AlertIdentity(null, "Saved"), FbwEfbForm.AlertIdentity("", "Saved"));
    }

    [Fact]
    public void New_words_under_the_same_key_are_a_new_alert()
    {
        Assert.NotEqual(FbwEfbForm.AlertIdentity("alert", "Fuel truck: called"), FbwEfbForm.AlertIdentity("alert", "Load fuel?"));
    }

    [Fact]
    public void The_same_words_under_a_new_key_are_a_new_alert()
    {
        Assert.NotEqual(FbwEfbForm.AlertIdentity("notice:1", "Error: OFP airport is invalid"),
                        FbwEfbForm.AlertIdentity("notice:2", "Error: OFP airport is invalid"));
    }

    [Fact]
    public void Alerts_arriving_together_are_one_sentence_each()
    {
        Assert.Equal("Error: Could not fetch airport. Warning: Aircraft needs to be powered to load presets",
            FbwEfbForm.JoinAlerts(new[] { "Error: Could not fetch airport", "Warning: Aircraft needs to be powered to load presets" }));
    }

    [Fact]
    public void An_alert_that_ends_a_sentence_gets_no_extra_period()
    {
        Assert.Equal("Error: Could not find OFP runway! Warning: Loading preset",
            FbwEfbForm.JoinAlerts(new[] { "Error: Could not find OFP runway!", "Warning: Loading preset" }));
        Assert.Equal("Saved.", FbwEfbForm.JoinAlerts(new[] { "Saved." }));
    }

    // ---- TakeNewAlerts: what one push speaks ----

    [Fact]
    public void An_alert_speaks_once_while_it_stays_and_again_after_it_returns()
    {
        var announced = new HashSet<string>();
        var push = new[] { Alert("notice:1", "Error: Could not fetch airport") };
        Assert.Equal(new[] { "Error: Could not fetch airport" }, FbwEfbForm.TakeNewAlerts(push, announced));
        Assert.Empty(FbwEfbForm.TakeNewAlerts(push, announced));
        Assert.Empty(FbwEfbForm.TakeNewAlerts(Array.Empty<(string, string?, string)>(), announced));
        Assert.Equal(new[] { "Error: Could not fetch airport" }, FbwEfbForm.TakeNewAlerts(push, announced));
    }

    [Fact]
    public void Several_new_alerts_speak_together_and_one_already_spoken_is_left_out()
    {
        var announced = new HashSet<string>();
        FbwEfbForm.TakeNewAlerts(new[] { Alert("notice:1", "Error: OFP airport is invalid") }, announced);
        var fresh = FbwEfbForm.TakeNewAlerts(new[]
        {
            Alert("notice:1", "Error: OFP airport is invalid"),
            Alert("notice:2", "Error: Could not fetch airport"),
            Alert("notice:3", "Warning: Aircraft needs to be powered to load presets"),
        }, announced);
        Assert.Equal(new[] { "Error: Could not fetch airport", "Warning: Aircraft needs to be powered to load presets" }, fresh);
    }

    [Fact]
    public void A_repeat_error_under_a_new_key_speaks_again()
    {
        var announced = new HashSet<string>();
        FbwEfbForm.TakeNewAlerts(new[] { Alert("notice:1", "Error: Could not find OFP runway!") }, announced);
        Assert.Equal(new[] { "Error: Could not find OFP runway!" },
            FbwEfbForm.TakeNewAlerts(new[] { Alert("notice:2", "Error: Could not find OFP runway!") }, announced));
    }

    [Fact]
    public void The_757_tablet_alert_under_its_fixed_key_speaks_each_new_text()
    {
        var announced = new HashSet<string>();
        FbwEfbForm.TakeNewAlerts(new[] { Alert("alert", "Fuel truck: called") }, announced);
        Assert.Equal(new[] { "Load fuel?" }, FbwEfbForm.TakeNewAlerts(new[] { Alert("alert", "Load fuel?") }, announced));
    }

    [Fact]
    public void Items_that_are_not_alerts_or_have_no_text_never_speak()
    {
        var announced = new HashSet<string>();
        var fresh = FbwEfbForm.TakeNewAlerts(new (string, string?, string)[]
        {
            ("button", "notice-dismiss:1", "Dismiss: Could not fetch airport"),
            ("text", null, "Remember to configure MSFS"),
            Alert("notice:2", "   "),
        }, announced);
        Assert.Empty(fresh);
        Assert.Empty(announced);
    }

    // The spoken text carries FBW's own numbers; nothing here may format them in the PC's culture.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Alerts_speak_FBW_numbers_unchanged_under_a_comma_decimal_culture(string culture)
    {
        UnderCulture(culture, () =>
        {
            var announced = new HashSet<string>();
            var fresh = FbwEfbForm.TakeNewAlerts(new[]
            {
                Alert("notice:1", "Error: Beyond Takeoff Weight Limit - Max 19943 lb"),
                Alert("inline:Axis 1: Idle (-0.05) overlaps with Climb (-0.25)", "Error: Axis 1: Idle (-0.05) overlaps with Climb (-0.25)"),
            }, announced);
            Assert.Equal("Error: Beyond Takeoff Weight Limit - Max 19943 lb. Error: Axis 1: Idle (-0.05) overlaps with Climb (-0.25)",
                FbwEfbForm.JoinAlerts(fresh));
        });
    }

    // ---- FocusRowAfterRebuild: where list mode puts the focus after a rebuild ----

    private static int Target(string[] oldIds, int focusedAt, int focusedIdx, (string Id, int Idx, bool Focusable)[] rows) =>
        FbwEfbForm.FocusRowAfterRebuild(oldIds, focusedAt, focusedIdx,
            rows.Select(r => r.Id).ToList(), rows.Select(r => r.Idx).ToList(), rows.Select(r => r.Focusable).ToList());

    [Fact]
    public void The_focused_control_still_shown_keeps_the_focus_when_rows_shift_above_it()
    {
        Assert.Equal(3, Target(new[] { "Clear", "Calculate" }, 1, 7,
            new[] { ("alert", 0, false), ("Dismiss", 100001, true), ("Clear", 6, true), ("Calculate", 7, true) }));
    }

    [Fact]
    public void A_pressed_Dismiss_moves_the_focus_to_the_next_control_not_the_one_after()
    {
        Assert.Equal(0, Target(new[] { "alert", "Dismiss", "Calculate", "Clear" }, 1, 100001,
            new[] { ("Calculate", 7, true), ("Clear", 8, true) }));
    }

    [Fact]
    public void A_rebuilt_button_keeps_the_focus_when_a_notice_arrives_above_it()
    {
        Assert.Equal(3, Target(new[] { "Back", "GPU Connect", "Next" }, 1, 2,
            new[] { ("alert", 0, false), ("Dismiss", 100001, true), ("Back", 1, true), ("GPU Disconnect", 2, true), ("Next", 3, true) }));
    }

    [Fact]
    public void With_no_row_before_it_the_rebuilt_control_wins_over_a_new_Dismiss_at_the_top()
    {
        Assert.Equal(2, Target(new[] { "GPU Connect", "Next" }, 0, 2,
            new[] { ("alert", 0, false), ("Dismiss", 100001, true), ("GPU Disconnect", 2, true), ("Next", 3, true) }));
    }

    [Fact]
    public void With_no_row_before_it_a_removed_control_hands_the_focus_to_the_next_row_not_a_new_Dismiss()
    {
        Assert.Equal(2, Target(new[] { "Fuel Truck", "Next" }, 0, 5,
            new[] { ("alert", 0, false), ("Dismiss", 100001, true), ("Next", 5, true) }));
    }

    [Fact]
    public void The_last_control_going_moves_the_focus_to_the_one_before_it()
    {
        Assert.Equal(1, Target(new[] { "Back", "Catering", "Fuel Truck" }, 2, 3,
            new[] { ("Back", 1, true), ("Catering", 2, true) }));
    }

    [Fact]
    public void Nothing_focusable_left_gives_no_target()
    {
        Assert.Equal(-1, Target(new[] { "alert", "Dismiss" }, 1, 100001, Array.Empty<(string, int, bool)>()));
        Assert.Equal(-1, Target(new[] { "Heading", "Dismiss" }, 1, 100001, new[] { ("Heading", 0, false) }));
    }
}

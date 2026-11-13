// The Fenix options window lists the options in groups, the way the Fenix EFB does, with the options that
// touch what the app does on top. What a pilot hears when the window opens and when F5 refreshes it is
// pinned here: the group order, the callouts' altitude order, the status line (including a warning for an
// option set to something the app was not tested with) and the refresh result.
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixOptionGroupingTests
{
    static FenixSetting S(string key, string? value, string type = "System.Boolean", string category = "Aircraft",
        string[]? possible = null, string source = "Internal", bool writable = true)
        => new(key, category, value, possible ?? Array.Empty<string>(), type, source, writable);

    // The 11 options that affect the app, as read live on the IAE A321 (2026-10-21).
    static List<FenixSetting> LiveEleven() => new()
    {
        S("encoder_acceleration_enabled", "false", category: "Realism"),
        S("efis_baro_linking_type", "Independent", "Enum", "Realism", new[] { "Independent", "LinkedWithStandby", "Linked" }),
        S("fd_ls_sync_enabled", "false", category: "Realism"),
        S("channel_spacing_type", "Khz8d33", "Enum", "Aircraft", new[] { "Khz25", "Khz8d33" }),
        S("fcu_readouts_type", "Old", "Enum", "Misc", new[] { "Old", "New" }),
        S("weight_unit", "Pounds", "Enum", "Aircraft", new[] { "Kilograms", "Pounds" }),
        S("baro_unit", "Inhg", "Enum", "Aircraft", new[] { "Hpa", "Inhg" }),
        S("control_position_type", "Captain", "Enum", "Misc", new[] { "Captain", "FirstOfficer" }),
        S("incremental_thrust_handling_enabled", "false", category: "Misc"),
        S("auto_toga_enabled", "true", category: "Aircraft"),
        S("tcas_ap_enabled", "false", category: "Aircraft"),
    };

    [Fact] public void Live_snapshot_reads_as_the_pilot_hears_it()
    {
        var first = FenixOptionGrouping.Group(LiveEleven())[0];
        Assert.Equal("Options that affect MSFS Blind Assist (11)", first.NodeText);
        Assert.Equal(new[] { "Encoder Acceleration: Off", "EFIS Linking: Baro: Independent", "EFIS Linking: FD/LS: Off",
            "Channel Spacing: 8.33 kHz", "FCU Readouts: Old", "Weight Unit: Pounds", "Barometric Unit: inHg",
            "Control Position: Captain", "Incremental Thrust Handling: Off", "Auto TOGA: On", "AP/FD TCAS: Off" },
            first.Options.Select(FenixOptionCatalog.RowText));
    }

    [Fact] public void Groups_come_in_order()
    {
        var cats = new[] { "Realism", "None", "Printer", "Misc", "GroundServices", "Connections", "Cabin", "Audio", "Aircraft" };
        var list = cats.Select((c, i) => S($"k{i}", "true", category: c)).Append(S("encoder_acceleration_enabled", "false", category: "Realism")).ToList();
        Assert.Equal(new[] { "Options that affect MSFS Blind Assist", "Aircraft", "Audio", "Cabin", "Connections", "Ground Services",
            "Misc", "Printer", "Realism", "Other, not in the Fenix EFB" }, FenixOptionGrouping.Group(list).Select(g => g.Name));
    }

    [Fact] public void Affecting_group_keeps_its_order_skips_missing_and_is_omitted_when_empty()
    {
        var two = new[] { S("fd_ls_sync_enabled", "false", category: "Realism"), S("encoder_acceleration_enabled", "false", category: "Realism") };
        var groups = FenixOptionGrouping.Group(two);
        Assert.Equal(new[] { "encoder_acceleration_enabled", "fd_ls_sync_enabled" }, groups[0].Options.Select(o => o.Key));
        Assert.Equal(2, groups.Single(g => g.Name == "Realism").Options.Count);
        Assert.Equal("Aircraft", FenixOptionGrouping.Group(new[] { S("registration", "N1", "System.String") })[0].Name);
    }

    [Fact] public void Callouts_sort_by_altitude_then_others_by_label()
    {
        var audio = new[] { "mute_cabin_audio_in_background", "gpws10", "gpws500_glide_deviation", "gpws2500b", "gpws500", "gpws2500" }
            .Select(k => S(k, "true", category: "Audio")).ToList();
        Assert.Equal(new[] { "gpws2500", "gpws2500b", "gpws500", "gpws500_glide_deviation", "gpws10", "mute_cabin_audio_in_background" },
            FenixOptionGrouping.Group(audio)[0].Options.Select(o => o.Key));
        var aircraft = new[] { S("weight_unit", "Pounds", "Enum"), S("ac_ess_auto_switching_enabled", "false"), S("baro_unit", "Inhg", "Enum") };
        Assert.Equal(new[] { "ac_ess_auto_switching_enabled", "baro_unit", "weight_unit" },
            FenixOptionGrouping.Group(aircraft).Single(g => g.Name == "Aircraft").Options.Select(o => o.Key));
    }

    [Fact] public void Unknown_category_is_humanized_and_sorted()
        => Assert.Equal(new[] { "Aircraft", "Datalink Things" },
            FenixOptionGrouping.Group(new[] { S("x1", "true", category: "DatalinkThings"), S("x2", "true") }).Select(g => g.Name));

    [Fact] public void Categories_that_read_alike_share_a_group_and_none_or_empty_is_other()
    {
        var groups = FenixOptionGrouping.Group(new[]
        {
            S("a", "true", category: "GroundServices"), S("b", "true", category: "Ground-Services"),
            S("c", "true", category: "None"), S("d", "true", category: ""),
        });
        Assert.Equal(new[] { "Ground Services", "Other, not in the Fenix EFB" }, groups.Select(g => g.Name));
        Assert.Equal(new[] { "a", "b" }, groups[0].Options.Select(o => o.Key));
        Assert.Equal(new[] { "c", "d" }, groups[1].Options.Select(o => o.Key));
    }

    [Fact] public void CountChanged_counts_changes_additions_and_removals()
    {
        Assert.Equal(2, FenixOptionGrouping.CountChanged(new[] { S("a", "1"), S("b", "2") }, new[] { S("a", "1"), S("b", "3"), S("c", "4") }));
        // An option the second read no longer has counts as one change.
        Assert.Equal(1, FenixOptionGrouping.CountChanged(new[] { S("a", "1"), S("b", "2") }, new[] { S("a", "1") }));
    }

    static FenixSetting[] Live(string key, string value)
        => LiveEleven().Select(s => s.Key == key ? s with { Value = value } : s).ToArray();

    [Fact] public void Untested_lists_the_option_the_app_has_a_tested_value_for_when_it_reads_as_something_else()
    {
        // Control Position is the one option left with a tested value (Captain): its First Officer seat is unmeasured.
        Assert.Empty(FenixOptionGrouping.Untested(LiveEleven()));
        Assert.Equal(new[] { "Control Position: First Officer" }, FenixOptionGrouping.Untested(Live("control_position_type", "FirstOfficer")));
        // Only the options the app was tested with a value for can be listed; the others carry no tested value.
        Assert.Empty(FenixOptionGrouping.Untested(Live("weight_unit", "Kilograms")));
    }

    [Fact] public void The_four_options_measured_on_2026_10_24_work_either_way_and_are_never_warned_about()
    {
        // M2 to M4 and the FCU readouts: nothing the app does depends on their value, so no tested value.
        foreach (string key in new[] { "efis_baro_linking_type", "fd_ls_sync_enabled", "channel_spacing_type", "fcu_readouts_type" })
        {
            Assert.Null(FenixOptionNotes.TestedValue(key));
            Assert.Contains("Measured 2026-10-24", FenixOptionNotes.AppNote(key));
        }
        Assert.Empty(FenixOptionGrouping.Untested(Live("efis_baro_linking_type", "Linked")));
        Assert.Empty(FenixOptionGrouping.Untested(Live("efis_baro_linking_type", "LinkedWithStandby")));
        Assert.Empty(FenixOptionGrouping.Untested(Live("fd_ls_sync_enabled", "True")));
        Assert.Empty(FenixOptionGrouping.Untested(Live("channel_spacing_type", "Khz25")));
        Assert.Empty(FenixOptionGrouping.Untested(Live("fcu_readouts_type", "New")));
        Assert.Equal("Captain", FenixOptionNotes.TestedValue("control_position_type"));
    }

    [Fact] public void The_measured_notes_say_what_the_owner_heard()
    {
        string baro = FenixOptionNotes.AppNote("efis_baro_linking_type")!;
        Assert.Contains("still land on the value you typed on both sides", baro);
        Assert.Contains("Linked Left+Right+Standby", baro);
        Assert.Contains("Linked Left+Right was not measured separately", baro);
        Assert.DoesNotContain("tested with Independent", baro);

        string fdLs = FenixOptionNotes.AppNote("fd_ls_sync_enabled")!;
        Assert.Contains("one press of FD or LS switches both sides", fdLs);
        Assert.Contains("checks the second side before it presses it, so its flows leave both on", fdLs);
        Assert.DoesNotContain("tested with this off", fdLs);

        string spacing = FenixOptionNotes.AppNote("channel_spacing_type")!;
        Assert.Contains("118.010 stayed 118.010", spacing);
        Assert.Contains("says what the RMP holds", spacing);
        Assert.DoesNotContain("may be changed", spacing);

        string readouts = FenixOptionNotes.AppNote("fcu_readouts_type")!;
        Assert.Contains("did not change with New", readouts);
        Assert.DoesNotContain("should not change", readouts);
    }

    [Fact] public void Encoder_acceleration_on_is_no_longer_a_warning()
    {
        // FNX-20 (2026-10-24): the typed FCU sets turn the knob in small checked steps, so they land either way.
        var on = LiveEleven().Select(s => s.Key == "encoder_acceleration_enabled" ? s with { Value = "True" } : s).ToArray();
        Assert.Empty(FenixOptionGrouping.Untested(on));
        Assert.Null(FenixOptionNotes.TestedValue("encoder_acceleration_enabled"));
        string note = FenixOptionNotes.AppNote("encoder_acceleration_enabled")!;
        Assert.Contains("small checked steps", note);
        Assert.Contains("altimeter entries are not affected", note);
        Assert.DoesNotContain("tested with this off", note);
    }

    [Fact] public void Status_texts()
    {
        Assert.Equal("Fenix 3.0.0.7426: 11 options. Read only: change options in the Fenix EFB.",
            FenixOptionGrouping.StatusText("3.0.0.7426", LiveEleven(), changesAllowed: false));
        var risky = LiveEleven().Select(s => s.Key switch
        {
            "encoder_acceleration_enabled" => s with { Value = "True" },
            "efis_baro_linking_type" => s with { Value = "Linked" },
            "control_position_type" => s with { Value = "FirstOfficer" },
            _ => s,
        }).ToList();
        // Encoder Acceleration on and Baro Linked are in the list but not warned about (FNX-20; measured 2026-10-24).
        Assert.Equal("Fenix version unknown: 11 options. Read only: change options in the Fenix EFB. Not what MSFS Blind Assist is tested with: "
            + "Control Position: First Officer.", FenixOptionGrouping.StatusText(null, risky, false));
        Assert.Equal("Fenix 3.0.0.7426: 11 options. F2 changes an on/off or choice option for this simulator session. "
            + "Shift+F2 makes an option permanent. Ctrl+F2 resets your change to the Fenix default.",
            FenixOptionGrouping.StatusText("3.0.0.7426", LiveEleven(), true));
        Assert.Equal("Fenix version unknown: 11 options. F2 changes an on/off or choice option for this simulator session. "
            + "Shift+F2 makes an option permanent. Ctrl+F2 resets your change to the Fenix default. "
            + "Not what MSFS Blind Assist is tested with: Control Position: First Officer.",
            FenixOptionGrouping.StatusText(null, risky, true));
        Assert.Equal("Refreshed, no changes.", FenixOptionGrouping.RefreshText(0));
        Assert.Equal("Refreshed, 1 option changed.", FenixOptionGrouping.RefreshText(1));
        Assert.Equal("Refreshed, 3 options changed.", FenixOptionGrouping.RefreshText(3));
    }

    [Theory] [InlineData("de-DE")] [InlineData("sv-SE")] [InlineData("en-US")]
    public void Order_and_texts_do_not_depend_on_the_culture(string culture)
    {
        var result = TestCulture.Run(culture, () => (
            FenixOptionGrouping.Group(LiveEleven()).Select(g => g.NodeText).ToArray(),
            FenixOptionGrouping.StatusText("3.0.0.7426", LiveEleven(), changesAllowed: false),
            FenixOptionGrouping.RefreshText(3)));
        Assert.Equal(new[] { "Options that affect MSFS Blind Assist (11)", "Aircraft (5)", "Misc (3)", "Realism (3)" }, result.Item1);
        Assert.Equal("Fenix 3.0.0.7426: 11 options. Read only: change options in the Fenix EFB.", result.Item2);
        Assert.Equal("Refreshed, 3 options changed.", result.Item3);
    }
}

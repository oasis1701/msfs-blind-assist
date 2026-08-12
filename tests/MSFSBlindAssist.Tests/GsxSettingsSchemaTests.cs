using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxSettingsSchemaTests
{
    private static GsxSettingsSchema Live()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-settings.json"));
        return GsxSettingsSchema.Parse(JsonDocument.Parse(json).RootElement.Clone());
    }

    [Fact]
    public void Parses_tabs_and_subtabs()
    {
        var s = Live();
        Assert.NotEmpty(s.Tabs);
        Assert.Contains(s.Tabs, t => t.Id == "simulation");
        var sim = s.Tabs.First(t => t.Id == "simulation");
        Assert.Contains(sim.Subtabs, st => st.Id == "services");
    }

    [Fact]
    public void Parses_a_toggle_with_label_and_tooltip()
    {
        var f = Live().AllFields().First(x => x.Key == "multiple_trips");
        Assert.Equal(GsxFieldType.Toggle, f.Type);
        Assert.Equal("Multiple trips", f.Label);
        Assert.False(string.IsNullOrWhiteSpace(f.Tooltip));
        Assert.Equal(1, f.NumericValue);
    }

    [Fact]
    public void Parses_a_choice_with_value_label_pairs()
    {
        var f = Live().AllFields().First(x => x.Key == "auto_reposition");
        Assert.Equal(GsxFieldType.Choice, f.Type);
        Assert.Equal(3, f.Choices.Count);
        Assert.Equal(0, f.Choices[0].Value);
        Assert.Equal("No", f.Choices[0].Label);
        Assert.Equal("Auto", f.Choices[2].Label);
    }

    [Fact]
    public void Parses_a_range_with_bounds_and_unit()
    {
        var f = Live().AllFields().First(x => x.Key == "pushback_speed_ms");
        Assert.Equal(GsxFieldType.Range, f.Type);
        Assert.Equal(1.5, f.Min);
        Assert.Equal(2.0, f.Max);
        Assert.Equal(0.025, f.Step);
        Assert.Equal("m/s", f.Unit);
        Assert.True(f.IsFloat);
    }

    [Fact]
    public void Parses_text_and_info_and_separator()
    {
        var all = Live().AllFields().ToList();

        var text = all.First(x => x.Type == GsxFieldType.Text);
        Assert.Equal("creator_nickname", text.Key);
        Assert.True(text.MaxLength > 0);

        var info = all.First(x => x.Type == GsxFieldType.Info);
        Assert.NotEmpty(info.Buttons);
        Assert.False(string.IsNullOrEmpty(info.Buttons[0].Key));

        Assert.Contains(all, x => x.Type == GsxFieldType.Separator);
    }

    [Fact]
    public void Unknown_field_type_is_Unknown_not_an_exception()
    {
        var s = GsxSettingsSchema.Parse(JsonDocument.Parse(
            """{"tabs":[{"id":"t","label":"T","subtabs":[{"id":"s","label":"S","fields":[{"type":"warpdrive","key":"k"}]}]}]}""")
            .RootElement);
        Assert.Equal(GsxFieldType.Unknown, s.AllFields().Single().Type);
    }

    [Fact]
    public void Missing_tabs_yields_empty_schema()
    {
        Assert.Empty(GsxSettingsSchema.Parse(JsonDocument.Parse("{}").RootElement).Tabs);
    }

    [Fact]
    public void Parses_tab_with_direct_fields_and_counts_all_81_fields()
    {
        var all = Live().AllFields().ToList();
        // Fixture has 42 fields in simulation subtabs + 39 direct on timings/audio/network/diagnostic tabs
        Assert.Equal(81, all.Count);

        // Verify direct tab-level fields are included
        Assert.Contains(all, x => x.Key == "menu_timeout"); // from timings tab
        Assert.Contains(all, x => x.Key == "audioVolume"); // from audio tab
    }

    [Fact]
    public void Parses_action_field_with_button_synthesized()
    {
        var all = Live().AllFields().ToList();
        var action = all.First(x => x.Type == GsxFieldType.Action);

        Assert.Equal("open_log_folder", action.Key);
        Assert.Equal("Diagnostic log", action.Label);
        Assert.NotEmpty(action.Buttons);
        Assert.Equal("open_log_folder", action.Buttons[0].Key);
        Assert.Equal("Open Log", action.Buttons[0].Label);
    }

    [Fact]
    public void Parses_choice_with_numeric_label_without_throwing()
    {
        var s = GsxSettingsSchema.Parse(JsonDocument.Parse(
            """{"tabs":[{"id":"t","label":"T","fields":[{"type":"choice","key":"c","choices":[[0,"Zero"],[1,100]]}]}]}""")
            .RootElement);
        var field = s.AllFields().Single();

        Assert.Equal(GsxFieldType.Choice, field.Type);
        // First choice has string label
        Assert.Equal("Zero", field.Choices[0].Label);
        // Second choice has numeric label, should be skipped or empty
        Assert.Equal("", field.Choices[1].Label);
    }

    [Fact]
    public void Range_missing_min_leaves_Min_as_null()
    {
        var s = GsxSettingsSchema.Parse(JsonDocument.Parse(
            """{"tabs":[{"id":"t","label":"T","fields":[{"type":"range","key":"r","max":100}]}]}""")
            .RootElement);
        var field = s.AllFields().Single();

        Assert.Equal(GsxFieldType.Range, field.Type);
        Assert.Null(field.Min);
        Assert.Equal(100, field.Max);
    }
}

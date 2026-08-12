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
}

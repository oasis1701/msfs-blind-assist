using System.Text.Json;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// GsxSettingsForm.RefreshSchema decides between two very different reactions to a
/// republished settings tree by comparing STRUCTURAL signatures: equal means "apply the
/// new values into the existing controls in place, touch nothing else"; different means
/// "dispose every control and rebuild the pages" (which moves screen-reader focus to the
/// sections list -- unavoidable when the control the pilot was on no longer exists).
///
/// The split exists because GSX ECHOES every settings.set back as a /settings patch. The
/// original signature folded each field's live NumericValue/TextValue in, so EVERY
/// adjustment -- a checkbox tick, a combo pick, each arrow-step of a NumericUpDown -- came
/// straight back as a "changed" schema, rebuilt the whole window, and yanked focus off the
/// field the pilot was still adjusting. Range controls were effectively unusable.
///
/// The structural signature is built from the SAME BuildPages traversal the form renders
/// from, so "the structure changed" and "the rendered content changed" cannot drift apart
/// -- these tests pin both halves. Internal type, reached via InternalsVisibleTo
/// (Properties/InternalsVisibleTo.cs), same pattern as GsxRangeBoundsResolverTests.
/// </summary>
public class GsxSettingsSchemaSignatureTests
{
    private static GsxSettingsSchema Schema(params string[] fieldsJson) =>
        GsxSettingsSchema.Parse(JsonDocument.Parse(
            $$"""{"tabs":[{"id":"t","label":"Tab","fields":[{{string.Join(",", fieldsJson)}}]}]}""")
            .RootElement);

    private static GsxSettingsSchema Live()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-settings.json"));
        return GsxSettingsSchema.Parse(JsonDocument.Parse(json).RootElement.Clone());
    }

    private const string Toggle = """{"type":"toggle","key":"multiple_trips","label":"Multiple trips","tooltip":"tip","value":1}""";
    private const string Range = """{"type":"range","key":"pushback_speed_ms","label":"Pushback speed","min":1.5,"max":2.0,"step":0.025,"unit":"m/s","float":true,"value":1.75}""";
    private const string Choice = """{"type":"choice","key":"auto_reposition","label":"Auto reposition","choices":[[0,"No"],[1,"Ask"],[2,"Auto"]],"value":1}""";
    private const string Text = """{"type":"text","key":"creator_nickname","label":"Nickname","maxlength":32,"placeholder":"name","value":"robin"}""";
    private const string Info = """{"type":"info","label":"Log file location","value":"C:\\logs","buttons":[{"key":"open_log","button":"Open Log","disabled":false}]}""";

    // ── (i) value-only changes are NOT structural ─────────────────────────

    [Fact]
    public void Toggle_value_change_keeps_structural_signature_equal()
    {
        var before = Schema(Toggle);
        var after = Schema(Toggle.Replace("\"value\":1", "\"value\":0"));

        Assert.NotEqual(before.AllFields().Single().NumericValue, after.AllFields().Single().NumericValue);
        Assert.Equal(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Range_value_change_keeps_structural_signature_equal()
    {
        // The exact live failure: each arrow-step of a NumericUpDown sends
        // settings.set, GSX echoes the new value, and the echoed schema must
        // NOT read as a rebuild.
        var before = Schema(Range);
        var after = Schema(Range.Replace("\"value\":1.75", "\"value\":1.775"));

        Assert.Equal(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Choice_value_change_keeps_structural_signature_equal()
    {
        var before = Schema(Choice);
        var after = Schema(Choice.Replace("\"value\":1", "\"value\":2"));

        Assert.Equal(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Text_value_change_keeps_structural_signature_equal()
    {
        var before = Schema(Text);
        var after = Schema(Text.Replace("\"value\":\"robin\"", "\"value\":\"kipp\""));

        Assert.Equal(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Info_value_change_keeps_structural_signature_equal()
    {
        // Toggling "diagnostic log" makes GSX republish the "Log file location"
        // info field with a new value -- a value update to a read-only box, not
        // a reason to rebuild.
        var before = Schema(Info);
        var after = Schema(Info.Replace("C:\\\\logs", "D:\\\\other"));

        Assert.NotEqual(before.AllFields().Single().TextValue, after.AllFields().Single().TextValue);
        Assert.Equal(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Value_only_change_across_a_whole_multi_field_page_keeps_structural_signature_equal()
    {
        var before = Schema(Toggle, Range, Choice, Text, Info);
        var after = Schema(
            Toggle.Replace("\"value\":1", "\"value\":0"),
            Range.Replace("\"value\":1.75", "\"value\":2.0"),
            Choice.Replace("\"value\":1", "\"value\":0"),
            Text.Replace("\"value\":\"robin\"", "\"value\":\"\""),
            Info.Replace("C:\\\\logs", ""));

        Assert.Equal(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    // ── (ii) structural changes ARE structural ────────────────────────────

    [Fact]
    public void Label_change_alters_structural_signature()
    {
        var before = Schema(Toggle);
        var after = Schema(Toggle.Replace("Multiple trips", "Several trips"));

        Assert.NotEqual(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Min_change_alters_structural_signature()
    {
        var before = Schema(Range);
        var after = Schema(Range.Replace("\"min\":1.5", "\"min\":1.0"));

        Assert.NotEqual(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Max_change_alters_structural_signature()
    {
        var before = Schema(Range);
        var after = Schema(Range.Replace("\"max\":2.0", "\"max\":3.0"));

        Assert.NotEqual(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Choices_change_alters_structural_signature()
    {
        var before = Schema(Choice);
        var after = Schema(Choice.Replace("[2,\"Auto\"]", "[2,\"Auto\"],[3,\"Always\"]"));

        Assert.NotEqual(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Button_disabled_change_alters_structural_signature()
    {
        // A button's enabled state is rendered as Button.Enabled at build time,
        // so it has to count as structure -- a value-only apply could not flip it.
        var before = Schema(Info);
        var after = Schema(Info.Replace("\"disabled\":false", "\"disabled\":true"));

        Assert.NotEqual(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Field_type_change_alters_structural_signature()
    {
        var before = Schema(Toggle);
        var after = Schema(Toggle.Replace("\"type\":\"toggle\"", "\"type\":\"info\""));

        Assert.NotEqual(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    [Fact]
    public void Page_title_change_alters_structural_signature()
    {
        var before = GsxSettingsSchema.Parse(JsonDocument.Parse(
            $$"""{"tabs":[{"id":"t","label":"Tab","fields":[{{Toggle}}]}]}""").RootElement);
        var after = GsxSettingsSchema.Parse(JsonDocument.Parse(
            $$"""{"tabs":[{"id":"t","label":"Other tab","fields":[{{Toggle}}]}]}""").RootElement);

        Assert.NotEqual(GsxSettingsSchemaSignature.Structural(before), GsxSettingsSchemaSignature.Structural(after));
    }

    // ── (iii) nothing renderable vs something renderable ─────────────────

    [Fact]
    public void Empty_schema_and_populated_schema_have_different_structural_signatures()
    {
        // The empty -> populated transition is what AccessGSXForm announces as
        // "GSX settings loaded." -- it must read as a rebuild, never a
        // value-only apply.
        Assert.NotEqual(
            GsxSettingsSchemaSignature.Structural(GsxSettingsSchema.Empty),
            GsxSettingsSchemaSignature.Structural(Schema(Toggle)));
    }

    [Fact]
    public void Two_empty_schemas_have_equal_structural_signatures()
    {
        Assert.Equal(
            GsxSettingsSchemaSignature.Structural(GsxSettingsSchema.Empty),
            GsxSettingsSchemaSignature.Structural(GsxSettingsSchema.Parse(JsonDocument.Parse("{}").RootElement)));
    }

    [Fact]
    public void Section_holding_only_separators_and_unknowns_yields_no_page()
    {
        // BuildPages contributes a page only when at least one field renders a
        // control (AppendField skips Separator and Unknown) -- otherwise the
        // pilot could select a tab that shows nothing.
        var schema = Schema(
            """{"type":"separator","label":"Heading"}""",
            """{"type":"warpdrive","key":"k","label":"Mystery"}""");

        Assert.Empty(GsxSettingsSchemaSignature.BuildPages(schema));
        Assert.Equal(
            GsxSettingsSchemaSignature.Structural(GsxSettingsSchema.Empty),
            GsxSettingsSchemaSignature.Structural(schema));
    }

    // ── BuildPages is the traversal the form renders from ─────────────────

    [Fact]
    public void BuildPages_renders_both_subtab_and_direct_tab_shapes_from_the_live_capture()
    {
        // The live capture has 5 top-level tabs: "simulation" splits its fields
        // across 4 subtabs (Services/Pushback/Parking/UI); "timings", "audio",
        // "network" and "diagnostic" carry theirs directly on the tab. An
        // earlier reader walked subtabs only and silently dropped four whole
        // tabs of settings.
        var pages = GsxSettingsSchemaSignature.BuildPages(Live());

        Assert.Equal(8, pages.Count);
        Assert.Contains(pages, p => p.Title == "Simulation - Services");
        Assert.Contains(pages, p => p.Title == "Simulation - UI");
        Assert.Contains(pages, p => p.Title == "Timings");
        Assert.Contains(pages, p => p.Title == "Diagnostic");
        // Every one of the 81 parsed fields lands on exactly one page.
        Assert.Equal(81, pages.Sum(p => p.Fields.Count));
    }

    [Fact]
    public void Live_capture_structural_signature_is_deterministic_and_value_independent()
    {
        var a = Live();
        var b = Live();
        Assert.Equal(GsxSettingsSchemaSignature.Structural(a), GsxSettingsSchemaSignature.Structural(b));

        // Step one live range value on the wire (pushback_speed_ms 2.0 -> 1.975,
        // exactly one NumericUpDown arrow-step) and re-parse: still
        // structurally identical.
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-settings.json"));
        string mutated = System.Text.RegularExpressions.Regex.Replace(
            json, "(\"key\": \"pushback_speed_ms\",[\\s\\S]*?\"value\": )2\\.0", "${1}1.975");
        Assert.NotEqual(json, mutated);
        var c = GsxSettingsSchema.Parse(JsonDocument.Parse(mutated).RootElement.Clone());
        Assert.Equal(1.975, c.AllFields().First(f => f.Key == "pushback_speed_ms").NumericValue);
        Assert.Equal(GsxSettingsSchemaSignature.Structural(a), GsxSettingsSchemaSignature.Structural(c));
    }
}

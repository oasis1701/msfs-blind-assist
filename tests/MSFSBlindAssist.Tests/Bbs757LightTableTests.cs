using System.Text.Json;
using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Plan 13's light table: every light is one of the 757's own (the lights fixture, made once from
/// the 518 light nodes of BlueBird's model XML), none twice, no backlight or test light, and the watch set
/// leaves out what another row already announces and every momentary button.
/// </summary>
public class Bbs757LightTableTests
{
    private static HashSet<string> FixtureLights()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-lights.json"));
        return new HashSet<string>(JsonSerializer.Deserialize<string[]>(json)!, StringComparer.Ordinal);
    }

    [Fact]
    public void Table_has_no_duplicates_and_every_lvar_is_in_the_lights_fixture()
    {
        var all = Bbs757LightTable.All;
        var fixture = FixtureLights();

        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(l => l.LVar).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(all.Count, all.Select(l => l.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(all, l => Assert.Contains(l.LVar, fixture));
        Assert.All(all, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.Label));
            Assert.False(string.IsNullOrWhiteSpace(l.OnPhrase));
            Assert.DoesNotContain(":", l.LVar);
        });
    }

    [Fact]
    public void No_backlight_or_test_light()
    {
        Assert.All(Bbs757LightTable.All, l =>
        {
            Assert.DoesNotContain("BRIGHTNESS", l.LVar);
            Assert.DoesNotContain("LIGHTING", l.LVar);
            Assert.DoesNotContain("TEST", l.LVar);
            Assert.DoesNotContain("SQUIB", l.LVar);
            Assert.DoesNotContain("BULB_FAILED", l.LVar);
        });
    }

    [Fact]
    public void Watch_set_skips_names_announced_elsewhere()
    {
        var lights = new[]
        {
            new Bbs757Light("LIGHT_A", "A", "A light on", "A light off"),
            new Bbs757Light("LIGHT_B", "B", "B light on", null),
        };
        var controls = new[]
        {
            Control("SWITCH_X", Bbs757Kinds.Switch, "SWITCH_X_STATE"),
            Control("SWITCH_Y", Bbs757Kinds.Toggle, "SWITCH_Y_STATE"),
        };
        var elsewhere = new HashSet<string>(StringComparer.Ordinal) { "LIGHT_B", "SWITCH_Y_STATE" };

        var set = Bbs757LightTable.WatchSet(lights, controls, elsewhere);

        Assert.Equal(new[] { "LIGHT_A", "SWITCH_X_STATE" }, set.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Momentary_buttons_are_not_watched()
    {
        var controls = new[]
        {
            Control("PUSH", Bbs757Kinds.Button, "PUSH_STATE"),
            Control("HOLD", Bbs757Kinds.Held, null),
            Control("NO_STATE", Bbs757Kinds.Switch, null),
            Control("COVER", Bbs757Kinds.Guard, "COVER_STATE"),
            Control("KNOB", Bbs757Kinds.Switch, "KNOB_STATE"),
        };

        var set = Bbs757LightTable.WatchSet(Array.Empty<Bbs757Light>(), controls, new HashSet<string>());

        Assert.Equal(new[] { "COVER_STATE", "KNOB_STATE" }, set.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void A_light_and_a_switch_sharing_a_var_are_watched_once()
    {
        var lights = new[] { new Bbs757Light("SHARED", "S", "S light on", null) };
        var controls = new[] { Control("SW", Bbs757Kinds.Toggle, "SHARED") };

        Assert.Equal(new[] { "SHARED" }, Bbs757LightTable.WatchSet(lights, controls, new HashSet<string>()));
    }

    [Fact]
    public void The_real_watch_set_leaves_out_the_definitions_announced_rows()
    {
        var def = new MSFSBlindAssist.Aircraft.BBS757Definition();
        var announced = def.GetVariables().Values.Where(v => v.IsAnnounced).Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(announced);

        var set = Bbs757LightTable.WatchSet(Bbs757LightTable.All, Bbs757ControlMap.Load().Controls, announced);

        Assert.NotEmpty(set);
        Assert.DoesNotContain(set, announced.Contains);
        // The captain's master caution is a status row the definition announces already.
        Assert.DoesNotContain("BBS_CA_MASTER_CAUTION", set);
    }

    internal static Bbs757Control Control(string node, string kind, string? stateVar) => new()
    {
        Node = node, Kind = kind, StateVar = stateVar, Panel = "Test panel", Label = node,
        Positions = new() { new(0, "OFF"), new(1, "ON") },
    };
}

using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Plan 13's cockpit-wide announcements: watched changes into speech (a light's phrase, a switch's
/// label and position), never the pilot's own pick, a preset's burst summed up in one line with
/// every light still said, silence until the flight has settled, and the Ctrl+M rows and mutes.
/// </summary>
public class Bbs757CockpitAnnouncementsTests
{
    private long _now = 100_000;

    private static readonly Bbs757Light ApuFault = new("APU_FAULT", "APU fault", "APU fault light on", "APU fault light off");
    private static readonly Bbs757Light ApuRun = new("APU_RUN", "APU run", "APU run light on", null);

    private static Bbs757Control Switch(string node, string panel, string label, params (double Value, string Name)[] positions) => new()
    {
        Node = node, Kind = Bbs757Kinds.Switch, StateVar = node + "_STATE", Panel = panel, Label = label,
        Positions = positions.Select(p => new Bbs757Position(p.Value, p.Name)).ToList(),
    };

    private static readonly Bbs757Control Seatbelts = Switch("SEATBELTS", "Signs", "Seatbelts", (0, "OFF"), (1, "AUTO"), (2, "ON"));
    private static readonly Bbs757Control LPack = Switch("L_PACK", "Air conditioning", "L Pack Selector", (0, "OFF"), (1, "AUTO"));

    private Bbs757CockpitAnnouncements Make(IReadOnlyList<Bbs757Control>? controls = null)
    {
        var a = new Bbs757CockpitAnnouncements(new[] { ApuFault, ApuRun }, controls ?? new[] { Seatbelts, LPack }, () => _now);
        a.Settle();
        _now += Bbs757CockpitAnnouncements.SettleQuietMs;
        return a;
    }

    private static IReadOnlyDictionary<string, double> Of(params (string Name, double Value)[] changes) =>
        changes.ToDictionary(c => c.Name, c => c.Value);

    private static bool None(string _) => false;

    [Fact]
    public void Light_phrase()
    {
        var a = Make();

        Assert.Equal(new[] { "APU fault light on" }, a.Changes(Of(("APU_FAULT", 1)), None, None));
        Assert.Equal(new[] { "APU fault light off" }, a.Changes(Of(("APU_FAULT", 0)), None, None));
        // An indication says nothing going out; a brighter or dimmer light that stays on says nothing.
        Assert.Equal(new[] { "APU run light on" }, a.Changes(Of(("APU_RUN", 1)), None, None));
        Assert.Empty(a.Changes(Of(("APU_RUN", 0.6)), None, None));
        Assert.Empty(a.Changes(Of(("APU_RUN", 0)), None, None));
    }

    [Fact]
    public void Switch_phrase_from_map_positions()
    {
        var a = Make();

        Assert.Equal(new[] { "Seatbelts AUTO" }, a.Changes(Of(("SEATBELTS_STATE", 1)), None, None));
        Assert.Equal(new[] { "L Pack Selector OFF" }, a.Changes(Of(("L_PACK_STATE", 0)), None, None));
        // A value with no named position says the number, invariant.
        Assert.Equal(new[] { "Seatbelts 1.5" }, a.Changes(Of(("SEATBELTS_STATE", 1.5)), None, None));
    }

    [Fact]
    public void Switch_number_is_invariant_under_a_comma_culture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var a = Make();
            Assert.Equal(new[] { "Seatbelts 1.5" }, a.Changes(Of(("SEATBELTS_STATE", 1.5)), None, None));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Own_write_not_repeated()
    {
        var a = Make();

        a.OwnWrite("SEATBELTS");
        _now += 1000;
        Assert.Empty(a.Changes(Of(("SEATBELTS_STATE", 2)), None, None));

        // Past the echo window, a move from elsewhere is said.
        _now += Bbs757CockpitAnnouncements.OwnWriteEchoMs;
        Assert.Equal(new[] { "Seatbelts OFF" }, a.Changes(Of(("SEATBELTS_STATE", 0)), None, None));
    }

    [Fact]
    public void Pending_pick_not_repeated()
    {
        var a = Make();

        Assert.Empty(a.Changes(Of(("SEATBELTS_STATE", 2)), node => node == "SEATBELTS", None));
        Assert.Equal(new[] { "L Pack Selector OFF" }, a.Changes(Of(("L_PACK_STATE", 0)), node => node == "SEATBELTS", None));
    }

    [Fact]
    public void Burst_is_summarized_but_lights_still_speak()
    {
        var controls = Enumerable.Range(1, 40).Select(i => Switch("SW" + i, "Panel", "Switch " + i, (0, "OFF"), (1, "ON"))).ToList();
        var a = Make(controls);
        var changes = controls.ToDictionary(c => c.StateVar!, _ => 1.0);
        changes["APU_FAULT"] = 1;

        var said = a.Changes(changes, None, None);

        Assert.Equal(new[] { "Panel state changed: 40 switches", "APU fault light on" }, said);
    }

    [Fact]
    public void A_burst_spread_over_two_reads_within_a_second_is_one_summary_each_read()
    {
        var controls = Enumerable.Range(1, 8).Select(i => Switch("SW" + i, "Panel", "Switch " + i, (0, "OFF"), (1, "ON"))).ToList();
        var a = Make(controls);

        var first = a.Changes(controls.Take(4).ToDictionary(c => c.StateVar!, _ => 1.0), None, None);
        _now += 500;
        var second = a.Changes(controls.Skip(4).ToDictionary(c => c.StateVar!, _ => 1.0), None, None);

        Assert.Equal(4, first.Count);   // five or fewer: each said
        Assert.Equal(new[] { "Panel state changed: 4 switches" }, second);
    }

    [Fact]
    public void First_sighting_is_silent()
    {
        var a = new Bbs757CockpitAnnouncements(new[] { ApuFault }, new[] { Seatbelts }, () => _now);

        // Before the flight has settled (a load flood), nothing is said.
        Assert.Empty(a.Changes(Of(("APU_FAULT", 1), ("SEATBELTS_STATE", 2)), None, None));
        a.Settle();
        Assert.Empty(a.Changes(Of(("SEATBELTS_STATE", 1)), None, None));   // still inside the quiet time
        _now += Bbs757CockpitAnnouncements.SettleQuietMs;
        Assert.Equal(new[] { "Seatbelts OFF" }, a.Changes(Of(("SEATBELTS_STATE", 0)), None, None));

        // A flight load starts over.
        a.Reset();
        _now += Bbs757CockpitAnnouncements.SettleQuietMs;
        Assert.Empty(a.Changes(Of(("SEATBELTS_STATE", 2)), None, None));
    }

    [Fact]
    public void Muted_row_silent()
    {
        var a = Make();
        string muted = Bbs757CockpitAnnouncements.RowKey("APU_FAULT");

        Assert.Empty(a.Changes(Of(("APU_FAULT", 1)), None, k => k == muted));
        Assert.Equal(new[] { "Seatbelts ON" }, a.Changes(Of(("SEATBELTS_STATE", 2)), None, k => k == muted));
    }

    [Fact]
    public void Watched_switches_leave_out_what_other_rows_speak_and_keep_silent_rows()
    {
        var def = new MSFSBlindAssist.Aircraft.BBS757Definition();
        var names = def.CockpitWatched().Switches.Select(c => c.StateVar).ToHashSet(StringComparer.Ordinal);

        // Spoken already under another variable: the Flaps readout, the gear call-out, the parking
        // brake readout, the A/T armed announcement.
        Assert.DoesNotContain("B757_FC_FLAP_LEVER", names);
        Assert.DoesNotContain("B757_GEAR_HANDLE_POS", names);
        Assert.DoesNotContain("B757_FC_PARK_BRAKE", names);
        Assert.DoesNotContain("AT_ARM_SWITCH", names);
        // A silent row (the IRS switch watch, read only to phrase the align light) speaks nothing of
        // the selector itself, so the selector is watched.
        Assert.Contains("B757_IRS_L_SWITCH", names);
    }

    [Fact]
    public void A_flood_of_lights_is_one_line()
    {
        var lights = Enumerable.Range(1, 12).Select(i => new Bbs757Light("L" + i, "Light " + i, $"Light {i} light on", $"Light {i} light off")).ToList();
        var a = new Bbs757CockpitAnnouncements(lights, new[] { Seatbelts }, () => _now);
        a.Settle();
        _now += Bbs757CockpitAnnouncements.SettleQuietMs;
        var changes = lights.Take(9).ToDictionary(l => l.LVar, _ => 1.0);
        changes["SEATBELTS_STATE"] = 2;

        Assert.Equal(new[] { "Seatbelts ON", "9 lights on" }, a.Changes(changes, None, None));

        // Going out together, mixed with one coming on, within the window.
        _now += 300;
        var mixed = lights.Take(9).ToDictionary(l => l.LVar, _ => 0.0);
        mixed["L10"] = 1;
        Assert.Equal(new[] { "1 light on, 9 off" }, a.Changes(mixed, None, None));

        // A few lights later on are each said again.
        _now += Bbs757CockpitAnnouncements.BurstWindowMs;
        Assert.Equal(new[] { "Light 11 light on" }, a.Changes(Of(("L11", 1)), None, None));
    }

    [Fact]
    public void All_lights_row_mutes_the_light_summary()
    {
        var lights = Enumerable.Range(1, 8).Select(i => new Bbs757Light("L" + i, "Light " + i, $"Light {i} light on", null)).ToList();
        var a = new Bbs757CockpitAnnouncements(lights, Array.Empty<Bbs757Control>(), () => _now);
        a.Settle();
        _now += Bbs757CockpitAnnouncements.SettleQuietMs;

        Assert.Empty(a.Changes(lights.ToDictionary(l => l.LVar, _ => 1.0), None, k => k == Bbs757CockpitAnnouncements.AllLightsKey));
    }

    [Fact]
    public void Unknown_names_are_ignored()
    {
        Assert.Empty(Make().Changes(Of(("SOMETHING_ELSE", 1)), None, None));
    }

    // ---- Task 5: Ctrl+M ----

    [Fact]
    public void Rows_have_unique_keys_and_prefixed_labels()
    {
        var rows = Bbs757CockpitAnnouncements.MonitorRows(new[] { ApuFault, ApuRun }, new[] { Seatbelts, LPack });

        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, r => Assert.StartsWith("BBS757W_", r.Key));
        Assert.Contains(rows, r => r.Label == "Light: APU fault" && r.Key == "BBS757W_APU_FAULT");
        Assert.Contains(rows, r => r.Label == "Switch: Signs, Seatbelts" && r.Key == "BBS757W_SEATBELTS_STATE");
    }

    [Fact]
    public void Group_rows_come_first()
    {
        var rows = Bbs757CockpitAnnouncements.MonitorRows(new[] { ApuFault }, new[] { Seatbelts, LPack });

        Assert.Equal("All Annunciator Lights", rows[0].Label);
        Assert.Equal(Bbs757CockpitAnnouncements.AllLightsKey, rows[0].Key);
        Assert.Equal("All Switches From Any Source", rows[1].Label);
        Assert.Equal(Bbs757CockpitAnnouncements.AllSwitchesKey, rows[1].Key);
        Assert.Equal(new[] { "Switches: Air conditioning", "Switches: Signs" }, rows.Skip(2).Take(2).Select(r => r.Label));
        Assert.StartsWith("Light: ", rows[4].Label);
    }

    [Fact]
    public void Panel_row_mutes_its_switches()
    {
        var a = Make();
        string panelKey = Bbs757CockpitAnnouncements.PanelKey("Signs");

        Assert.Empty(a.Changes(Of(("SEATBELTS_STATE", 2)), None, k => k == panelKey));
        Assert.Equal(new[] { "L Pack Selector OFF" }, a.Changes(Of(("L_PACK_STATE", 0)), None, k => k == panelKey));
        Assert.True(Bbs757CockpitAnnouncements.IsMuted("SEATBELTS_STATE", false, "Signs", k => k == panelKey));
        Assert.False(Bbs757CockpitAnnouncements.IsMuted("L_PACK_STATE", false, "Air conditioning", k => k == panelKey));
    }

    [Fact]
    public void All_lights_row_mutes_every_light_but_no_switch()
    {
        var a = Make();
        Func<string, bool> muted = k => k == Bbs757CockpitAnnouncements.AllLightsKey;

        Assert.Equal(new[] { "Seatbelts ON" }, a.Changes(Of(("APU_FAULT", 1), ("SEATBELTS_STATE", 2)), None, muted));
    }

    [Fact]
    public void All_switches_row_mutes_the_burst_summary()
    {
        var controls = Enumerable.Range(1, 10).Select(i => Switch("SW" + i, "Panel", "Switch " + i, (0, "OFF"), (1, "ON"))).ToList();
        var a = Make(controls);
        var changes = controls.ToDictionary(c => c.StateVar!, _ => 1.0);
        changes["APU_FAULT"] = 1;

        var said = a.Changes(changes, None, k => k == Bbs757CockpitAnnouncements.AllSwitchesKey);

        Assert.Equal(new[] { "APU fault light on" }, said);
    }

    [Fact]
    public void Rows_appended_after_todays_rows()
    {
        var def = new MSFSBlindAssist.Aircraft.BBS757Definition();
        var rows = def.MonitorRows();
        var today = MSFSBlindAssist.Services.MonitorRowBuilder.Build(def.GetVariables());

        int first = rows.ToList().FindIndex(r => r.Key == Bbs757CockpitAnnouncements.AllLightsKey);
        Assert.True(first >= today.Count, "the cockpit rows come after the variables' rows");
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(rows, r => r.Label.StartsWith("Light: ", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Label.StartsWith("Switch: ", StringComparison.Ordinal));
        // A light the definition announces already gets no second row.
        Assert.DoesNotContain(rows, r => r.Key == Bbs757CockpitAnnouncements.RowKey("BBS_CA_MASTER_CAUTION"));
    }
}

// PMDG 737 annunciator flash filter. Why it exists lives on Pmdg737FlashingLights; this file pins
// the timing (edge, hold, flashing, settle) and the definition's wiring of it.

using MSFSBlindAssist.Aircraft;
using Kind = MSFSBlindAssist.Aircraft.Pmdg737FlashingLights.Kind;

namespace MSFSBlindAssist.Tests;

public class Pmdg737FlashingLightsTests
{
    private const string Align = "IRS_annunALIGN_0";
    private const int Hold = Pmdg737FlashingLights.DefaultHoldMs;

    private readonly Pmdg737FlashingLights _f = new();
    private readonly List<(string Key, Kind Kind)> _said = new();

    private void Set(string key, bool lit, long t)
    {
        if (_f.OnValue(key, lit, t) is { } c) _said.Add((c.Key, c.Kind));
    }

    private void Tick(long t)
    {
        foreach (var c in _f.Tick(t)) _said.Add((c.Key, c.Kind));
    }

    // Blinks from `from` to `to` with the given half-period, ticking every 250 ms like the definition.
    private long Blink(string key, long from, long to, int halfPeriodMs, bool startLit = true)
    {
        bool lit = startLit;
        long t = from;
        for (; t < to; t += halfPeriodMs)
        {
            Set(key, lit, t);
            for (long k = t; k < t + halfPeriodMs; k += 250) Tick(k);
            lit = !lit;
        }
        return t;
    }

    [Fact]
    public void On_is_spoken_at_once_at_the_first_lit_edge()
    {
        Set(Align, true, 0);
        Assert.Equal(new[] { (Align, Kind.On) }, _said);
    }

    [Fact]
    public void An_off_edge_is_held_and_spoken_only_after_the_hold()
    {
        Set(Align, true, 0);
        Tick(5000);                       // on long enough to be steady
        Set(Align, false, 6000);
        Tick(6000 + Hold - 1);
        Assert.Equal(new[] { (Align, Kind.On) }, _said);
        Tick(6000 + Hold);
        Assert.Equal(new[] { (Align, Kind.On), (Align, Kind.Off) }, _said);
    }

    [Fact]
    public void A_light_blinking_once_a_second_says_on_then_flashing_once_and_nothing_more()
    {
        Blink(Align, 0, 20_000, 500);
        Assert.Equal(new[] { (Align, Kind.On), (Align, Kind.Flashing) }, _said);
        Assert.Equal(new[] { Align }, _f.FlashingKeys);
    }

    [Fact]
    public void One_relight_inside_the_hold_is_not_flashing_and_stays_silent()
    {
        Set(Align, true, 0);
        Tick(5000);
        Set(Align, false, 5000);
        Set(Align, true, 5500);           // a single blip off
        Tick(10_000);
        Assert.Equal(new[] { (Align, Kind.On) }, _said);
        Assert.Empty(_f.FlashingKeys);
    }

    [Fact]
    public void A_flashing_light_that_stays_lit_settles_steady_and_clears_the_mark()
    {
        long t = Blink(Align, 0, 10_000, 500);   // ends having just gone dark, so relight it
        Set(Align, true, t);
        Tick(t + Hold - 1);
        Assert.Equal(new[] { Align }, _f.FlashingKeys);
        Tick(t + Hold);
        Assert.Equal(new[] { (Align, Kind.On), (Align, Kind.Flashing), (Align, Kind.Steady) }, _said);
        Assert.Empty(_f.FlashingKeys);
        Assert.False(_f.HasPending);
    }

    [Fact]
    public void A_flashing_light_that_stays_dark_settles_off()
    {
        long t = Blink(Align, 0, 10_000, 500);
        Set(Align, false, t);
        Tick(t + Hold);
        Assert.Equal(new[] { (Align, Kind.On), (Align, Kind.Flashing), (Align, Kind.Off) }, _said);
        Assert.Empty(_f.FlashingKeys);
    }

    [Fact]
    public void A_steady_light_that_starts_flashing_says_flashing_once()
    {
        Set(Align, true, 0);
        Tick(5000);
        Blink(Align, 5000, 15_000, 500, startLit: false);
        Assert.Equal(new[] { (Align, Kind.On), (Align, Kind.Flashing) }, _said);
    }

    [Fact]
    public void A_light_first_seen_going_dark_is_held_then_spoken_off()
    {
        // It was lit in the connect snapshot, which never reaches the filter.
        Set(Align, false, 0);
        Tick(Hold);
        Assert.Equal(new[] { (Align, Kind.Off) }, _said);
    }

    [Fact]
    public void A_slower_blink_than_the_default_hold_needs_its_override()
    {
        // 3 s on, 3 s off: each dark spell outlasts the 2 s default, so it reads as on/off.
        Blink(Align, 0, 18_000, 3000);
        Assert.Contains((Align, Kind.Off), _said);
        Assert.DoesNotContain((Align, Kind.Flashing), _said);

        var slow = new Pmdg737FlashingLights(new Dictionary<string, int> { [Align] = 4000 });
        var said = new List<Kind>();
        bool lit = true;
        for (long t = 0; t < 18_000; t += 3000, lit = !lit)
        {
            if (slow.OnValue(Align, lit, t) is { } c) said.Add(c.Kind);
            for (long k = t; k < t + 3000; k += 250) said.AddRange(slow.Tick(k).Select(x => x.Kind));
        }
        Assert.Equal(new[] { Kind.On, Kind.Flashing }, said);
        Assert.Equal(4000, slow.HoldMsFor(Align));
        Assert.Equal(Hold, slow.HoldMsFor("IRS_annunALIGN_1"));
    }

    [Fact]
    public void Each_light_is_tracked_on_its_own()
    {
        Blink(Align, 0, 10_000, 500);
        Set("IRS_annunGPS", true, 10_000);
        Assert.Equal((("IRS_annunGPS", Kind.On)), _said[^1]);
        Assert.Equal(new[] { Align }, _f.FlashingKeys);
    }

    [Fact]
    public void Reset_forgets_flashing_and_pending_lights()
    {
        Blink(Align, 0, 10_000, 500);
        _f.Reset();
        Assert.Empty(_f.FlashingKeys);
        Assert.False(_f.HasPending);
    }

    [Fact]
    public void The_list_has_one_row_per_flashing_light_or_says_none()
    {
        Assert.Equal(new[] { "No lights flashing" }, Pmdg737FlashingLights.ListRows(Array.Empty<string>()));
        Assert.Equal(new[] { "IRS Left ALIGN", "IRS Right ALIGN" },
            Pmdg737FlashingLights.ListRows(new[] { "IRS Left ALIGN", "IRS Right ALIGN" }));
    }

    // IRS ALIGN edges measured 2026-10-13 (debug.log PMDG737Lamps, no IRS position entered), in ms
    // from the first lit edge at 14:31:54.629: one blink every 5 s, dark spells of 2 to 3 s.
    private static readonly (long T, bool Lit)[] MeasuredAlign =
    {
        (0, true), (3000, false), (5004, true), (8005, false), (10018, true), (12999, false),
        (16002, true), (18004, false), (21013, true), (23022, false), (26023, true),
    };

    private static List<Kind> Replay(Pmdg737FlashingLights f, (long T, bool Lit)[] edges)
    {
        var said = new List<Kind>();
        long end = edges[^1].T + 1000;
        int next = 0;
        for (long t = 0; t <= end; t += 250)
        {
            while (next < edges.Length && edges[next].T <= t)
            {
                if (f.OnValue(Align, edges[next].Lit, edges[next].T) is { } c) said.Add(c.Kind);
                next++;
            }
            said.AddRange(f.Tick(t).Select(c => c.Kind));
        }
        return said;
    }

    [Fact]
    public void The_measured_IRS_ALIGN_blink_was_spoken_on_off_with_the_2_s_default()
    {
        // What the pilot heard on the first in-sim test: each 2-3 s dark spell outlasted the hold.
        Assert.Contains(Kind.Off, Replay(new Pmdg737FlashingLights(), MeasuredAlign));
    }

    [Fact]
    public void The_measured_IRS_ALIGN_blink_says_on_then_flashing_with_its_override()
    {
        var f = new Pmdg737FlashingLights(PMDG737Definition.FlashHoldOverrides);
        Assert.Equal(new[] { Kind.On, Kind.Flashing }, Replay(f, MeasuredAlign));
        Assert.Equal(4000, PMDG737Definition.FlashHoldOverrides["IRS_annunALIGN_0"]);
        Assert.Equal(4000, PMDG737Definition.FlashHoldOverrides["IRS_annunALIGN_1"]);
    }

    [Fact]
    public void Every_hold_override_names_a_lamp_the_filter_owns()
    {
        var vars = Def().GetVariables();
        foreach (var key in PMDG737Definition.FlashHoldOverrides.Keys)
            Assert.True(vars.TryGetValue(key, out var v) && Pmdg737FlashingLights.IsLamp(v), key);
    }

    // ----- the definition's wiring -----

    private static PMDG737Definition Def() => new();

    [Fact]
    public void The_definition_list_names_flashing_lights_from_its_own_variables()
    {
        var def = Def();
        Assert.Equal(new[] { "No lights flashing" }, def.GetLiveListItems("WARN_FlashingLights"));
        foreach (var key in new[] { "IRS_annunALIGN_0", "IRS_annunALIGN_1" })
            for (int i = 0; i < 6; i++)
                def.FlashingLights.OnValue(key, i % 2 == 0, i * 500);
        var vars = def.GetVariables();
        Assert.Equal(new[] { vars["IRS_annunALIGN_0"].DisplayName, vars["IRS_annunALIGN_1"].DisplayName },
            def.GetLiveListItems("WARN_FlashingLights"));
        Assert.Null(def.GetLiveListItems("IRS_annunALIGN_0"));
    }

    [Fact]
    public void Every_two_state_annunciator_is_a_lamp_and_IRS_ALIGN_is_one()
    {
        var vars = Def().GetVariables();
        Assert.True(Pmdg737FlashingLights.IsLamp(vars["IRS_annunALIGN_0"]));
        Assert.True(Pmdg737FlashingLights.IsLamp(vars["ELEC_annunTRANSFER_BUS_OFF_0"]));   // inverted
        Assert.True(Pmdg737FlashingLights.IsLamp(vars["DOOR_annunFWD_ENTRY"]));            // door
        // A three-state fuel valve "annunciator" is a selector, not a lamp.
        Assert.False(Pmdg737FlashingLights.IsLamp(vars["FUEL_annunXFEED_VALVE_OPEN"]));
        // Every field the SDK names an annunciator with a 0/1 on/off description is covered.
        foreach (var (key, v) in vars.Where(kv => kv.Value.Name.Contains("_annun")
                     && kv.Value.ValueDescriptions.Count == 2))
            Assert.True(Pmdg737FlashingLights.IsLamp(v), key);
    }

    [Fact]
    public void A_lamp_row_follows_its_value_when_the_definition_handles_it()
    {
        foreach (var (key, v) in Def().GetVariables().Where(kv => Pmdg737FlashingLights.IsLamp(kv.Value)))
        {
            Assert.True(v.RefreshesControlWhenDefHandled(0), key);
            Assert.True(v.RefreshesControlWhenDefHandled(1), key);
        }
    }

    [Fact]
    public void The_flashing_lights_list_is_in_the_Warnings_panel_and_never_read_from_the_sim()
    {
        var def = Def();
        var v = def.GetVariables()["WARN_FlashingLights"];
        Assert.Contains("WARN_FlashingLights", def.GetPanelControls()["Warnings"]);
        Assert.True(v.RenderAsLiveList);
        Assert.False(v.RenderAsButton);
        Assert.False(v.IsAnnounced);
        Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Never, v.UpdateFrequency);
    }

    [Fact]
    public void Phrases_use_the_lamps_own_descriptions()
    {
        var vars = Def().GetVariables();
        var align = vars["IRS_annunALIGN_0"];
        var busOff = vars["ELEC_annunTRANSFER_BUS_OFF_0"];
        Assert.Equal($"{align.DisplayName}: on", Pmdg737FlashingLights.Phrase(new(Align, Kind.On), align));
        Assert.Equal($"{align.DisplayName}: off", Pmdg737FlashingLights.Phrase(new(Align, Kind.Off), align));
        Assert.Equal($"{align.DisplayName}: flashing", Pmdg737FlashingLights.Phrase(new(Align, Kind.Flashing), align));
        Assert.Equal($"{align.DisplayName}: steady", Pmdg737FlashingLights.Phrase(new(Align, Kind.Steady), align));
        // An inverted lamp lit means the system is off.
        Assert.Equal($"{busOff.DisplayName}: off", Pmdg737FlashingLights.Phrase(new("x", Kind.On), busOff));
    }

    [Fact]
    public void Phrases_and_list_rows_are_the_same_under_a_comma_decimal_culture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            var align = Def().GetVariables()["IRS_annunALIGN_0"];
            Assert.Equal($"{align.DisplayName}: flashing", Pmdg737FlashingLights.Phrase(new(Align, Kind.Flashing), align));
            Assert.Equal(new[] { "No lights flashing" }, Pmdg737FlashingLights.ListRows(Array.Empty<string>()));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }
}

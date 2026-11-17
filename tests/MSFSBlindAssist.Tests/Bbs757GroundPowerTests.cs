using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// BlueBird's ground power, as far as it needs no simulator. The Ground Services GPU tile TOGGLES
/// <c>L:BBS_757_GPU_AC_PWR_CONNECTED</c> (measured 2026-10-25, session 8, and again 2026-10-26); pressing it is the tablet
/// press pipeline's like any other tile's (plan 18) and nothing here refuses it. This pins what the Ground Power row says
/// when the variable changes and the variable's name. The second half is the definition's side (BBS757Definition.Efb.cs):
/// the row on the Electrical panel, its speech, and the echo the window opens around its own press (armed before its tap,
/// ended when it settles) so a change it made is said once, by the window.
/// </summary>
public class Bbs757GroundPowerTests
{
    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- what the row says ----------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, "Ground power connected")]
    [InlineData(0.0, "Ground power disconnected")]
    [InlineData(0.51, "Ground power connected")]
    [InlineData(0.5, "Ground power disconnected")]
    [InlineData(2.0, "Ground power connected")]
    [InlineData(-1.0, "Ground power disconnected")]
    [InlineData(double.NaN, null)]                               // not finite: nothing to say
    [InlineData(double.PositiveInfinity, null)]
    [InlineData(double.NegativeInfinity, null)]
    public void A_change_of_the_ground_power_row_says_connected_or_disconnected(double value, string? phrase)
    {
        Assert.Equal(phrase, Bbs757GroundPower.ChangePhrase(value));
    }

    [Fact]
    public void Every_phrase_is_the_same_under_a_comma_decimal_culture()
    {
        // Values with a fraction beside the ones that read as unread: no phrase may follow the PC's culture.
        double?[] values = { null, double.NaN, 0.0, 0.3, 0.5, 0.7, 1.0 };

        List<string?> Phrases() => values.OfType<double>().Select(Bbs757GroundPower.ChangePhrase).ToList();

        List<string?> expected = new();
        InCulture("", () => expected = Phrases());

        Assert.Contains("Ground power connected", expected);
        Assert.Contains("Ground power disconnected", expected);
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(expected, Phrases()));
    }

    // ---- the variables and the run window ----------------------------------------------------------

    [Fact]
    public void The_variable_name_and_the_run_window_are_pinned()
    {
        Assert.Equal("BBS_757_GPU_AC_PWR_CONNECTED", Bbs757GroundPower.ConnectedVar);
        Assert.Equal(60_000, Bbs757GroundPower.WindowRunMaxMs);
    }

    // ---- the definition: the Ground Power row, its speech and the window's echo ---------------------------

    private const string GpuKey = Bbs757SharedKeys.GpuConnected;
    private const string RowConnected = "Ground power connected";
    private const string RowDisconnected = "Ground power disconnected";

    /// <summary>A definition whose BBS Nest check is settled and whose clock is a field.</summary>
    private sealed class Rig
    {
        public long Now { get; set; } = 100_000;
        public readonly BBS757Definition Def;
        public readonly SpeechCapture Speech = new();

        public Rig() => Def = new BBS757Definition { NestRunning = () => true, NowMs = () => Now };

        /// <summary>One delivery of the ground power row; what ProcessSimVarUpdate answered.</summary>
        public bool Deliver(double value) => Def.ProcessSimVarUpdate(GpuKey, value, Speech);
    }

    [Fact]
    public void Ground_power_row_on_electrical()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var vars = def.GetVariables();

        // The connection: a streamed, announced status row with its Ctrl+M row.
        var gpu = vars[GpuKey];
        Assert.Equal("BBS_757_GPU_AC_PWR_CONNECTED", gpu.Name);
        Assert.Equal(Bbs757GroundPower.ConnectedVar, gpu.Name);
        Assert.Equal("Ground Power", gpu.DisplayName);
        Assert.Equal(SimVarType.LVar, gpu.Type);
        Assert.Equal("number", gpu.Units);
        Assert.Equal(UpdateFrequency.Continuous, gpu.UpdateFrequency);
        Assert.True(gpu.IsAnnounced);
        Assert.True(gpu.RenderAsReadOnlyStatus);
        Assert.False(gpu.ExcludeFromMonitorManager);
        Assert.Equal(new Dictionary<double, string> { [0] = "Not connected", [1] = "Connected" }, gpu.ValueDescriptions);
        Assert.Contains(def.MonitorRows(), r => r.Key == GpuKey && r.Label == "Ground Power");

        // It is no control. Which panel's status display shows it is proved below through an in-memory map, not
        // the generated one (the plan set's convention: tests never depend on another plan's map).
        Assert.DoesNotContain(GpuKey, def.GetPanelControls().SelectMany(p => p.Value));

        // The status display says it in words.
        Assert.True(def.TryGetDisplayOverride(GpuKey, 1, out string connected));
        Assert.Equal("Connected", connected);
        Assert.True(def.TryGetDisplayOverride(GpuKey, 0, out string notConnected));
        Assert.Equal("Not connected", notConnected);
        Assert.True(def.TryGetDisplayOverride(GpuKey, double.NaN, out string unread));
        Assert.Equal("unavailable", unread);
    }

    [Theory]
    // The literal, not BBS757Definition.GroundPowerPanel: a row fed the constant passes whatever it says (and a row
    // of each is a duplicate, xUnit1025). The body looks the display up by the constant, so a changed value fails.
    [InlineData("Electrical", true)]
    [InlineData("Pedestal", false)]
    public void The_row_joins_the_electrical_panel_only_when_the_map_has_one(string panel, bool joins)
    {
        // An in-memory map of one control, on the panel under test (no dependence on the generated map).
        var map = Bbs757ControlMap.Parse($$"""
            {
              "controls": [
                {
                  "node": "B757_OVHD_BATTERY_BUTTON", "section": "Overhead", "panel": "{{panel}}",
                  "label": "Battery", "kind": "toggle", "state_var": "B757_OVHD_BATTERY_BUTTON"
                }
              ]
            }
            """);
        var def = new BBS757Definition(map) { NestRunning = () => true };
        var displays = def.GetPanelDisplayVariables();

        if (joins)
        {
            Assert.Single(displays[BBS757Definition.GroundPowerPanel], GpuKey);   // on that panel's display ...
            Assert.Single(displays.SelectMany(p => p.Value), GpuKey);            // ... and on no other
        }
        else
        {
            // A display of a panel the map lacks would be an orphan entry whose rows silently vanish.
            Assert.DoesNotContain(BBS757Definition.GroundPowerPanel, def.GetPanelControls().Keys);
            Assert.DoesNotContain(BBS757Definition.GroundPowerPanel, displays.Keys);
            Assert.DoesNotContain(GpuKey, displays.SelectMany(p => p.Value));
        }
        Assert.Contains(GpuKey, def.GetVariables().Keys);   // the row speaks whatever panels the map has
        Assert.DoesNotContain(GpuKey, def.GetPanelControls().SelectMany(p => p.Value));
    }

    [Theory]
    [InlineData(0.0, 1.0, RowConnected)]
    [InlineData(1.0, 0.0, RowDisconnected)]
    public void Change_announced_after_silent_first(double first, double then, string said)
    {
        var rig = new Rig();

        // The cockpit as found, or BlueBird's own GPU/jetway connecting at the flight load: the baseline.
        Assert.True(rig.Deliver(first));
        rig.Deliver(first);
        Assert.Empty(rig.Speech.All);

        // A change speaks once; the same value again does not.
        Assert.True(rig.Deliver(then));
        rig.Deliver(then);
        Assert.Equal(new[] { said }, rig.Speech.All);

        // A flight load, then a reconnect: the next reading is a baseline each time.
        rig.Def.OnSimContextReset();
        rig.Deliver(first);
        rig.Def.ResetAnnouncementBaselines();
        rig.Deliver(then);
        Assert.Equal(new[] { said }, rig.Speech.All);
    }

    [Fact]
    public void An_unreadable_reading_is_neither_spoken_nor_a_baseline()
    {
        var rig = new Rig();

        Assert.True(rig.Deliver(double.NaN));   // consumed: the generic path never says a raw value
        rig.Deliver(1);                         // the first reading there is: the baseline
        rig.Deliver(double.PositiveInfinity);
        rig.Deliver(1);
        Assert.Empty(rig.Speech.All);

        rig.Deliver(0);
        Assert.Equal(new[] { RowDisconnected }, rig.Speech.All);
    }

    [Fact]
    public void Window_run_not_said_twice()
    {
        var rig = new Rig();
        rig.Deliver(0);                       // not connected: the baseline
        rig.Def.ArmGroundPowerEcho();         // right before the window's taps

        rig.Now += 30_000;                    // a slow run, inside the 60 s
        rig.Deliver(1);                       // the tile took: tracked, not spoken
        Assert.Empty(rig.Speech.All);

        rig.Def.SettleGroundPowerEcho(1);     // the run is over; the window said the result itself
        rig.Deliver(1);                       // the delivery in flight: no change
        Assert.Empty(rig.Speech.All);

        rig.Deliver(0);                       // another source takes it away, right after: spoken
        Assert.Equal(new[] { RowDisconnected }, rig.Speech.All);
    }

    [Fact]
    public void A_settle_rebaselines_to_what_the_window_read()
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();

        // The window read the connection after its taps (the delivery had not come yet) and said it.
        rig.Def.SettleGroundPowerEcho(1);
        rig.Deliver(1);                       // that delivery is no change, though the echo is over

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_late_change_after_a_run_whose_read_back_missed_the_wish_is_spoken()
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();         // Connect ground power
        rig.Now += 6000;                      // the taps took their six seconds ...

        // ... and the read-back was still not connected: the window said "BlueBird did not connect the ground power".
        rig.Def.SettleGroundPowerEcho(0);
        rig.Now += 3000;
        rig.Deliver(0);                       // nothing yet
        Assert.Empty(rig.Speech.All);

        rig.Deliver(1);                       // BlueBird connects it after all: the aircraft's change, spoken
        Assert.Equal(new[] { RowConnected }, rig.Speech.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_unread_settle_keeps_the_baseline_and_ends_the_echo(double? after)
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();
        rig.Now += 2000;                      // a quick run that read nothing at the end

        rig.Def.SettleGroundPowerEcho(after);
        rig.Deliver(0);                       // still as it was: nothing
        Assert.Empty(rig.Speech.All);

        rig.Deliver(1);                       // a change from the baseline of 0, after the run: spoken
        Assert.Equal(new[] { RowConnected }, rig.Speech.All);
    }

    [Fact]
    public void A_change_during_the_run_stays_silent_when_the_settle_reads_nothing()
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();
        rig.Now += 5000;
        rig.Deliver(1);                       // during the armed run, before the settle: tracked, not spoken
        Assert.Empty(rig.Speech.All);

        rig.Def.SettleGroundPowerEcho(null);  // nothing read at the end: the baseline is what the run tracked
        rig.Deliver(1);                       // still: nothing
        Assert.Empty(rig.Speech.All);

        rig.Deliver(0);                       // another source's change, right after the run: spoken
        Assert.Equal(new[] { RowDisconnected }, rig.Speech.All);
    }

    [Fact]
    public void A_run_that_never_settles_hides_changes_for_no_longer_than_its_longest_time()
    {
        var rig = new Rig();
        rig.Deliver(0);
        rig.Def.ArmGroundPowerEcho();

        rig.Now += Bbs757GroundPower.WindowRunMaxMs - 1;
        rig.Deliver(1);                       // still inside the longest run: its echo
        Assert.Empty(rig.Speech.All);

        rig.Now += 2;
        rig.Deliver(0);                       // past it: another source's change
        Assert.Equal(new[] { RowDisconnected }, rig.Speech.All);
    }

    [Fact]
    public void A_muted_ground_power_row_says_nothing()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new GatedSpeechCapture();
        var settings = new UserSettings { BBS757DisabledMonitorVariables = new() { GpuKey } };
        settings.RebuildDisabledMonitorVariableCaches();

        MuteWrap.Deliver(def, speech, settings, GpuKey, 0);
        MuteWrap.Deliver(def, speech, settings, GpuKey, 1);

        Assert.Empty(speech.All);
        Assert.False(def.IsMuteWrapExempt(GpuKey));

        // The same deliveries with nothing muted speak: the silence above is the mute's, not the row's.
        var loud = new GatedSpeechCapture();
        var unmuted = new UserSettings();
        unmuted.RebuildDisabledMonitorVariableCaches();
        var other = new BBS757Definition { NestRunning = () => true };
        MuteWrap.Deliver(other, loud, unmuted, GpuKey, 0);
        MuteWrap.Deliver(other, loud, unmuted, GpuKey, 1);
        Assert.Equal(new[] { RowConnected }, loud.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_row_reads_the_same_under_a_comma_decimal_culture(string culture)
    {
        // The texts are words, but a reading with a fraction must not make either follow the PC's culture:
        // 0 is the baseline, 0.51 is the first reading above one half (connected), the unreadable ones
        // change nothing, and the last 0.3 is the change back.
        double[] values = { 0, 0.3, 0.5, 0.51, 0.7, 1, double.NaN, double.PositiveInfinity, 0.3 };

        (List<string> Display, List<string> Spoken) Texts()
        {
            var rig = new Rig();
            var display = new List<string>();
            foreach (double v in values)
            {
                Assert.True(rig.Def.TryGetDisplayOverride(GpuKey, v, out string text));
                display.Add(text);
                rig.Deliver(v);
            }
            return (display, rig.Speech.All.ToList());
        }

        (List<string> Display, List<string> Spoken) expected = default;
        InCulture("", () => expected = Texts());

        Assert.Equal(new[]
        {
            "Not connected", "Not connected", "Not connected", "Connected", "Connected", "Connected",
            "unavailable", "unavailable", "Not connected",
        }, expected.Display);
        Assert.Equal(new[] { RowConnected, RowDisconnected }, expected.Spoken);
        InCulture(culture, () =>
        {
            var inCulture = Texts();
            Assert.Equal(expected.Display, inCulture.Display);
            Assert.Equal(expected.Spoken, inCulture.Spoken);
        });
    }
}

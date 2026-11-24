using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's pressurization landing altitude, as far as it runs without a sim: the typed text (rounded
/// to the cockpit's 10 ft step, range checked after the rounding), the calculator write that sets it
/// (measured: <c>1020 (&gt;L:B757_OVHD_LDG_ELEV) 1 (&gt;L:B757_OVHD_LDG_ELEV_CHG)</c> reads 1030) and the
/// phrases. CI runs under en-US, so the de-DE and sv-SE cases are the ones that catch a culture leak:
/// de-DE's "1.030" is a thousands group and "1030,0" a decimal, and sv-SE writes its minus sign as
/// U+2212, which the RPN parser rejects. The second half is the definition's: the field first on the
/// Pressurization panel with its read-back row, the typed text written, read back fresh and spoken as
/// read (a write that did not take speaks the old value), and the panel set the field never sends.
/// </summary>
public class Bbs757LandingAltitudeTests
{
    private const string Unavailable = "Landing altitude unavailable";

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

    [Fact]
    public void The_names_and_range_are_the_measured_ones()
    {
        Assert.Equal("B757_OVHD_LDG_ELEV", Bbs757LandingAltitude.ValueVar);
        Assert.Equal("B757_OVHD_LDG_ELEV_CHG", Bbs757LandingAltitude.StepVar);
        Assert.Equal("BBS757_LDG_ALT_SET", Bbs757LandingAltitude.SetKey);
        Assert.Equal("BBS757_LDG_ALT", Bbs757LandingAltitude.DisplayKey);
        Assert.Equal(-1070, Bbs757LandingAltitude.MinFeet);
        Assert.Equal(14100, Bbs757LandingAltitude.MaxFeet);
        Assert.Equal("Landing altitude must be from -1070 to 14100 feet.", Bbs757LandingAltitude.InvalidMessage);
    }

    // ---- Typed text ---------------------------------------------------------------------------

    [Theory]
    [InlineData("1030", 1030)]
    [InlineData(" 1030 ", 1030)]
    [InlineData("+1030", 1030)]
    [InlineData("1034", 1030)]
    [InlineData("1035", 1040)]          // a half rounds away from zero
    [InlineData("645", 650)]
    [InlineData("-65", -70)]
    [InlineData("-64", -60)]
    [InlineData("1034.5", 1030)]        // nearest 10 of 1034.5 is 1030, not 1035 then 1040
    [InlineData("1034.9", 1030)]
    [InlineData("5", 10)]
    [InlineData("4", 0)]
    [InlineData("-4", 0)]
    [InlineData("-5", -10)]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("14100", 14100)]
    [InlineData("14104", 14100)]        // range is checked on the rounded value: 14104 is 14100
    [InlineData("-1070", -1070)]
    [InlineData("-1074", -1070)]        // and -1074 is -1070
    [InlineData("1030.0", 1030)]
    [InlineData("1030,0", 1030)]
    public void Parses_and_rounds(string text, int expected)
    {
        Assert.True(Bbs757LandingAltitude.TryParseText(text, out int feet, out string error));
        Assert.Equal(expected, feet);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_empty_with_its_own_words(string? text)
    {
        Assert.False(Bbs757LandingAltitude.TryParseText(text, out int feet, out string error));
        Assert.Equal(0, feet);
        Assert.Equal("Type the landing altitude in feet first.", error);
    }

    [Theory]
    [InlineData("14105")]               // rounds to 14110
    [InlineData("14110")]
    [InlineData("14200")]
    [InlineData("99999")]
    [InlineData("-1075")]               // rounds to -1080
    [InlineData("-1080")]
    [InlineData("-5000")]
    [InlineData("abc")]
    [InlineData("10 30")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]               // overflows to infinity
    [InlineData("1.030")]               // a possible thousands group is refused, never guessed (CORE-17)
    [InlineData("1,030")]
    [InlineData("14.100")]
    [InlineData("1.234,5")]
    public void Refuses_out_of_range_and_unreadable_with_the_range(string text)
    {
        Assert.False(Bbs757LandingAltitude.TryParseText(text, out int feet, out string error));
        Assert.Equal(0, feet);
        Assert.Equal("Landing altitude must be from -1070 to 14100 feet.", error);
    }

    [Fact]
    public void Refuses_empty_and_out_of_range_with_different_words()
    {
        Assert.False(Bbs757LandingAltitude.TryParseText("", out _, out string empty));
        Assert.False(Bbs757LandingAltitude.TryParseText("20000", out _, out string range));
        Assert.NotEqual(empty, range);
    }

    [Fact]
    public void De_DE_typing()
    {
        InCulture("de-DE", () =>
        {
            // "1030,0" is a decimal and "1030.0" a decimal point: both are the pilot's 1030 ft.
            Assert.True(Bbs757LandingAltitude.TryParseText("1030,0", out int a, out string errorA));
            Assert.Equal(1030, a);
            Assert.Equal("", errorA);

            Assert.True(Bbs757LandingAltitude.TryParseText("1030.0", out int b, out string errorB));
            Assert.Equal(1030, b);
            Assert.Equal("", errorB);

            Assert.True(Bbs757LandingAltitude.TryParseText("-70,0", out int c, out _));
            Assert.Equal(-70, c);

            Assert.True(Bbs757LandingAltitude.TryParseText("1030", out int d, out _));
            Assert.Equal(1030, d);

            // "1.030" could be a thousands group (it is how a German writes 1030): CORE-17 refuses it
            // rather than guess, and the pilot gets the range sentence, never 1.03 ft or 103000 ft.
            foreach (string grouped in new[] { "1.030", "1,030", "14.100" })
            {
                Assert.False(Bbs757LandingAltitude.TryParseText(grouped, out int feet, out string error));
                Assert.Equal(0, feet);
                Assert.Equal("Landing altitude must be from -1070 to 14100 feet.", error);
            }

            // What goes out is invariant, whatever the PC's culture.
            Assert.Equal("1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)", Bbs757LandingAltitude.Write(1030));
            Assert.Equal("-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)", Bbs757LandingAltitude.Write(-60));
        });
    }

    [Theory]
    [InlineData("sv-SE", "1030,0", 1030)]
    [InlineData("sv-SE", "1030.0", 1030)]
    [InlineData("sv-SE", "-70", -70)]
    [InlineData("en-US", "1030", 1030)]
    [InlineData("en-US", "1030,0", 1030)]
    public void Typing_reads_the_same_in_any_culture(string culture, string text, int expected)
    {
        InCulture(culture, () =>
        {
            Assert.True(Bbs757LandingAltitude.TryParseText(text, out int feet, out string error));
            Assert.Equal(expected, feet);
            Assert.Equal("", error);
        });
    }

    // ---- The write ----------------------------------------------------------------------------

    [Theory]
    [InlineData("en-US", 1030, "1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]   // measured: reads 1030
    [InlineData("en-US", -60, "-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]     // measured: reads -60
    [InlineData("en-US", 0, "-10 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("en-US", 14100, "14090 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("en-US", -1070, "-1080 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("de-DE", 1030, "1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("de-DE", -60, "-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("de-DE", 14100, "14090 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("sv-SE", 1030, "1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("sv-SE", -60, "-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]     // not U+2212, which the RPN parser rejects
    [InlineData("sv-SE", -1070, "-1080 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    public void Writes_the_measured_sequence(string culture, int feet, string expected)
    {
        InCulture(culture, () =>
        {
            string rpn = Bbs757LandingAltitude.Write(feet);
            Assert.Equal(expected, rpn);
            Assert.DoesNotContain('−', rpn);
            Assert.True(rpn.All(ch => ch < 0x80), "the RPN is plain ASCII");
        });
    }

    // ---- The phrases --------------------------------------------------------------------------

    [Theory]
    [InlineData(1030.0, "Landing altitude 1030 feet")]
    [InlineData(-60.0, "Landing altitude minus 60 feet")]
    [InlineData(0.0, "Landing altitude 0 feet")]
    [InlineData(14100.0, "Landing altitude 14100 feet")]          // never "14,100" or "14.100"
    [InlineData(-1070.0, "Landing altitude minus 1070 feet")]
    [InlineData(1029.6, "Landing altitude 1030 feet")]            // a read-back is whole feet
    [InlineData(1030.4, "Landing altitude 1030 feet")]
    [InlineData(1030.5, "Landing altitude 1031 feet")]            // a half rounds away from zero
    [InlineData(-60.4, "Landing altitude minus 60 feet")]
    [InlineData(-0.4, "Landing altitude 0 feet")]                 // never "minus 0"
    [InlineData(null, Unavailable)]
    [InlineData(double.NaN, Unavailable)]
    [InlineData(double.PositiveInfinity, Unavailable)]
    [InlineData(double.NegativeInfinity, Unavailable)]
    public void Phrases(double? feet, string expected)
    {
        Assert.Equal(expected, Bbs757LandingAltitude.Phrase(feet));
    }

    [Theory]
    [InlineData(1030.0, "1030 feet")]
    [InlineData(-60.0, "-60 feet")]
    [InlineData(0.0, "0 feet")]
    [InlineData(14100.0, "14100 feet")]
    [InlineData(1029.6, "1030 feet")]
    [InlineData(-0.4, "0 feet")]
    [InlineData(double.NaN, "unavailable")]
    [InlineData(double.PositiveInfinity, "unavailable")]
    [InlineData(double.NegativeInfinity, "unavailable")]
    public void Displays(double feet, string expected)
    {
        Assert.Equal(expected, Bbs757LandingAltitude.Display(feet));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Display_and_phrase_are_invariant(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("14100 feet", Bbs757LandingAltitude.Display(14100));
            Assert.Equal("-60 feet", Bbs757LandingAltitude.Display(-60));        // an ASCII hyphen-minus, never U+2212
            Assert.Equal("1030 feet", Bbs757LandingAltitude.Display(1029.6));
            Assert.Equal("unavailable", Bbs757LandingAltitude.Display(double.NaN));

            Assert.Equal("Landing altitude 14100 feet", Bbs757LandingAltitude.Phrase(14100));
            Assert.Equal("Landing altitude minus 60 feet", Bbs757LandingAltitude.Phrase(-60));
            Assert.Equal("Landing altitude minus 1070 feet", Bbs757LandingAltitude.Phrase(-1070.0));
            Assert.Equal("Landing altitude 1030 feet", Bbs757LandingAltitude.Phrase(1029.6));
            Assert.Equal(Unavailable, Bbs757LandingAltitude.Phrase(null));
            Assert.Equal(Unavailable, Bbs757LandingAltitude.Phrase(double.NaN));

            Assert.DoesNotContain('−', Bbs757LandingAltitude.Display(-60));
            Assert.DoesNotContain(',', Bbs757LandingAltitude.Display(14100));
            Assert.DoesNotContain('.', Bbs757LandingAltitude.Display(14100));
        });
    }

    // ---- The definition: the field on the Pressurization panel ----------------------------------------

    private const string ModeSelectNode = "B757_OVHD_MODE_SELECT_SWITCH";
    private const string WriteFor1030 = "1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)";

    /// <summary>A Pressurization control, built in memory (never the shipped map's rows).</summary>
    private static Bbs757Control ModeSelect() => new()
    {
        Node = ModeSelectNode, Section = "Overhead", Panel = "Pressurization", Label = "Mode Select",
        Kind = Bbs757Kinds.Switch, StateVar = ModeSelectNode,
        Positions = { new(0, "AUTO 1"), new(1, "AUTO 2"), new(2, "MAN") },
    };

    private static Bbs757Control Electrical() => new()
    {
        Node = "B757_OVHD_BATTERY_BUTTON", Section = "Overhead", Panel = "Electrical", Label = "Battery",
        Kind = Bbs757Kinds.Toggle, StateVar = "B757_OVHD_BATTERY_BUTTON",
        Positions = { new(0, "Off"), new(1, "On") },
    };

    private static Bbs757Control FuelCrossfeed() => new()
    {
        Node = "B757_PED_XFEED", Section = "Pedestal", Panel = "Fuel control", Label = "Crossfeed",
        Kind = Bbs757Kinds.Toggle, StateVar = "B757_PED_XFEED",
        Positions = { new(0, "Closed"), new(1, "Open") },
    };

    private static Bbs757ControlMap MapOf(params Bbs757Control[] controls)
    {
        var map = new Bbs757ControlMap();
        foreach (var control in controls) map.Controls.Add(control);
        return map;
    }

    /// <summary>A 757 definition built from in-memory controls, with a fake sim behind its writes and a speech capture.</summary>
    private sealed class Rig
    {
        public readonly Bbs757FakeIo Sim = new();
        public readonly SpeechCapture Speech = new();
        public readonly SimConnectManager Manager = new(IntPtr.Zero);
        public readonly BBS757Definition Def;

        public Rig(Bbs757ControlMap? map = null)
        {
            Def = new BBS757Definition(map ?? MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => Sim.Io };
        }

        /// <summary>The panel's text box applying <paramref name="text"/>: MainForm's call into the definition.</summary>
        public bool Type(string text)
            => Def.HandleUITypedText(Bbs757LandingAltitude.SetKey, text, Def.GetVariables()[Bbs757LandingAltitude.SetKey], Manager, Speech);
    }

    [Fact]
    public void Pressurization_panel_starts_with_the_field()
    {
        var def = new Rig().Def;

        // The field, then the map's controls; plan 12's cabin buttons follow at the end (Bbs757SpringControlsTests).
        Assert.Equal(new[] { Bbs757LandingAltitude.SetKey, ModeSelectNode }, def.GetPanelControls()["Pressurization"].Take(2));
        Assert.Equal(new[] { Bbs757LandingAltitude.DisplayKey, Bbs757SpringControls.CabinValveVar }, def.GetPanelDisplayVariables()["Pressurization"]);
        Assert.Equal(new[] { "Pressurization" }, def.GetPanelStructure()["Overhead"]);   // the panel is listed once
    }

    [Fact]
    public void The_row_follows_the_panels_own_status_rows()
    {
        var map = MapOf(ModeSelect());
        map.Status.Add(new Bbs757StatusRow { Key = "B757_PRESS_CABIN_ALT", Panel = "Pressurization", Label = "Cabin Altitude" });
        var def = new Rig(map).Def;

        Assert.Equal(new[] { "B757_PRESS_CABIN_ALT", Bbs757LandingAltitude.DisplayKey, Bbs757SpringControls.CabinValveVar }, def.GetPanelDisplayVariables()["Pressurization"]);
    }

    [Fact]
    public void A_map_without_a_pressurization_panel_still_gets_the_field()
    {
        // The Overhead section has other panels: the new panel is appended to them.
        var withOverhead = new Rig(MapOf(Electrical())).Def;
        Assert.Equal(new[] { "Electrical", "Pressurization" }, withOverhead.GetPanelStructure()["Overhead"]);
        Assert.Equal(Bbs757LandingAltitude.SetKey, withOverhead.GetPanelControls()["Pressurization"][0]);   // plan 12's cabin buttons follow it
        Assert.Equal(new[] { Bbs757LandingAltitude.DisplayKey, Bbs757SpringControls.CabinValveVar }, withOverhead.GetPanelDisplayVariables()["Pressurization"]);
        Assert.Equal(new[] { "B757_OVHD_BATTERY_BUTTON" }, withOverhead.GetPanelControls()["Electrical"]);   // the others are untouched

        // No Overhead section at all: it is created.
        var noOverhead = new Rig(MapOf(FuelCrossfeed())).Def;
        Assert.Equal(new[] { "Pressurization" }, noOverhead.GetPanelStructure()["Overhead"]);
        Assert.Equal(Bbs757LandingAltitude.SetKey, noOverhead.GetPanelControls()["Pressurization"][0]);
        Assert.Equal(new[] { Bbs757LandingAltitude.DisplayKey, Bbs757SpringControls.CabinValveVar }, noOverhead.GetPanelDisplayVariables()["Pressurization"]);
        Assert.Equal("Fuel control", noOverhead.GetPanelStructure()["Pedestal"][0]);   // untouched by this panel (other plans append their own, such as plan 04's COM radios)

        // No controls at all.
        var empty = new Rig(new Bbs757ControlMap()).Def;
        Assert.Equal(new[] { "Pressurization" }, empty.GetPanelStructure()["Overhead"]);
        Assert.Equal(Bbs757LandingAltitude.SetKey, empty.GetPanelControls()["Pressurization"][0]);
    }

    [Fact]
    public void Registers_the_field_and_the_row()
    {
        var vars = new Rig().Def.GetVariables();

        // The field: an Event registration (a key containing "_SET" is a text box Enter applies), never sent.
        var field = vars[Bbs757LandingAltitude.SetKey];
        Assert.Equal(SimVarType.Event, field.Type);
        Assert.Equal("BBS757.LDG_ALT_SET", field.Name);
        Assert.Equal("Landing Altitude", field.DisplayName);
        Assert.Equal(UpdateFrequency.OnRequest, field.UpdateFrequency);
        Assert.True(field.UnparseableTextAsNaN);

        // The row: BlueBird's landing altitude in feet, read on request, shown and never announced.
        var row = vars[Bbs757LandingAltitude.DisplayKey];
        Assert.Equal(SimVarType.LVar, row.Type);
        Assert.Equal("B757_OVHD_LDG_ELEV", row.Name);
        Assert.Equal("feet", row.Units);
        Assert.Equal("Landing Altitude", row.DisplayName);
        Assert.Equal(UpdateFrequency.OnRequest, row.UpdateFrequency);
        Assert.True(row.RenderAsReadOnlyStatus);
        Assert.True(row.ExcludeFromMonitorManager);
        Assert.False(row.IsAnnounced);
    }

    [Fact]
    public void Display_says_feet()
    {
        var def = new Rig().Def;

        Assert.True(def.TryGetDisplayOverride(Bbs757LandingAltitude.DisplayKey, 1030, out string feet));
        Assert.Equal("1030 feet", feet);
        Assert.True(def.TryGetDisplayOverride(Bbs757LandingAltitude.DisplayKey, -60, out string below));
        Assert.Equal("-60 feet", below);
        Assert.True(def.TryGetDisplayOverride(Bbs757LandingAltitude.DisplayKey, double.NaN, out string nothing));
        Assert.Equal("unavailable", nothing);

        // Any other row is left to the rest of the definition.
        Assert.False(def.TryGetDisplayOverride(ModeSelectNode, 1, out _));

        InCulture("de-DE", () =>
        {
            Assert.True(def.TryGetDisplayOverride(Bbs757LandingAltitude.DisplayKey, 14100, out string de));
            Assert.Equal("14100 feet", de);   // never "14.100"
        });
    }

    // ---- The definition: typed text ---------------------------------------------------------------

    [Fact]
    public async Task Typed_altitude_writes_and_reads_back()
    {
        var rig = new Rig();
        rig.Sim.Hold = ordinal => ordinal == 0;   // the wait before the read-back
        rig.Sim.Cache[Bbs757LandingAltitude.DisplayKey] = 1000;   // a stale cached value must not be what is read

        Assert.True(rig.Type("1030"));

        // The write is out and the app waits before it reads: nothing is said or re-read yet.
        Assert.Equal(new[] { WriteFor1030 }, rig.Sim.Writes);
        Assert.Equal(new[] { 1500 }, rig.Sim.Delays);
        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Sim.Resyncs);

        rig.Sim.Fresh[Bbs757LandingAltitude.DisplayKey] = 1030;   // the panel has taken it by then
        rig.Sim.Release(0);
        await rig.Sim.ResyncsReached(1);

        Assert.Equal(new[] { Bbs757FakeIo.WriteEvent(WriteFor1030), Bbs757FakeIo.DelayEvent(1500) }, rig.Sim.Events);
        Assert.Equal(new[] { "Landing altitude 1030 feet" }, rig.Speech.All);
        Assert.Equal(new[] { Bbs757LandingAltitude.DisplayKey }, rig.Sim.Resyncs);   // so the status row shows it
    }

    [Theory]
    [InlineData("1030", 1030.0, WriteFor1030, "Landing altitude 1030 feet")]
    [InlineData("1030", 1000.0, WriteFor1030, "Landing altitude 1000 feet")]   // the write did not take (BBS Nest stopped): what is read back
    [InlineData("1030", null, WriteFor1030, "Landing altitude unavailable")]   // nothing read back
    [InlineData("1030,0", 1030.0, WriteFor1030, "Landing altitude 1030 feet")]
    [InlineData("645", 650.0, "640 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)", "Landing altitude 650 feet")]   // typed 645: the step is 10
    [InlineData("-65", -70.0, "-80 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)", "Landing altitude minus 70 feet")]
    [InlineData("14100", 14100.0, "14090 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)", "Landing altitude 14100 feet")]
    public async Task Typed_altitude_speaks_what_came_back(string typed, double? readBack, string write, string said)
    {
        var rig = new Rig();
        if (readBack is double value) rig.Sim.Fresh[Bbs757LandingAltitude.DisplayKey] = value;

        Assert.True(rig.Type(typed));
        await rig.Sim.ResyncsReached(1);

        Assert.Equal(new[] { write }, rig.Sim.Writes);
        Assert.Equal(new[] { said }, rig.Speech.All);
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);   // a typed number's confirmation always speaks
        Assert.Equal(new[] { Bbs757LandingAltitude.DisplayKey }, rig.Sim.Resyncs);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Typed_altitude_reads_and_goes_out_the_same_in_any_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var rig = new Rig();
            rig.Sim.Fresh[Bbs757LandingAltitude.DisplayKey] = -60;

            Assert.True(rig.Type("-60"));
            Assert.True(rig.Type("1030,0"));
            Assert.True(rig.Type("1.030"));   // a possible thousands group: refused, never guessed

            Assert.Equal(new[] { "-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)", WriteFor1030 }, rig.Sim.Writes);
            Assert.Equal(new[]
            {
                "Landing altitude minus 60 feet",
                "Landing altitude minus 60 feet",   // the fake still reads -60 back
                Bbs757LandingAltitude.InvalidMessage,
            }, rig.Speech.All);
        });
    }

    [Theory]
    [InlineData("", true, "Type the landing altitude in feet first.")]
    [InlineData("   ", true, "Type the landing altitude in feet first.")]
    [InlineData("", false, "Type the landing altitude in feet first.")]
    [InlineData("1.030", true, "Landing altitude must be from -1070 to 14100 feet.")]
    [InlineData("20000", true, "Landing altitude must be from -1070 to 14100 feet.")]
    [InlineData("-1080", true, "Landing altitude must be from -1070 to 14100 feet.")]
    [InlineData("abc", true, "Landing altitude must be from -1070 to 14100 feet.")]
    [InlineData("1030", false, "Landing altitude unavailable")]   // valid, but a write cannot land
    public void Refusals_speak_and_write_nothing(string typed, bool canWrite, string said)
    {
        var rig = new Rig();
        rig.Sim.CanWrite = canWrite;
        rig.Sim.Fresh[Bbs757LandingAltitude.DisplayKey] = 1030;

        Assert.True(rig.Type(typed));

        Assert.Equal(new[] { said }, rig.Speech.All);
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);   // a validation error always speaks
        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Empty(rig.Sim.Resyncs);
    }

    [Fact]
    public void A_panel_set_of_the_field_sends_nothing()
    {
        // The registration's event is never sent (ruling R13): the definition claims the key, so the
        // generic path cannot transmit it, and nothing is written, waited for, said or re-read.
        var rig = new Rig();
        var field = rig.Def.GetVariables()[Bbs757LandingAltitude.SetKey];

        Assert.True(rig.Def.HandleUIVariableSet(Bbs757LandingAltitude.SetKey, 1030, field, rig.Manager, rig.Speech));
        Assert.True(rig.Def.HandleUIVariableSet(Bbs757LandingAltitude.SetKey, double.NaN, field, rig.Manager, rig.Speech));

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Empty(rig.Sim.Resyncs);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_field_does_not_take_the_squawk_boxs_text_nor_the_other_way_round()
    {
        var rig = new Rig();

        // Another box is not claimed here: the squawk's own path answers it.
        Assert.True(rig.Def.HandleUITypedText(Bbs757Squawk.SetKey, "12a4", rig.Def.GetVariables()[Bbs757Squawk.SetKey], rig.Manager, rig.Speech));
        Assert.Equal(new[] { Bbs757Squawk.InvalidMessage }, rig.Speech.Interrupts);
        Assert.False(rig.Def.HandleUITypedText("SOME_OTHER_SET", "12", new SimVarDefinition(), rig.Manager, rig.Speech));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_switch_away_during_the_wait_ends_the_flow_silently()
    {
        var rig = new Rig();
        rig.Sim.Hold = ordinal => ordinal == 0;

        var typed = rig.Def.SetLandingAltitudeAsync("1030", rig.Manager, rig.Speech);
        Assert.Equal(new[] { WriteFor1030 }, rig.Sim.Writes);   // the write is out, the wait is held

        rig.Def.OnSwitchedAway();   // the pilot picks another aircraft profile
        await typed.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, rig.Sim.CanceledDelays);
        Assert.Empty(rig.Speech.All);    // nothing is said into the other aircraft's session
        Assert.Empty(rig.Sim.Resyncs);
        Assert.Equal(new[] { WriteFor1030 }, rig.Sim.Writes);
    }

    [Theory]
    [InlineData("1030")]
    [InlineData("")]
    [InlineData("20000")]
    public void Typing_after_a_switch_away_writes_and_says_nothing(string typed)
    {
        var rig = new Rig();
        rig.Def.OnSwitchedAway();

        Assert.True(rig.Type(typed));

        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Empty(rig.Sim.Resyncs);
    }

    [Fact]
    public async Task A_read_that_throws_is_said_and_never_escapes()
    {
        var sim = new Bbs757FakeIo();
        var io = sim.Io;
        var failing = new Bbs757SimIo
        {
            CanWrite = io.CanWrite, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead, Delay = io.Delay,
            ReadFresh = (_, _) => throw new InvalidOperationException("no link"),
        };
        var def = new BBS757Definition(MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => failing };
        var speech = new SpeechCapture();

        await def.SetLandingAltitudeAsync("1030", new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { WriteFor1030 }, sim.Writes);
        Assert.Equal(new[] { "Landing altitude unavailable" }, speech.All);   // a typed number never goes unanswered
    }
}

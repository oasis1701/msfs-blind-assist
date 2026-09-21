using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The composition rule for an MD-11 control's spoken state (spec §3.3), in order: lit legends
/// win; else the proven latch position; else "unpowered" when the DC gate is off; else the dark
/// meaning. Pinned because every branch was reached by a live probe on 2026-09-05 — the
/// cold-and-dark cockpit reads every lamp as 0, so "dark" must never be spoken as "normal"
/// without the gate.
/// </summary>
public class Md11ControlStateTests
{
    private static Md11StateSpec ExtPower() => new()
    {
        Lamps = new()
        {
            new() { Var = "MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", Legend = "AVAIL", Lit = "Available" },
            new() { Var = "MD11_OVHD_ELEC_EXT_PWR_ON_LT", Legend = "ON", Lit = "On" },
        },
        Dark = "Not available",
    };

    private static Md11StateSpec Battery() => new()
    {
        Lamps = new() { new() { Var = "MD11_OVHD_ELEC_BATT_OFF_LT", Legend = "OFF", Lit = "Off" } },
        Latch = new() { Var = "MD11_OVHD_ELEC_BATT_BT", On = "On", Off = "Off" },
        Dark = "On",
    };

    // An "APU blank light"-style lamp: it lights up but has no spoken word of its own, e.g. a
    // legend position with no text painted on it. Paired here with a normal named lamp so both
    // halves of the empty-Lit clause in rule 1 get exercised.
    private static Md11StateSpec BlankAndNamedLamp() => new()
    {
        Lamps = new()
        {
            new() { Var = "BLANK", Legend = "", Lit = "" },
            new() { Var = "NAMED", Legend = "ON", Lit = "On" },
        },
        Dark = "Off",
    };

    private static Func<string, double?> Values(params (string var, double? value)[] pairs)
        => v => pairs.FirstOrDefault(p => p.var == v).value;

    [Fact]
    public void LitLegends_AreJoinedInLampOrder()
    {
        var text = Md11ControlState.Compose(ExtPower(),
            Values(("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", 1), ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", 1)), powered: true);
        Assert.Equal("Available, On", text);
    }

    [Fact]
    public void RepeatedLitWords_AreSpokenOnce()
    {
        var spec = new Md11StateSpec { Lamps = new() { new() { Var = "A", Legend = "ON", Lit = "On" }, new() { Var = "B", Legend = "PA", Lit = "On" } } };
        Assert.Equal("On", Md11ControlState.Compose(spec, Values(("A", 1), ("B", 1)), true));
    }

    [Fact]
    public void BlankLitWord_ContributesNothingWhenAnotherLegendIsAlsoLit()
    {
        // Both lamps are lit, but the blank one has no word to say — only "On" is spoken.
        var text = Md11ControlState.Compose(BlankAndNamedLamp(),
            Values(("BLANK", 1), ("NAMED", 1)), powered: true);
        Assert.Equal("On", text);
    }

    [Fact]
    public void BlankLitWord_IsNotSpoken_FallsThroughToTheDarkMeaning()
    {
        // Only the blank lamp is lit; it contributes nothing, so rule 1 has no answer and the
        // dark meaning applies (the control is powered, so this is not "unpowered" either).
        var text = Md11ControlState.Compose(BlankAndNamedLamp(),
            Values(("BLANK", 1), ("NAMED", 0)), powered: true);
        Assert.Equal("Off", text);
    }

    [Fact]
    public void LitLegend_OutranksTheLatch()
    {
        var text = Md11ControlState.Compose(Battery(),
            Values(("MD11_OVHD_ELEC_BATT_OFF_LT", 1), ("MD11_OVHD_ELEC_BATT_BT", 1)), powered: true);
        Assert.Equal("Off", text);
    }

    [Fact]
    public void Latch_AnswersWhenEveryLegendIsDark_EvenUnpowered()
    {
        // Cold and dark: BATT_OFF_LT reads 0 because nothing powers it; the latch says Off.
        var text = Md11ControlState.Compose(Battery(),
            Values(("MD11_OVHD_ELEC_BATT_OFF_LT", 0), ("MD11_OVHD_ELEC_BATT_BT", 0)), powered: false);
        Assert.Equal("Off", text);
    }

    [Fact]
    public void Unpowered_WhenDarkAndNoLatchAndNoDcPower()
    {
        var text = Md11ControlState.Compose(ExtPower(),
            Values(("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", 0), ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", 0)), powered: false);
        Assert.Equal(Md11ControlState.Unpowered, text);
    }

    [Fact]
    public void DarkMeaning_WhenPoweredAndNothingLit()
    {
        var text = Md11ControlState.Compose(ExtPower(),
            Values(("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", 0), ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", 0)), powered: true);
        Assert.Equal("Not available", text);
    }

    [Fact]
    public void UncachedLamp_CountsAsDark()
    {
        var text = Md11ControlState.Compose(ExtPower(), Values(), powered: true);
        Assert.Equal("Not available", text);
    }

    [Fact]
    public void NoSpec_OrNothingToSay_IsNoOpinion()
    {
        Assert.Null(Md11ControlState.Compose(null, Values(), true));
        var empty = new Md11StateSpec();   // no lamps, no latch, no dark
        Assert.Null(Md11ControlState.Compose(empty, Values(), true));
        // Rule 3 only speaks "unpowered" when the spec actually has a lamp or a dark word to
        // withhold; an empty spec has nothing to say regardless of power state, so unpowered
        // must stay a no-opinion null too, not manufacture "unpowered" out of nothing.
        Assert.Null(Md11ControlState.Compose(empty, Values(), false));
    }

    [Fact]
    public void EmptyDark_IsNotADarkWordToWithhold_EvenUnpowered()
    {
        // No lamps, no latch, and Dark is "" rather than null — rule 4 already treats an empty
        // Dark as "nothing to say" (IsNullOrEmpty), so rule 3's gate must agree and not fire
        // "unpowered" over a blank word it would never actually speak.
        var spec = new Md11StateSpec { Dark = "" };
        Assert.Null(Md11ControlState.Compose(spec, Values(), powered: false));
    }

    [Fact]
    public void Guard_ReadsOpenOrClosedFromItsOwnVar()
    {
        var guard = new Md11StateSpec { Latch = new() { Var = "MD11_OVHD_ELEC_BATT_GRD", On = "Open", Off = "Closed" } };
        Assert.Equal("Open", Md11ControlState.Compose(guard, Values(("MD11_OVHD_ELEC_BATT_GRD", 1)), false));
        Assert.Equal("Closed", Md11ControlState.Compose(guard, Values(("MD11_OVHD_ELEC_BATT_GRD", 0)), false));
    }

    [Theory]
    // volts, DC bus 1 OFF lamp, powered
    [InlineData(null, null, false)]   // nothing delivered yet
    [InlineData(0.0, 0.0, false)]     // cold and dark
    [InlineData(19.9, 0.0, false)]
    [InlineData(24.0, 1.0, false)]    // measured: battery only — 24 V, but DC bus 1 annunciated off
    [InlineData(24.0, null, false)]   // the lamp has not arrived; unknown means unpowered
    [InlineData(20.0, 0.0, true)]
    [InlineData(24.0, 0.0, true)]     // measured: external, APU or generator power
    public void IsPowered_NeedsALiveBusAndDcBusOneNotAnnunciatedOff(double? volts, double? dc1BusOff, bool expected)
    {
        // Volts alone say only that something is on: on the battery the main bus already reads
        // 24 V while the DC busses feeding the systems annunciators are dead, so every OFF-legend
        // lamp reads 0 and a volts-only gate composed "Tank 1 Fuel Pumps: On" for a pump that was
        // off (measured live, 2026-09-05).
        Assert.Equal(expected, Md11ControlState.IsPowered(volts, dc1BusOff));
    }

    [Fact]
    public void OnBatteryAlone_ALampOnlyControlReadsUnpowered_ButALatchedOneStillAnswers()
    {
        // The two halves of the battery-only picture, together: 24 V, DC bus 1 OFF lit.
        bool powered = Md11ControlState.IsPowered(24.0, 1.0);
        Assert.Equal(Md11ControlState.Unpowered, Md11ControlState.Compose(ExtPower(),
            Values(("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", 0), ("MD11_OVHD_ELEC_EXT_PWR_ON_LT", 0)), powered));
        Assert.Equal("On", Md11ControlState.Compose(Battery(),
            Values(("MD11_OVHD_ELEC_BATT_OFF_LT", 0), ("MD11_OVHD_ELEC_BATT_BT", 1)), powered));
    }

    [Fact]
    public void EmbeddedMap_CarriesStateForTheBatteryAndNamesItsLamp()
    {
        var map = Md11ControlMap.Load();
        var batt = map.Controls.Single(c => c.NodeId == "MD11_OVHD_ELEC_BATT_BT");
        Assert.NotNull(batt.State);
        Assert.Equal("MD11_OVHD_ELEC_BATT_BT", batt.State!.Latch?.Var);
        Assert.Contains(batt.State.Lamps, l => l.Var == "MD11_OVHD_ELEC_BATT_OFF_LT" && l.Lit == "Off");
        var lamp = map.Controls.Single(c => c.NodeId == "MD11_OVHD_ELEC_BATT_OFF_LT");
        Assert.Equal("Battery OFF light", lamp.Label);
    }

    [Fact]
    public void EmbeddedMap_HasNoDerivedLampNamesOnTheSystemsPanels()
    {
        var map = Md11ControlMap.Load();
        var derived = map.Controls
            .Where(c => c.Kind == Md11Kinds.Annunciator && c.LabelSource == "derived"
                        && (c.Area.StartsWith("Overhead") || c.Area.StartsWith("Aft Overhead") || c.Area.StartsWith("Glareshield")))
            .Select(c => c.NodeId).ToList();
        Assert.Empty(derived);
    }

    [Fact]
    public void EmbeddedMap_HasOneLampPerVar()
    {
        var map = Md11ControlMap.Load();
        var dupes = map.Controls.Where(c => c.Kind == Md11Kinds.Annunciator)
            .GroupBy(c => c.StateVar).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }
}

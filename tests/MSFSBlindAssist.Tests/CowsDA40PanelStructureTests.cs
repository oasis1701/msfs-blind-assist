using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Structural checks for the COWS DA40 panel tree.
///
/// The load-bearing one is <see cref="EveryPanelHasAControlsEntry"/>: MainForm's panel
/// build returns early for any panel missing from GetPanelControls(), so a panel named
/// in GetPanelStructure() but absent from BuildPanelControls() renders COMPLETELY BLANK
/// with no error anywhere. That trap cost the HS787 seven empty Flight Data panels
/// (docs/hs787.md), and it is silent — only a test catches it.
/// </summary>
/// ⚠️ CORRECTION: "CACHED" IS NOT THE SAME AS "BATCH-COVERED", AND THIS FILE USED TO CONFLATE
/// THEM. The predicate below was Continuous AND IsAnnounced AND NOT ExcludeFromBatch, which is
/// the test for riding the shared 1 Hz BATCH. The CACHE is wider: SetupDataDefinitions gives an
/// ExcludeFromBatch var its OWN periodic subscription, and its deliveries land in
/// ProcessIndividualVariableResponse, which writes lastVariableValues exactly as the batch path
/// does. So a per-var subscription caches too - verified in that method, not inferred.
///
/// It matters because the fast radio and altimeter subscales are ExcludeFromBatch ON PURPOSE:
/// that is what earns them a SIM_FRAME subscription instead of a 1 Hz sample. Under the old
/// predicate every one of them read as "not cached" and this test failed on a change that was
/// correct.
public class CowsDA40PanelStructureTests
{
    private static CowsDA40Definition Ng() => new(DA40Variant.NG);
    private static CowsDA40Definition Xls() => new(DA40Variant.XLS);

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPanelHasAControlsEntry(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var controls = def.GetPanelControls();

        var missing = def.GetPanelStructure()
                         .SelectMany(section => section.Value)
                         .Where(panel => !controls.ContainsKey(panel))
                         .ToList();

        Assert.True(missing.Count == 0,
            $"{variant}: these panels would render blank — {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void PanelNamesAreUniqueAcrossSections(DA40Variant variant)
    {
        // Panel names key a flat dictionary, so a duplicate in two sections silently
        // collapses into one entry and one of the two sections loses its panel.
        var all = new CowsDA40Definition(variant)
            .GetPanelStructure().SelectMany(s => s.Value).ToList();

        var dupes = all.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(dupes.Count == 0, $"{variant}: duplicate panel names — {string.Join(", ", dupes)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoSectionIsEmpty(DA40Variant variant)
    {
        var empty = new CowsDA40Definition(variant)
            .GetPanelStructure().Where(s => s.Value.Count == 0).Select(s => s.Key).ToList();

        Assert.True(empty.Count == 0, $"{variant}: empty sections — {string.Join(", ", empty)}");
    }

    [Fact]
    public void BothVariantsShareTheCommonSections()
    {
        // No G1000 sections: the displays are driveable and scrapeable in their own
        // right, and the radios and transponder can ONLY be tuned there, so the G1000
        // gets a display window rather than panels that would be a worse copy of it.
        var expected = new[]
        {
            "Instrument Panel", "Center Console", "Circuit Breakers",
            "Autopilot", "Cabin", "Simulation"
        };

        Assert.Equal(expected, Ng().GetPanelStructure().Keys.ToArray());
        Assert.Equal(expected, Xls().GetPanelStructure().Keys.ToArray());
    }

    [Fact]
    public void NgHasTheFadecPanelsAndNotTheLycomingOnes()
    {
        var ng = Ng().GetPanelStructure();

        Assert.Contains("ECU", ng["Instrument Panel"]);

        // There is no separate "Fuel Transfer" panel: the transfer pump lives on the Fuel
        // System panel with the valve and the pumps, because they are one system.
        Assert.DoesNotContain("Fuel Transfer", ng["Center Console"]);
        Assert.Contains("Fuel System", ng["Center Console"]);

        Assert.DoesNotContain("Magnetos", ng["Instrument Panel"]);
        Assert.DoesNotContain("Mixture and Propeller", ng["Center Console"]);
        Assert.DoesNotContain("Priming", ng["Center Console"]);
    }

    [Fact]
    public void XlsHasTheLycomingPanelsAndNotTheFadecOnes()
    {
        var xls = Xls().GetPanelStructure();

        Assert.Contains("Magnetos", xls["Instrument Panel"]);
        Assert.Contains("Mixture and Propeller", xls["Center Console"]);
        Assert.Contains("Priming", xls["Center Console"]);

        Assert.DoesNotContain("ECU", xls["Instrument Panel"]);
    }

    [Fact]
    public void VariantIdentityIsReported()
    {
        Assert.Equal("COWS_DA40NG", Ng().AircraftCode);
        Assert.Equal("COWS_DA40XLS", Xls().AircraftCode);
        Assert.Equal("COWS Diamond DA40-NG", Ng().AircraftName);
        Assert.Equal("COWS Diamond DA40-XLS", Xls().AircraftName);
    }

    [Fact]
    public void AltitudeIsIncrementDecrement_BecauseTheGfc700HasNoAbsoluteSet()
    {
        // Measured on the aircraft: AP_ALT_VAR_SET_ENGLISH ignores its parameter and
        // adds +1000 ft; AP_ALT_VAR_INC/_DEC are +/-100 ft. There is no absolute set,
        // so SetValue would silently do the wrong thing.
        Assert.Equal(MSFSBlindAssist.Aircraft.FCUControlType.IncrementDecrement,
                     Ng().GetAltitudeControlType());
    }

    [Fact]
    public void VisualGuidanceIsScaledForLightGa_NotTheA320Defaults()
    {
        var p = Ng().GetVisualGuidanceProfile();

        Assert.Equal(72.0, p.ReferenceVrefKnots);          // A320 default is 140
        Assert.True(p.MaxPitchRateDegPerSec > 2.5);        // light airframe, more authority
        Assert.True(p.TonePitchRangeDeg > 6.0);            // wider GA attitude envelope
    }

    // ==============================================================================
    // Electrical panel
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPanelControlKeyExistsInGetVariables(DA40Variant variant)
    {
        // A key listed in a panel but absent from GetVariables() renders as a missing
        // control with no error — the same silent class of failure as the blank panel.
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        var missing = def.GetPanelControls()
                         .SelectMany(p => p.Value.Select(k => new { Panel = p.Key, Key = k }))
                         .Where(x => !vars.ContainsKey(x.Key))
                         .Select(x => $"{x.Panel}/{x.Key}")
                         .ToList();

        Assert.True(missing.Count == 0, $"{variant}: undefined control keys — {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryDisplayVariableKeyExistsInGetVariables(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        var missing = def.GetPanelDisplayVariables()
                         .SelectMany(p => p.Value.Select(k => new { Panel = p.Key, Key = k }))
                         .Where(x => !vars.ContainsKey(x.Key))
                         .Select(x => $"{x.Panel}/{x.Key}")
                         .ToList();

        Assert.True(missing.Count == 0, $"{variant}: undefined display keys — {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryDisplayPanelAlsoHasAControlsEntry(DA40Variant variant)
    {
        // Display-only panels still need a BuildPanelControls entry or MainForm never
        // builds them and the display vars never render.
        var def = new CowsDA40Definition(variant);
        var controls = def.GetPanelControls();

        var missing = def.GetPanelDisplayVariables().Keys
                         .Where(panel => !controls.ContainsKey(panel)).ToList();

        Assert.True(missing.Count == 0, $"{variant}: display panels with no controls entry — {string.Join(", ", missing)}");
    }

    [Fact]
    public void ElectricalPanelExposesEverySwitchOnThePanel()
    {
        var controls = Ng().GetPanelControls()["Electrical"];

        Assert.Contains("DA40_ELEC_MASTER_BATTERY", controls);
        Assert.Contains("DA40_ELEC_AVIONICS_MASTER", controls);
        Assert.Contains("DA40_ELEC_ESS_BUS", controls);
        Assert.Contains("DA40_ELEC_EMER_BATT", controls);
        Assert.Contains("DA40_ELEC_EMER_BATT_COVER", controls);
        // Engine Master is NOT here — it belongs to Engine Start.
        Assert.DoesNotContain("DA40_START_ENGINE_MASTER", controls);
        Assert.Equal(5, controls.Count);
    }

    [Fact]
    public void ElectricalPanelHasNoAlternatorSwitch()
    {
        // The AE300's alternator is ECU-controlled — there is no switch. STATE_ALTERNATOR
        // reads 0 while the alternator produces 28 A, so offering it as a toggle would be
        // both non-functional and a lie about the aeroplane.
        var vars = Ng().GetVariables();

        Assert.DoesNotContain(vars, kv => kv.Value.Name == "STATE_ALTERNATOR");
    }

    [Fact]
    public void ElectricalStatusDisplayCoversEveryBusAndBattery()
    {
        var display = Ng().GetPanelDisplayVariables()["Electrical"];

        // Per-bus, not a single "electrical OK" — raw values are the point.
        Assert.Contains("DA40_ELEC_BUS_MAIN_VOLT", display);
        Assert.Contains("DA40_ELEC_BUS_ESS_VOLT", display);
        Assert.Contains("DA40_ELEC_BUS_EMER_VOLT", display);
        Assert.Contains("DA40_ELEC_BUS_HOT_VOLT", display);
        Assert.Contains("DA40_ELEC_BUS_ECU1_VOLT", display);

        // All three batteries.
        Assert.Contains("DA40_ELEC_BATT_PERCENT", display);
        Assert.Contains("DA40_ELEC_BATT_ECU_PERCENT", display);
        Assert.Contains("DA40_ELEC_BATT_EMER_VOLT", display);

        Assert.Contains("DA40_ELEC_ALT_AMPS", display);
    }

    [Fact]
    public void ElectricalReadoutsAreReadOnlyAndCarryUnits()
    {
        var vars = Ng().GetVariables();

        foreach (var key in Ng().GetPanelDisplayVariables()["Electrical"])
        {
            var v = vars[key];
            Assert.True(v.RenderAsReadOnlyStatus, $"{key} should be a read-only status field");
            Assert.False(string.IsNullOrWhiteSpace(v.Units), $"{key} has no units");
            Assert.False(string.IsNullOrWhiteSpace(v.DisplayName), $"{key} has no display name");
        }
    }

    [Fact]
    public void ElectricalSwitchesAreTwoStateCombosWithLabels()
    {
        var vars = Ng().GetVariables();

        foreach (var key in Ng().GetPanelControls()["Electrical"])
        {
            var v = vars[key];
            Assert.Equal(2, v.ValueDescriptions.Count);
            Assert.False(v.RenderAsReadOnlyStatus, $"{key} must be settable");
        }
    }

    // ==============================================================================
    // Mixture and Propeller (XLS) - the panel the XLS exists for: per-cylinder EGT and CHT,
    // the lean assist, and the states the mixture can put the engine in.
    [Fact]
    public void XlsMixtureAndPropellerCarriesTheCylindersAndTheLeanAssist()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        var controls = def.GetPanelControls()["Mixture and Propeller"];
        var display = def.GetPanelDisplayVariables()["Mixture and Propeller"];

        Assert.Contains("DA40_XLS_BEST_MIXTURE", controls);
        Assert.Contains("DA40_XLS_PROP_CYCLE", controls);
        // The levers themselves stay on Power and Levers - one control, one panel.
        Assert.DoesNotContain("DA40_XLS_MIXTURE_SET", controls);
        Assert.DoesNotContain("DA40_XLS_PROP_SET", controls);

        // The REGIME leads, because it changes what every row under it means: with
        // automixture on the lever is choosing an air/fuel ratio rather than driving the
        // valve, and the engine cannot be flooded at all. The lean assist follows it -
        // it is what a pilot actually leaning is listening for.
        Assert.Equal("DA40_XLS_AUTOMIXTURE", display[0]);
        Assert.Equal("DA40_XLS_LEAN_ASSIST", display[1]);
        foreach (var key in new[] { "DA40_XLS_CHT_HOT", "DA40_XLS_EGT_HOT", "DA40_XLS_EGT_1", "DA40_XLS_CHT_1",
                     "DA40_XLS_RED_BOX", "DA40_XLS_FOULING", "DA40_XLS_SHOCK_COOLING", "DA40_XLS_CYL_HEALTH",
                     "DA40_XLS_PROP_PRIME", "DA40_XLS_AFR" })
        {
            Assert.Contains(key, display);
        }
    }

    // Engine Start panel (XLS) - the start scan: readiness first, then what a pilot watches
    // while the starter turns. The ignition key is NOT here: on the XLS it is also the L/R/BOTH
    // selector for the run-up, and one control on two panels is the heading-bug mistake.
    [Fact]
    public void XlsEngineStartIsTheStartScan_AndTheKeyStaysOnMagnetos()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        var controls = def.GetPanelControls()["Engine Start"];
        var display = def.GetPanelDisplayVariables()["Engine Start"];

        // Auto-start and its counterpart: the aircraft binds ENGINE_AUTO_SHUTDOWN too
        // (mixture to cut-off, pump off, then throttle closed and the key to OFF once the
        // engine has stopped) - a one-button shutdown a blind pilot otherwise does across
        // three panels.
        Assert.Equal(new[] { "DA40_XLS_AUTO_START", "DA40_XLS_AUTO_SHUTDOWN" }, controls);
        Assert.Equal("DA40_XLS_START_READY", display[0]);
        foreach (var key in new[] { "DA40_XLS_AUTO_STEP", "DA40_XLS_COMBUSTION", "DA40_XLS_CRANK_RPM",
                     "DA40_MAG_STARTER", "DA40_XLS_RPM", "DA40_XLS_OIL_PRESSURE",
                     "DA40_XLS_FUEL_PRESSURE", "DA40_PRIME_CYL_1" })
        {
            Assert.Contains(key, display);
        }
        Assert.DoesNotContain("DA40_MAG_KEY", display);
        Assert.DoesNotContain("DA40_MAG_KEY", controls);
    }

    // Engine Start panel (NG)
    // ==============================================================================

    [Fact]
    public void EngineStartExposesTheKeyAndTheEngineMaster()
    {
        var controls = Ng().GetPanelControls()["Engine Start"];

        Assert.Contains("DA40_START_STARTER_ENGAGE", controls);
        Assert.Contains("DA40_START_STARTER_RELEASE", controls);
        // The AFM legend groups the Engine Master with the engine controls, so it lives
        // here — and ONLY here.
        Assert.Contains("DA40_START_ENGINE_MASTER", controls);
        Assert.Contains("DA40_START_ENGINE_MASTER_COVER", controls);
    }

    [Fact]
    public void StartKeyPositionIsReadOnly_BecauseTheLvarIsADerivedMirror()
    {
        // Writing L:STARTER_SWITCH does nothing (measured: wrote 1, read back 0). It is
        // computed from the battery master and the starter, so it can only be a readout.
        var v = Ng().GetVariables()["DA40_START_KEY_POSITION"];

        Assert.True(v.RenderAsReadOnlyStatus);
        Assert.Equal("STARTER_SWITCH", v.Name);
        Assert.Equal(3, v.ValueDescriptions.Count);
        Assert.Equal("Start", v.ValueDescriptions[2]);
    }

    [Fact]
    public void StarterButtonsAreMomentaryAndCarryTheAfmLimit()
    {
        var vars = Ng().GetVariables();

        foreach (var key in new[] { "DA40_START_STARTER_ENGAGE", "DA40_START_STARTER_RELEASE" })
        {
            var v = vars[key];
            Assert.True(v.RenderAsButton, $"{key} should be a button");
            Assert.True(v.SuppressRestingButtonState, $"{key} has no meaningful resting value");
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Never, v.UpdateFrequency);
        }
    }

    [Fact]
    public void EngineStartScanCoversTheAfmStartChecks()
    {
        var display = Ng().GetPanelDisplayVariables()["Engine Start"];

        // AFM 4A.5.3: glow, then crank, then oil pressure out of the red within 3 s,
        // then idle RPM 710 +/- 30.
        Assert.Contains("DA40_START_GLOW_ON", display);
        Assert.Contains("DA40_START_STARTER_ENGAGED", display);
        Assert.Contains("DA40_START_COMBUSTION", display);
        Assert.Contains("DA40_START_OIL_PRESSURE", display);
        Assert.Contains("DA40_START_RPM", display);
        Assert.Contains("DA40_START_GEARBOX_TEMP", display);
    }

    [Fact]
    public void XlsHasNoNgEngineStartVariables()
    {
        // The XLS starts on magnetos and a primer, not a FADEC glow-plug cycle.
        var vars = Xls().GetVariables();

        Assert.DoesNotContain("DA40_START_GLOW_ON", vars.Keys);
        Assert.DoesNotContain("DA40_START_STARTER_ENGAGE", vars.Keys);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoControlAppearsInTwoPanels(DA40Variant variant)
    {
        // One control, one home. A variable reachable from two panels is disorienting
        // with a screen reader and makes "where is that switch" ambiguous.
        var dupes = new CowsDA40Definition(variant).GetPanelControls()
            .SelectMany(p => p.Value.Select(k => new { p.Key, Var = k }))
            .GroupBy(x => x.Var)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} in {string.Join(" + ", g.Select(x => x.Key))}")
            .ToList();

        Assert.True(dupes.Count == 0, $"{variant}: duplicated controls — {string.Join("; ", dupes)}");
    }

    // ==============================================================================
    // ECU panel (NG)
    // ==============================================================================

    [Fact]
    public void EcuPanelHasTheVoterAndTheTest()
    {
        var controls = Ng().GetPanelControls()["ECU"];

        Assert.Contains("DA40_ECU_VOTER", controls);
        Assert.Contains("DA40_ECU_TEST", controls);

        // ONE BUTTON, TWO FUNCTIONS. The aircraft's own Tips and Help page: "Reseting
        // failures/Charging battery - Press and hold the ECU test button for 10 seconds
        // with the engine master turned off." Same button as the test, told apart by the
        // engine master and the duration, so it earns its own control rather than a
        // footnote on the test's help text.
        Assert.Contains("DA40_ECU_RESET_HOLD", controls);
        Assert.Equal(3, controls.Count);
    }

    [Fact]
    public void EcuVoterUsesTheModelsOwnLabelOrder_NotTheIntuitiveOne()
    {
        // The obvious guess is A / AUTO / B. The model's ANIMTIPs say otherwise, and the
        // tooltips are what a sighted pilot actually reads off the switch.
        var v = Ng().GetVariables()["DA40_ECU_VOTER"];

        Assert.Equal("ECU B", v.ValueDescriptions[0]);
        Assert.Equal("Auto",  v.ValueDescriptions[1]);
        Assert.Equal("ECU A", v.ValueDescriptions[2]);
    }

    [Fact]
    public void EcuTestIsAButtonAndNotAToggle()
    {
        // ECU_TEST:1 is a *_Held button the airframe zeroes every frame; a combo would
        // write once and the test would never run.
        var v = Ng().GetVariables()["DA40_ECU_TEST"];

        Assert.True(v.RenderAsButton);
        Assert.True(v.SuppressRestingButtonState);
        Assert.Empty(v.ValueDescriptions);
    }

    [Fact]
    public void EcuScanShowsAllFiveAfmPreconditions()
    {
        var display = Ng().GetPanelDisplayVariables()["ECU"];

        Assert.Contains("DA40_ECU_PRE_POWER_LEVER", display);
        Assert.Contains("DA40_ECU_PROP_SENSED", display);
        Assert.Contains("DA40_ECU_PRE_GEARBOX", display);
        Assert.Contains("DA40_ECU_PRE_ON_GROUND", display);
        // The fifth (voter in Auto) is the control itself, on the same panel.
        Assert.Contains("DA40_ECU_VOTER", Ng().GetPanelControls()["ECU"]);
    }

    [Fact]
    public void EcuScanDistinguishesLatchedFromUnlatchedFaults()
    {
        // The POH's whole ECU-failure procedure turns on this distinction: an unlatched
        // error clears with a voter cycle, a latched one only via the MFD Reset: ECU.
        var display = Ng().GetPanelDisplayVariables()["ECU"];

        Assert.Contains("DA40_ECU_FAIL_A", display);
        Assert.Contains("DA40_ECU_FAIL_B", display);
        Assert.Contains("DA40_ECU_LATCH_A", display);
        Assert.Contains("DA40_ECU_LATCH_B", display);
    }

    [Fact]
    public void XlsHasNoEcuPanelOrVariables()
    {
        Assert.DoesNotContain("ECU", Xls().GetPanelStructure()["Instrument Panel"]);
        Assert.DoesNotContain("DA40_ECU_VOTER", Xls().GetVariables().Keys);
    }

    // ==============================================================================
    // Lighting Switches panel
    // ==============================================================================

    [Fact]
    public void LightingCoversEverySwitchTheAfmNames()
    {
        // AFM abbreviation list: LANDING, TAXI/MAP, POSITION, STROBE, INST. LT, FLOOD.
        var controls = Ng().GetPanelControls()["Lighting Switches"];

        Assert.Contains("DA40_LIGHT_LANDING", controls);
        Assert.Contains("DA40_LIGHT_TAXI", controls);
        Assert.Contains("DA40_LIGHT_POSITION", controls);
        Assert.Contains("DA40_LIGHT_STROBE", controls);
        Assert.Contains("DA40_LIGHT_INSTRUMENT_SET", controls);
        Assert.Contains("DA40_LIGHT_FLOOD_SET", controls);
    }

    [Fact]
    public void EachCabinLightIsIndividuallySwitchable()
    {
        // Three overhead switches on the aeroplane, so three controls here. The
        // all-at-once shortcut is an extra, never a replacement.
        var controls = Ng().GetPanelControls()["Lighting Switches"];

        Assert.Contains("DA40_LIGHT_CABIN_RIGHT", controls);
        Assert.Contains("DA40_LIGHT_CABIN_LEFT", controls);
        Assert.Contains("DA40_LIGHT_CABIN_BAGGAGE", controls);
        // The COWS all-at-once clickspot is a mouse shortcut, not a panel control, and
        // the three switches already do everything it does.
        Assert.DoesNotContain("DA40_LIGHT_CABIN_ALL", controls);
        Assert.Equal(9, controls.Count);
    }

    [Fact]
    public void InstrumentAndFloodAreBrightnessKnobs_NotOnOffSwitches()
    {
        // AFM legend item 10 is "Rotary buttons for instrument lighting and flood light",
        // and the model drives them from LIGHT POTENTIOMETER:3 and :5 as percentages.
        var vars = Ng().GetVariables();

        foreach (var key in new[] { "DA40_LIGHT_INSTRUMENT_SET", "DA40_LIGHT_FLOOD_SET" })
        {
            var v = vars[key];
            Assert.True(v.RenderAsSlider, $"{key} should be a brightness slider");
            Assert.Equal(0, v.SliderMin);
            Assert.Equal(100, v.SliderMax);
            Assert.Empty(v.ValueDescriptions);
        }

        Assert.Equal("LIGHT POTENTIOMETER:3", vars["DA40_LIGHT_INSTRUMENT_SET"].Name);
        Assert.Equal("LIGHT POTENTIOMETER:5", vars["DA40_LIGHT_FLOOD_SET"].Name);
    }

    [Fact]
    public void IceLightIsStatusOnly_ThereIsNoSwitchForIt()
    {
        var def = Ng();

        Assert.Contains("DA40_LIGHT_ICE_STATE", def.GetPanelDisplayVariables()["Lighting Switches"]);
        Assert.DoesNotContain("DA40_LIGHT_ICE_STATE", def.GetPanelControls()["Lighting Switches"]);
    }

    [Fact]
    public void EcuShowsOnePropellerReading_NotTwoNearIdenticalOnes()
    {
        var def = Ng();

        Assert.Contains("DA40_ECU_PROP_SENSED", def.GetPanelDisplayVariables()["ECU"]);
        Assert.DoesNotContain("DA40_ECU_PRE_PROP_RPM", def.GetVariables().Keys);
        // The sensed speed is the one the stage machine gates on.
        Assert.Equal("PROP_RPM_SENS:1", def.GetVariables()["DA40_ECU_PROP_SENSED"].Name);
    }

    [Fact]
    public void EveryLightSwitchIsTwoPosition()
    {
        // No multi-position light switches exist on this airframe; the only three-position
        // switches are the ECU voter, the ignition key and the fuel selector.
        var vars = Ng().GetVariables();

        foreach (var key in Ng().GetPanelControls()["Lighting Switches"])
        {
            var v = vars[key];
            if (v.RenderAsSlider) continue;   // the two brightness knobs
            Assert.Equal(2, v.ValueDescriptions.Count);
        }
    }

    // ==============================================================================
    // Ice and Pitot panel
    // ==============================================================================

    [Fact]
    public void IceAndPitotHasAllThreeControls()
    {
        var controls = Ng().GetPanelControls()["Ice and Pitot"];

        Assert.Contains("DA40_ICE_PITOT_HEAT", controls);
        // Not on the AFM legend, but an OPEN/CLOSED item throughout the procedures.
        Assert.Contains("DA40_ICE_ALTERNATE_AIR", controls);
        Assert.Contains("DA40_ICE_ALTERNATE_STATIC", controls);
        Assert.Equal(3, controls.Count);
    }

    [Fact]
    public void IceLightStaysOnLighting_NotDuplicatedHere()
    {
        var def = Ng();

        Assert.Contains("DA40_LIGHT_ICE_STATE", def.GetPanelDisplayVariables()["Lighting Switches"]);
        Assert.DoesNotContain("DA40_LIGHT_ICE_STATE", def.GetPanelDisplayVariables()["Ice and Pitot"]);
    }

    [Fact]
    public void InductionAirFactorIsOnTheScan_SoTheDoorCanBeSeenWorking()
    {
        // Opening alternate air moved ENG_ALT_AIR_FACTOR from 1.00 to 0.98 live; without
        // it there is no way to tell the door did anything.
        Assert.Contains("DA40_ICE_ALT_AIR_FACTOR", Ng().GetPanelDisplayVariables()["Ice and Pitot"]);
    }

    // ==============================================================================
    // Auto-announce and the Monitor Manager
    // ==============================================================================

    /// <summary>
    /// Controls whose value is a NUMBER rather than a state.
    /// Adding one here is a deliberate decision that its value is a NUMBER to be read, not
    /// an event to be announced.
    ///
    /// The line is not "is it numeric" — the standby altimeter subscale is numeric and DOES
    /// announce, because it is DIALLED to discrete settings and each change is one
    /// deliberate act. These are different: the lever and the trim are swept by hardware,
    /// and a payload station runs continuously while GSX boards passengers. Announcing any
    /// of them would speak a new number several times a second over everything else.
    /// </summary>
    private static readonly string[] SilentNumericControls =
    {
        // ⚠️ WRITE-ONLY TRANSACTION FIELDS. These are not switches and have no state of
        // their own to mirror: the pilot types how many gallons they want in a tank and
        // the fuel goes in. What the tank actually HOLDS is DA40_FUEL_MAIN_ACTUAL and
        // DA40_FUEL_AUX_ACTUAL, which ARE Continuous and announced - so a tank filling
        // from GSX, or draining through a leak, still speaks. Announcing the request as
        // well would say the same number twice.
        "DA40_FUEL_MAIN_LOAD_SET",
        "DA40_FUEL_AUX_LOAD_SET",
        // The XLS tank loads: the same typed-gallons transaction, left and right.
        "DA40_XLS_FUEL_LEFT_LOAD_SET",
        "DA40_XLS_FUEL_RIGHT_LOAD_SET",
        // Swept by hardware - a throttle quadrant, a trim wheel.
        "DA40_POWER_LEVER_SET",
        "DA40_TRIM_SET",
        // Run continuously while GSX boards passengers.
        "DA40_PAYLOAD_PILOT_SET",
        "DA40_PAYLOAD_FRONT_PAX_SET",
        "DA40_PAYLOAD_REAR_LEFT_SET",
        "DA40_PAYLOAD_REAR_RIGHT_SET",
        "DA40_PAYLOAD_BAGGAGE_SET",
        // The autopilot's selected values. Each is a NUMBER the pilot types, not a
        // switch: the altitude preselect sweeping past on a hardware knob would otherwise
        // announce every hundred feet, and the heading bug every degree.
        "DA40_AP_ALT_SET", "DA40_AP_VS_SET", "DA40_AP_IAS_SET",
        "DA40_AP_HDG_SET", "DA40_AP_CRS_SET",
        // ⚠️ A WRITE-ONLY PROXY, not a silent switch. DA40_G1000_BARO_SET exists only to
        // give MainForm a key containing "_SET" so the G1000 subscale gets a text box
        // instead of a bare button; it binds no SimVar of its own. The announcing is done
        // by DA40_G1000_BARO, the readout it is seeded from, which has the baro settle
        // announcer on it - so an external change to the subscale IS spoken, just not by
        // this key.
        "DA40_G1000_BARO_SET",
        // Radio frequencies and courses: numbers, typed once.
        "DA40_RADIO_COM1_SET", "DA40_RADIO_COM2_SET",
        "DA40_RADIO_NAV1_SET", "DA40_RADIO_NAV2_SET",
        // Failure SEVERITIES. Nothing outside MSFSBA sets these, so there is no background
        // change to miss, and a percentage is a number to be read like any other.
        "DA40_FAIL_COOLANT_LEAK_SET",
        "DA40_FAIL_CHT_BAFFLE",
        "DA40_FAIL_TURBO_SET",
        "DA40_FAIL_VACC_LEAK",
        "DA40_FAIL_BOOST_LEAK_SET",
        "DA40_FAIL_FUEL_PUMP",
        "DA40_FAIL_FUEL_SPRING",
        "DA40_FAIL_FUEL_LEAK",
        "DA40_FAIL_FUEL_LEAK_L",
        "DA40_FAIL_FUEL_LEAK_R",
        "DA40_FAIL_INJ_1",
        "DA40_FAIL_INJ_2",
        "DA40_FAIL_INJ_3",
        "DA40_FAIL_INJ_4"
    };

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EverySettableSwitchAnnouncesExternalChanges(DA40Variant variant)
    {
        // A switch thrown in the cockpit, by hardware, or by a failure MUST speak.
        // IsAnnounced governs BACKGROUND changes only — MSFSBA's own combo sets are
        // suppressed by the global _uiSetEcho wrap, so this does not cause double-talk.
        // Continuous is required as well: OnRequest vars are never polled, so an external
        // change is simply never seen.
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        var silent = def.GetPanelControls()
            .SelectMany(p => p.Value)
            .Distinct()
            .Select(k => new { Key = k, Def = vars[k] })
            // Buttons are momentary and have no state to announce; sliders are numeric.
            .Where(x => !x.Def.RenderAsButton && !x.Def.RenderAsSlider)
            // A NUMBER is not a switch - see the list for where that line falls, and why
            // the standby subscale is on the other side of it.
            .Where(x => !SilentNumericControls.Contains(x.Key))
            .Where(x => !x.Def.IsAnnounced
                     || x.Def.UpdateFrequency != MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous)
            .Select(x => x.Key)
            .ToList();

        Assert.True(silent.Count == 0,
            $"{variant}: these switches will not announce external changes — {string.Join(", ", silent)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NumericReadoutsStaySilent(DA40Variant variant)
    {
        // Volts, temperatures and RPM change constantly. Announcing them would bury the
        // changes that matter; they are read on demand from the status display instead.
        //
        // Scoped to the DA40's OWN readouts. A shared MON_* variable from the base is
        // announced by MSFSBA's own debounced custom logic for every aircraft - the trim
        // call-out is the live example - so its IsAnnounced is not this def's decision to
        // make, and reusing one rather than defining a second key is the whole point.
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        // A variable can be IsAnnounced and still silent: batch membership REQUIRES
        // IsAnnounced (Continuous alone does not get a variable polled), so anything a
        // hotkey reads from the cache has to carry the flag and be silenced instead in
        // ProcessSimVarUpdate, which returns true for it.
        var noisy = def.GetPanelDisplayVariables()
            .SelectMany(p => p.Value)
            .Distinct()
            .Where(k => k.StartsWith("DA40_"))
            .Where(k => vars[k].IsAnnounced)
            .Where(k => !CowsDA40Definition.SilentCachedReadoutKeys.Contains(k))
            // The radio frequencies are the deliberate exception. They carry IsAnnounced
            // because batch membership REQUIRES it - without it the cache was empty, which
            // is exactly why a swap announced a frequency the radio did not hold - but
            // they never reach the generic announcer: NoteRadioChange intercepts them and
            // speaks the settled value the RADIO reported.
            .Where(k => !CowsDA40Definition.RadioAnnouncedKeys.Contains(k))
            // ENGINE HEALTH is the same shape of exception, one step further on. These
            // carry IsAnnounced to reach the batch, and NoteEngineHealth intercepts them:
            // health sitting at 100 percent is not news and a percentage that drifts would
            // chatter, so only a material FALL speaks. That is a number worth interrupting
            // a pilot for - damage happens during something, and the moment it starts is
            // the moment they can still do something about it.
            .Where(k => !CowsDA40Definition.HealthKeyNames.Contains(k))
            // GRADED values - the coolant leak, the turbo, the boost leak and the induction
            // filter restriction. Percentages that CLIMB, whose onset is the news and whose
            // value is not, so NoteGradedFailure intercepts them and speaks once when they
            // start rather than on every percent.
            .Where(k => !CowsDA40Definition.GradedFailureKeys.Contains(k))
            // A DESCRIBED STATE IS NOT A NUMERIC READOUT, and this test is about numbers.
            // The standby gyro's CAGED and TOPPLED flags carry ValueDescriptions and are
            // 0/1: an instrument that has been caged, or has tumbled and is showing a
            // plausible lie, interrupts a sighted pilot the moment they look at it, so by
            // this aircraft's own announcement rule it must interrupt a blind one too.
            .Where(k => vars[k].ValueDescriptions is not { Count: > 0 })
            .ToList();

        Assert.True(noisy.Count == 0,
            $"{variant}: these readouts would speak on every change — {string.Join(", ", noisy)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryAnnouncedSwitchGetsAMonitorManagerRow(DA40Variant variant)
    {
        // Ctrl+M must be able to mute anything that speaks, or the pilot has no control
        // over the chatter. MonitorRowBuilder lists Continuous + IsAnnounced vars.
        var def = new CowsDA40Definition(variant);
        var rows = MSFSBlindAssist.Services.MonitorRowBuilder.Build(def.GetVariables());
        var rowKeys = rows.Select(r => r.Key).ToHashSet();

        var announced = def.GetVariables()
            .Where(kv => kv.Value.IsAnnounced
                      && kv.Value.UpdateFrequency == MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous
                      && !kv.Value.ExcludeFromMonitorManager)
            .Select(kv => kv.Key);

        foreach (var key in announced)
        {
            Assert.True(rowKeys.Contains(key), $"{variant}: {key} announces but has no Ctrl+M row");
        }

        Assert.NotEmpty(rows);
    }

    [Fact]
    public void MonitorRowsCarryReadableLabels()
    {
        var rows = MSFSBlindAssist.Services.MonitorRowBuilder.Build(Ng().GetVariables());

        foreach (var row in rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Label));
            // The label should be the display name, not a raw DA40_ key.
            Assert.False(row.Label.StartsWith("DA40_"), $"{row.Key} shows a raw key as its label");
        }
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void ScansDoNotRepeatControls(DA40Variant variant)
    {
        // A control already reads its own position when you tab to it. Repeating it on
        // the scan is duplication, and it drags an announcing variable into a list that
        // is supposed to be silent.
        var def = new CowsDA40Definition(variant);
        var controls = def.GetPanelControls().SelectMany(p => p.Value).ToHashSet();

        var repeated = def.GetPanelDisplayVariables()
            .SelectMany(p => p.Value)
            .Where(controls.Contains)
            .Distinct()
            .ToList();

        Assert.True(repeated.Count == 0,
            $"{variant}: controls repeated on a scan — {string.Join(", ", repeated)}");
    }

    // ==============================================================================
    // Standby Instruments panel
    // ==============================================================================

    [Fact]
    public void StandbyHasItsOwnAltimeterSubscale()
    {
        // The standby subscale is SEPARATE from the G1000's, which is why the AFM descent
        // check reads "Altimeters (2) SET".
        //
        // It READS THE MIRROR and WRITES THE INPUT, this aeroplane's own rule. The input,
        // L:KOHLSMAN SETTING HG:2, carries a space and a colon - a stock-SimVar shape that
        // defeats both normal paths at once (SetLVar's data-def fallback lands on the
        // STOCK SimVar of that name, and a data-def read in "inHg" converts a raw number
        // and returned zero). L:STATE_BARO2 is the airframe's own mirror of the same
        // subscale with a clean name, so it reads through the ordinary path; the write
        // goes to the input via SetStandbyBaro.
        var v = Ng().GetVariables()["DA40_STBY_ALTIMETER_SET"];

        Assert.Equal("STATE_BARO2", v.Name);

        // "number", never a pressure unit: an L:var holds a raw number and SimConnect
        // would convert it. This is the assertion that pins the reported zero.
        Assert.Equal("number", v.Units);
        // Typed entry, not a slider — see StandbyAltimeterIsTypedNotASlider. The 28.00 to
        // 31.50 travel is enforced on the write instead, where it belongs.
        Assert.False(v.RenderAsSlider);
    }

    [Fact]
    public void GyroCageIsAHeldButton_NotAToggle()
    {
        // ATT_CAGE is zeroed every frame; a combo would write once and nothing would happen.
        var v = Ng().GetVariables()["DA40_STBY_GYRO_CAGE"];

        Assert.True(v.RenderAsButton);
        Assert.True(v.SuppressRestingButtonState);
        Assert.Empty(v.ValueDescriptions);
    }

    [Fact]
    public void StandbyScanReportsGyroHealth_NotJustAttitude()
    {
        // A toppled gyro shows a plausible lie. Spin, rigidity and topple are the only way
        // to know the instrument has stopped being trustworthy.
        var display = Ng().GetPanelDisplayVariables()["Standby Instruments"];

        Assert.Contains("DA40_STBY_GYRO_PITCH", display);
        Assert.Contains("DA40_STBY_GYRO_BANK", display);
        Assert.Contains("DA40_STBY_GYRO_SPEED", display);
        Assert.Contains("DA40_STBY_GYRO_TOPPLE", display);
        Assert.Contains("DA40_STBY_GYRO_CAGED", display);
    }

    [Fact]
    public void StandbyCoversAllFourBackupInstruments()
    {
        // AFM legend 17-20: airspeed, artificial horizon, altimeter, compass.
        var display = Ng().GetPanelDisplayVariables()["Standby Instruments"];

        Assert.Contains("DA40_STBY_AIRSPEED", display);
        Assert.Contains("DA40_STBY_ALTITUDE", display);
        Assert.Contains("DA40_STBY_COMPASS", display);
        Assert.Contains("DA40_STBY_GYRO_PITCH", display);
    }

    [Fact]
    public void PropellerRpmUsesTheFineSource_NotTheQuantisedEisValue()
    {
        // DISP_PROP_RPM is rounded to 10 by the airframe (measured 710 against a true
        // 705.12). The needle moves smoothly, so the sensed value is what matches it.
        Assert.Equal("PROP_RPM_SENS:1", Ng().GetVariables()["DA40_START_RPM"].Name);
    }

    // ==============================================================================
    // Annunciators panel
    // ==============================================================================

    [Fact]
    public void AnnunciatorsAreReadOnly_BecauseALampIsAnOutput()
    {
        var def = Ng();

        Assert.Empty(def.GetPanelControls()["Annunciators"]);
        Assert.NotEmpty(def.GetPanelDisplayVariables()["Annunciators"]);
    }

    [Fact]
    public void AnnunciatorsCoverThePhysicalLampsAndWhatCanExtinguishThem()
    {
        // Three flap lights and the essential-bus lamp are the only real lamps on the
        // G1000 variant. A flap light can be dark for three different reasons, so the
        // breakers and the actual flap travel sit beside them.
        var display = Ng().GetPanelDisplayVariables()["Annunciators"];

        Assert.Contains("DA40_ANN_FLAP_UP", display);
        Assert.Contains("DA40_ANN_FLAP_TO", display);
        Assert.Contains("DA40_ANN_FLAP_LDG", display);
        Assert.Contains("DA40_ANN_ESS_BUS_VOLTS", display);
        Assert.Contains("DA40_ANN_CB_FLAP", display);
        Assert.Contains("DA40_ANN_FLAP_TRAVEL", display);
    }

    [Fact]
    public void StandbyAltimeterIsTypedNotASlider()
    {
        // MainForm's TrackBar is hardcoded 0-100 and maps the value as a percentage of the
        // slider range, so a subscale rendered as one reported "0 to 100" instead of
        // 28 to 31.5. The key ends in _SET, which gives a typed entry instead.
        var v = Ng().GetVariables()["DA40_STBY_ALTIMETER_SET"];

        Assert.False(v.RenderAsSlider);
        Assert.False(v.PreventTextInput);
        Assert.Contains("_SET", "DA40_STBY_ALTIMETER_SET");
    }

    [Fact]
    public void StandbyScanShowsIndicatedAndActualAttitude()
    {
        // The standby horizon drifts. Measured 2.2 degrees indicated against a true -3.0,
        // and the only way to notice is to compare the two.
        var display = Ng().GetPanelDisplayVariables()["Standby Instruments"];

        Assert.Contains("DA40_STBY_GYRO_PITCH", display);
        Assert.Contains("DA40_STBY_TRUE_PITCH", display);
        Assert.Contains("DA40_STBY_GYRO_BANK", display);
        Assert.Contains("DA40_STBY_TRUE_BANK", display);
    }

    // ==============================================================================
    // Output-mode readouts
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void ReadoutHotkeysHaveTheirVariablesInTheCache(DA40Variant variant)
    {
        // A readout answers from the SimConnect cache, and only Continuous variables are
        // in it. An OnRequest variable here would make the key answer a stale zero, which
        // is indistinguishable from a broken key.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (var key in new[] { "DA40_G1000_BARO", "DA40_FLAPS_POSITION" })
        {
            Assert.True(vars.ContainsKey(key), $"{variant}: {key} missing");
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous,
                         vars[key].UpdateFrequency);
        }
    }

    [Fact]
    public void ReadoutSupportVariablesDoNotCluttertheMonitorManager()
    {
        // Silent plumbing for the hotkeys gets no Ctrl+M row, because the checkbox would
        // mute nothing.
        var vars = Ng().GetVariables();

        // THE G1000 SUBSCALE IS THE EXCEPTION, and deliberately so. It used to be silent
        // plumbing, which is exactly why a subscale changed on external hardware said
        // nothing at all. It now announces once the knob settles, so it MUST have a Ctrl+M
        // row - a checkbox that silences nothing should not exist, and the converse is
        // that one which does, must.
        Assert.False(vars["DA40_G1000_BARO"].ExcludeFromMonitorManager);
        Assert.True(vars["DA40_G1000_BARO"].IsAnnounced);
        Assert.DoesNotContain("DA40_G1000_BARO", CowsDA40Definition.SilentCachedReadoutKeys);

        // DA40_FLAPS_POSITION is NOT plumbing any more. The Flaps panel promoted the same
        // key into its selector control rather than defining a second copy, so it now
        // announces external changes and earns its Ctrl+M row like every other switch.
        Assert.False(vars["DA40_FLAPS_POSITION"].ExcludeFromMonitorManager);
        Assert.True(vars["DA40_FLAPS_POSITION"].IsAnnounced);
    }

    // ==============================================================================
    // Power and Levers panel (NG)
    // ==============================================================================

    [Fact]
    public void NgPowerPanelHasExactlyOneLever()
    {
        // The FADEC sets propeller and mixture from the power lever, so the NG pedestal
        // is a single quadrant. A prop or mixture control here would be inventing one.
        var controls = Ng().GetPanelControls()["Power and Levers"];

        Assert.Equal(new[] { "DA40_POWER_LEVER_SET" }, controls.ToArray());
    }

    [Fact]
    public void PowerScanShowsCommandedRpmBesideActual()
    {
        // The lever commands LOAD, and commanded RPM is not proportional to it — it FALLS
        // from 2150 at idle to 1800 at 20 percent before climbing. Without the commanded
        // figure there is no way to tell the propeller is being used as an airbrake.
        var display = Ng().GetPanelDisplayVariables()["Power and Levers"];

        Assert.Contains("DA40_POWER_TARGET_RPM", display);
        Assert.Contains("DA40_POWER_RPM", display);
        Assert.Contains("DA40_POWER_LOAD", display);
    }

    [Fact]
    public void BothFadecLeverChannelsAreReported()
    {
        // The FADEC reads the lever once per ECU and the model gives each channel its own
        // failure modes. Two channels disagreeing IS the symptom of a lever-sensor fault.
        var display = Ng().GetPanelDisplayVariables()["Power and Levers"];

        Assert.Contains("DA40_POWER_LEVER_A", display);
        Assert.Contains("DA40_POWER_LEVER_B", display);
    }

    [Fact]
    public void XlsHasNoNgPowerPanelVariables()
    {
        Assert.DoesNotContain("DA40_POWER_LEVER_SET", Xls().GetVariables().Keys);
    }

    // ==============================================================================
    // Fuel System panel (NG)
    // ==============================================================================

    [Fact]
    public void FuelPanelCarriesTheWireBreakerBesideTheValve()
    {
        // The valve CANNOT leave Main until the safety wire is broken - verified live.
        // Two AFM procedures need it moved (Emergency feed, and Off for an engine fire),
        // so without a way to break the wire a blind pilot could not fly either one.
        var controls = Ng().GetPanelControls()["Fuel System"];

        Assert.Contains("DA40_FUEL_VALVE", controls);
        Assert.Contains("DA40_FUEL_WIRE", controls);
    }

    [Fact]
    public void FuelValveOffersAllThreePositionsInTheModelsOwnOrder()
    {
        // MAIN / EMERGENCY / OFF, from the model's ANIMTIPs - not an assumed ordering.
        var v = Ng().GetVariables()["DA40_FUEL_VALVE"];

        Assert.Equal("Main", v.ValueDescriptions![0]);
        Assert.Equal("Emergency", v.ValueDescriptions[1]);
        Assert.Equal("Off", v.ValueDescriptions[2]);
    }

    [Fact]
    public void FuelScanReportsBothIndicatedAndMeasuredQuantities()
    {
        // The gauge saturates at 14 US gal: measured live, the tank held 18.78 while the
        // gauge read exactly 14.0. Reporting only the indication would tell a blind pilot
        // the tank holds 14 gallons, which is not what the instrument means.
        var display = Ng().GetPanelDisplayVariables()["Fuel System"];

        Assert.Contains("DA40_FUEL_MAIN_IND", display);
        Assert.Contains("DA40_FUEL_MAIN_ACTUAL", display);
        Assert.Contains("DA40_FUEL_AUX_IND", display);
        Assert.Contains("DA40_FUEL_AUX_ACTUAL", display);
    }

    [Fact]
    public void TankDifferenceIsListedAfterBothTanks()
    {
        // The difference is computed from the two quantities as they render, in list
        // order. Moving it above either tank would silently compute it from a stale pair.
        var display = Ng().GetPanelDisplayVariables()["Fuel System"];

        int main = display.IndexOf("DA40_FUEL_MAIN_ACTUAL");
        int aux = display.IndexOf("DA40_FUEL_AUX_ACTUAL");
        int diff = display.IndexOf("DA40_FUEL_DIFFERENCE");

        Assert.True(diff > main && diff > aux,
            "the tank difference must render after both tank quantities");
    }

    [Fact]
    public void TransferPumpReportsWhetherItIsActuallyRunning()
    {
        // The switch can be ON with the pump stopped for four different reasons - main
        // tank full, auxiliary empty, breaker out, bus volts low - and the AFM says the
        // switch deliberately stays put when the pump stops itself.
        var display = Ng().GetPanelDisplayVariables()["Fuel System"];

        Assert.Contains("DA40_FUEL_TRANSFER_RUNNING", display);
        Assert.Contains("DA40_FUEL_CB_XFER", display);
    }

    [Fact]
    public void FuelQuantityAndTemperatureCarryTheirAfmArcs()
    {
        Assert.NotNull(DA40InstrumentBands.For("DA40_FUEL_MAIN_IND"));
        Assert.NotNull(DA40InstrumentBands.For("DA40_FUEL_MAIN_TEMP"));

        // AFM 2.5: below 1 US gal is red, and there is no lower caution band at all.
        var qty = DA40InstrumentBands.For("DA40_FUEL_MAIN_IND")!;
        Assert.Equal(GaugeBand.LowerRed, qty.Classify(0.5));
        Assert.Equal(GaugeBand.Normal, qty.Classify(7));

        // Above 60 C costs high-pressure pump efficiency.
        var temp = DA40InstrumentBands.For("DA40_FUEL_MAIN_TEMP")!;
        Assert.Equal(GaugeBand.Normal, temp.Classify(41));
        Assert.Equal(GaugeBand.UpperRed, temp.Classify(65));
    }

    [Fact]
    public void XlsHasNoNgFuelSystemControls()
    {
        Assert.DoesNotContain("DA40_FUEL_VALVE", Xls().GetVariables().Keys);
    }

    // ==============================================================================
    // Flaps panel (both variants)
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void FlapsPanelExistsOnBothVariants(DA40Variant variant)
    {
        // The flap system is identical on both airframes; only the limit speeds differ.
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Flaps"];

        Assert.Equal(new[] { "DA40_FLAPS_POSITION" }, controls.ToArray());
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void FlapSelectorUsesTheAeroplanesOwnPositionNames(DA40Variant variant)
    {
        // UP / T-O / LDG. The detents are not proportional - measured 0, 47 and 100
        // percent travel - so "flaps 1" and "half flap" would both be wrong.
        var v = new CowsDA40Definition(variant).GetVariables()["DA40_FLAPS_POSITION"];

        Assert.Equal("UP", v.ValueDescriptions![0]);
        Assert.Equal("T/O", v.ValueDescriptions[1]);
        Assert.Equal("LDG", v.ValueDescriptions[2]);
    }

    [Fact]
    public void FlapPositionIsDefinedExactlyOnce()
    {
        // It was registered in Shared.cs for the L readout before the panel existed, and
        // the panel PROMOTED it rather than adding a second copy. Two Continuous batched
        // keys sharing one SimVar Name shift every later variable's struct slot.
        var vars = Ng().GetVariables();

        var sharingTheName = vars
            .Where(kv => kv.Value.Name == "FLAPS HANDLE INDEX"
                      && kv.Value.UpdateFrequency == MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous)
            .Select(kv => kv.Key)
            .ToList();

        Assert.Single(sharingTheName);
        Assert.Equal("DA40_FLAPS_POSITION", sharingTheName[0]);
    }

    [Fact]
    public void FlapScanReportsEachSideSeparately()
    {
        // AFM 4B.5 opens with "FLAPS position ... check visually", which is the one
        // instruction in the manual a blind pilot cannot follow. A split flap is a real
        // modelled failure, so both sides and a computed asymmetry are reported.
        var display = Ng().GetPanelDisplayVariables()["Flaps"];

        Assert.Contains("DA40_FLAPS_TRAVEL_LEFT", display);
        Assert.Contains("DA40_FLAPS_TRAVEL_RIGHT", display);
        Assert.Contains("DA40_FLAPS_ASYMMETRY", display);
    }

    [Fact]
    public void FlapAsymmetryIsListedAfterBothSides()
    {
        // Computed from the two travels as they render, in list order.
        var display = Ng().GetPanelDisplayVariables()["Flaps"];

        int left = display.IndexOf("DA40_FLAPS_TRAVEL_LEFT");
        int right = display.IndexOf("DA40_FLAPS_TRAVEL_RIGHT");
        int split = display.IndexOf("DA40_FLAPS_ASYMMETRY");

        Assert.True(split > left && split > right,
            "flap asymmetry must render after both sides");
    }

    [Fact]
    public void FlapLimitSpeedsDifferBetweenVariants()
    {
        // NG 110 / 98, XLS 108 / 91 - the panel reads them from DA40Speeds rather than
        // carrying its own copy.
        Assert.Equal(110, DA40Speeds.For(DA40Variant.NG).VfeTakeoff);
        Assert.Equal(98, DA40Speeds.For(DA40Variant.NG).VfeLanding);
        Assert.Equal(108, DA40Speeds.For(DA40Variant.XLS).VfeTakeoff);
        Assert.Equal(91, DA40Speeds.For(DA40Variant.XLS).VfeLanding);
    }

    // ==============================================================================
    // No panel may be silently empty
    // ==============================================================================

    /// <summary>
    /// Panels that are in the structure but not built yet, on EITHER variant. Every name
    /// here renders as an empty panel today, which is the price of publishing the whole
    /// roadmap up front - but it has to be a DELIBERATE price, listed, and it has to
    /// shrink.
    /// </summary>
    private static readonly string[] NotBuiltYet =
    {
        // Center Console
        // Cabin
        // (The Autopilot section's GFC 700 and Flight Director panels were on this list
        // and are now BUILT - the list shrank, which is what it is for.)
    };

    /// <summary>
    /// Panels the XLS additionally has nothing behind yet. Three of them - Engine Start,
    /// Power and Levers, Fuel System - are BUILT, but only for the NG: the Lycoming has a
    /// magneto key rather than a FADEC start, three levers rather than one, and a
    /// left/right tank selector rather than a transfer pump, so none of the NG code can
    /// be reused as-is. The XLS comes after the NG is finished, by plan, and until then
    /// this list is what stops those empty panels being mistaken for broken ones.
    /// </summary>
    // Magnetos came off this list first: it is the ignition key and the starter in one,
    // built from the run-up on the live aircraft (docs/da40-xls-variables.md).
    private static readonly string[] NotBuiltYetOnXls = Array.Empty<string>();

    private static bool IsKnownUnbuilt(DA40Variant variant, string panel)
        => NotBuiltYet.Contains(panel)
           || (variant == DA40Variant.XLS && NotBuiltYetOnXls.Contains(panel));

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPanelInTheStructureIsBuiltOrKnownUnbuilt(DA40Variant variant)
    {
        // A panel with no controls and no status rows renders BLANK, and a blank panel is
        // indistinguishable from a broken one. This is how a "Fuel Transfer" panel from
        // the planning sketch survived being folded into Fuel System and was reported
        // live as empty.
        var def = new CowsDA40Definition(variant);
        var controls = def.GetPanelControls();
        var display = def.GetPanelDisplayVariables();

        var empty = def.GetPanelStructure()
            .SelectMany(section => section.Value)
            .Where(panel => !(controls.TryGetValue(panel, out var c) && c.Count > 0)
                         && !(display.TryGetValue(panel, out var d) && d.Count > 0))
            .Where(panel => !IsKnownUnbuilt(variant, panel))
            .Distinct()
            .ToList();

        Assert.True(empty.Count == 0,
            $"{variant}: these panels render empty and are not on the unbuilt list — " +
            string.Join(", ", empty));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheUnbuiltListDoesNotNameAPanelThatIsNowBuilt(DA40Variant variant)
    {
        // The other direction: building a panel must remove it from the list, or the list
        // stops being a truthful record of what is still missing.
        var def = new CowsDA40Definition(variant);
        var controls = def.GetPanelControls();
        var display = def.GetPanelDisplayVariables();

        var stale = NotBuiltYet
            .Concat(variant == DA40Variant.XLS ? NotBuiltYetOnXls : Array.Empty<string>())
            .Where(panel => (controls.TryGetValue(panel, out var c) && c.Count > 0)
                         || (display.TryGetValue(panel, out var d) && d.Count > 0))
            .ToList();

        Assert.True(stale.Count == 0,
            $"{variant}: these are built and should come off the unbuilt list — " +
            string.Join(", ", stale));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoPanelHasVariablesWithoutAppearingInTheStructure(DA40Variant variant)
    {
        // The mirror of the empty-panel bug: a panel built but never listed is
        // unreachable, and nothing else would notice.
        var def = new CowsDA40Definition(variant);
        var listed = def.GetPanelStructure().SelectMany(s => s.Value).ToHashSet();

        var orphans = def.GetPanelControls().Keys
            .Concat(def.GetPanelDisplayVariables().Keys)
            .Where(panel => !listed.Contains(panel))
            .Distinct()
            .ToList();

        Assert.True(orphans.Count == 0,
            $"{variant}: panels with content but no place in the structure — " +
            string.Join(", ", orphans));
    }

    // ==============================================================================
    // Readout hotkeys answer from the cache, which is keyed by VARIABLE KEY
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryVariableAReadoutNeedsIsContinuousOnBothVariants(DA40Variant variant)
    {
        // The B, F, L and W keys read these. SimConnectManager caches by VARIABLE KEY, and
        // only Continuous variables are in the cache at all — an OnRequest one is polled
        // only while its own panel is open. Both halves of that bit MSFSBA live: the keys
        // were passing SimVar NAMES to a key-keyed cache and reporting a full aeroplane as
        // "0 hectopascals" and "0.0 gallons".
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (var key in new[]
                 {
                     "DA40_G1000_BARO",          // B
                     "DA40_STBY_ALTIMETER_SET",  // B, the second altimeter
                     "DA40_FUEL_MAIN_ACTUAL",    // F
                     "DA40_FUEL_AUX_ACTUAL",     // F
                     "DA40_FLAPS_POSITION",      // L, and the Vfe readout
                     "DA40_GROSS_WEIGHT"         // W
                 })
        {
            Assert.True(vars.ContainsKey(key), $"{variant}: {key} is not defined");
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous,
                         vars[key].UpdateFrequency);
        }
    }

    // ==============================================================================
    // Elevator Trim panel (both variants)
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TrimPanelOffersTheWheelAndTheStickSwitch(DA40Variant variant)
    {
        // Two genuinely different paths to the same axis: the mechanical wheel (a typed
        // setting, works with everything off) and the electric stick switch (held, needs
        // its circuit). Plus a centring reference.
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Elevator Trim"];

        // AP DISC lives HERE rather than waiting for the Autopilot panel: its other job
        // is the trim interrupt, which is what the before-takeoff check exercises, and it
        // needs nothing from the autopilot to work.
        Assert.Equal(new[]
        {
            "DA40_TRIM_SET",
            "DA40_TRIM_NOSE_UP",
            "DA40_TRIM_NOSE_DOWN",
            "DA40_TRIM_CENTRE",
            "DA40_TRIM_AP_DISC"
        }, controls.ToArray());
    }

    [Fact]
    public void TrimSettingIsATypedEntryNotASlider()
    {
        // The range is -100 to +100 and MainForm's TrackBar is hardcoded 0-100, mapping
        // the value as a percentage of its own range. The standby altimeter shipped that
        // bug once already.
        var v = Ng().GetVariables()["DA40_TRIM_SET"];

        Assert.False(v.RenderAsSlider);
        Assert.False(v.RenderAsButton);
    }

    [Fact]
    public void TrimScanCarriesRunawayAndTheInterrupt()
    {
        // A runaway trim moves with nobody touching it and is otherwise silent, and the
        // AFM's remedy is the AP disconnect button, whose effect is read from here.
        var display = Ng().GetPanelDisplayVariables()["Elevator Trim"];

        Assert.Contains("DA40_TRIM_RUNAWAY", display);
        Assert.Contains("DA40_TRIM_INHIBITED", display);
        Assert.Contains("DA40_TRIM_CIRCUIT", display);
    }

    [Fact]
    public void TrimPanelOffersNoRudderOrAileronTrim()
    {
        // The DA40 has neither in the cockpit - the rudder trim is a ground-adjustable
        // tab. Offering one would be inventing a control.
        var vars = Ng().GetVariables().Keys;

        Assert.DoesNotContain("DA40_TRIM_RUDDER", vars);
        Assert.DoesNotContain("DA40_TRIM_AILERON", vars);
    }

    [Fact]
    public void TrimPositionUsesTheSharedElevatorTrimVariable()
    {
        // MSFSBA already reads ELEVATOR TRIM POSITION for every aircraft and announces it
        // as "Trim up 1.74". A DA40 copy of the same SimVar was a second key on one
        // quantity, and it disagreed with the announcement because Format defaults to
        // "F0": the scan read whole degrees.
        var def = Ng();

        Assert.Contains("MON_ElevatorTrim", def.GetPanelDisplayVariables()["Elevator Trim"]);
        Assert.DoesNotContain("DA40_TRIM_POSITION", def.GetVariables().Keys);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryFractionalReadoutSetsItsOwnFormat(DA40Variant variant)
    {
        // SimVarDefinition.Format DEFAULTS to "F0". A readout that leaves it alone reads
        // whole numbers, which for degrees, bar, inches or gallons is a silently wrong
        // value rather than a rounded one - it is how the trim row came to say "1" while
        // the announcement said 1.74.
        var fractional = new[] { "degrees", "bar", "inHg", "gallons", "volts", "amperes" };

        var wrong = new CowsDA40Definition(variant).GetVariables()
            .Where(kv => kv.Key.StartsWith("DA40_"))
            .Where(kv => kv.Value.RenderAsReadOnlyStatus)
            .Where(kv => kv.Value.ValueDescriptions == null)
            .Where(kv => fractional.Contains(kv.Value.Units))
            .Where(kv => kv.Value.Format == "F0")
            .Select(kv => $"{kv.Key} ({kv.Value.Units})")
            .ToList();

        Assert.True(wrong.Count == 0,
            $"{variant}: fractional readouts left on the F0 default - {string.Join(", ", wrong)}");
    }

    // ==============================================================================
    // Brakes panel (both variants)
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void BrakesPanelOwnsTheParkingBrakeAndNothingElse(DA40Variant variant)
    {
        // The wheel brakes are toe pedals - a flight control, flown from the pilot's own
        // hardware, no more a panel item than the stick is.
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Brakes"];

        Assert.Equal(new[] { "DA40_BRAKE_PARK" }, controls.ToArray());
    }

    [Fact]
    public void BrakeScanReportsTemperatureAndFadePerWheel()
    {
        // The model really does fade: above 400 C braking authority drops, and by 760 C
        // ninety percent of it is gone. The aeroplane has no brake temperature gauge, so
        // a sighted pilot learns this from smell and feel - a blind pilot from nothing.
        // The two wheels heat separately.
        var display = Ng().GetPanelDisplayVariables()["Brakes"];

        foreach (var key in new[]
                 {
                     "DA40_BRAKE_TEMP_L", "DA40_BRAKE_TEMP_R",
                     "DA40_BRAKE_FADE_L", "DA40_BRAKE_FADE_R"
                 })
        {
            Assert.Contains(key, display);
        }
    }

    [Fact]
    public void BrakeScanReportsBothParkPressures()
    {
        var display = Ng().GetPanelDisplayVariables()["Brakes"];

        Assert.Contains("DA40_BRAKE_PRESS_L", display);
        Assert.Contains("DA40_BRAKE_PRESS_R", display);
    }

    // ==============================================================================
    // Audio panel (both variants)
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void AudioPanelCarriesWhatTheAudioPanelDoes(DA40Variant variant)
    {
        // The GMA 1347 itself is in the G1000 bezel, so it belongs to the display window.
        // What is here is what it DOES, reachable without it, plus the headset jack -
        // the only audio item COWS models on its own.
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Audio"];

        Assert.Equal(new[]
        {
            "DA40_AUDIO_TRANSMIT",
            "DA40_AUDIO_MONITOR_BOTH",
            "DA40_AUDIO_HEADSET"
        }, controls.ToArray());
    }

    [Fact]
    public void TransmitSelectionReadsTheWayTheSelectionDoes()
    {
        // Bound to COM 2's transmit flag, so 0 is COM 1 and 1 is COM 2 rather than the
        // inverted reading COM 1's flag would give.
        var v = Ng().GetVariables()["DA40_AUDIO_TRANSMIT"];

        Assert.Equal("COM TRANSMIT:2", v.Name);
        Assert.Equal("COM 1", v.ValueDescriptions![0]);
        Assert.Equal("COM 2", v.ValueDescriptions[1]);
    }

    [Fact]
    public void ComFrequenciesKeepTheirDecimals()
    {
        // Format defaults to "F0", which would render 121.500 as a bare "121".
        //
        // ⚠️ THE AUDIO PANEL NO LONGER DEFINES THESE - it SHOWS the Radios panel's keys.
        // It used to re-register COM ACTIVE FREQUENCY:1 and :2 under DA40_AUDIO_* while the
        // Radios panel already carried them as DA40_RADIO_*, one Continuous on its own
        // SIM_FRAME subscription and the other OnRequest, both asking the sim for the same
        // name. The Radios panel then read "COM 2 Active: --" while the Audio panel showed
        // that very frequency correctly. One key per SimVar; a display row needs a KEY, not
        // a definition of its own.
        var def = Ng();
        foreach (var key in new[] { "DA40_RADIO_COM1_ACTIVE", "DA40_RADIO_COM2_ACTIVE" })
        {
            // A frequency must never render as a bare integer, whichever mechanism does it.
            Assert.True(def.TryGetDisplayOverride(key, 121.5, out string shown));
            Assert.Contains("121.500", shown);
        }

        Assert.False(def.GetVariables().ContainsKey("DA40_AUDIO_COM1_ACTIVE"),
            "The Audio panel must not re-define a SimVar the Radios panel owns.");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void CabinHeatAndVentHasItsTwoLevers(DA40Variant variant)
    {
        // This panel was WRONGLY DELETED once, on a sweep that concluded COWS modelled
        // neither. Both are there, inside Component ID="PASSENGER" - named after the
        // occupants rather than the system - and the air lever's node is called
        // PRESSURIZATION_Switch_Bleed on an aeroplane with no pressurization at all.
        // Searching for the system found nothing; the Asobo templates are what name it.
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Cabin Heat and Vent"];

        Assert.Equal(new[] { "DA40_CABIN_HEAT_SET", "DA40_CABIN_AIR_SET" }, controls.ToArray());
    }

    [Fact]
    public void CabinLeversAreSlidersBecauseTheyReallyArePercentages()
    {
        // The one place a slider is right: MainForm's TrackBar maps the value as a
        // percentage of 0-100, which is exactly what these are. The trim and the standby
        // subscale are not, which is why they are typed entries.
        foreach (var key in new[] { "DA40_CABIN_HEAT_SET", "DA40_CABIN_AIR_SET" })
        {
            Assert.True(Ng().GetVariables()[key].RenderAsSlider);
        }
    }

    [Fact]
    public void EachDoorHasItsOwnPositionRow()
    {
        // The control is a two-state combo and can only say Closed or Open; a travelling
        // door is neither. The position rows are OnRequest on purpose - two CONTINUOUS
        // variables sharing one SimVar name would collide in the continuous batch.
        var def = Ng();
        var display = def.GetPanelDisplayVariables()["Doors and Windows"];

        foreach (var key in new[] { "DA40_DOOR_CANOPY_POS", "DA40_DOOR_REAR_POS",
                                    "DA40_DOOR_STORM_L_POS", "DA40_DOOR_STORM_R_POS" })
        {
            Assert.Contains(key, display);
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.OnRequest,
                         def.GetVariables()[key].UpdateFrequency);
        }
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryVariableReadFromTheCacheIsBatchEligible(DA40Variant variant)
    {
        // CONTINUOUS ALONE IS NOT ENOUGH. Batch membership - the thing that actually gets
        // a variable polled and cached - is Continuous AND IsAnnounced. ⚠️ It is NOT "and not
        // ExcludeFromBatch": that flag chooses the shared 1 Hz batch or a per-var
        // subscription, and BOTH end in lastVariableValues. A Continuous variable with
        // IsAnnounced false falls to the individual-data-def branch, which is only read on
        // request, so it never reaches the cache: that is why B, F and W answered "not
        // available yet" even after being pointed at the right keys.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (var key in new[]
                 {
                     "DA40_G1000_BARO", "DA40_STBY_ALTIMETER_SET",
                     "DA40_FUEL_MAIN_ACTUAL", "DA40_FUEL_AUX_ACTUAL",
                     "DA40_FLAPS_POSITION", "DA40_GROSS_WEIGHT",
                     "DA40_AIRSPEED", "DA40_TRIM_SET"
                 })
        {
            var v = vars[key];
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced, $"{key} is Continuous but not IsAnnounced, so it is never batched or cached");
            // ⚠️ ExcludeFromBatch is NOT checked, and asserting it here was wrong. It decides
            // WHICH subscription carries the variable, not whether it is cached: an excluded
            // var gets its OWN periodic subscription and its deliveries land in
            // ProcessIndividualVariableResponse, which writes lastVariableValues exactly as
            // the batch path does. The fast radio and altimeter subscales are excluded ON
            // PURPOSE - that is what earns them SIM_FRAME instead of a 1 Hz sample - and this
            // line failed them for being faster.
        }
    }

    [Fact]
    public void EverySilentCachedReadoutActuallyExists()
    {
        // The silence comes from a name match in ProcessSimVarUpdate, so a typo or a
        // renamed key silently stops silencing and the pilot gets a number spoken at them
        // several times a second.
        //
        // Checked against the UNION of both variants: the set is shared, but some of its
        // keys are variant-gated (the power lever is NG-only), and a key missing on one
        // airframe is correct rather than a typo.
        var known = Ng().GetVariables().Keys
            .Concat(Xls().GetVariables().Keys)
            .ToHashSet();

        var missing = CowsDA40Definition.SilentCachedReadoutKeys
            .Where(k => !known.Contains(k))
            .ToList();

        Assert.True(missing.Count == 0,
            $"silenced keys that no longer exist - {string.Join(", ", missing)}");
    }

    // ==============================================================================
    // Circuit Breakers (both variants)
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryBreakerTheAeroplaneHasIsPresent(DA40Variant variant)
    {
        // Thirty-four, read out of the model's own CircuitBreakers.xml. Not a curated
        // subset - the checklist says "circuit breakers CHECKED IN" and a pilot cannot
        // check what is not there.
        var def = new CowsDA40Definition(variant);

        var breakers = def.GetPanelControls()
            .Where(p => p.Key is "Engine and Fuel" or "Flight Instruments" or "Avionics"
                              or "Bus and Power" or "Lighting" or "Airframe Systems")
            .SelectMany(p => p.Value)
            .Distinct()
            .ToList();

        Assert.Equal(34, breakers.Count);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryBreakerAnnouncesAndReadsInOrPulled(DA40Variant variant)
    {
        // A breaker moving without being touched means something failed - the definition
        // of a background change worth speaking. 0 is IN, 1 is PULLED, from the model's
        // own click code.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (var kv in vars.Where(kv => kv.Key.StartsWith("DA40_CB_")
                                         && !kv.Key.EndsWith("_OUT")))
        {
            Assert.True(kv.Value.IsAnnounced, $"{kv.Key} would not announce");
            Assert.Equal("In", kv.Value.ValueDescriptions![0]);
            Assert.Equal("PULLED", kv.Value.ValueDescriptions[1]);
        }
    }

    [Fact]
    public void EveryBreakerPanelCountsItsOwnBreakers()
    {
        // "Circuit breakers CHECKED IN" appears three times in the checklist, and
        // answering it by tabbing thirty-four combos is auditing, not checking.
        var display = Ng().GetPanelDisplayVariables();

        foreach (var panel in new[] { "Engine and Fuel", "Flight Instruments", "Avionics",
                                      "Bus and Power", "Lighting", "Airframe Systems" })
        {
            Assert.Single(display[panel]);
            Assert.EndsWith("_OUT", display[panel][0]);
        }
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void ThereIsNoCopilotBreakerPanelOrG1000Panel(DA40Variant variant)
    {
        // Both were planning sketches. The aeroplane has no copilot breaker set, and
        // everything a G1000 panel would carry is ON the displays - which are clickable
        // and driveable over the Coherent debugger, and are the ONLY way to tune the
        // radios and the transponder. The G1000 gets a display window instead.
        var panels = new CowsDA40Definition(variant).GetPanelStructure()
            .SelectMany(section => section.Value)
            .ToList();

        Assert.DoesNotContain("Copilot", panels);
        Assert.DoesNotContain("PFD Readout", panels);
        Assert.DoesNotContain("CAS Messages", panels);
        Assert.DoesNotContain("Engine Indication", panels);

        // The fuel calculator stays forbidden for the same reason, and it is worth saying
        // why out loud: it WAS added as a panel once, caught here, and removed. It is a
        // readout the Engine page already draws and the display window already reads.
        // "Aircraft Options" is the deliberate exception beside it - those are SETTINGS a
        // pilot changes, not values a display shows.
        Assert.DoesNotContain("Fuel Calculator", panels);
    }

    // ==============================================================================
    // Doors and Windows (both variants)
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void DoorsPanelHasAllFourOpenings(DA40Variant variant)
    {
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Doors and Windows"];

        Assert.Equal(new[]
        {
            "DA40_DOOR_CANOPY",
            "DA40_DOOR_REAR",
            "DA40_DOOR_STORM_L",
            "DA40_DOOR_STORM_R"
        }, controls.ToArray());
    }

    [Fact]
    public void DoorSimVarIndicesAreOneLowerThanTheirExitEvents()
    {
        // The trap: K:TOGGLE_AIRCRAFT_EXIT index is one HIGHER than the INTERACTIVE POINT
        // OPEN index. Verified live - event 3 opened :2, event 7 opened :6 - and getting
        // it wrong means reading a different door than the one being toggled.
        var vars = Ng().GetVariables();

        Assert.Equal("INTERACTIVE POINT OPEN:2", vars["DA40_DOOR_CANOPY"].Name);
        Assert.Equal("INTERACTIVE POINT OPEN:3", vars["DA40_DOOR_REAR"].Name);
        Assert.Equal("INTERACTIVE POINT OPEN:6", vars["DA40_DOOR_STORM_L"].Name);
        Assert.Equal("INTERACTIVE POINT OPEN:7", vars["DA40_DOOR_STORM_R"].Name);
    }

    [Fact]
    public void DoorsAreRegisteredAsSimVarsNotLVars()
    {
        // A name with a space and a colon is a STOCK SimVar. Force-registering one as an
        // L:var once broke A380 detection outright.
        foreach (var key in new[] { "DA40_DOOR_CANOPY", "DA40_DOOR_REAR",
                                    "DA40_DOOR_STORM_L", "DA40_DOOR_STORM_R" })
        {
            Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.SimVar,
                         Ng().GetVariables()[key].Type);
        }
    }

    [Fact]
    public void DoorScanCarriesTheWindThatRefusesThem()
    {
        // The click is gated at 30 knots and a 1 Hz Update slams an open door shut at the
        // same figure. Without the wind on the scan a refusal is indistinguishable from a
        // broken control.
        Assert.Contains("DA40_DOOR_WIND", Ng().GetPanelDisplayVariables()["Doors and Windows"]);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheEltIsExposed(DA40Variant variant)
    {
        // An earlier audit concluded the ELT had no interactive component and there was
        // nothing to expose. It does - the component is named SAFETY, after the system
        // rather than the switch, which is how it was missed. The shutdown checklist item
        // is "ELT check not transmitting", so the state is the whole point.
        var def = new CowsDA40Definition(variant);

        Assert.Equal(new[] { "DA40_ELT" }, def.GetPanelControls()["ELT"].ToArray());
        Assert.Contains("ELT", def.GetPanelStructure()["Instrument Panel"]);

        var v = def.GetVariables()["DA40_ELT"];
        Assert.Equal("Armed", v.ValueDescriptions![0]);
        Assert.True(v.IsAnnounced);
    }

    [Fact]
    public void CabinLeversShowWhatDecidesThem()
    {
        // There is no cabin temperature to show - MSFS has no such SimVar and nothing in
        // the package reads either lever - so the scan carries what a pilot would actually
        // reason from: outside air, and the coolant that IS the heat source.
        var display = Ng().GetPanelDisplayVariables()["Cabin Heat and Vent"];

        Assert.Contains("DA40_CABIN_OAT", display);

        // ⚠️ THE COOLANT ROW USED TO BE ITS OWN DEFINITION ON DISP_CT AND READ 0 C WITH THE
        // ENGINE HOT. Measured live against a running engine whose EIS coolant gauge sat at
        // 59 per cent of its arc: DISP_CT = 0, DISP_WT = 81. It now shows the Engine Start
        // panel's key, which was on DISP_WT all along - a display row needs a KEY, not a
        // definition of its own.
        Assert.Contains("DA40_START_COOLANT_TEMP", display);
        Assert.False(Ng().GetVariables().ContainsKey("DA40_CABIN_HEAT_SOURCE"),
            "The dead DISP_CT definition must not come back.");

        // ⚠️ AND THE XLS MUST NOT GAIN A COOLANT ROW. Its Lycoming is AIR-cooled and takes
        // cabin heat from an exhaust muff; there is no coolant temperature to show there.
        var xls = new CowsDA40Definition(DA40Variant.XLS)
            .GetPanelDisplayVariables()["Cabin Heat and Vent"];
        Assert.Contains("DA40_CABIN_OAT", xls);
        Assert.DoesNotContain("DA40_START_COOLANT_TEMP", xls);
    }

    // ==============================================================================
    // Seating and Payload (both variants)
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void PayloadPanelHasTheFiveStationsAndBothCrewFigures(DA40Variant variant)
    {
        // Five stations is what the aeroplane declares in flight_model.cfg - not a
        // curated subset - plus the two crew figures the cockpit can toggle.
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Seating and Payload"];

        Assert.Equal(new[]
        {
            "DA40_PAYLOAD_PILOT_SET",
            "DA40_PAYLOAD_FRONT_PAX_SET",
            "DA40_PAYLOAD_REAR_LEFT_SET",
            "DA40_PAYLOAD_REAR_RIGHT_SET",
            "DA40_PAYLOAD_BAGGAGE_SET",
            "DA40_PAYLOAD_PILOT_FIGURE",
            "DA40_PAYLOAD_COPILOT_FIGURE"
        }, controls.ToArray());
    }

    [Fact]
    public void CrewFiguresAreTriStateNotOnOff()
    {
        // L:FORCE_PILOT runs -1 / 0 / +1 and the model's own tooltip spells them
        // "Pilot OFF" / "Pilot Normal" / "Pilot ON". A two-position control would throw
        // the middle position - the aeroplane deciding for itself - away.
        var v = Ng().GetVariables()["DA40_PAYLOAD_PILOT_FIGURE"];

        Assert.Equal("Off", v.ValueDescriptions![-1]);
        Assert.Equal("Normal", v.ValueDescriptions[0]);
        Assert.Equal("On", v.ValueDescriptions[1]);
    }

    [Fact]
    public void PayloadScanIsTheLoadingSheet()
    {
        // A blind pilot cannot read the AFM's loading envelope. This is the part of it the
        // simulation actually computes: weight against the maximum, what is left, the CG,
        // and whether the baggage is legal.
        var display = Ng().GetPanelDisplayVariables()["Seating and Payload"];

        Assert.Contains("DA40_GROSS_WEIGHT", display);
        Assert.Contains("DA40_PAYLOAD_MARGIN", display);
        Assert.Contains("DA40_PAYLOAD_CG", display);
        Assert.Contains("DA40_PAYLOAD_BAGGAGE_CHECK", display);
    }

    [Fact]
    public void StationIndicesAreOneBasedAgainstTheZeroBasedConfig()
    {
        // flight_model.cfg's station_load.0 is SimConnect's PAYLOAD STATION WEIGHT:1.
        var vars = Ng().GetVariables();

        Assert.Equal("PAYLOAD STATION WEIGHT:1", vars["DA40_PAYLOAD_PILOT_SET"].Name);
        Assert.Equal("PAYLOAD STATION WEIGHT:5", vars["DA40_PAYLOAD_BAGGAGE_SET"].Name);
    }

    // ==============================================================================
    // Simulation - failures
    // ==============================================================================

    [Fact]
    public void FailurePanelsCoverTheNgsOwnSystems()
    {
        // COWS's Failures.txt is largely the LYCOMING's list - magnetos, mixture and
        // propeller cables, manifold pressure, CHT and EGT - none of which an AE300 has.
        // The NG's real failures are in the L:var table and absent from that document, so
        // the panels are built from the aircraft's variables and the document supplies
        // only the wording.
        var controls = Ng().GetPanelControls();

        Assert.Contains("DA40_FAIL_CRANK_A", controls["FADEC and Sensors"]);
        Assert.Contains("DA40_FAIL_CAM_B", controls["FADEC and Sensors"]);
        Assert.Contains("DA40_FAIL_LEVER_A", controls["FADEC and Sensors"]);
        Assert.Contains("DA40_FAIL_GLOW", controls["FADEC and Sensors"]);
        Assert.Contains("DA40_FAIL_COOLANT_LEAK_SET", controls["Engine Failures"]);
    }

    [Fact]
    public void NoFailurePanelOffersAMagnetoOnTheDiesel()
    {
        // The L:vars exist on the NG because both variants share one model, but an AE300
        // has no magnetos and setting them would do nothing.
        var vars = Ng().GetVariables().Keys;

        Assert.DoesNotContain("DA40_FAIL_MAG_L", vars);
        Assert.DoesNotContain("DA40_FAIL_MIX_LEVER", vars);
    }

    [Fact]
    public void ModeFailuresNameTheirModesRatherThanNumberThem()
    {
        // Each number is a DIFFERENT failure of the same part - "stuck open" and "stuck
        // closed" are not degrees of one thing.
        var v = Ng().GetVariables()["DA40_FAIL_BYPASS"];

        Assert.Equal("Normal", v.ValueDescriptions![0]);
        Assert.Equal("Stuck closed", v.ValueDescriptions[1]);
        Assert.Equal("Stuck open", v.ValueDescriptions[2]);
        Assert.Equal("Stuck as is", v.ValueDescriptions[3]);
    }

    [Fact]
    public void TrimRunawayKeepsBothDirections()
    {
        // FAILURES_AFCS_TRIM_RUN is -1/+1, and which way it runs is the whole point.
        var v = Ng().GetVariables()["DA40_FAIL_AFCS_TRIM_RUN"];

        Assert.Equal("Runs nose down", v.ValueDescriptions![-1]);
        Assert.Equal("Runs nose up", v.ValueDescriptions[1]);
    }

    [Fact]
    public void EveryFailureAnnouncesExceptTheSeverities()
    {
        // A failure appearing without being asked for is the most important background
        // change this aeroplane can produce. The severities are numbers, so they follow
        // the numeric rule instead.
        var vars = Ng().GetVariables();

        foreach (var kv in vars.Where(kv => kv.Key.StartsWith("DA40_FAIL_")
                                         && kv.Value.ValueDescriptions is { Count: > 0 }))
        {
            Assert.True(kv.Value.IsAnnounced, $"{kv.Key} would not announce");
        }
    }

    /// <summary>
    /// SIX resets, matching the MFD's own Reset Menu one for one - and ⚠️ THE MENU IS NOT
    /// THE SAME ON BOTH AIRFRAMES, which this test used to assert it was.
    ///
    /// The COWS POH lists the menu per variant and the two installed packages agree
    /// exactly: RESET_ECU and RESET_WIRE are in the NG's XML and NOT in the XLS's, while
    /// RESET_FLOOD and RESET_PLUGS are in the XLS's and not the NG's. Giving both variants
    /// the NG set left the XLS with two buttons that wrote L:vars the aeroplane does not
    /// have - announcing "ECUs reset" and doing nothing - and made the two it does have
    /// unreachable. Those two are the expensive ones: the XLS is the variant that can be
    /// FLOODED, and the one that FOULS PLUGS, which MSFSBA reports per plug with no other
    /// way to clear.
    ///
    /// The variable behind Clear Failures is RESET_FAILURES - the vendor document's
    /// FAILURES_RESET is read by nothing, verified live by watching a raised failure stay
    /// raised. The state-clearing three are what matter when the aeroplane will not start
    /// at all: a saved state restored FLAT batteries at VCBI, and only "Reset: ECU" would
    /// clear an ECU A FAIL the AFM's own procedure could not.
    /// </summary>
    [Fact]
    public void ResetIsAlwaysAvailableAndCarriesEachVariantsOwnMenu()
    {
        Assert.Equal(new[]
        {
            "DA40_FAIL_RESET_DAMAGE",
            "DA40_FAIL_RESET",
            "DA40_FAIL_RESET_BATT",
            "DA40_FAIL_RESET_ECU",
            "DA40_FAIL_RESET_WIRE",
            "DA40_FAIL_RESET_ALL"
        }, Ng().GetPanelControls()["Reset"].ToArray());

        Assert.Equal(new[]
        {
            "DA40_FAIL_RESET_DAMAGE",
            "DA40_FAIL_RESET",
            "DA40_FAIL_RESET_BATT",
            "DA40_FAIL_RESET_FLOOD",
            "DA40_FAIL_RESET_PLUGS",
            "DA40_FAIL_RESET_ALL"
        }, Xls().GetPanelControls()["Reset"].ToArray());

        foreach (var variant in new[] { DA40Variant.NG, DA40Variant.XLS })
        {
            Assert.Contains("Reset",
                new CowsDA40Definition(variant).GetPanelStructure()["Simulation"]);
        }
    }

    [Fact]
    public void TheXlsDropsOnlyTheFadecFailurePanel()
    {
        // ⚠️ RULING CHANGED, AND THIS TEST IS THE INVERSION OF WHAT IT SAID. It used to
        // assert the XLS had no Engine Damage panel either, which was true only because
        // nobody had built one: the XLS has its OWN per-cylinder failures, fuel failures
        // and damage accumulators, all measured on the airframe, so all three panels stay
        // and only FADEC and Sensors goes - the XLS has no FADEC to fail.
        var xls = Xls().GetPanelStructure()["Simulation"];

        Assert.DoesNotContain("FADEC and Sensors", xls);
        Assert.Contains("Engine Failures", xls);
        Assert.Contains("Fuel Failures", xls);
        Assert.Contains("Engine Damage", xls);
        Assert.Contains("Light Failures", xls);
        Assert.Contains("Reset", xls);
    }

    // ==============================================================================
    // Radios (both variants)
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheAutopilotOwnsTheHeadingBugAndCourse(DA40Variant variant)
    {
        // They are PFD bezel knobs, which is why they began life on the Radios panel
        // beside the radio knobs. Functionally they are what the autopilot flies, so they
        // sit with it - and MOVED, never duplicated, which is what the second half pins.
        var panels = new CowsDA40Definition(variant).GetPanelControls();

        Assert.Contains("DA40_AP_HDG_SET", panels["GFC 700"]);
        Assert.Contains("DA40_AP_CRS_SET", panels["GFC 700"]);

        // ⚠️ THEY ARE GONE ENTIRELY NOW, not merely off this panel. They were defined and
        // had live setter cases but sat in NO panel, NO display list and NO hotkey, so a
        // pilot could never reach them - two dead SimConnect definitions duplicating
        // NAV OBS:1 and AUTOPILOT HEADING LOCK DIR, which is the batch-collision shape.
        var all = new CowsDA40Definition(variant).GetVariables();
        Assert.False(all.ContainsKey("DA40_RADIO_HDG_BUG_SET"));
        Assert.False(all.ContainsKey("DA40_RADIO_CRS1_SET"));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void RadiosPanelTunesBothComsAndBothNavs(DA40Variant variant)
    {
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Radios"];

        foreach (var key in new[]
                 {
                     "DA40_RADIO_COM1_SET", "DA40_RADIO_COM1_SWAP",
                     "DA40_RADIO_COM2_SET", "DA40_RADIO_COM2_SWAP",
                     "DA40_RADIO_NAV1_SET", "DA40_RADIO_NAV1_SWAP",
                     "DA40_RADIO_NAV2_SET", "DA40_RADIO_NAV2_SWAP"
                     // The heading bug and course knob are NOT here any more: they moved
                     // to the GFC 700 panel, where a pilot selecting heading mode needs
                     // them. See TheAutopilotOwnsTheHeadingBugAndCourse below.
                 })
        {
            Assert.Contains(key, controls);
        }
    }

    [Theory]
    [InlineData(124.80, 0x2480)]
    [InlineData(118.00, 0x1800)]
    [InlineData(136.975, 0x3697)]   // the third decimal is LOST - see below
    [InlineData(127.85, 0x2785)]
    public void ComFrequenciesEncodeAsBcd16(double mhz, int expected)
    {
        // The COM radios take BCD16 and NOTHING else - COM1_STBY_RADIO_SET_HZ was measured
        // doing nothing at all, while the BCD form moved the standby immediately. The NAV
        // radios are the opposite, taking raw hertz. Two encodings, one aeroplane.
        //
        // BCD16 carries FOUR digits, so it holds 25 kHz spacing and no finer: 136.975
        // encodes as 0x3697 and comes back 136.97. That is a real limit of the only event
        // this radio accepts, not a rounding choice - and it is why the help text says
        // 25 kHz rather than pretending 8.33 channels can be typed.
        Assert.Equal(expected, CowsDA40Definition.ComBcd16(mhz));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPlainReadoutCarriesItsUnit(DA40Variant variant)
    {
        // MainForm's numeric branch renders anything outside its own little unit switch as
        // a bare whole number, so the DA40 formats its own readouts. That fallback tested
        // ValueDescriptions == null, which is NEVER true - SimVarDefinition initialises it
        // to an EMPTY DICTIONARY - so it was dead from the day it was written and every
        // readout without its own override rendered as a bare integer ("COM 1 Active: 133").
        //
        // This pins the shape the fallback depends on rather than the fallback itself: a
        // readout with units and no descriptions must be reachable by a Count test.
        var vars = new CowsDA40Definition(variant).GetVariables();

        var plain = vars.Where(kv => kv.Key.StartsWith("DA40_")
                                  && kv.Value.RenderAsReadOnlyStatus
                                  && !string.IsNullOrWhiteSpace(kv.Value.Units))
                        .Where(kv => kv.Value.ValueDescriptions is not { Count: > 0 })
                        .ToList();

        Assert.NotEmpty(plain);
        Assert.All(plain, kv => Assert.NotNull(kv.Value.ValueDescriptions));
    }

    [Fact]
    public void ValueDescriptionsIsNeverNull()
    {
        // The trap behind the bug above, pinned on its own: every definition has a
        // dictionary, so "is it empty" is the only meaningful question. Anything testing
        // for null is testing something that cannot happen.
        Assert.All(Ng().GetVariables().Values, v => Assert.NotNull(v.ValueDescriptions));
    }
}

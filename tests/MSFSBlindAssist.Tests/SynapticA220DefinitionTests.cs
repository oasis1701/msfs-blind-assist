using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the Synaptic A220 definition's pure structure:
/// the aural-exclusion and lamp invariants from docs/a220-plan.md, panel/variable
/// consistency, the NAV standby-only rule, and the heading-wrap walk math.
/// </summary>
public class SynapticA220DefinitionTests
{
    private static readonly SynapticA220Definition Def = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Def.GetVariables();

    [Fact]
    public void Identity_IsPinned()
    {
        Assert.Equal("SYNAPTIC_A220", Def.AircraftCode);
        Assert.Equal(FCUControlType.IncrementDecrement, Def.GetAltitudeControlType());
        Assert.Equal(FCUControlType.IncrementDecrement, Def.GetHeadingControlType());
        Assert.Equal(FCUControlType.IncrementDecrement, Def.GetSpeedControlType());
        Assert.Equal(FCUControlType.IncrementDecrement, Def.GetVerticalSpeedControlType());
    }

    /// <summary>
    /// The EXEC prompt must be a SPOKEN monitor, not a cache-only quiet var. A
    /// pending flight-plan modification is shown to a sighted pilot as a lamp on the
    /// MKP EXEC key (whose SEQ2_CODE is this very L:var) plus a MOD flag on the
    /// display — a blind pilot has neither, so if this var stops being announced the
    /// EXEC prompt becomes completely invisible again. It also stays IN the Ctrl+M
    /// monitor manager so it can be switched off deliberately.
    /// </summary>
    [Fact]
    public void FlightPlanModified_IsAnnouncedAndUserSilenceable()
    {
        var v = Assert.Contains("A22X_FPLN_MODIFIED", (IDictionary<string, SimVarDefinition>)Vars);
        Assert.Equal("A22X Flight Plan Modified", v.Name);
        Assert.Equal(SimVarType.LVar, v.Type);
        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced, "the EXEC prompt must be announced");
        Assert.False(v.ExcludeFromMonitorManager, "must remain switchable in Ctrl+M");
    }

    /// <summary>Gotcha #5: the aircraft plays its own aural callouts — MSFSBA must
    /// never register any of the ~91 "L:A22X Aural *" flags.</summary>
    [Fact]
    public void NoNativeAuralFlag_IsEverRegistered()
    {
        var auralBareNames = SynapticA220SimVarData.AuralVars
            .Select(n => n.StartsWith("L:") ? n.Substring(2) : n)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, def) in Vars)
            Assert.False(auralBareNames.Contains(def.Name),
                $"{key} registers native aural flag {def.Name}");
    }

    /// <summary>Every documented lamp is a batch-covered monitor var (Continuous +
    /// IsAnnounced, no ExcludeFromBatch = zero individual data defs) and never a
    /// panel control (A380 invariant: panels are for operating, not scanning).</summary>
    [Fact]
    public void Lamps_AreBatchMonitorsAndNotPanelControls()
    {
        var lampBareNames = SynapticA220SimVarData.LampVars
            .Select(n => n.StartsWith("L:") ? n.Substring(2) : n).ToHashSet();
        var lampKeys = Vars.Where(kv => lampBareNames.Contains(kv.Value.Name))
            .Select(kv => kv.Key).ToHashSet();
        Assert.Equal(SynapticA220SimVarData.LampVars.Length, lampKeys.Count);

        foreach (var key in lampKeys)
        {
            var def = Vars[key];
            Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
            Assert.True(def.IsAnnounced, $"{key} lamp must be announced");
            Assert.False(def.ExcludeFromBatch, $"{key} lamp must stay batch-covered");
        }

        var allPanelKeys = Def.GetPanelControls().Values.SelectMany(x => x).ToHashSet();
        Assert.Empty(lampKeys.Intersect(allPanelKeys));
    }

    [Fact]
    public void EveryPanelControlKey_HasAVariableDefinition()
    {
        foreach (var (panel, keys) in Def.GetPanelControls())
            foreach (var key in keys)
                Assert.True(Vars.ContainsKey(key), $"panel '{panel}' references unknown key {key}");
        foreach (var (panel, keys) in Def.GetPanelDisplayVariables())
            foreach (var key in keys)
                Assert.True(Vars.ContainsKey(key), $"display '{panel}' references unknown key {key}");
    }

    [Fact]
    public void EveryStructurePanel_HasControlsOrDisplays()
    {
        var controls = Def.GetPanelControls();
        var displays = Def.GetPanelDisplayVariables();
        foreach (var (section, panels) in Def.GetPanelStructure())
            foreach (var panel in panels)
                Assert.True(controls.ContainsKey(panel) || displays.ContainsKey(panel),
                    $"section '{section}' panel '{panel}' has no controls and no display entry");
    }

    /// <summary>Combo labels must be the documented enum labels verbatim.</summary>
    [Theory]
    [InlineData("A22X_APU_SWITCH", "A22X APU Switch")]
    [InlineData("A22X_TAXI_LIGHTS", "A22X Taxi Lights")]
    [InlineData("A22X_ENG_START_MODE", "A22X Eng Start Mode")]
    [InlineData("A22X_MANUAL_TRANSFER", "A22X Manual Transfer")]
    [InlineData("A22X_FWD_CARGO_AIR", "A22X Fwd Cargo Air")]
    [InlineData("A22X_BUS_ISOLATION", "A22X Bus Isolation Mode")]
    public void ComboLabels_MatchDocumentedEnums(string key, string bareName)
    {
        var doc = SynapticA220SimVarData.Vars.Single(x => x.Name == "L:" + bareName);
        var def = Vars[key];
        Assert.NotNull(doc.EnumLabels);
        Assert.Equal(doc.EnumLabels!.Length, def.ValueDescriptions.Count);
        for (int i = 0; i < doc.EnumLabels.Length; i++)
            Assert.Equal(doc.EnumLabels[i], def.ValueDescriptions[i]);
    }

    /// <summary>NAV-to-NAV transfer protection: no NAV active-set and no NAV swap
    /// control may exist — standby set only (A220-FOT-22-30-012).</summary>
    [Fact]
    public void NavRadios_AreStandbyOnly()
    {
        foreach (var (key, def) in Vars)
        {
            Assert.False(def.Name.Contains("NAV1_RADIO_SET", StringComparison.OrdinalIgnoreCase), key);
            Assert.False(def.Name.Contains("NAV2_RADIO_SET", StringComparison.OrdinalIgnoreCase), key);
            Assert.False(def.Name.Contains("NAV1_RADIO_SWAP", StringComparison.OrdinalIgnoreCase), key);
            Assert.False(def.Name.Contains("NAV2_RADIO_SWAP", StringComparison.OrdinalIgnoreCase), key);
        }
    }

    /// <summary>No spoiler-arm control may be exposed — the aircraft masks the stock
    /// arm events (no arm function exists on the A220).</summary>
    [Fact]
    public void NoSpoilerArmControl()
    {
        foreach (var (key, def) in Vars)
            Assert.False(def.Name.Contains("SPOILERS_ARM", StringComparison.OrdinalIgnoreCase), key);
    }

    [Theory]
    [InlineData(340, 20, 40)]
    [InlineData(20, 340, -40)]
    [InlineData(10, 350, -20)]
    [InlineData(180, 180, 0)]
    [InlineData(0, 180, 180)]
    public void HeadingWalk_TakesTheShortWayAround(double current, double target, double expectedDelta)
    {
        Assert.Equal(expectedDelta, SynapticA220Definition.WrapHeadingDelta(target - current), 3);
    }

    /// <summary>Space-containing L:var names are the A220 norm — the definition must
    /// carry them bare (no "L:" prefix; the read/write layers add it).</summary>
    [Fact]
    public void LvarNames_AreBareAndSpaceContaining()
    {
        foreach (var (key, def) in Vars)
        {
            if (def.Type != SimVarType.LVar) continue;
            Assert.False(def.Name.StartsWith("L:", StringComparison.Ordinal),
                $"{key} carries an L: prefix — reads/writes would double it");
        }
        Assert.Contains(Vars.Values, d => d.Type == SimVarType.LVar && d.Name.Contains(' '));
    }

    // ---- Plan A systems rows (CommBus, rendered by TryGetDisplayOverride) ------------

    private static A220Systems.SystemsBlocks FixtureSystems() => new()
    {
        lpfd = SynapticA220SystemsTests.Store<A220Systems.PfdBlock>("A22X.L PFD Data"),
        atc = SynapticA220SystemsTests.Store<A220Systems.AtcBlock>("A22X.ATC Data"),
        gear = SynapticA220SystemsTests.Store<A220Systems.GearBlock>("A22X.Gear Data"),
        doors = SynapticA220SystemsTests.Store<A220Systems.DoorsBlock>("A22X.Doors Data"),
        cas = SynapticA220SystemsTests.Store<A220Systems.CasBlock>("A22X.CAS Data"),
        eng = SynapticA220SystemsTests.Store<A220Systems.EngineBlock>("A22X.Engine Data"),
        lctp = SynapticA220SystemsTests.Store<A220Systems.CtpBlock>("A22X.L CTP Data"),
    };

    public static TheoryData<string> SystemsRowKeys()
    {
        var data = new TheoryData<string>();
        foreach (var (key, _) in SynapticA220Definition.SystemsRows) data.Add(key);
        foreach (var door in A220Systems.Doors) data.Add(SynapticA220Definition.DoorKey(door.Id));
        return data;
    }

    [Theory, MemberData(nameof(SystemsRowKeys))]
    public void SystemsRow_IsAPanelDisplayRow_ThatSaysNotAvailableBeforeData(string key)
    {
        var def = new SynapticA220Definition();
        Assert.True(Vars.ContainsKey(key), $"{key} has no variable");
        Assert.True(Vars[key].RenderAsReadOnlyStatus);
        Assert.Contains(def.GetPanelDisplayVariables().Values, list => list.Contains(key));
        Assert.True(def.TryGetDisplayOverride(key, 0, out string text));
        Assert.Equal(A220Systems.NotAvailable, text);
    }

    [Fact]
    public void SystemsRows_RenderTheLiveCapture()
    {
        var def = new SynapticA220Definition();
        def.ProcessSystems(FixtureSystems());
        string Row(string key) { Assert.True(def.TryGetDisplayOverride(key, 0, out string t)); return t; }
        Assert.Equal("down and locked", Row("A22X_GEAR_STATE"));
        Assert.Equal("1, 1, 1, 1", Row("A22X_BRAKE_TEMPS"));
        Assert.Equal("all closed", Row("A22X_DOORS_STATE"));
        Assert.Equal("closed, armed", Row("A22X_DOOR_FWD_PAX"));
        Assert.Equal("running, N1 18.5 percent", Row("A22X_ENG1_STATE"));
        Assert.Equal("1 advisory, 4 status", Row("A22X_CAS_STATE"));
        Assert.Equal("squawk 2000, AUTO", Row("A22X_XPDR_STATE"));
        Assert.Equal("30.13 inches", Row("A22X_L_ALTIMETER_STATE"));
        Assert.Equal("baro 1210 feet", Row("A22X_L_MINIMUMS_STATE"));
        Assert.Equal(A220Systems.NotAvailable, Row("A22X_R_ALTIMETER_STATE"));   // no R CTP in the capture
        Assert.Equal(A220Systems.NotAvailable, Row("A22X_IRS_STATE"));          // aligned before capture: no FMS position
    }

    [Fact]
    public void DoorsAndIrs_AreUtilitiesPanels()
    {
        var structure = Def.GetPanelStructure();
        Assert.Contains("Doors", structure["Utilities"]);
        Assert.Contains("IRS", structure["Utilities"]);
        Assert.True(Def.GetPanelControls().ContainsKey("Doors"));   // HS-5: a display-only panel needs an entry
        Assert.True(Def.GetPanelControls().ContainsKey("IRS"));
    }

    [Fact]
    public void GearLever_IsAZeroToOneFraction_SoShiftGsThresholdOfHalfHolds()
    {
        // FormatGearReadout(leverPct, ...) compares leverPct >= 0.5: the lever var must read 0..1.
        Assert.Equal("percent over 100", Vars["A22X_GEAR_LEVER"].Units);
    }

    // ---- The pump's intake: one odd `sys` field must not cost the rest of the snapshot ----

    private const string GoodGearJson =
        "{\"nose_icon\":2,\"left_icon\":2,\"right_icon\":2,\"nose_color\":3,\"left_color\":3,\"right_color\":3}";

    [Fact]
    public void Snapshot_WithAGoodSysBlock_FeedsTheRows()
    {
        var def = new SynapticA220Definition();
        def.ProcessDisplaysSnapshot("{\"ok\":true,\"sys\":{\"gear\":" + GoodGearJson + "}}");
        Assert.NotNull(def.LatestSystems);
        Assert.True(def.TryGetDisplayOverride("A22X_GEAR_STATE", 0, out string text));
        Assert.Equal("down and locked", text);
    }

    [Theory]
    [InlineData("\"nope\"")]                             // not an object at all
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void Snapshot_WithAMalformedSysField_StillTakesTheRestOfTheSnapshot(string sysJson)
    {
        var def = new SynapticA220Definition();
        def.ProcessDisplaysSnapshot("{\"ok\":true,\"sys\":{\"gear\":" + GoodGearJson + "}}");   // rows were live
        Assert.NotNull(def.LatestSystems);

        def.ProcessDisplaysSnapshot("{\"ok\":true,\"fc\":{\"pitch_trim\":4.6},\"sys\":" + sysJson + "}");

        Assert.Equal(4.6, def.LatestFlightControl?.pitch_trim);   // FD/trim/FMA/CAS intake survived the odd field
        Assert.Null(def.LatestSystems);                           // the systems rows no longer claim a state
        Assert.True(def.TryGetDisplayOverride("A22X_GEAR_STATE", 0, out string text));
        Assert.Equal(A220Systems.NotAvailable, text);
    }

    // A field of the wrong type inside ONE store costs that store alone: the other stores' rows
    // (and every read-back that polls them) keep their state.
    [Fact]
    public void Snapshot_WithAnOddFieldInOneStore_KeepsTheOtherStoresRows()
    {
        var def = new SynapticA220Definition();
        def.ProcessDisplaysSnapshot("{\"ok\":true,\"sys\":{\"gear\":" + GoodGearJson + "}}");   // rows were live

        def.ProcessDisplaysSnapshot("{\"ok\":true,\"fc\":{\"pitch_trim\":4.6},\"sys\":{\"gear\":{\"nose_icon\":\"two\"},"
            + "\"lctp\":{\"minimums_mode\":0,\"minimums\":1210}}}");

        Assert.Equal(4.6, def.LatestFlightControl?.pitch_trim);
        Assert.True(def.TryGetDisplayOverride("A22X_GEAR_STATE", 0, out string gear));
        Assert.Equal(A220Systems.NotAvailable, gear);                   // the odd store claims no state
        Assert.True(def.TryGetDisplayOverride("A22X_L_MINIMUMS_STATE", 0, out string minimums));
        Assert.Equal("baro 1210 feet", minimums);                       // the rest of the block survived
    }

    [Fact]
    public void CasWindow_ListsEveryMessage_WarningsFirst_OrSaysWhyNot()
    {
        var def = new SynapticA220Definition();
        Assert.Equal("CAS not available — the A220 display link is not connected.", def.CasWindowText());
        def.ProcessSystems(FixtureSystems());
        Assert.Equal("Advisory: EXT PWR AVAIL\nStatus: SEAT BELTS\nStatus: AUTOBRAKE RTO\nStatus: PARK BRAKE ON\nStatus: APU ON",
            def.CasWindowText());
    }

    [Fact]
    public void TakeoffFeed_IsAPerFrameAirspeed_HiddenFromCtrlM()
    {
        var v = Vars[SynapticA220Definition.TakeoffIasKey];
        Assert.Equal("AIRSPEED INDICATED", v.Name);
        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced);
        Assert.True(v.ExcludeFromBatch);
        Assert.True(v.HighFrequency);
        Assert.True(v.ExcludeFromMonitorManager);
        Assert.Equal(SynapticA220Definition.TakeoffIasKey, Def.TakeoffCalloutFeedKey);
        Assert.True(Def.IsMuteWrapExempt(SynapticA220Definition.TakeoffIasKey));
    }

    // ---- The CommBus bridge and the transponder / TCAS controls ([A220-35], [A220-39]) ----

    [Fact]
    public void Bridge_EmitsBusOnlyStates_OnChangeOnly_AndAllAgainAfterAReset()
    {
        var def = new SynapticA220Definition();
        var seen = new List<(string Key, double Value)>();
        def.CommBusValueChanged += (k, v) => seen.Add((k, v));
        def.ProcessSystems(FixtureSystems());
        Assert.Contains(("A22X_XPDR_MODE", 0.0), seen);
        Assert.Contains(("A22X_TCAS_ALT_LIMIT", 0.0), seen);
        Assert.Contains(("A22X_XPDR_CODE_BUS", 2000.0), seen);
        int first = seen.Count;
        def.ProcessSystems(FixtureSystems());
        Assert.Equal(first, seen.Count);                         // unchanged: nothing re-sent
        def.RequestSystemsRebaseline();
        def.ProcessSystems(FixtureSystems());
        Assert.Equal(2 * first, seen.Count);                     // a context reset re-sends it all
    }

    [Theory]
    [InlineData("A22X_XPDR_MODE", 6)]
    [InlineData("A22X_TCAS_ALT_LIMIT", 4)]
    public void BusCombos_AreNeverReadFromTheSim(string key, int positions)
    {
        var v = Vars[key];
        Assert.Equal(SimVarType.LVar, v.Type);
        Assert.Equal(UpdateFrequency.Never, v.UpdateFrequency);   // no data definition: the bridge owns its state
        Assert.Equal(positions, v.ValueDescriptions.Count);
        Assert.Contains(key, Def.GetPanelControls()["Transponder"]);
    }

    [Fact]
    public void BusCombos_TakeTheLabelsOfTheAircraftsOwnDialog_InItsOrder()
    {
        Assert.Equal(A220Systems.XpdrModeLabels,
            Vars["A22X_XPDR_MODE"].ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
        Assert.Equal(A220Systems.AltLimitLabels,
            Vars["A22X_TCAS_ALT_LIMIT"].ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
    }

    [Fact]
    public void Transponder_Panel_UsesTheBusCodeEntry_NotTheStockOne()
    {
        Assert.False(Vars.ContainsKey("TRANSPONDER_CODE_SET"));
        var entry = Vars["A22X_XPDR_CODE_SET"];
        Assert.Equal("A22X_XPDR_CODE_BUS", entry.CurrentValueSourceKey);
        Assert.True(entry.UnparseableTextAsNaN);
        Assert.Equal(new[] { "A22X_XPDR_MODE", "A22X_TCAS_ALT_LIMIT", "A22X_XPDR_CODE_SET", "A22X_XPDR_IDENT", "A22X_XPDR_FLIGHT_ID" },
            Def.GetPanelControls()["Transponder"]);
        Assert.True(Vars["A22X_XPDR_FLIGHT_ID"].RenderAsButton);
        Assert.True(Vars.ContainsKey("A22X_XPDR_CODE_BUS"));      // the entry's prefill source must exist
    }

    // The stock word the X key and the squawk fallback decode as BCD16 (A220Systems.Bcd16ToSquawk).
    // Live 2026-10-15 on this aircraft, units "number" read squawk 2345 as 2345 (which that decode
    // rejects) and units "Bco16" read it as 9029 = 0x2345, so the registration must ask for Bco16.
    [Fact]
    public void StockTransponderCode_IsReadInBco16_TheUnitThatGivesTheBcdWord()
    {
        var code = Vars["A22X_XPDR_CODE"];
        Assert.Equal("TRANSPONDER CODE:1", code.Name);
        Assert.Equal("Bco16", code.Units);
    }

    [Fact]
    public async Task TcasAltitudeLimit_IsRefusedAloudInAuto_AndTheCombosAreResentTheTruth()
    {
        var def = new SynapticA220Definition();
        var seen = new List<(string Key, double Value)>();
        def.CommBusValueChanged += (k, v) => seen.Add((k, v));
        var speech = new SpeechCapture();
        def.ProcessSystems(FixtureSystems());                      // the fixture transponder is in AUTO (mode 0)
        int first = seen.Count;

        await def.SetTcasAltLimitAsync(1, speech);                 // refused before anything reaches the agent

        Assert.Equal(new[] { "The TCAS altitude limit cannot be changed in AUTO. Select TA/RA or TA ONLY first." },
            speech.Interrupts);
        def.ProcessSystems(FixtureSystems());
        Assert.Equal(2 * first, seen.Count);                       // the combo that was picked is shown the truth again
        Assert.Equal(0.0, seen.Last(e => e.Key == "A22X_TCAS_ALT_LIMIT").Value);
    }

    // The stock TRANSPONDER CODE:1 word is BCD16 (squawk 2000 reads 8192), never the decimal code.
    [Theory]
    [InlineData(2000, 2000, 0.0, true)]       // the bus says it landed
    [InlineData(2000, 1200, 0.0, false)]      // the bus says something else
    [InlineData(2000, 1200, 8192.0, false)]   // a present bus block outranks the stock word
    [InlineData(2000, null, 8192.0, true)]    // link down: the stock word, as the BCD of 2000
    [InlineData(2000, null, 2000.0, false)]   // link down: a decimal 2000 is NOT the squawk 2000
    [InlineData(1200, null, 4608.0, true)]    // 0x1200
    [InlineData(2000, null, double.NaN, false)]
    public void SquawkLanded_ComparesTheStockWordAsBcd_OnlyWhenTheBusIsDown(int code, int? busCode, double stockWord, bool expected)
    {
        var atc = busCode is { } c ? new A220Systems.AtcBlock { code = c } : null;
        Assert.Equal(expected, SynapticA220Definition.SquawkLanded(code, atc, stockWord));
    }

    // The Flight ID dialog names the current ID (no other readout carries it) and, since an empty
    // box is refused by the form before the validator runs, clears through its own button.
    [Theory]
    [InlineData(false, null, "unknown")]      // no ATC block: the display link is down
    [InlineData(true, null, "none")]
    [InlineData(true, "", "none")]
    [InlineData(true, "00000000", "none")]    // what a clear leaves behind
    [InlineData(true, "BAW123", "BAW123")]
    public void FlightIdNow_NamesTheCurrentId_OrWhyNot(bool haveBlock, string? id, string expected)
    {
        var atc = haveBlock ? new A220Systems.AtcBlock { id = id } : null;
        Assert.Equal(expected, SynapticA220Definition.FlightIdNow(atc));
    }

    [Theory]
    [InlineData(1200, 0.0, "1200")]           // the bus block is what the transponder reads
    [InlineData(null, 8192.0, "2000")]        // link down: the stock BCD word, decoded
    [InlineData(null, 2000.0, "----")]        // not a squawk: never guessed
    public void SquawkReading_NamesWhatTheTransponderReads(int? busCode, double stockWord, string expected)
    {
        var atc = busCode is { } c ? new A220Systems.AtcBlock { code = c } : null;
        Assert.Equal(expected, SynapticA220Definition.SquawkReading(atc, stockWord));
    }

    // ---- The write paths against a scripted display agent -----------------------------------
    // The real link is a Coherent socket, so these drive the verdict logic through the definition's
    // AgentCallOverride seam: write calls answer SENT, sys() answers the scripted ATC Data.

    private sealed class ScriptedAgent
    {
        public Func<string?> Sys { get; init; } = () => "{}";
        public Task<string?> Call(string call) => Task.FromResult(call == "sys()" ? Sys() : "SENT");
    }

    private static (SynapticA220Definition Def, SpeechCapture Speech) Scripted(string? sysJson)
    {
        var agent = new ScriptedAgent { Sys = () => sysJson };
        var def = new SynapticA220Definition
        {
            AgentCallOverride = agent.Call,
            PollIntervalMs = 5,
            PollTimeoutCapMs = 150
        };
        return (def, new SpeechCapture());
    }

    private static string Atc(string fields) => "{\"atc\":{" + fields + "}}";

    // A missing ATC block is "not confirmed", never a success and never a guess ("cleared",
    // "reads empty"): FlightIdLanded("00000000", null) is true, so the poll and the verdict both
    // have to insist on a block.
    [Theory]
    [InlineData("00000000", "{}")]            // a clear, the store is stale
    [InlineData("BAW123", "{}")]              // a set, the store is stale
    [InlineData("00000000", null)]            // a clear, the agent answers nothing at all
    [InlineData("BAW123", null)]
    public async Task FlightId_WithNoAtcBlock_IsNotConfirmed_NeverClearedOrReadAsEmpty(string id, string? sysJson)
    {
        var (def, speech) = Scripted(sysJson);
        await def.SetFlightIdAsync(id, speech);
        Assert.Equal(new[] { "Flight ID not confirmed: the A220 display link is not connected." }, speech.All);
    }

    [Fact]
    public async Task FlightId_ReadBack_SpeaksWhatTheAircraftKept()
    {
        var set = Scripted(Atc("\"id\":\"BAW123\""));
        await set.Def.SetFlightIdAsync("BAW123", set.Speech);
        Assert.Equal(new[] { "Flight ID BAW123" }, set.Speech.All);

        var cleared = Scripted(Atc("\"id\":\"00000000\""));
        await cleared.Def.SetFlightIdAsync("00000000", cleared.Speech);
        Assert.Equal(new[] { "Flight ID cleared" }, cleared.Speech.All);

        var stuck = Scripted(Atc("\"id\":\"OLD1\""));
        await stuck.Def.SetFlightIdAsync("00000000", stuck.Speech);
        Assert.Equal(new[] { "Flight ID did not change — reads OLD1." }, stuck.Speech.All);

        var blank = Scripted(Atc("\"mode\":2"));   // a block that carries no ID: the ID reads empty
        await blank.Def.SetFlightIdAsync("BAW123", blank.Speech);
        Assert.Equal(new[] { "Flight ID did not change — reads empty." }, blank.Speech.All);

        var echoed = Scripted(Atc("\"id\":\"00000000\""));   // the cleared marker is empty too
        await echoed.Def.SetFlightIdAsync("BAW123", echoed.Speech);
        Assert.Equal(new[] { "Flight ID did not change — reads empty." }, echoed.Speech.All);
    }

    // ---- Latest write wins: arrowing through a combo starts overlapping writes ----------------
    // Every arrow press commits and writes. A write the pilot has already moved past reads the
    // aircraft, sees the LATER pick, and used to speak "did not change — still ..." about a value
    // nobody was asking for any more.

    [Fact]
    public async Task OverlappingModeWrites_OnlyTheLatestOneIsJudged()
    {
        var (def, speech) = Scripted(Atc("\"mode\":2"));          // the second pick landed; STBY was never seen
        var first = def.SetXpdrModeAsync(1, speech);              // AUTO -> STBY ...
        var second = def.SetXpdrModeAsync(2, speech);             // ... on to TA/RA before the first is read back
        await Task.WhenAll(first, second);
        Assert.Empty(speech.All);                                 // success is silent; no false failure
    }

    [Fact]
    public async Task OverlappingModeWrites_TheLatestStillReportsItsOwnFailure_Once()
    {
        var (def, speech) = Scripted(Atc("\"mode\":0"));          // the aircraft took neither
        await Task.WhenAll(def.SetXpdrModeAsync(1, speech), def.SetXpdrModeAsync(2, speech));
        Assert.Equal(new[] { "Transponder mode did not change — still AUTO." }, speech.All);
    }

    [Fact]
    public async Task OverlappingAltitudeLimitWrites_OnlyTheLatestOneIsJudged()
    {
        var (def, speech) = Scripted(Atc("\"mode\":2,\"tss_limits_sel\":2"));
        await Task.WhenAll(def.SetTcasAltLimitAsync(1, speech), def.SetTcasAltLimitAsync(2, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task OverlappingSquawkWrites_TheSupersededOneNeitherSpeaksNorFallsBackToTheStockEvent()
    {
        var (def, speech) = Scripted(Atc("\"code\":1200"));       // the second entry landed
        var sim = new SimConnectManager(IntPtr.Zero);             // never connected: sends nothing
        await Task.WhenAll(def.SetSquawkAsync(4321, sim, speech), def.SetSquawkAsync(1200, sim, speech));
        Assert.Equal(new[] { "Squawk 1200" }, speech.All);        // exactly once; the first said nothing
    }

    [Fact]
    public async Task OverlappingFlightIdWrites_OnlyTheLatestOneIsJudged()
    {
        var (def, speech) = Scripted(Atc("\"id\":\"BAW2\""));
        await Task.WhenAll(def.SetFlightIdAsync("BAW1", speech), def.SetFlightIdAsync("BAW2", speech));
        Assert.Equal(new[] { "Flight ID BAW2" }, speech.All);
    }

    // The squawk fallback end to end: the bus never shows the code, the stock event goes out, and
    // the failure names what the transponder really reads.
    [Fact]
    public async Task Squawk_ThatNeverLands_NamesWhatTheTransponderReads()
    {
        var (def, speech) = Scripted(Atc("\"code\":1200"));
        await def.SetSquawkAsync(4321, new SimConnectManager(IntPtr.Zero), speech);
        Assert.Equal(new[] { "Squawk not set — the transponder reads 1200." }, speech.All);
    }

    // ---- The bridge holds a control's key while its write is in flight --------------------------
    // MainForm sets a snapshot value straight onto the combo (UpdateControlFromSimVar has no
    // just-set or focus guard), so an emission mid-write would snap the combo to an intermediate
    // value under the pilot's arrow key.

    [Fact]
    public void Bridge_HoldsAKey_WhileItsWriteIsInFlight_AndOnlyThatKey()
    {
        var def = new SynapticA220Definition();
        var seen = new List<(string Key, double Value)>();
        def.CommBusValueChanged += (k, v) => seen.Add((k, v));

        def.HoldBridge("A22X_XPDR_MODE");
        def.EmitBridgeValue("A22X_XPDR_MODE", 1);
        def.EmitBridgeValue("A22X_XPDR_MODE", 2, force: true);       // force does not pierce a hold
        def.EmitBridgeValue("A22X_TCAS_ALT_LIMIT", 3);
        Assert.Equal(new[] { ("A22X_TCAS_ALT_LIMIT", 3.0) }, seen);

        def.HoldBridge("A22X_XPDR_MODE");                           // a second write overlaps the first
        def.ReleaseBridge("A22X_XPDR_MODE");
        def.EmitBridgeValue("A22X_XPDR_MODE", 2);
        Assert.Single(seen);                                        // still held: one write is in flight

        def.ReleaseBridge("A22X_XPDR_MODE");
        def.EmitBridgeValue("A22X_XPDR_MODE", 2);
        Assert.Equal(("A22X_XPDR_MODE", 2.0), seen[^1]);            // released: the truth goes out
    }

    [Fact]
    public async Task ModeWrite_HoldsTheBridgeUntilItIsJudged_ThenResendsTheTruth()
    {
        var def = new SynapticA220Definition { PollIntervalMs = 5, PollTimeoutCapMs = 150 };
        var speech = new SpeechCapture();
        var seen = new List<(string Key, double Value)>();
        def.CommBusValueChanged += (k, v) => seen.Add((k, v));
        def.AgentCallOverride = call =>
        {
            // Mid-write the pump delivers an old value: the held key must not reach the combo.
            if (call == "sys()") def.EmitBridgeValue("A22X_XPDR_MODE", 1);
            return Task.FromResult<string?>(call == "sys()" ? Atc("\"mode\":2") : "SENT");
        };
        await def.SetXpdrModeAsync(2, speech);
        Assert.DoesNotContain(seen, e => e.Key == "A22X_XPDR_MODE");
        def.ProcessSystems(new A220Systems.SystemsBlocks { atc = new A220Systems.AtcBlock { mode = 2 } });
        Assert.Contains(("A22X_XPDR_MODE", 2.0), seen);             // released and forced: the truth
        Assert.Empty(speech.All);
    }

    // ---- CTP altimeter, STD and minimums ([A220-33]) ------------------------------------------

    [Fact]
    public void CtpPanels_CarryTheAltimeterAndMinimumsControls()
    {
        Assert.Equal(new[]
        {
            "A22X_L_QNH_SET", "A22X_L_BARO_UNIT", "A22X_L_BARO_STD",
            "A22X_L_MINIMUMS_MODE", "A22X_L_MINIMUMS_SET", "A22X_L_NAV_SOURCE", "A22X_L_CROSSTUNE"
        }, Def.GetPanelControls()["CTP Captain"]);
        Assert.Equal(new[]
        {
            "A22X_R_QNH_SET", "A22X_R_BARO_UNIT", "A22X_R_BARO_STD",
            "A22X_R_MINIMUMS_MODE", "A22X_R_MINIMUMS_SET", "A22X_R_NAV_SOURCE", "A22X_R_CROSSTUNE"
        }, Def.GetPanelControls()["CTP First Officer"]);

        Assert.Equal(UpdateFrequency.Never, Vars["A22X_L_BARO_STD"].UpdateFrequency);   // a bus combo now, not a press flag
        Assert.False(Vars["A22X_L_BARO_STD"].RenderAsButton);
        // [VAR-7]: shares the batched HPA mirror's name, so it must itself stay off the batch.
        Assert.Equal(UpdateFrequency.OnRequest, Vars["A22X_L_BARO_UNIT"].UpdateFrequency);
        Assert.Equal("A22X L Altimeter HPA", Vars["A22X_L_BARO_UNIT"].Name);
        Assert.Equal("A22X_KOHLSMAN_MB", Vars["A22X_L_QNH_SET"].CurrentValueSourceKey);
        Assert.Equal("A22X_KOHLSMAN_MB_2", Vars["A22X_R_QNH_SET"].CurrentValueSourceKey);
        Assert.Equal("KOHLSMAN SETTING MB:2", Vars["A22X_KOHLSMAN_MB_2"].Name);          // the entry's prefill source must exist
        Assert.Equal("A22X_L_MINIMUMS", Vars["A22X_L_MINIMUMS_SET"].CurrentValueSourceKey);
        Assert.True(Vars["A22X_L_QNH_SET"].UnparseableTextAsNaN);
        Assert.True(Vars["A22X_R_MINIMUMS_SET"].UnparseableTextAsNaN);
    }

    [Fact]
    public void Bridge_CarriesBaroStdFromTheAds_AndMinimumsFromTheCtp()
    {
        var def = new SynapticA220Definition();
        var seen = new Dictionary<string, double>();
        def.CommBusValueChanged += (k, v) => seen[k] = v;
        var sys = FixtureSystems();
        sys.lads = new A220Systems.AdsBlock { qnh_std = true, qnh_unit = 0, qnh_inhg = 29.92 };
        def.ProcessSystems(sys);
        Assert.Equal(1, seen["A22X_L_BARO_STD"]);
        Assert.False(seen.ContainsKey("A22X_R_BARO_STD"));         // no R side in the capture: nothing claimed
        Assert.Equal(0, seen["A22X_L_MINIMUMS_MODE"]);
        Assert.Equal(1210, seen["A22X_L_MINIMUMS"]);
    }

    [Fact]
    public void AltimeterDescriptions_ReadTheCtpTruth()
    {
        var def = new SynapticA220Definition();
        def.ProcessSystems(FixtureSystems());
        Assert.Equal("Captain: 30.13 inches; minimums baro 1210 feet\r\nFirst officer: "
            + A220Systems.NotAvailable + "; minimums " + A220Systems.NotAvailable, def.DescribeAltimeters());
        Assert.Equal(((bool?)null, (bool?)false, (int?)0), def.DescribeCtp(1));   // STD unknown, inches, baro
        Assert.Equal("30.13", def.CurrentQnhEntry(1));
    }

    [Fact]
    public void ReadAltimeter_SpeaksWhatThePfdShows_OrSaysStandardIsNotKnown()
    {
        var sim = new SimConnectManager(IntPtr.Zero);                  // never connected: nothing cached
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(new A220Systems.SystemsBlocks
        {
            lads = new A220Systems.AdsBlock { qnh_std = true, qnh_unit = 0, qnh_inhg = 29.92 },
            rads = new A220Systems.AdsBlock { qnh_std = false, qnh_unit = 0, qnh_inhg = 30.13 },
        });
        def.HandleHotkeyAction(HotkeyAction.ReadAltimeter, sim, speech, null!, null!);
        Assert.Equal(new[] { "Altimeter standard, first officer 30.13 inches" }, speech.All);

        var down = new SpeechCapture();
        new SynapticA220Definition().HandleHotkeyAction(HotkeyAction.ReadAltimeter, sim, down, null!, null!);
        Assert.Equal(new[]
        {
            "Altimeter 29.92 inches, 1013 hectopascals. Standard not known — the display link is not connected."
        }, down.All);
    }

    // VAR-5: the older readouts in the hotkey switch speak invariant numbers. CI runs en-US, so only
    // a comma-decimal culture catches a "Mach 0,78" or "-3,0 degrees".
    [Theory]
    [InlineData(HotkeyAction.ReadSpeed, "{\"spd_in_mach\":true,\"spd_sel_mach\":0.78,\"spd_sel_ias\":250,\"spd_fms\":false}",
        "Manual speed Mach 0.78")]
    [InlineData(HotkeyAction.ReadFCUVerticalSpeedFPA, "{\"vs_mode\":1,\"vs_sel\":-3.0}", "Flight path angle -3.0 degrees")]
    public void FcpReadouts_SpeakInvariantNumbers_UnderAGermanCulture(HotkeyAction action, string fcpJson, string expected)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var def = new SynapticA220Definition();
            def.ProcessDisplaysSnapshot("{\"ok\":true,\"fcp\":" + fcpJson + "}");
            var speech = new SpeechCapture();
            Assert.True(def.HandleHotkeyAction(action, new SimConnectManager(IntPtr.Zero), speech, null!, null!));
            Assert.Equal(new[] { expected }, speech.All);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    // The write paths against a scripted display agent: write calls are recorded and answer SENT,
    // sys() answers the scripted blocks.

    private static string SysJson(params string[] stores) => "{" + string.Join(",", stores) + "}";
    private static string CtpStore(string side, string fields) => $"\"{side}ctp\":{{{fields}}}";
    private static string AdsStore(string side, string fields) => $"\"{side}ads\":{{{fields}}}";
    private static string QnhFields(string type, string value) => $"\"qnh\":{{\"type\":\"{type}\",\"value\":{value}}}";

    private static A220Systems.SystemsBlocks Blocks(params string[] stores) => A220Systems.ParseSystems(SysJson(stores))!;

    private static SynapticA220Definition CtpDef(Func<string?> sys, List<string>? sent = null)
    {
        var def = new SynapticA220Definition { PollIntervalMs = 5, PollTimeoutCapMs = 150 };
        def.AgentCallOverride = call =>
        {
            if (call == "sys()") return Task.FromResult(sys());
            lock (def) sent?.Add(call);
            return Task.FromResult<string?>("SENT");
        };
        return def;
    }

    private static string CtpQnhCall(int side, string type, string value)
        => $"ctpSet({side}, {{\"qnh\":{{\"type\":\"{type}\",\"value\":{value}}}}})";

    private static readonly IReadOnlyList<int> Cap = SynapticA220Definition.Captain;
    private static readonly IReadOnlyList<int> Fo = SynapticA220Definition.FirstOfficer;
    private static readonly IReadOnlyList<int> Both = SynapticA220Definition.BothSides;

    [Fact]
    public async Task Qnh_IsSentInEachSidesOwnUnit_AndTheReadBackIsSpoken()
    {
        var sent = new List<string>();
        var speech = new SpeechCapture();
        var def = CtpDef(() => SysJson(CtpStore("l", QnhFields("InHg", "30.12")), CtpStore("r", QnhFields("Hpa", "1020"))), sent);
        def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "29.92")), CtpStore("r", QnhFields("Hpa", "1013"))));

        await def.SetQnhAsync(1020, Both, speech);

        Assert.Equal(new[]
        {
            "ctpSet(1, {\"qnh\":{\"type\":\"InHg\",\"value\":30.12}})",
            "ctpSet(2, {\"qnh\":{\"type\":\"Hpa\",\"value\":1020}})"
        }, sent);
        Assert.DoesNotContain(speech.All, line => line.Contains("did not take"));
        Assert.Single(speech.All);
    }

    [Fact]
    public async Task Qnh_ThatLands_IsConfirmed_AndOneThatDoesNot_NamesWhatTheAircraftReads()
    {
        var landed = new SpeechCapture();
        var def = CtpDef(() => SysJson(CtpStore("l", QnhFields("InHg", "30.12"))));
        def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "29.92"))));
        await def.SetQnhAsync(30.12, Cap, landed);
        Assert.Equal(new[] { "Captain altimeter 30.12 inches" }, landed.All);

        var stuck = new SpeechCapture();
        var kept = CtpDef(() => SysJson(CtpStore("l", QnhFields("InHg", "29.92"))));
        kept.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "29.92"))));
        await kept.SetQnhAsync(30.12, Cap, stuck);
        Assert.Equal(new[] { "Captain altimeter did not take the setting — reads 29.92 inches." }, stuck.All);
    }

    [Theory]
    [InlineData(500.0)]
    [InlineData(26.99)]
    [InlineData(double.NaN)]
    public async Task Qnh_OutOfRange_IsRefusedAloud_AndNothingIsSent(double input)
    {
        var sent = new List<string>();
        var speech = new SpeechCapture();
        await CtpDef(() => "{}", sent).SetQnhAsync(input, Cap, speech);
        Assert.Equal(new[] { "QNH must be 940 to 1090 hectopascals or 27.00 to 32.00 inches." }, speech.All);
        Assert.Empty(sent);
    }

    // CI runs en-US: only a comma-decimal culture catches a "30,13" in the payload or in speech.
    [Fact]
    public async Task Qnh_UnderAGermanCulture_StaysInvariant()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var sent = new List<string>();
            var speech = new SpeechCapture();
            var def = CtpDef(() => SysJson(CtpStore("l", QnhFields("InHg", "30.13"))), sent);
            def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "29.92"))));
            await def.SetQnhAsync(30.13, Cap, speech);
            Assert.Equal(new[] { "ctpSet(1, {\"qnh\":{\"type\":\"InHg\",\"value\":30.13}})" }, sent);
            Assert.Equal(new[] { "Captain altimeter 30.13 inches" }, speech.All);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public async Task Unit_ReSendsTheCurrentSettingInTheOtherUnit_AndSpeaksTheConvertedValue()
    {
        var sent = new List<string>();
        var speech = new SpeechCapture();
        var def = CtpDef(() => SysJson(CtpStore("l", QnhFields("Hpa", "1020"))), sent);
        def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "30.13"))));

        await def.SetBaroUnitAsync(true, Cap, speech);

        Assert.Equal(new[] { "ctpSet(1, {\"qnh\":{\"type\":\"Hpa\",\"value\":1020}})" }, sent);
        Assert.Equal(new[] { "Captain altimeter 1020 hectopascals" }, speech.All);
    }

    [Fact]
    public async Task Unit_TheAircraftKeeps_IsSpoken_AndNoUnitIsGuessedWithoutCtpData()
    {
        var kept = new SpeechCapture();
        var def = CtpDef(() => SysJson(CtpStore("l", QnhFields("InHg", "30.13"))));
        def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "30.13"))));
        await def.SetBaroUnitAsync(true, Cap, kept);
        Assert.Equal(new[] { "The A220 did not change the altimeter unit — still inches." }, kept.All);

        var sent = new List<string>();
        var none = new SpeechCapture();
        await CtpDef(() => "{}", sent).SetBaroUnitAsync(true, Cap, none);   // no CTP block was ever read
        Assert.Equal(new[] { "Altimeter unit not set: the A220 display link is not connected." }, none.All);
        Assert.Empty(sent);
    }

    // STD is a QNH SetConfig too (live 2026-10-15): qnh_std moves nothing, but EXACTLY 29.92 in /
    // 1013 hPa selects STD on that side only, and the side's own QNH sent back takes it out.
    private static string Ads(string side, bool std, int unit) => AdsStore(side, $"\"qnh_std\":{(std ? "true" : "false")},\"qnh_unit\":{unit}");

    [Fact]
    public async Task Std_On_IsTheExactStandardQnh_PerSide_InEachSidesPfdUnit()
    {
        var sent = new List<string>();
        var speech = new SpeechCapture();
        var def = CtpDef(() => SysJson(Ads("l", true, 0), Ads("r", true, 1)), sent);
        def.ProcessSystems(Blocks(Ads("l", false, 0), Ads("r", false, 1)));
        await def.SetStdAsync(true, Both, speech);
        Assert.Equal(new[] { CtpQnhCall(1, "InHg", "29.92"), CtpQnhCall(2, "Hpa", "1013") }, sent);
        Assert.Equal(new[] { "Altimeter standard, both sides" }, speech.All);
    }

    [Fact]
    public async Task Std_FromAPanelCombo_IsSilentWhenItLands_AndSpokenWhenItDoesNot()
    {
        var sent = new List<string>();
        var landed = new SpeechCapture();
        var def = CtpDef(() => SysJson(Ads("l", true, 0)), sent);
        def.ProcessSystems(Blocks(Ads("l", false, 0)));
        await def.SetStdAsync(true, Cap, landed, speakSuccess: false);
        Assert.Equal(new[] { CtpQnhCall(1, "InHg", "29.92") }, sent);
        Assert.Empty(landed.All);

        var stuck = new SpeechCapture();
        var def2 = CtpDef(() => SysJson(Ads("l", false, 0)));
        def2.ProcessSystems(Blocks(Ads("l", false, 0)));
        await def2.SetStdAsync(true, Cap, stuck, speakSuccess: false);
        Assert.Equal(new[] { "Captain baro did not change — still QNH." }, stuck.All);
    }

    [Fact]
    public async Task Std_Off_ReSendsTheSidesOwnCtpQnh_AndNeverAGuess()
    {
        var sent = new List<string>();
        var off = new SpeechCapture();
        var def = CtpDef(() => SysJson(Ads("l", false, 0), CtpStore("l", QnhFields("InHg", "30.13"))), sent);
        def.ProcessSystems(Blocks(Ads("l", true, 0), CtpStore("l", QnhFields("InHg", "30.13"))));
        await def.SetStdAsync(false, Cap, off);
        Assert.Equal(new[] { CtpQnhCall(1, "InHg", "30.13") }, sent);
        Assert.Equal(new[] { "Captain altimeter 30.13 inches" }, off.All);

        // A kept QNH that IS the standard value is sent as one the CTP will not take as STD.
        var nudged = new List<string>();
        var def3 = CtpDef(() => SysJson(Ads("r", false, 1), CtpStore("r", QnhFields("Hpa", "1013"))), nudged);
        def3.ProcessSystems(Blocks(Ads("r", true, 1), CtpStore("r", QnhFields("Hpa", "1013"))));
        await def3.SetStdAsync(false, Fo, new SpeechCapture());
        Assert.Equal(new[] { CtpQnhCall(2, "Hpa", "1013.25") }, nudged);

        // No CTP QNH reported for the side: nothing is sent, and it is said.
        var none = new List<string>();
        var unknown = new SpeechCapture();
        var def2 = CtpDef(() => "{}", none);
        def2.ProcessSystems(Blocks(Ads("l", true, 0)));
        await def2.SetStdAsync(false, Cap, unknown);
        Assert.Empty(none);
        Assert.Equal(new[] { "Captain baro not set: the A220 display link has not reported that altimeter." }, unknown.All);
    }

    [Fact]
    public async Task Qnh_OfExactlyTheStandardValue_IsSentAsAQnh_NeverAsStd()
    {
        // Live 2026-10-15: a SetConfig of exactly 1013 / 29.92 selects STD; the pilot typed a QNH.
        var hpa = new List<string>();
        var hpaSpeech = new SpeechCapture();
        var def = CtpDef(() => SysJson(CtpStore("l", QnhFields("Hpa", "1013"))), hpa);   // the CTP stores 1013.25 as 1013
        def.ProcessSystems(Blocks(Ads("l", false, 1)));
        await def.SetQnhAsync(1013, Cap, hpaSpeech);
        Assert.Equal(new[] { CtpQnhCall(1, "Hpa", "1013.25") }, hpa);
        Assert.Equal(new[] { "Captain altimeter 1013 hectopascals" }, hpaSpeech.All);

        var inches = new List<string>();
        var def2 = CtpDef(() => SysJson(CtpStore("r", QnhFields("InHg", "29.921253"))), inches);
        def2.ProcessSystems(Blocks(Ads("r", false, 0)));
        await def2.SetQnhAsync(29.92, Fo, new SpeechCapture());
        Assert.Equal(new[] { CtpQnhCall(2, "InHg", "29.921253") }, inches);
    }

    [Fact]
    public async Task Unit_InStd_SendsTheStandardValueInTheNewUnit_AndReadsTheAdsUnit()
    {
        // Live 2026-10-15: in STD the CTP takes exactly 1013 / 29.92 as a unit change and stays standard;
        // the side's own QNH re-sent would take it out of STD.
        var sent = new List<string>();
        var speech = new SpeechCapture();
        var def = CtpDef(() => SysJson(AdsStore("l", "\"qnh_std\":true,\"qnh_unit\":1"), CtpStore("l", QnhFields("InHg", "30.13"))), sent);
        def.ProcessSystems(Blocks(AdsStore("l", "\"qnh_std\":true,\"qnh_unit\":0"), CtpStore("l", QnhFields("InHg", "30.13"))));
        await def.SetBaroUnitAsync(true, Cap, speech);
        Assert.Equal(new[] { "ctpSet(1, {\"qnh\":{\"type\":\"Hpa\",\"value\":1013}})" }, sent);
        Assert.Equal(new[] { "Captain altimeter standard, unit hectopascals" }, speech.All);
    }

    [Fact]
    public async Task ToggleStd_FlipsTheFirstChosenSidesState_OrSaysItIsNotKnown()
    {
        var sent = new List<string>();
        var speech = new SpeechCapture();
        var def = CtpDef(() => SysJson(Ads("l", true, 0)), sent);
        def.ProcessSystems(Blocks(Ads("l", false, 0)));
        await def.ToggleStdAsync(Cap, speech);
        Assert.Equal(new[] { CtpQnhCall(1, "InHg", "29.92") }, sent);
        Assert.Equal(new[] { "Captain altimeter standard" }, speech.All);

        var unknownSent = new List<string>();
        var unknown = new SpeechCapture();
        await CtpDef(() => "{}", unknownSent).ToggleStdAsync(Cap, unknown);
        Assert.Equal(new[] { "Standard is not known: the A220 display link is not connected." }, unknown.All);
        Assert.Empty(unknownSent);
    }

    // A lost read-back saying "did not change — still QNH" would invite a second press, which the
    // toggle would turn back into STD off (a reviewer-identified risk). Nothing is claimed, and the
    // toggle will not act on the unknown state.
    [Fact]
    public async Task Std_WithNoReadBack_IsNotConfirmed_AndTheToggleWillNotActOnIt()
    {
        var sent = new List<string>();
        var speech = new SpeechCapture();
        var def = CtpDef(() => "{}", sent);                         // the write goes out; no ADS/CTP store answers
        def.ProcessSystems(Blocks(Ads("l", false, 0)));

        await def.ToggleStdAsync(Cap, speech);
        Assert.Equal(new[] { "Captain baro not confirmed: the A220 display link did not report it." }, speech.All);

        await def.ToggleStdAsync(Cap, speech);                      // pressed again
        Assert.Equal(new[] { CtpQnhCall(1, "InHg", "29.92") }, sent);   // nothing sent from an unknown state
        Assert.Equal("Standard is not known: the A220 display link is not connected.", speech.All[^1]);
    }

    [Fact]
    public async Task TransponderWrites_WithNoAtcBlock_AreNotConfirmed()
    {
        var (def, speech) = Scripted("{}");
        await def.SetXpdrModeAsync(2, speech);
        await def.SetTcasAltLimitAsync(1, speech);
        Assert.Equal(new[]
        {
            "Transponder mode not confirmed: the A220 display link did not report it.",
            "TCAS altitude limit not confirmed: the A220 display link did not report it."
        }, speech.All);
    }

    [Fact]
    public async Task MinimumsMode_IsSilentWhenItLands_AndNamesWhatItKeptWhenItDoesNot()
    {
        var sent = new List<string>();
        var landed = new SpeechCapture();
        await CtpDef(() => SysJson(CtpStore("l", "\"minimums_mode\":1,\"minimums\":200")), sent).SetMinimumsModeAsync(1, Cap, landed);
        Assert.Equal(new[] { "ctpSet(1, {\"minimums_mode\":1})" }, sent);
        Assert.Empty(landed.All);

        var stuck = new SpeechCapture();
        await CtpDef(() => SysJson(CtpStore("l", "\"minimums_mode\":0,\"minimums\":1210"))).SetMinimumsModeAsync(1, Cap, stuck);
        Assert.Equal(new[] { "Minimums mode did not change — captain still baro 1210 feet." }, stuck.All);
    }

    [Fact]
    public async Task Minimums_AreConfirmedFromTheReadBack_AndRefusedWhenOutOfRange()
    {
        var sent = new List<string>();
        var landed = new SpeechCapture();
        await CtpDef(() => SysJson(CtpStore("l", "\"minimums_mode\":0,\"minimums\":200")), sent).SetMinimumsAsync(200, Cap, landed);
        Assert.Equal(new[] { "ctpSet(1, {\"minimums\":200})" }, sent);
        Assert.Equal(new[] { "Captain minimums baro 200 feet" }, landed.All);

        var both = new SpeechCapture();
        await CtpDef(() => SysJson(CtpStore("l", "\"minimums\":200"), CtpStore("r", "\"minimums\":200"))).SetMinimumsAsync(200, Both, both);
        Assert.Equal(new[] { "Minimums 200 feet, both sides" }, both.All);

        var stuck = new SpeechCapture();
        await CtpDef(() => SysJson(CtpStore("l", "\"minimums_mode\":0,\"minimums\":1210"))).SetMinimumsAsync(200, Cap, stuck);
        Assert.Equal(new[] { "Captain minimums did not take 200 feet — reads baro 1210 feet." }, stuck.All);

        var outOfRange = new SpeechCapture();
        var none = new List<string>();
        await CtpDef(() => "{}", none).SetMinimumsAsync(41001, Cap, outOfRange);
        Assert.Equal(new[] { "Minimums must be 0 to 41000 feet." }, outOfRange.All);
        Assert.Empty(none);
    }

    [Fact]
    public async Task CtpWrites_AreRefusedAloud_WhenTheDisplayLinkCannotCarryThem()
    {
        var speech = new SpeechCapture();
        var def = new SynapticA220Definition { PollIntervalMs = 5, PollTimeoutCapMs = 150 };
        def.AgentCallOverride = _ => Task.FromResult<string?>("NO_BUS");
        def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "29.92"))));

        await def.SetQnhAsync(1013, Cap, speech);
        await def.SetBaroUnitAsync(true, Cap, speech);
        await def.SetStdAsync(true, Cap, speech);
        await def.SetMinimumsModeAsync(1, Cap, speech);
        await def.SetMinimumsAsync(200, Cap, speech);

        Assert.Equal(new[]
        {
            "Altimeter not set: the A220 display link is not connected.",
            "Altimeter unit not set: the A220 display link is not connected.",
            "Baro standard not set: the A220 display link is not connected.",
            "Minimums mode not set: the A220 display link is not connected.",
            "Minimums not set: the A220 display link is not connected."
        }, speech.All);
    }

    [Theory]
    [InlineData("A22X_L_QNH_SET", 1013.0, 1)]
    [InlineData("A22X_R_QNH_SET", 1013.0, 2)]
    [InlineData("A22X_L_BARO_UNIT", 1.0, 1)]
    [InlineData("A22X_R_BARO_UNIT", 1.0, 2)]
    [InlineData("A22X_L_BARO_STD", 1.0, 1)]
    [InlineData("A22X_R_BARO_STD", 1.0, 2)]
    [InlineData("A22X_L_MINIMUMS_MODE", 1.0, 1)]
    [InlineData("A22X_R_MINIMUMS_MODE", 1.0, 2)]
    [InlineData("A22X_L_MINIMUMS_SET", 200.0, 1)]
    [InlineData("A22X_R_MINIMUMS_SET", 200.0, 2)]
    public void PanelControls_AreClaimed_AndWrittenThroughTheirSidesCtp(string key, double value, int side)
    {
        var sent = new List<string>();
        var def = CtpDef(() => "{}", sent);
        def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "30.13")), CtpStore("r", QnhFields("InHg", "30.13"))));

        bool handled = def.HandleUIVariableSet(key, value, Vars[key], new SimConnectManager(IntPtr.Zero), new SpeechCapture());

        Assert.True(handled);
        Assert.StartsWith($"ctpSet({side}, ", Assert.Single(sent));   // the first write goes out synchronously
    }

    // ---- Latest write wins, per control and per side ------------------------------------------
    // Arrowing through a DropDownList combo commits and writes on every press; a write the pilot has
    // already moved past reads back a LATER pick and used to report a failure about a value nobody
    // was asking for any more.

    [Fact]
    public async Task OverlappingStdWrites_OnlyTheLatestOneIsJudged()
    {
        var (def, speech) = (CtpDef(() => SysJson(Ads("l", false, 0), CtpStore("l", QnhFields("InHg", "30.13")))), new SpeechCapture());
        def.ProcessSystems(Blocks(Ads("l", false, 0), CtpStore("l", QnhFields("InHg", "30.13"))));
        await Task.WhenAll(def.SetStdAsync(true, Cap, speech, speakSuccess: false), def.SetStdAsync(false, Cap, speech, speakSuccess: false));
        Assert.Empty(speech.All);                                 // the second pick landed; the first said nothing
    }

    [Fact]
    public async Task OverlappingStdWrites_TheLatestStillReportsItsOwnFailure_Once()
    {
        var (def, speech) = (CtpDef(() => SysJson(Ads("l", false, 0))), new SpeechCapture());
        def.ProcessSystems(Blocks(Ads("l", false, 0)));
        await Task.WhenAll(def.SetStdAsync(true, Cap, speech, speakSuccess: false), def.SetStdAsync(true, Cap, speech, speakSuccess: false));
        Assert.Equal(new[] { "Captain baro did not change — still QNH." }, speech.All);
    }

    [Fact]
    public async Task OverlappingMinimumsModeWrites_OnlyTheLatestOneIsJudged()
    {
        var (def, speech) = (CtpDef(() => SysJson(CtpStore("l", "\"minimums_mode\":1,\"minimums\":200"))), new SpeechCapture());
        await Task.WhenAll(def.SetMinimumsModeAsync(0, Cap, speech), def.SetMinimumsModeAsync(1, Cap, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task OverlappingMinimumsWrites_OnlyTheLatestOneIsConfirmed()
    {
        var (def, speech) = (CtpDef(() => SysJson(CtpStore("l", "\"minimums_mode\":0,\"minimums\":500"))), new SpeechCapture());
        await Task.WhenAll(def.SetMinimumsAsync(300, Cap, speech), def.SetMinimumsAsync(500, Cap, speech));
        Assert.Equal(new[] { "Captain minimums baro 500 feet" }, speech.All);
    }

    [Fact]
    public async Task OverlappingQnhWrites_OnlyTheLatestOneIsConfirmed()
    {
        var (def, speech) = (CtpDef(() => SysJson(CtpStore("l", QnhFields("InHg", "30.12")))), new SpeechCapture());
        def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "29.92"))));
        await Task.WhenAll(def.SetQnhAsync(29.92, Cap, speech), def.SetQnhAsync(30.12, Cap, speech));
        Assert.Equal(new[] { "Captain altimeter 30.12 inches" }, speech.All);
    }

    [Fact]
    public async Task OverlappingUnitWrites_OnlyTheLatestOneIsJudged()
    {
        var (def, speech) = (CtpDef(() => SysJson(CtpStore("l", QnhFields("Hpa", "1020")))), new SpeechCapture());
        def.ProcessSystems(Blocks(CtpStore("l", QnhFields("InHg", "30.13"))));
        await Task.WhenAll(def.SetBaroUnitAsync(false, Cap, speech), def.SetBaroUnitAsync(true, Cap, speech));
        Assert.Equal(new[] { "Captain altimeter 1020 hectopascals" }, speech.All);
    }

    // The two sides are independent: a first-officer write does not silence a captain write that
    // nobody has overridden.
    [Fact]
    public async Task WritesToDifferentSides_DoNotSupersedeEachOther()
    {
        var (def, speech) = (CtpDef(() => SysJson(Ads("l", false, 0), Ads("r", true, 0))), new SpeechCapture());
        def.ProcessSystems(Blocks(Ads("l", false, 0), Ads("r", false, 0)));
        await Task.WhenAll(def.SetStdAsync(true, Cap, speech, speakSuccess: false), def.SetStdAsync(true, Fo, speech, speakSuccess: false));
        Assert.Equal(new[] { "Captain baro did not change — still QNH." }, speech.All);   // only the captain's failed
    }

    // A both-sides write that a newer first-officer write overtakes loses only the first-officer
    // side: it neither sends that side's stale value over the newer one nor judges it.
    [Fact]
    public async Task ABothSidesWrite_OvertakenOnOneSide_KeepsTheOtherSide_AndNeverSendsItsStaleValue()
    {
        var sent = new List<string>();
        var gate = new TaskCompletionSource<string?>();
        int writes = 0;
        var def = new SynapticA220Definition { PollIntervalMs = 5, PollTimeoutCapMs = 150 };
        def.AgentCallOverride = call =>
        {
            if (call == "sys()") return Task.FromResult<string?>(SysJson(Ads("l", true, 0), Ads("r", false, 0)));
            sent.Add(call);
            return writes++ == 0 ? gate.Task : Task.FromResult<string?>("SENT");   // the first write is still in flight
        };
        def.ProcessSystems(Blocks(Ads("l", false, 0), Ads("r", true, 0), CtpStore("r", QnhFields("InHg", "30.13"))));
        var speech = new SpeechCapture();

        var both = def.SetStdAsync(true, Both, speech, speakSuccess: false);        // captain write in flight
        var fo = def.SetStdAsync(false, Fo, speech, speakSuccess: false);           // overtakes the first-officer side
        gate.SetResult("SENT");
        await Task.WhenAll(both, fo);

        Assert.Equal(new[] { CtpQnhCall(1, "InHg", "29.92"), CtpQnhCall(2, "InHg", "30.13") }, sent);
        Assert.Empty(speech.All);                                                    // both landed; no false failure
    }

    // ---- The bridge holds a CTP control's key while its write is in flight ----------------------

    [Fact]
    public async Task StdWrite_HoldsItsBridgeKeyUntilJudged_ThenResendsTheTruth()
    {
        var def = new SynapticA220Definition { PollIntervalMs = 5, PollTimeoutCapMs = 150 };
        var speech = new SpeechCapture();
        def.ProcessSystems(Blocks(Ads("l", false, 0)));             // the side's unit, before the bridge is watched
        var seen = new List<(string Key, double Value)>();
        def.CommBusValueChanged += (k, v) => seen.Add((k, v));
        def.AgentCallOverride = call =>
        {
            // Mid-write the pump delivers the old value: the held key must not reach the combo.
            if (call == "sys()") def.EmitBridgeValue("A22X_L_BARO_STD", 0);
            return Task.FromResult<string?>(call == "sys()" ? SysJson(Ads("l", true, 0)) : "SENT");
        };

        await def.SetStdAsync(true, Cap, speech, speakSuccess: false);

        Assert.DoesNotContain(seen, e => e.Key == "A22X_L_BARO_STD");
        def.ProcessSystems(Blocks(Ads("l", true, 0)));
        Assert.Contains(("A22X_L_BARO_STD", 1.0), seen);              // released and forced: the truth
        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task MinimumsModeWrite_HoldsItsBridgeKey_ThenResendsTheTruth()
    {
        var def = new SynapticA220Definition { PollIntervalMs = 5, PollTimeoutCapMs = 150 };
        var speech = new SpeechCapture();
        var seen = new List<(string Key, double Value)>();
        def.CommBusValueChanged += (k, v) => seen.Add((k, v));
        def.AgentCallOverride = call =>
        {
            if (call == "sys()") def.EmitBridgeValue("A22X_L_MINIMUMS_MODE", 0);   // the old mode, mid-write
            return Task.FromResult<string?>(call == "sys()" ? SysJson(CtpStore("l", "\"minimums_mode\":1")) : "SENT");
        };

        await def.SetMinimumsModeAsync(1, Cap, speech);

        Assert.DoesNotContain(seen, e => e.Key == "A22X_L_MINIMUMS_MODE");
        def.ProcessSystems(Blocks(CtpStore("l", "\"minimums_mode\":1")));
        Assert.Contains(("A22X_L_MINIMUMS_MODE", 1.0), seen);
        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task MinimumsWrite_HoldsTheBusValueItPrefillsFrom_ThenResendsTheTruth()
    {
        var def = new SynapticA220Definition { PollIntervalMs = 5, PollTimeoutCapMs = 150 };
        var speech = new SpeechCapture();
        var seen = new List<(string Key, double Value)>();
        def.CommBusValueChanged += (k, v) => seen.Add((k, v));
        def.AgentCallOverride = call =>
        {
            if (call == "sys()") def.EmitBridgeValue("A22X_L_MINIMUMS", 1210);   // the old value, mid-write
            return Task.FromResult<string?>(call == "sys()" ? SysJson(CtpStore("l", "\"minimums\":200")) : "SENT");
        };

        await def.SetMinimumsAsync(200, Cap, speech);

        Assert.DoesNotContain(seen, e => e.Key == "A22X_L_MINIMUMS");
        def.ProcessSystems(Blocks(CtpStore("l", "\"minimums\":200")));
        Assert.Contains(("A22X_L_MINIMUMS", 200.0), seen);
    }

    [Fact]
    public void Autobrake_IsTheAircraftsFivePositionScale()
    {
        var v = Vars["A22X_AUTOBRAKE"];
        Assert.Equal("A22X Autobrake", v.Name);
        Assert.Equal(new[] { "RTO", "OFF", "LO", "MED", "HI" }, Enumerable.Range(0, 5).Select(i => v.ValueDescriptions[i]));
    }

    [Fact]
    public void Autobrake_IsNeverSpokenByTheGenericPath()
    {
        // CORE-7: the step walk says a refusal itself and a success is silent (the reader reads the
        // pick). MainForm's generic announce is Continuous + IsAnnounced only, so this OnRequest
        // combo must stay out of it or every walked detent would be spoken a second time.
        var v = Vars["A22X_AUTOBRAKE"];
        Assert.False(v.IsAnnounced);
        Assert.NotEqual(UpdateFrequency.Continuous, v.UpdateFrequency);
    }

    [Fact]
    public void AutobrakeWalk_ReleasesTheSelector_WhenItEnds()
    {
        var def = new SynapticA220Definition();
        def.StopDisplayMonitoring(null!);                   // retired: ProcessSimVarUpdate must not start a real Coherent poll
        var speech = new SpeechCapture();
        var mgr = new SimConnectManager(IntPtr.Zero);       // never connected: the walk ends at once, however it ends
        Assert.True(def.HandleUIVariableSet("A22X_AUTOBRAKE", 4, Vars["A22X_AUTOBRAKE"], mgr, speech));

        // A walk in flight consumes the selector's deliveries (the combo is not yanked through each
        // detent the knob passes); once it ends they must reach the combo again, or the panel never
        // shows the selector's truth after the walk. In flight is not assertable here (the walk's
        // first read needs the SimConnect assembly): that is in the in-sim plan.
        Assert.True(SpinWait.SpinUntil(() => !def.ProcessSimVarUpdate("A22X_AUTOBRAKE", 1, speech), 5000),
            "the walk never released the selector");
    }
}

[Collection("SettingsManagerGlobalState")]
public class SynapticA220SystemsSpeechTests : IDisposable
{
    private readonly List<string> _savedMutes = SettingsManager.Current.A220DisabledMonitorVariables;

    public SynapticA220SystemsSpeechTests()
    {
        SettingsManager.Current.A220DisabledMonitorVariables = new List<string>();
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    public void Dispose()
    {
        SettingsManager.Current.A220DisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static void Mute(params string[] rowKeys)
    {
        SettingsManager.Current.A220DisabledMonitorVariables = new List<string>(rowKeys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static A220Systems.SystemsBlocks WithGear(int icon) => new()
    {
        gear = new A220Systems.GearBlock
        {
            nose_icon = icon, left_icon = icon, right_icon = icon,
            nose_color = 3, left_color = 3, right_color = 3
        }
    };

    [Fact]
    public void GearTransition_IsSpoken_AndItsRowMutesIt()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.UseAnnouncer(speech);
        def.ProcessSystems(WithGear(2));
        def.ProcessSystems(WithGear(0));
        Assert.Equal(new[] { "Gear in transit" }, speech.All);

        SettingsManager.Current.A220DisabledMonitorVariables = new List<string> { A220MonitorRows.GearKey };
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
        def.ProcessSystems(WithGear(1));
        Assert.Equal(new[] { "Gear in transit" }, speech.All);       // "Gear up" muted
    }

    [Fact]
    public void ContextReset_RebaselinesTheMonitors()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.UseAnnouncer(speech);
        def.ProcessSystems(WithGear(2));
        def.RequestSystemsRebaseline();                              // a flight load
        def.ProcessSystems(WithGear(1));                             // the loaded flight is airborne
        Assert.Empty(speech.All);                                    // not a gear movement
    }

    // ---- Every call-out answers to its OWN Ctrl+M row (the pump runs outside the mute wrap) ----

    /// <summary>`before` then `after` speaks exactly <paramref name="expected"/>; with the row muted, nothing.</summary>
    private static void AssertSpokenUnlessMuted(string rowKey, A220Systems.SystemsBlocks before,
        A220Systems.SystemsBlocks after, params string[] expected)
    {
        Mute();
        var loud = new SynapticA220Definition();
        var heard = new SpeechCapture();
        loud.UseAnnouncer(heard);
        loud.ProcessSystems(before);
        loud.ProcessSystems(after);
        Assert.Equal(expected, heard.All);

        Mute(rowKey);
        var quiet = new SynapticA220Definition();
        var silent = new SpeechCapture();
        quiet.UseAnnouncer(silent);
        quiet.ProcessSystems(before);
        quiet.ProcessSystems(after);
        Assert.Empty(silent.All);
    }

    private static A220Systems.SystemsBlocks WithCas(params int[] indices) => new()
    {
        cas = new A220Systems.CasBlock { msg = SynapticA220SystemsTests.Words(indices) }
    };

    [Fact]
    public void CasCautions_AreMutedByTheWarningsRow()
        => AssertSpokenUnlessMuted(A220MonitorRows.CasWarningsKey, WithCas(), WithCas(526), "Caution: EMER LTS OFF");

    [Fact]
    public void CasAdvisories_AreMutedByTheAdvisoriesRow()
        => AssertSpokenUnlessMuted(A220MonitorRows.CasAdvisoriesKey, WithCas(), WithCas(341), "Advisory: EXT PWR AVAIL");

    [Fact]
    public void EngineStart_IsMutedByItsRow()
        => AssertSpokenUnlessMuted(A220MonitorRows.EngineStartKey,
            new A220Systems.SystemsBlocks { eng = new A220Systems.EngineBlock { eng1_start = false } },
            new A220Systems.SystemsBlocks { eng = new A220Systems.EngineBlock { eng1_start = true } },
            "Engine 1 starting");

    private static A220Systems.SystemsBlocks WithIrs(int mode) => new()
    {
        fmspos = new A220Systems.FmsPositionBlock
        {
            irs_1 = new A220Systems.IrsSlot { irs = new A220Systems.IrsUnit { mode = mode, time_to_align = 6 } }
        }
    };

    [Fact]
    public void IrsAligned_IsMutedByItsRow()
        => AssertSpokenUnlessMuted(A220MonitorRows.IrsKey, WithIrs(1), WithIrs(3), "IRS aligned");

    [Fact]
    public void IrsCallOut_KeepsItsBaselineAcrossAPollWithNeitherBlock()
    {
        // The call site passes haveData = (lpfd != null || fmspos != null): a poll that carries
        // neither store changes nothing, so aligning -> (gap) -> aligned still speaks.
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.UseAnnouncer(speech);
        def.ProcessSystems(WithIrs(1));
        def.ProcessSystems(new A220Systems.SystemsBlocks());
        def.ProcessSystems(WithIrs(3));
        Assert.Equal(new[] { "IRS aligned" }, speech.All);
    }

    private static A220Systems.SystemsBlocks WithFwdPaxDoor(int value) => new()
    {
        doors = new A220Systems.DoorsBlock { fwd_pax = value }
    };

    [Fact]
    public void DoorChange_IsMutedByItsRow()
        => AssertSpokenUnlessMuted(A220MonitorRows.DoorsKey, WithFwdPaxDoor(3), WithFwdPaxDoor(1),
            "Forward passenger door open");

    // ---- CAS source switch: the store is the truth, the EICAS scrape only a fallback ([A220-36]) ----

    private const string Caution = "[{\"t\":\"EMER LTS OFF\",\"c\":\"amber\"}]";

    private static string Snap(string casTokens, string sysJson = "")
        => "{\"ok\":true,\"cas\":" + casTokens + (sysJson.Length == 0 ? "" : ",\"sys\":" + sysJson) + "}";

    private static string StoreMsg(params int[] indices)
        => "{\"cas\":{\"msg\":[" + string.Join(",", SynapticA220SystemsTests.Words(indices)) + "]}}";

    private static (SynapticA220Definition Def, SpeechCapture Speech) Pump()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.UseAnnouncer(speech);
        return (def, speech);
    }

    [Fact]
    public void CasScrape_Speaks_WhileTheStoreIsMissing()
    {
        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Snap("[]"));                      // silent baseline
        def.ProcessDisplaysSnapshot(Snap(Caution));
        Assert.Equal(new[] { "Caution: EMER LTS OFF" }, speech.All);
    }

    [Fact]
    public void CasScrape_IsSilent_WhileTheStoreIsLive_AndRebaselinesWhenItTakesBackOver()
    {
        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Snap("[]", StoreMsg()));
        def.ProcessDisplaysSnapshot(Snap(Caution, StoreMsg()));       // scrape changed, store says nothing new
        Assert.Empty(speech.All);

        def.ProcessDisplaysSnapshot(Snap(Caution, "{}"));             // store gone: the scrape re-baselines silently
        Assert.Empty(speech.All);

        def.ProcessDisplaysSnapshot(Snap("[]", "{}"));                // and now diffs against that baseline
        Assert.Equal(new[] { "EMER LTS OFF cleared" }, speech.All);
    }

    [Fact]
    public void CasStore_Speaks_ThroughThePump()
    {
        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Snap("[]", StoreMsg()));
        def.ProcessDisplaysSnapshot(Snap("[]", StoreMsg(526)));
        Assert.Equal(new[] { "Caution: EMER LTS OFF" }, speech.All);
        Assert.True(def.TryGetDisplayOverride("A22X_CAS_STATE", 0, out string row));
        Assert.Equal("1 caution", row);
    }

    [Fact]
    public void CasRow_SaysNotAvailable_NeverNoMessages_WhenTheStoreIsMissing()
    {
        var (def, _) = Pump();
        def.ProcessDisplaysSnapshot(Snap("[]", StoreMsg()));
        Assert.True(def.TryGetDisplayOverride("A22X_CAS_STATE", 0, out string live));
        Assert.Equal("no messages", live);

        def.ProcessDisplaysSnapshot(Snap("[]", "{}"));                // the CAS store went stale for a poll
        Assert.True(def.TryGetDisplayOverride("A22X_CAS_STATE", 0, out string gap));
        Assert.Equal(A220Systems.NotAvailable, gap);
    }

    // ---- A burst of new CAS messages is ONE interrupting utterance ----
    // Every AnnounceImmediate cancels the speech before it, so one call per message left only the
    // last (the lowest caution) audible and cut the warning off (TKO-5's "V1" cut off by "Rotate").

    [Fact]
    public void CasBurst_FromTheStore_IsOneInterruptingUtterance_WarningFirst()
    {
        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Snap("[]", StoreMsg()));
        def.ProcessDisplaysSnapshot(Snap("[]", StoreMsg(0, 243, 526)));   // L ENG FIRE, FMS PERF DEP CONFIG, EMER LTS OFF
        Assert.Equal(new[] { "Warning: L ENG FIRE. Caution: EMER LTS OFF. Caution: FMS PERF DEP CONFIG" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);
    }

    [Fact]
    public void CasBurst_FromTheScrape_IsOneInterruptingUtterance_AndTheRestStaysQueued()
    {
        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Snap("[]"));
        def.ProcessDisplaysSnapshot(Snap("[{\"t\":\"L ENG FIRE\",\"c\":\"red\"},{\"t\":\"EMER LTS OFF\",\"c\":\"amber\"},"
            + "{\"t\":\"EXT PWR AVAIL\",\"c\":\"cyan\"}]"));
        Assert.Equal(new[] { "Warning: L ENG FIRE. Caution: EMER LTS OFF" }, speech.Interrupts);
        Assert.Equal(new[] { "Warning: L ENG FIRE. Caution: EMER LTS OFF", "Advisory: EXT PWR AVAIL" }, speech.All);
    }

    [Fact]
    public void CasScrape_NeverSpeaksStatusLines_AndEachSeverityHonoursItsRow()
    {
        const string mixed = "[{\"t\":\"SEAT BELTS\",\"c\":\"white\"},{\"t\":\"EXT PWR AVAIL\",\"c\":\"cyan\"},{\"t\":\"EMER LTS OFF\",\"c\":\"amber\"}]";
        string[] Run()
        {
            var (def, speech) = Pump();
            def.ProcessDisplaysSnapshot(Snap("[]"));
            def.ProcessDisplaysSnapshot(Snap(mixed));
            return speech.All.ToArray();
        }

        Mute();
        Assert.Equal(new[] { "Caution: EMER LTS OFF", "Advisory: EXT PWR AVAIL" }, Run());   // no "Memo: SEAT BELTS"
        Mute(A220MonitorRows.CasAdvisoriesKey);
        Assert.Equal(new[] { "Caution: EMER LTS OFF" }, Run());
        Mute(A220MonitorRows.CasWarningsKey);
        Assert.Equal(new[] { "Advisory: EXT PWR AVAIL" }, Run());
    }

    [Fact]
    public void Pump_FeedsTheSystemsMonitors()
    {
        var (def, speech) = Pump();
        string Gear(int icon) => "{\"gear\":{\"nose_icon\":" + icon + ",\"left_icon\":" + icon + ",\"right_icon\":" + icon
            + ",\"nose_color\":3,\"left_color\":3,\"right_color\":3}}";
        def.ProcessDisplaysSnapshot(Snap("[]", Gear(2)));
        def.ProcessDisplaysSnapshot(Snap("[]", Gear(0)));
        Assert.Equal(new[] { "Gear in transit" }, speech.All);
    }

    [Fact]
    public void FmaCallouts_AreMutedByTheirRow_AndTheBaselineStillAdvances()
    {
        static string Fma(string tokens) => "{\"ok\":true,\"fma\":" + tokens + "}";
        const string retard = "[{\"t\":\"RETARD\",\"c\":\"green\"}]";

        var (loud, heard) = Pump();
        loud.ProcessDisplaysSnapshot(Fma("[]"));                      // silent baseline
        loud.ProcessDisplaysSnapshot(Fma(retard));
        Assert.Equal(new[] { "Retard" }, heard.All);

        Mute(A220MonitorRows.FmaKey);
        var (quiet, silent) = Pump();
        quiet.ProcessDisplaysSnapshot(Fma("[]"));
        quiet.ProcessDisplaysSnapshot(Fma(retard));
        Assert.Empty(silent.All);

        Mute();                                                       // un-muted mid-flight: the old change is not replayed
        quiet.ProcessDisplaysSnapshot(Fma(retard));
        Assert.Empty(silent.All);
    }

    // ---- Take-off roll: "Rotate" on the per-frame airspeed feed ([A220-37]) ----

    private static A220Systems.SystemsBlocks WithVSpeeds(double? v1, double? vr, double? v2) => new()
    {
        lpfd = new A220Systems.PfdBlock { v1 = v1, vr = vr, v2 = v2 }
    };

    private static void Roll(SynapticA220Definition def, SpeechCapture speech, params double[] ias)
    {
        foreach (double sample in ias)
            def.ProcessSimVarUpdate(SynapticA220Definition.TakeoffIasKey, sample, speech);
    }

    [Fact]
    public void Rotate_FiresAtVrOnTheFeed_V1AndV2AreLeftToTheAircraft()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(new A220Systems.SystemsBlocks { lpfd = new A220Systems.PfdBlock { v1 = 131, vr = 135, v2 = 139 } });
        foreach (double ias in new[] { 20.0, 60, 120, 132, 136, 140, 150 })
            def.ProcessSimVarUpdate(SynapticA220Definition.TakeoffIasKey, ias, speech);
        Assert.Equal(new[] { "Rotate" }, speech.All);
    }

    [Fact]
    public void Rotate_IsMutedByItsRow()
    {
        SettingsManager.Current.A220DisabledMonitorVariables = new List<string> { A220MonitorRows.RotateKey };
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(new A220Systems.SystemsBlocks { lpfd = new A220Systems.PfdBlock { v1 = 131, vr = 135, v2 = 139 } });
        foreach (double ias in new[] { 20.0, 60, 136, 150 })
            def.ProcessSimVarUpdate(SynapticA220Definition.TakeoffIasKey, ias, speech);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void ARollWithoutVr_SaysSoOnce_NeverGuessesASpeed()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(new A220Systems.SystemsBlocks { lpfd = new A220Systems.PfdBlock() });
        foreach (double ias in new[] { 20.0, 45, 80, 140 })
            def.ProcessSimVarUpdate(SynapticA220Definition.TakeoffIasKey, ias, speech);
        Assert.Equal(new[] { "V-speeds not set — no Rotate call" }, speech.All);
    }

    [Fact]
    public void ARollWithoutVr_IsMutedByTheRotateRow_AndASkippedSampleDoesNotHideTheRoll()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(WithVSpeeds(null, null, null));
        Roll(def, speech, 20, double.NaN, 45, 80);                   // a NaN frame between 20 and 45
        Assert.Equal(new[] { "V-speeds not set — no Rotate call" }, speech.All);

        Mute(A220MonitorRows.RotateKey);
        var quiet = new SynapticA220Definition();
        var silent = new SpeechCapture();
        quiet.ProcessSystems(WithVSpeeds(null, null, null));
        Roll(quiet, silent, 20, 45, 80);
        Assert.Empty(silent.All);
    }

    [Fact]
    public void Rotate_FallsBackToTheScrapedVSpeeds_WhileThePfdStoreIsMissing()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessDisplaysSnapshot("{\"ok\":true,\"spd\":[{\"t\":\"V1 131\",\"y\":340},{\"t\":\"VR 135\",\"y\":300}]}");
        Roll(def, speech, 20, 100, 134, 136);
        Assert.Equal(new[] { "Rotate" }, speech.All);
    }

    [Fact]
    public void Rotate_TheStoreBeatsTheScrape_AndAStoreThatSaysUnsetIsNotOverruledByIt()
    {
        const string scrape = "\"spd\":[{\"t\":\"V1 131\",\"y\":340},{\"t\":\"VR 135\",\"y\":300}]";

        var set = new SynapticA220Definition();
        var heard = new SpeechCapture();
        set.ProcessDisplaysSnapshot("{\"ok\":true," + scrape + ",\"sys\":{\"lpfd\":{\"v1\":138,\"vr\":142}}}");
        Roll(set, heard, 20, 100, 136, 141);                          // past the scrape's VR 135 ...
        Assert.Empty(heard.All);
        Roll(set, heard, 143);                                        // ... Rotate waits for the store's VR 142
        Assert.Equal(new[] { "Rotate" }, heard.All);

        var unset = new SynapticA220Definition();
        var silent = new SpeechCapture();
        unset.ProcessDisplaysSnapshot("{\"ok\":true," + scrape + ",\"sys\":{\"lpfd\":{\"v1\":0,\"vr\":0}}}");
        Roll(unset, silent, 20, 45, 136);                             // the store says unset: no guess from the tape
        Assert.Equal(new[] { "V-speeds not set — no Rotate call" }, silent.All);
    }

    // ---- A poll with no V-speed source must not cost the roll its speeds (review focus 1) ----

    [Fact]
    public void Rotate_SurvivesPollsWithNoVSpeedSource_MidRoll()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(WithVSpeeds(131, 135, 139));
        Roll(def, speech, 20, 60);                                    // armed, rolling
        def.ProcessSystems(null);                                     // the tap hiccups: no store, no tape bugs
        def.ProcessSystems(WithGear(2));                              // the store is back but this block has no PFD
        Roll(def, speech, 120, 132, 136, 150);
        Assert.Equal(new[] { "Rotate" }, speech.All);
    }

    [Fact]
    public void Rotate_AGapAcrossTheArmingCrossing_KeepsTheSpeeds_AndSpeaksNoFalseWarning()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(WithVSpeeds(131, 135, 139));               // parked, speeds from the FMS
        def.ProcessSystems(null);                                     // the gap lands on the roll's first seconds
        Roll(def, speech, 20, 45, 100, 136);
        def.ProcessSystems(WithGear(2));
        Assert.Equal(new[] { "Rotate" }, speech.All);                 // no "V-speeds not set" on the way
    }

    [Fact]
    public void Rotate_ASpeedTheStoreDoesNotCarry_TakesTheTapeBugOrKeepsWhatItHeld()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(WithVSpeeds(131, 135, 139));
        // The store is down; the tape shows only the VR bug, and the pilot has changed it to 140.
        def.ProcessDisplaysSnapshot("{\"ok\":true,\"spd\":[{\"t\":\"VR 140\",\"y\":300}]}");
        Roll(def, speech, 20, 60, 136);                               // V1 and V2 held, VR from the tape
        Assert.Empty(speech.All);
        Roll(def, speech, 141);
        Assert.Equal(new[] { "Rotate" }, speech.All);
    }

    [Fact]
    public void ASpeedTheLiveStoreReportsAsNull_IsUnset_NotHeldFromThePreviousFlight()
    {
        // The live store reports an unset speed as null (the 2026-10-12 CommBus capture has "vt":null
        // beside "v1":131), and the agent emits null for any field the store has not carried. With the PFD
        // block present that is the store saying "unset": a wiped FMS, a flight load or a fly-again
        // must not leave the previous flight's VR calling Rotate.
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(WithVSpeeds(131, 135, 139));
        def.ProcessSystems(WithVSpeeds(null, null, null));            // the FMS wiped them
        Roll(def, speech, 20, 45, 136);
        Assert.Equal(new[] { "V-speeds not set — no Rotate call" }, speech.All);

        var fresh = new SynapticA220Definition();                      // the same through the pump's JSON
        var heard = new SpeechCapture();
        fresh.ProcessDisplaysSnapshot("{\"ok\":true,\"sys\":{\"lpfd\":{\"v1\":131,\"vr\":135,\"v2\":139}}}");
        fresh.ProcessDisplaysSnapshot("{\"ok\":true,\"sys\":{\"lpfd\":{\"v1\":null,\"vr\":null,\"v2\":null,\"vref\":null}}}");
        Roll(fresh, heard, 20, 45, 136);
        Assert.Equal(new[] { "V-speeds not set — no Rotate call" }, heard.All);
    }

    [Fact]
    public void ARollWithoutV1_IsNotKnownEither_BecauseTheMachineNeedsBoth()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(WithVSpeeds(null, 135, null));             // VR alone cannot arm the machine
        Roll(def, speech, 20, 45, 136);
        Assert.Equal(new[] { "V-speeds not set — no Rotate call" }, speech.All);
    }

    [Fact]
    public void Rotate_NeedsAGroundSampleBelowTheArmSpeed_SoConnectingMidRollIsSilent()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(WithVSpeeds(131, 135, 139));
        Roll(def, speech, 100, 134, 136, 150);                       // connected mid-roll: never armed
        Assert.Empty(speech.All);
    }

    [Fact]
    public void ResetAnnouncementBaselines_DropsTheRollsArm_ButKeepsTheSpeeds()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.ProcessSystems(WithVSpeeds(131, 135, 139));
        Roll(def, speech, 20, 60);                                    // armed, rolling
        def.ResetAnnouncementBaselines();                             // a reconnect
        Roll(def, speech, 134, 136);                                  // the arm was dropped: no call
        Assert.Empty(speech.All);

        Roll(def, speech, 20, 60, 134, 136);                          // a fresh roll with the kept speeds
        Assert.Equal(new[] { "Rotate" }, speech.All);
    }

    [Fact]
    public void OnSimContextReset_DropsTheRollsArm_AndRebaselinesTheSystemsMonitors()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        def.UseAnnouncer(speech);
        def.ProcessSystems(WithVSpeeds(131, 135, 139));
        Roll(def, speech, 20, 60);
        def.ProcessSystems(WithGear(2));
        def.OnSimContextReset();                                      // a flight load
        Roll(def, speech, 134, 136);
        def.ProcessSystems(WithGear(1));                              // the loaded flight is airborne
        Assert.Empty(speech.All);
    }

    // ---- A flight load must not diff the loaded CAS against the old flight's (review focus 4) ----

    [Fact]
    public void ContextReset_DropsTheEicasScrapeBaseline_SoTheFallbackRebaselinesSilently()
    {
        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Snap("[]"));                      // the scrape baseline: no messages
        def.OnSimContextReset();                                      // a flight load
        def.ProcessDisplaysSnapshot(Snap(Caution));                   // the loaded flight already has a caution
        Assert.Empty(speech.All);                                     // not a change: the new baseline

        def.ProcessDisplaysSnapshot(Snap("[]"));                      // and a real change after it still speaks
        Assert.Equal(new[] { "EMER LTS OFF cleared" }, speech.All);
    }

    [Fact]
    public void RebaselineRequest_ReachesTheScrapeAndTheMonitors_OncePerRequest()
    {
        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Snap("[]", Gear(2)));
        def.RequestSystemsRebaseline();
        def.ProcessDisplaysSnapshot(Snap(Caution, Gear(1)));          // both loaded values are the new baseline
        Assert.Empty(speech.All);

        def.ProcessDisplaysSnapshot(Snap("[]", Gear(0)));             // one request, one rebaseline: changes speak again
        Assert.Equal(new[] { "EMER LTS OFF cleared", "Gear in transit" }, speech.All);
    }

    private static string Gear(int icon) => "{\"gear\":{\"nose_icon\":" + icon + ",\"left_icon\":" + icon + ",\"right_icon\":" + icon
        + ",\"nose_color\":3,\"left_color\":3,\"right_color\":3}}";

    // ---- ... nor the loaded FMA and trim against the old flight's ----

    [Fact]
    public void RebaselineRequest_DropsTheFmaBaseline_SoTheLoadedFlightsModesAreNotSpokenAsAChange()
    {
        static string Fma(string tokens) => "{\"ok\":true,\"fma\":" + tokens + "}";
        const string spd = "[{\"t\":\"SPD\",\"c\":\"green\"}]";
        const string flc = "[{\"t\":\"FLC\",\"c\":\"green\"}]";

        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Fma(spd));                        // the old flight's baseline
        def.RequestSystemsRebaseline();                               // a flight load
        def.ProcessDisplaysSnapshot(Fma(flc));                        // the loaded flight's modes
        Assert.Empty(speech.All);                                     // a new baseline, not a change

        def.ProcessDisplaysSnapshot(Fma(spd));                        // a real change after it still speaks
        Assert.Equal(new[] { "SPD" }, speech.All);
    }

    [Fact]
    public void RebaselineRequest_DropsTheTrimBaseline_SoTheLoadedFlightsTrimIsNotSpokenAsAChange()
    {
        static string Trim(double pitch) => FormattableString.Invariant($"{{\"ok\":true,\"fc\":{{\"pitch_trim\":{pitch}}}}}");

        var (def, speech) = Pump();
        def.ProcessDisplaysSnapshot(Trim(4.6));
        def.ProcessDisplaysSnapshot(Trim(4.6));                       // the old flight's settled trim: the baseline
        def.RequestSystemsRebaseline();                               // a flight load
        def.ProcessDisplaysSnapshot(Trim(6.0));
        def.ProcessDisplaysSnapshot(Trim(6.0));                       // the loaded flight's trim, settled
        Assert.Empty(speech.All);

        def.ProcessDisplaysSnapshot(Trim(7.0));
        def.ProcessDisplaysSnapshot(Trim(7.0));                       // a real trim change after it still speaks
        Assert.StartsWith("Stab trim", Assert.Single(speech.All));
    }

    // ---- The silent readouts, from the store ----

    private static SpeechCapture Press(SynapticA220Definition def, HotkeyAction action)
    {
        var speech = new SpeechCapture();
        Assert.True(def.HandleHotkeyAction(action, null!, speech, null!, null!));
        return speech;
    }

    [Fact]
    public void SpeedBugHotkeys_ReadThePfdStore()
    {
        var def = new SynapticA220Definition();
        def.ProcessSystems(new A220Systems.SystemsBlocks
        {
            lpfd = new A220Systems.PfdBlock { v1 = 131, vr = 135, v2 = 139, vref = 128, vac = 150, vf1 = 168 }
        });
        Assert.Equal("V1 131 knots", Press(def, HotkeyAction.ReadSpeedGD).All.Single());
        Assert.Equal("VR 135 knots", Press(def, HotkeyAction.ReadSpeedS).All.Single());
        Assert.Equal("V2 139 knots", Press(def, HotkeyAction.ReadSpeedF).All.Single());
        Assert.Equal("VREF 128 knots", Press(def, HotkeyAction.ReadSpeedVLS).All.Single());
        Assert.Equal("VAC 150 knots", Press(def, HotkeyAction.ReadSpeedVS).All.Single());
        Assert.Equal("Flap speeds F1 168 knots", Press(def, HotkeyAction.ReadSpeedVFE).All.Single());
    }

    [Fact]
    public void SpeedBugHotkeys_SayNotAvailable_WithoutTheStore()
    {
        var def = new SynapticA220Definition();
        Assert.Equal("V-speeds not available — display link not connected", Press(def, HotkeyAction.ReadSpeedGD).All.Single());
        Assert.Equal("Flap speeds not available — display link not connected", Press(def, HotkeyAction.ReadSpeedVFE).All.Single());
    }

    [Fact]
    public void NdWaypointHotkey_ReadsTheCaptainsToWaypoint()
    {
        var def = new SynapticA220Definition();
        Assert.Equal("Next waypoint not available — display link not connected", Press(def, HotkeyAction.ReadNDWaypoint).All.Single());
        def.ProcessDisplaysSnapshot("{\"ok\":true,\"sys\":{\"lnav\":{\"to\":\"WALET\",\"dtg\":12.34,\"source\":0}}}");
        Assert.Equal("Next waypoint WALET, 12.3 miles", Press(def, HotkeyAction.ReadNDWaypoint).All.Single());
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayND, "Ctrl+W")]
    [InlineData(HotkeyAction.ReadDisplayPFD, "Shift+1 to Shift+6")]
    [InlineData(HotkeyAction.ReadDisplayISIS, "B reads the altimeter")]
    public void AiDisplayReads_PointAtWhereTheDataIs(HotkeyAction action, string pointer)
        => Assert.Contains(pointer, Press(new SynapticA220Definition(), action).All.Single());
}

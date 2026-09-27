using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// How the MD-11 definition registers its controls for composed state (spec §3.5): which
/// buttons are read, which stay write-only, what a lamp is called, and that the whole thing
/// stays inside SimConnect's data-definition budget.
/// </summary>
public class Md11DefinitionStateTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    [Fact]
    public void Battery_IsReadOnRequest_AndDependsOnItsLampLatchAndPower()
    {
        var d = Vars["MD11_OVHD_ELEC_BATT_BT"];
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
        Assert.True(d.RenderAsButton);
        Assert.NotNull(d.StateVariables);
        Assert.Contains("MD11_OVHD_ELEC_BATT_OFF_LT", d.StateVariables!);
        Assert.Contains("MD11_OVHD_ELEC_BATT_BT", d.StateVariables!);
        Assert.Contains(TFDiMD11Definition.DcPowerKey, d.StateVariables!);
        Assert.Contains(TFDiMD11Definition.Dc1BusOffKey, d.StateVariables!);
    }

    [Fact]
    public void EveryStateBearingRow_WatchesBothHalvesOfThePowerGate()
    {
        // The gate is volts AND the DC bus 1 OFF lamp, so a change in either can flip every
        // composed state on a visible panel to "unpowered" and back. MainForm's reverse index
        // only relabels rows that named the variable as a dependency.
        // The one exception is a COMPOSITE row (the engine fire handles and the Elevator Feel knob):
        // it composes from its two positions alone, with no power term — a position is not an
        // annunciator — so it watches exactly its two keys (pinned in Md11FireHandleTests and
        // Md11ElevatorFeelTests).
        var composites = Md11ControlMap.Load().Controls.Where(c => c.Composite != null)
            .Select(c => c.NodeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = Vars.Where(kv => kv.Value.StateVariables != null && !composites.Contains(kv.Key))
            .Where(kv => !kv.Value.StateVariables!.Contains(TFDiMD11Definition.DcPowerKey)
                      || !kv.Value.StateVariables!.Contains(TFDiMD11Definition.Dc1BusOffKey))
            .Select(kv => kv.Key).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void ExternalPower_StaysWriteOnly_ButDependsOnItsLamps()
    {
        var d = Vars["MD11_OVHD_ELEC_EXT_PWR_BT"];
        Assert.Equal(UpdateFrequency.Never, d.UpdateFrequency);
        Assert.Contains("MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", d.StateVariables!);
        Assert.Contains("MD11_OVHD_ELEC_EXT_PWR_ON_LT", d.StateVariables!);
    }

    [Fact]
    public void MomentaryButton_HasNoDependencies()
    {
        Assert.Null(Vars["MD11_OVHD_ANNUNLT_TEST_BT"].StateVariables);
        Assert.Equal(UpdateFrequency.Never, Vars["MD11_OVHD_ANNUNLT_TEST_BT"].UpdateFrequency);
    }

    [Fact]
    public void Lamp_SpeaksItsSystemName_WithLitAndDarkWords()
    {
        var d = Vars["MD11_OVHD_ELEC_AC1_OFF_LT"];
        Assert.Equal("AC Bus 1", d.DisplayName);
        Assert.Equal("Off", d.ValueDescriptions[1]);
        Assert.Equal("Powered", d.ValueDescriptions[0]);
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.True(d.RenderAsReadOnlyStatus);
        Assert.Contains(TFDiMD11Definition.DcPowerKey, d.StateVariables!);
    }

    [Fact]
    public void TheDcBusOneLamp_IsAnOrdinaryRegisteredAnnunciator()
    {
        // Half the power gate, and it must stay a batch-covered lamp: the gate reads it from the
        // same cache every other lamp lands in, so it costs no data definition of its own.
        var d = Vars[TFDiMD11Definition.Dc1BusOffKey];
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.False(d.ExcludeFromBatch);
    }

    [Fact]
    public void PairedLamp_IsNamedFromItsOwnerAndLegend()
    {
        Assert.Equal("External Power AVAIL light", Vars["MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT"].DisplayName);
    }

    [Fact]
    public void Guard_IsReadOnRequest_RenderedAsButton_AndKnowsItself()
    {
        var d = Vars["MD11_OVHD_ELEC_BATT_GRD"];
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
        Assert.True(d.RenderAsButton);
        Assert.Equal("Battery guard", d.DisplayName);
        Assert.Contains("MD11_OVHD_ELEC_BATT_GRD", d.StateVariables!);
    }

    [Fact]
    public void EveryGuard_RegistersItsOwnCoverVar_NotTheCoveredControls()
    {
        // The guard's definition is what EnsureGuardOpenAsync force-reads to decide whether to
        // lift the cover, so its Name must be the cover's own L:var. Registered under the covered
        // button's var, "button off" read as "cover closed" and the auto-open lowered an open
        // cover onto the press (Fuel Dump, Fuel Dump Emergency Stop, Center Gear Uplock, Main
        // Cargo Door Arm). The battery guard above is one of the 28 that were always right.
        var vars = Vars;
        var map = Md11ControlMap.Load();
        var offenders = map.Controls
            .Where(c => c.Kind == Md11Kinds.Guard)
            .Where(c => !vars.TryGetValue(c.NodeId, out var d) || !string.Equals(d.Name, c.NodeId, StringComparison.Ordinal))
            .Select(c => $"{c.NodeId} reads {(vars.TryGetValue(c.NodeId, out var d) ? d.Name : "(unregistered)")}")
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void OptionFlags_AreNotRegistered()
    {
        Assert.DoesNotContain("MD11_OPT_EFB", Vars.Keys);
        Assert.DoesNotContain("MD11_OPT_ISFD", Vars.Keys);
    }

    [Fact]
    public void DcPowerGate_IsAContinuousStockSimVar_KeptOffCtrlM()
    {
        var d = Vars[TFDiMD11Definition.DcPowerKey];
        Assert.Equal("ELECTRICAL MAIN BUS VOLTAGE", d.Name);
        Assert.Equal(SimVarType.SimVar, d.Type);
        Assert.Equal("Volts", d.Units);
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.True(d.ExcludeFromMonitorManager);
    }

    [Fact]
    public void EveryStateDependency_ResolvesToAReadableVariable()
    {
        // A StateVariables entry is a KEY MainForm force-reads and watches. A key that is not
        // registered, or registered UpdateFrequency.Never (write-only, zero data definitions), is
        // never read — GetCachedVariableValue answers null forever and Compose silently skips the
        // rule that key was there to feed. That is how MD11_OVHD_PNEU_SYSTEM_SEL_BT's own latch
        // resolved to MD11_OVHD_PNEU_ECON_BT (which merely READS the selector's var and sorts
        // first) and "Air System Mode" could never say Auto or Manual.
        var unreadable = new List<string>();
        foreach (var (key, d) in Vars)
        {
            if (d.StateVariables == null) continue;
            foreach (var dep in d.StateVariables)
            {
                if (!Vars.TryGetValue(dep, out var target))
                    unreadable.Add($"{key} → {dep} (not registered)");
                else if (target.UpdateFrequency == UpdateFrequency.Never)
                    unreadable.Add($"{key} → {dep} (write-only)");
            }
        }
        Assert.Empty(unreadable);
    }

    [Fact]
    public void AirSystemSelector_ReadsItsOwnLatch_NotTheEconButtonsCopyOfIt()
    {
        // The regression above, named: ECON's tooltip reads the selector's var, so both controls
        // carry state_var = MD11_OVHD_PNEU_SYSTEM_SEL_BT. The selector owns its own name.
        var d = Vars["MD11_OVHD_PNEU_SYSTEM_SEL_BT"];
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
        Assert.Contains("MD11_OVHD_PNEU_SYSTEM_SEL_BT", d.StateVariables!);
        Assert.DoesNotContain("MD11_OVHD_PNEU_ECON_BT", d.StateVariables!);
    }

    [Fact]
    public void TheWordlessLamp_IsKeptOffCtrlM_WhileTheFlapRowsStay()
    {
        // Md11MonitorManagerForm now honours ExcludeFromMonitorManager, so the flag decides
        // whether a pilot gets a checkbox and must mean exactly "muted by plumbing" — the DC-bus
        // voltage gate (pinned above) and the lamp with no word of its own (APU BLANK). The flap
        // lever and thumbwheel DO speak, through ProcessSimVarUpdate rather than the generic
        // gate, so unticking them silences something and they must keep their rows.
        Assert.True(Vars["MD11_AOVHD_APU_BLANK_LT"].ExcludeFromMonitorManager);
        Assert.False(Vars["MD11_FLAP_LATCH"].ExcludeFromMonitorManager);
        Assert.False(Vars["MD11_DIALAFLAP_WHEEL_RNG"].ExcludeFromMonitorManager);
        // Every other lamp is a real announcement and stays mutable.
        Assert.False(Vars["MD11_OVHD_ELEC_AC1_OFF_LT"].ExcludeFromMonitorManager);
    }

    [Fact]
    public void BatchCoveredNames_AreUnique()
    {
        // Two batch entries with one Name shift every later slot (VarNameCollision invariant).
        var dupes = Vars.Values
            .Where(v => v.UpdateFrequency == UpdateFrequency.Continuous && v.IsAnnounced && !v.ExcludeFromBatch)
            .GroupBy(v => v.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void IndividualDefinitions_StayFarInsideTheBudget()
    {
        int individual = Vars.Values.Count(v =>
            v.UpdateFrequency == UpdateFrequency.OnRequest ||
            (v.UpdateFrequency == UpdateFrequency.Continuous && (!v.IsAnnounced || v.ExcludeFromBatch)));
        Assert.InRange(individual, 1, 500);   // cap is 900; ~206 before this work, ~170 latches added
    }

    [Fact]
    public void BeforeAttach_TheHookHasNoOpinion()
    {
        Assert.False(new TFDiMD11Definition().TryDescribeControlState("MD11_OVHD_ELEC_BATT_BT", out _));
    }

    /// <summary>
    /// No control reading one of TFDi's read-only EXPORTS may render as a walkable combo. The FCP
    /// mode knobs read MD11_AP_HDG_TRK / IAS_MACH / VS_FPA and the EFIS minimums caps read
    /// MD11_CAP/FO_MINIMUMS, but their wheel moves the VALUE, never the mode: as combos their walk
    /// stalled and the direct-write fallback zeroed the export. Stated over the whole map, so a
    /// future export-backed knob is caught too; the set is computed here without the helper so the
    /// test compiles and fails at the pre-fix definition on exactly the offending ids.
    /// </summary>
    [Fact]
    public void NoControlReadingAnExport_IsRenderedAsAWalkableCombo()
    {
        var map = Md11ControlMap.Load();
        var exports = new HashSet<string>(map.ExportVars, StringComparer.OrdinalIgnoreCase);
        string[] positional = { Md11Kinds.Switch, Md11Kinds.Knob, Md11Kinds.KnobPush, Md11Kinds.KnobPushPull, Md11Kinds.Lever, Md11Kinds.Handle };
        var backed = map.Controls
            .Where(c => positional.Contains(c.Kind) && !string.IsNullOrEmpty(c.StateVar) && exports.Contains(c.StateVar))
            .Select(c => c.NodeId).ToList();

        Assert.Contains("MD11_CGS_HDG_KB", backed);          // the rule has teeth: the FCP knobs are in the set
        Assert.Contains("MD11_LECP_MINIMUMS_CAP", backed);
        var walkable = backed.Where(id => !Vars[id].RenderAsReadOnlyStatus).ToList();
        Assert.Empty(walkable);
    }

    /// <summary>The FCP mode rows stay readable: a status field that still names the mode.</summary>
    [Theory]
    [InlineData("MD11_CGS_HDG_KB", "Heading", "Track")]
    [InlineData("MD11_CGS_SPD_KB", "IAS", "MACH")]
    [InlineData("MD11_CGS_VS_KB", "VS", "FPA")]
    public void FcpModeKnobRow_IsReadOnly_AndStillNamesBothModes(string key, string zero, string one)
    {
        var d = Vars[key];
        Assert.True(d.RenderAsReadOnlyStatus);
        Assert.Equal(zero, d.ValueDescriptions[0]);
        Assert.Equal(one, d.ValueDescriptions[1]);
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
    }

    /// <summary>
    /// The minimums caps show the FEET as a read-only number (MainForm's numeric TextBox branch —
    /// Units defaults to "number"), never the mode switch's Radio/Baro words the generator once
    /// lifted from a companion var in their tooltip.
    /// </summary>
    [Theory]
    [InlineData("MD11_LECP_MINIMUMS_CAP")]
    [InlineData("MD11_RECP_MINIMUMS_CAP")]
    public void MinimumsCapRow_IsAReadOnlyNumber_NotAModeCombo(string key)
    {
        var d = Vars[key];
        Assert.True(d.RenderAsReadOnlyStatus);
        Assert.Empty(d.ValueDescriptions);
        Assert.Equal("number", d.Units);
    }

    /// <summary>
    /// A position control with no value map renders as a read-only numeric field (RenderAsReadOnlyStatus
    /// with no ValueDescriptions and the default Units "number" takes MainForm's read-only TextBox
    /// branch). That is what hid the third IRS switch. Every IRS switch must be a two-position combo.
    /// </summary>
    [Theory]
    [InlineData("MD11_OVHD_IRS_1_KB")]
    [InlineData("MD11_OVHD_IRS_2_KB")]
    [InlineData("MD11_OVHD_IRS_3_KB")]
    public void IrsSwitch_RendersAsAnOffNavCombo(string key)
    {
        var d = Vars[key];
        Assert.False(d.RenderAsReadOnlyStatus);
        Assert.False(d.RenderAsButton);
        Assert.Equal("Off", d.ValueDescriptions[0]);
        Assert.Equal("Nav", d.ValueDescriptions[1]);
        Assert.Equal(2, d.ValueDescriptions.Count);
    }

    [Theory]
    [InlineData("MD11_OVHD_PNEU_COCKPIT_TEMP", 8)]
    [InlineData("MD11_OVHD_PNEU_MID_CAB_TEMP", 8)]
    [InlineData("MD11_OVHD_PNEU_FWD_CARGO_TEMP", 3)]
    [InlineData("MD11_OVHD_PNEU_AFT_CARGO_TEMP", 7)]
    public void TemperatureKnob_RendersAsACombo_WithOnePositionPerDetent(string key, int positions)
    {
        var d = Vars[key];
        Assert.False(d.RenderAsReadOnlyStatus);
        Assert.Equal(positions, d.ValueDescriptions.Count);
        Assert.Equal("1 (full cold)", d.ValueDescriptions[0]);
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
    }

    /// <summary>
    /// Autothrottle engagement is news (the 777 and A320 both speak it); the autopilot was
    /// announced here, the autothrottle was a silent read-out. Same path as MD11_AP_STATE: the
    /// generic monitor speaks the decoded value, baseline-first, and Ctrl+M carries the row.
    /// </summary>
    [Fact]
    public void Autothrottle_IsAnnouncedOnChange_WithAMonitorRow()
    {
        var d = Vars["MD11_ATS_STATE"];
        Assert.Equal("Autothrottle", d.DisplayName);
        Assert.True(d.IsAnnounced);
        Assert.False(d.ExcludeFromMonitorManager);
        Assert.Equal("off", d.ValueDescriptions[0]);
        Assert.Equal("on", d.ValueDescriptions[1]);
        Assert.Equal("on", d.ValueDescriptions[2]);   // 2 never observed; Md11AutoflightState treats ≥0.5 as on
    }

    /// <summary>The captain's altimeter now speaks (Task 7), so it keeps a Ctrl+M row — the flag means "muted by plumbing" and must never sit on a var that speaks.</summary>
    [Fact]
    public void CaptainAltimeter_KeepsItsMonitorRow_BecauseItSpeaks()
    {
        var d = Vars["MD11_CAP_ALTIMETER"];
        Assert.True(d.IsAnnounced);
        Assert.False(d.ExcludeFromMonitorManager);
    }

    [Fact]
    public void MinimumsFields_AreTypedEntries_PreFilledFromTheReading()
    {
        foreach (var side in Md11Minimums.Sides)
        {
            var d = Vars[side.SetKey];
            Assert.Equal($"{side.Name} Minimums", d.DisplayName);
            Assert.Equal(side.WriteVar, d.Name);
            Assert.Equal(SimVarType.LVar, d.Type);
            Assert.Equal(UpdateFrequency.Never, d.UpdateFrequency);   // claimed by HandleUIVariableSet; never read, never generic-written
            Assert.Equal(side.ReadKey, d.CurrentValueSourceKey);
            Assert.False(d.PreventTextInput);
        }
    }

    /// <summary>
    /// The minimums rows read their mode word from a silent batch-covered MIRROR of each side's
    /// mode switch. The switch itself stays OnRequest: batch-covering it (94c60c42, reverted) had
    /// no individual data definition, which downgraded its combo's walk to the legacy cache-poll
    /// protocol that can call a real move "did not move". Two keys, one Name, only one batched.
    /// </summary>
    [Theory]
    [InlineData("MD11_LECP_MINIMUMS_KB", "MD11_CAP_MINIMUMS_MODE", "Captain minimums mode (mirror)")]
    [InlineData("MD11_RECP_MINIMUMS_KB", "MD11_FO_MINIMUMS_MODE", "First Officer minimums mode (mirror)")]
    public void MinimumsModeSwitch_StaysOnRequest_AndItsMirrorRidesTheBatch(string switchKey, string mirrorKey, string mirrorName)
    {
        var sw = Vars[switchKey];
        Assert.Equal(UpdateFrequency.OnRequest, sw.UpdateFrequency);
        Assert.False(sw.IsAnnounced);
        Assert.Equal("Radio", sw.ValueDescriptions[0]);
        Assert.Equal("Baro", sw.ValueDescriptions[1]);

        var mirror = Vars[mirrorKey];
        Assert.Equal(switchKey, mirror.Name);
        Assert.Equal(mirrorName, mirror.DisplayName);
        Assert.Equal(UpdateFrequency.Continuous, mirror.UpdateFrequency);
        Assert.True(mirror.IsAnnounced);
        Assert.False(mirror.ExcludeFromBatch);
        Assert.True(mirror.ExcludeFromMonitorManager);
        Assert.Empty(mirror.ValueDescriptions);   // silent: consumed with the other Export-style read-outs
    }
}

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Coverage pin for the ECL "first officer" actuation map (docs/a220-plan.md W6):
/// every one of the 215 sensed ECL variables is either mapped to an EXISTING def
/// control key with a target value inside that control's declared range, or
/// carries an explicit exclusion reason — zero unaccounted entries, no orphans.
/// </summary>
public class A220EclActionMapTests
{
    private static readonly Dictionary<string, SimVarDefinition> Vars =
        new SynapticA220Definition().GetVariables();

    [Fact]
    public void EveryEclVariable_IsMappedOrExplicitlyExcluded()
    {
        foreach (string name in SynapticA220EclData.EclVariableNames)
        {
            Assert.True(A220EclActionMap.TryGet(name, out var action),
                $"ECL variable {name} is unaccounted for in A220EclActionMap");

            if (!action.IsMapped)
            {
                Assert.NotEqual(A220EclExclusion.None, action.Exclusion);
                Assert.Null(action.ControlKey);
                continue;
            }

            Assert.True(Vars.TryGetValue(action.ControlKey!, out var def),
                $"{name} maps to unknown control key {action.ControlKey}");

            if (def!.ValueDescriptions is { Count: > 0 })
            {
                Assert.True(def.ValueDescriptions.ContainsKey(action.Value),
                    $"{name}: value {action.Value} is outside {action.ControlKey}'s declared positions");
            }
            else
            {
                // Valueless targets must be pushbutton controls pulsed with 1.
                Assert.True(def.RenderAsButton, $"{name}: {action.ControlKey} has no value table and is not a button");
                Assert.Equal(1, action.Value);
            }
        }
    }

    [Fact]
    public void Map_HasNoOrphanEntries()
    {
        var known = SynapticA220EclData.EclVariableNames.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(known.Count, A220EclActionMap.Actions.Count);
        foreach (string key in A220EclActionMap.Actions.Keys)
            Assert.Contains(key, known);
    }

    [Fact]
    public void ScopeRulings_ArePinned()
    {
        // Engine + APU start ARE in scope (user ruling 2026-07-27). APU_START value 2
        // triggers the shared hold-to-start write in HandleUIVariableSet.
        Assert.True(A220EclActionMap.TryGet("APU_START", out var apu));
        Assert.Equal("A22X_APU_SWITCH", apu.ControlKey);
        Assert.Equal(2, apu.Value);
        Assert.True(A220EclActionMap.TryGet("L_ENG_RUN_ON", out var eng1) && eng1.IsMapped);
        Assert.True(A220EclActionMap.TryGet("ENG_START_AUTO", out var startMode) && startMode.IsMapped);

        // Fire handles + bottles: guarded destructive, never auto-actuated.
        foreach (string fire in new[]
        {
            "L_ENG_FIRE_PRESSED", "R_ENG_FIRE_PRESSED", "APU_FIRE_PRESSED",
            "APU_BTL_SW_PRESSED", "L_ENG_BTL_1_SW_PRESSED", "R_ENG_BTL_2_SW_PRESSED",
        })
        {
            Assert.True(A220EclActionMap.TryGet(fire, out var a));
            Assert.Equal(A220EclExclusion.GuardedDestructive, a.Exclusion);
        }

        // Thrust levers + sidestick: hardware inputs MSFSBA never fights.
        foreach (string axis in new[]
        {
            "THRUST_LEVERS_MAX", "L_THRUST_LEVER_REV", "L_SIDESTICK_PTY", "R_SIDESTICK_PTY",
        })
        {
            Assert.True(A220EclActionMap.TryGet(axis, out var a));
            Assert.Equal(A220EclExclusion.HardwareAxis, a.Exclusion);
        }

        // Flap detents map straight through the lever walk.
        Assert.True(A220EclActionMap.TryGet("SLAT_FLAP_LEVER_3", out var flap));
        Assert.Equal("A22X_FLAP_LEVER", flap.ControlKey);
        Assert.Equal(3, flap.Value);

        // Enum-position spot checks against the declared labels.
        Assert.True(A220EclActionMap.TryGet("MAN_XFR_L", out var xfrL));
        Assert.Equal(3, xfrL.Value); // Off/Right/Center/Left
        Assert.True(A220EclActionMap.TryGet("HYD_1_SOV_CLSD", out var sov));
        Assert.Equal(0, sov.Value);  // Closed=0/Open=1
    }

    [Fact]
    public void Autobrake_MapsToTheAircraftsScale()
    {
        // 0 RTO, 1 OFF, 2 LO, 3 MED, 4 HI ([A220-34]) — RTO is mapped now that the scale is known.
        foreach (var (name, value) in new[]
        {
            ("AUTOBRAKE_RTO", 0.0), ("AUTOBRAKE_OFF", 1.0), ("AUTOBRAKE_LO", 2.0),
            ("AUTOBRAKE_MED", 3.0), ("AUTOBRAKE_HI", 4.0)
        })
        {
            Assert.True(A220EclActionMap.TryGet(name, out var a), name);
            Assert.Equal("A22X_AUTOBRAKE", a.ControlKey);
            Assert.Equal(value, a.Value);
        }
    }

    /// <summary>Plan B's panel controls are reachable from the ECL "do it for me".</summary>
    [Fact]
    public void PlanBPanelControls_AreMapped()
    {
        static void Is(string ecl, string key, double value)
        {
            Assert.True(A220EclActionMap.TryGet(ecl, out var a), ecl);
            Assert.True(a.IsMapped, $"{ecl} is excluded ({a.Exclusion})");
            Assert.Equal(key, a.ControlKey);
            Assert.Equal(value, a.Value);
        }
        Is("INLET_OFF", "A22X_COOLING_INLET", 0);           // 1 = AUTO, 0 = OFF
        Is("EXHAUST_VLV_ONLY", "A22X_COOLING_EXHAUST", 0);
        Is("EXHAUST_AUTO", "A22X_COOLING_EXHAUST", 1);
        Is("EXHAUST_ON", "A22X_COOLING_EXHAUST", 2);
        Is("CVR_TEST", "A22X_CVR_TEST", 1);
        Is("CVR_ERASE", "A22X_CVR_ERASE", 1);
        Is("SERV_INT_ON", "A22X_SERVICE_INTERCOM", 1);
        Is("PROBE_HEAT_ON", "A22X_PROBE_HEAT", 1);           // the ground-test press
        for (int n = 0; n <= 5; n++) Is($"SPOILER_LEVER_{n}", "A22X_SPOILER_LEVER", n);
        // Plan A's autobrake scale: RTO 0, OFF 1, LO 2, MED 3, HI 4.
        Is("AUTOBRAKE_RTO", "A22X_AUTOBRAKE", 0);
        Is("AUTOBRAKE_OFF", "A22X_AUTOBRAKE", 1);
        Is("AUTOBRAKE_LO", "A22X_AUTOBRAKE", 2);
        Is("AUTOBRAKE_MED", "A22X_AUTOBRAKE", 3);
        Is("AUTOBRAKE_HI", "A22X_AUTOBRAKE", 4);
    }

    /// <summary>Spec decision 6 / [A220-23]: every fire push and bottle is panel-only.</summary>
    [Theory]
    [InlineData("L_ENG_FIRE_PRESSED")]
    [InlineData("R_ENG_FIRE_PRESSED")]
    [InlineData("APU_FIRE_PRESSED")]
    [InlineData("FWD_CARGO_FIRE_PRESSED")]
    [InlineData("AFT_CARGO_FIRE_PRESSED")]
    [InlineData("CARGO_FIRE_BTL_PRESSED")]
    [InlineData("APU_BTL_SW_PRESSED")]
    [InlineData("L_ENG_BTL_1_SW_PRESSED")]
    [InlineData("L_ENG_BTL_2_SW_PRESSED")]
    [InlineData("R_ENG_BTL_1_SW_PRESSED")]
    [InlineData("R_ENG_BTL_2_SW_PRESSED")]
    public void EveryFireControl_StaysPanelOnly(string ecl)
    {
        Assert.True(A220EclActionMap.TryGet(ecl, out var a), ecl);
        Assert.Equal(A220EclExclusion.GuardedDestructive, a.Exclusion);
        Assert.Null(a.ControlKey);
    }
}

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
}

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

    // ---- "inverted" pack items ([A220-60]) ---------------------------------------------

    /// <summary>Shaped like the aircraft's own checklists.json items (ecl-editor schema): an
    /// inverted item is complete when its sensed bit is FALSE. Not the aircraft's file — a few
    /// items written the way it writes them.</summary>
    private const string InvertedPackJson = """
    {
      "name": "Test Pack",
      "partNumber": "ECL_TEST_INVERTED",
      "normal": [
        {
          "name": "Power-on",
          "phase": "preflight",
          "items": [
            { "type": "action", "challenge": "EQUIP COOLING, INLET", "response": "Select auto", "sensed": "INLET_OFF", "inverted": true },
            { "type": "action", "challenge": "L ENG RUN", "response": "OFF", "sensed": "L_ENG_RUN_ON", "inverted": true },
            { "type": "action", "challenge": "PARK BRAKE", "response": "ON", "sensed": "PARK_BRAKE_ON" }
          ]
        },
        {
          "name": "Before taxi",
          "items": [
            { "type": "action", "challenge": "NOSE STEER", "response": "Select on", "sensed": "NOSE_STEER_OFF", "inverted": true },
            { "type": "action", "challenge": "DOME", "response": "ON", "sensed": "DOME_ON", "inverted": false }
          ]
        },
        {
          "name": "HYD 1 SOV FAIL",
          "items": [
            {
              "type": "conditional",
              "challenge": "HYD 1 SOV closed:",
              "paths": {
                "NO": [
                  { "type": "action", "challenge": "HYD 1 SOV", "response": "Select open", "sensed": "HYD_1_SOV_CLSD", "inverted": true }
                ]
              }
            }
          ]
        }
      ],
      "non_normal": [],
      "procedure": []
    }
    """;

    private static A220ChecklistPack.Item PackItem(string challenge)
        => A220ChecklistPack.Parse(InvertedPackJson)!.All.SelectMany(c => c.Items).Single(i => i.Challenge == challenge);

    /// <summary>The form's path: pack item → its sensed name's action → the target def → the value.</summary>
    private static (string? Key, A220EclTarget Target) Resolve(A220ChecklistPack.Item item)
    {
        Assert.True(A220EclActionMap.TryGet(item.Sensed!, out var action), item.Sensed);
        Assert.True(action.IsMapped, $"{item.Sensed} is excluded ({action.Exclusion})");
        return (action.ControlKey, A220EclActionMap.ResolveTarget(action, item.Inverted, Vars.GetValueOrDefault(action.ControlKey!)));
    }

    [Fact]
    public void ChecklistPack_ParsesTheInvertedFlag()
    {
        Assert.True(PackItem("EQUIP COOLING, INLET").Inverted);
        Assert.True(PackItem("HYD 1 SOV").Inverted);           // inside a conditional branch
        Assert.Equal("NO", PackItem("HYD 1 SOV").Path);
        Assert.False(PackItem("PARK BRAKE").Inverted);         // absent = false
        Assert.False(PackItem("DOME").Inverted);               // explicit false
    }

    [Theory]
    [InlineData("EQUIP COOLING, INLET", "A22X_COOLING_INLET", 1.0)]   // mapped OFF (0) -> AUTO
    [InlineData("NOSE STEER", "A22X_NOSE_STEER_OFF", 0.0)]            // mapped Off (1) -> On
    [InlineData("L ENG RUN", "A22X_ENG_MASTER_1", 0.0)]               // mapped On (1) -> Off
    [InlineData("HYD 1 SOV", "A22X_HYD_1_SOV", 1.0)]                  // mapped Closed (0) -> Open
    public void ResolveTarget_InvertedTwoPositionItem_WritesTheOtherPosition(string challenge, string key, double value)
    {
        var (controlKey, target) = Resolve(PackItem(challenge));
        Assert.Equal(key, controlKey);
        Assert.Null(target.Refusal);
        Assert.Equal(value, target.Value);
    }

    [Theory]
    [InlineData("PARK BRAKE", "A22X_PARKING_BRAKE", 1.0)]
    [InlineData("DOME", "A22X_DOME_LIGHTS", 1.0)]
    public void ResolveTarget_NotInverted_KeepsTheMappedValue(string challenge, string key, double value)
    {
        var (controlKey, target) = Resolve(PackItem(challenge));
        Assert.Equal(key, controlKey);
        Assert.Equal(A220EclTarget.Write(value), target);
    }

    [Fact]
    public void ResolveTarget_NotInverted_IsTheMappedValueForEveryMappedName()
    {
        foreach (var action in A220EclActionMap.Actions.Values.Where(a => a.IsMapped))
            Assert.Equal(A220EclTarget.Write(action.Value),
                A220EclActionMap.ResolveTarget(action, inverted: false, Vars[action.ControlKey!]));
    }

    /// <summary>A button (even one with two labels, like a momentary push) or a control with three or
    /// more positions has no single "other" position: the item is refused aloud, never guessed.</summary>
    [Theory]
    [InlineData("CVR_TEST")]          // LMomentary: Released/Pressed labels, but a button
    [InlineData("PROBE_HEAT_ON")]     // LPress: a write-1 button with no positions
    [InlineData("HYD_3A_AUTO")]       // Off/Auto/On
    [InlineData("AUTOBRAKE_OFF")]     // five positions
    [InlineData("SPOILER_LEVER_2")]   // six detents
    public void ResolveTarget_InvertedButtonOrMultiPositionControl_Refuses(string sensed)
    {
        Assert.True(A220EclActionMap.TryGet(sensed, out var action) && action.IsMapped, sensed);
        var target = A220EclActionMap.ResolveTarget(action, inverted: true, Vars[action.ControlKey!]);
        Assert.Equal(A220EclTarget.Refuse(A220EclActionMap.InvertedRefusal), target);
    }

    [Fact]
    public void ResolveTarget_InvertedWithoutADefinition_Refuses()
    {
        Assert.True(A220EclActionMap.TryGet("DOME_ON", out var action));
        Assert.Equal(A220EclActionMap.InvertedRefusal, A220EclActionMap.ResolveTarget(action, true, null).Refusal);
    }

    [Fact]
    public void InvertedRefusal_IsOneShortLine()
    {
        // Braille: under 80 cells (two lines of a 40-cell display), plain words and one dash.
        Assert.True(A220EclActionMap.InvertedRefusal.Length < 80, A220EclActionMap.InvertedRefusal);
        Assert.Equal("This item asks for the opposite of its sensed state — set it from the panel.",
            A220EclActionMap.InvertedRefusal);
    }

    /// <summary>
    /// The 26 sensed names the aircraft's default pack (ECL_BCS3_SYN_072826_KG, read 2026-10-15)
    /// marks <c>"inverted": true</c> on 82 items, each checked against its target: every one is a
    /// two-position combo, so every one resolves to a position. A name added here that maps to a
    /// button or a 3+-position control would be refused instead — check the cockpit first.
    /// </summary>
    [Theory]
    [InlineData("APU_BLEED_OFF", "A22X_APU_BLEED_OFF", 0.0)]
    [InlineData("APU_GEN_OFF", "A22X_APU_GEN_OFF", 0.0)]
    [InlineData("AUTO_PRESS_MAN", "A22X_MAN_PRESS", 0.0)]
    [InlineData("BEACON_LTS_ON", "A22X_BEACON_LIGHTS", 0.0)]
    [InlineData("DOME_ON", "A22X_DOME_LIGHTS", 0.0)]
    [InlineData("ELEC_L_GEN_OFF", "A22X_L_GEN_OFF", 0.0)]
    [InlineData("ELEC_R_GEN_OFF", "A22X_R_GEN_OFF", 0.0)]
    [InlineData("EMER_DEPRESS_ON", "A22X_EMER_DEPRESS", 0.0)]
    [InlineData("GRAV_XFR_ON", "A22X_GRAVITY_TRANSFER", 0.0)]
    [InlineData("HYD_1_SOV_CLSD", "A22X_HYD_1_SOV", 1.0)]
    [InlineData("HYD_2_SOV_CLSD", "A22X_HYD_2_SOV", 1.0)]
    [InlineData("INLET_OFF", "A22X_COOLING_INLET", 1.0)]
    [InlineData("L_BLEED_OFF", "A22X_L_BLEED_OFF", 0.0)]
    [InlineData("L_ENG_RUN_ON", "A22X_ENG_MASTER_1", 0.0)]
    [InlineData("L_PACK_OFF", "A22X_L_PACK_OFF", 0.0)]
    [InlineData("NOSE_STEER_OFF", "A22X_NOSE_STEER_OFF", 0.0)]
    [InlineData("PFCC1_OFF", "A22X_PFCC_1_OFF", 0.0)]
    [InlineData("RAM_AIR_OPEN", "A22X_RAM_AIR", 0.0)]
    [InlineData("R_BLEED_OFF", "A22X_R_BLEED_OFF", 0.0)]
    [InlineData("R_ENG_RUN_ON", "A22X_ENG_MASTER_2", 0.0)]
    [InlineData("R_PACK_OFF", "A22X_R_PACK_OFF", 0.0)]
    [InlineData("TRIM_AIR_OFF", "A22X_TRIM_AIR_OFF", 0.0)]
    [InlineData("WINDOW_HEAT_L_WINDOW_OFF", "A22X_L_WINDOW_HEAT_OFF", 0.0)]
    [InlineData("WINDOW_HEAT_L_WSHLD_OFF", "A22X_L_WSHLD_HEAT_OFF", 0.0)]
    [InlineData("WINDOW_HEAT_R_WINDOW_OFF", "A22X_R_WINDOW_HEAT_OFF", 0.0)]
    [InlineData("WINDOW_HEAT_R_WSHLD_OFF", "A22X_R_WSHLD_HEAT_OFF", 0.0)]
    public void TheAircraftPacksInvertedNames_EachResolveToTheOtherPosition(string sensed, string key, double value)
    {
        Assert.True(A220EclActionMap.TryGet(sensed, out var action) && action.IsMapped, sensed);
        Assert.Equal(key, action.ControlKey);
        Assert.Equal(A220EclTarget.Write(value), A220EclActionMap.ResolveTarget(action, inverted: true, Vars[key]));
    }
}

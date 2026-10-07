using System;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using Scene = MSFSBlindAssist.FirstOfficer.Fenix.FenixActionExecutor.CockpitLightScene;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix A320 First Officer's cockpit-lighting scenes, at the A32NX's four points (power-up
/// bright, after start dim, shutdown bright, secure off), on the Fenix's own controls and
/// encodings: the annunciator switch S_OH_IN_LT_ANN_LT (0 Dim / 1 Bright / 2 Test), the dome
/// switch S_OH_INT_LT_DOME (0 Off / 1 Dim / 2 Bright) and four 0–1 knobs. The Fenix has no
/// standby-compass light, so the A32NX's three *_STBYCOMPASS lines have no twin here.
/// </summary>
public class FenixLightingSceneTests
{
    private static readonly string[] Knobs =
        { "A_OH_LIGHTING_OVD", "A_PED_LIGHTING_PEDESTAL", "A_MIP_LIGHTING_FLOOD_MAIN", "A_MIP_LIGHTING_FLOOD_PEDESTAL" };

    private static readonly string[] Phases = { "EPU", "AS", "SD", "SC" };

    private static FlowStep<FenixStateEvaluator> Step(string flow, string id)
        => FenixFlowDefinitions.Build().Single(f => f.Id == flow).Steps.Single(s => s.Id == id);

    private static ChecklistItem<FenixActionExecutor, FenixStateEvaluator> Item(string id)
        => FenixChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    [Theory]
    [InlineData(Scene.DayPrep, 1.0, 0.5)]
    [InlineData(Scene.DimFlight, 0.5, 0.3)]
    [InlineData(Scene.ParkingBright, 1.0, 0.5)]
    [InlineData(Scene.Off, 0.0, 0.0)]
    public void Scene_levels_are_the_A32NX_levels_scaled_to_0_1(Scene scene, double integral, double flood)
    {
        var w = FenixActionExecutor.CockpitLightingWrites(scene).ToDictionary(x => x.Key, x => x.Value);
        Assert.Equal(Knobs.OrderBy(k => k), w.Keys.OrderBy(k => k));
        Assert.Equal(integral, w["A_OH_LIGHTING_OVD"], 3);
        Assert.Equal(integral, w["A_PED_LIGHTING_PEDESTAL"], 3);
        Assert.Equal(flood, w["A_MIP_LIGHTING_FLOOD_MAIN"], 3);
        Assert.Equal(flood, w["A_MIP_LIGHTING_FLOOD_PEDESTAL"], 3);
    }

    /// <summary>Every scene value lands on a detent the Fenix definition registers: the
    /// integral knobs move in 0.05 steps, the flood knobs in 0.1 steps, all within 0–1.</summary>
    [Fact]
    public void Scene_values_sit_on_each_knobs_own_steps()
    {
        foreach (Scene scene in Enum.GetValues<Scene>())
            foreach (var (key, value) in FenixActionExecutor.CockpitLightingWrites(scene))
            {
                Assert.InRange(value, 0.0, 1.0);
                double perStep = key.StartsWith("A_MIP_LIGHTING_FLOOD", StringComparison.Ordinal) ? 10 : 20;
                Assert.Equal(Math.Round(value * perStep), value * perStep, 6);
            }
    }

    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_COCKPITLT", 1, "EPU_DOME", 2)]
    [InlineData("AFTER_START", "AS_COCKPITLT", 0, "AS_DOME", 1)]
    [InlineData("SHUTDOWN", "SD_COCKPITLT", 1, "SD_DOME", 2)]
    [InlineData("SECURE", "SC_COCKPITLT", 1, "SC_DOME", 0)]
    public void Ann_and_dome_steps_use_the_Fenix_encodings(string flow, string annId, int ann, string domeId, int dome)
    {
        var steps = FenixFlowDefinitions.Build().Single(f => f.Id == flow).Steps.ToDictionary(s => s.Id);
        Assert.Equal("S_OH_IN_LT_ANN_LT", steps[annId].EventName);   // 0 Dim / 1 Bright / 2 Test
        Assert.Equal(ann, steps[annId].TargetValue);
        Assert.Equal("S_OH_INT_LT_DOME", steps[domeId].EventName);   // 0 Off / 1 Dim / 2 Bright
        Assert.Equal(dome, steps[domeId].TargetValue);
    }

    /// <summary>ANN position 2 is TEST: it lights every annunciator, the LDG GEAR upper legends
    /// and red arrow among them, which FenixGearConfirmation reads as reds. No lighting write,
    /// in a flow or behind a checklist line, may ever select it.</summary>
    [Fact]
    public void Nothing_selects_the_annunciator_TEST_position()
    {
        var annSteps = FenixFlowDefinitions.Build().SelectMany(f => f.Steps)
            .Where(s => s.EventName == "S_OH_IN_LT_ANN_LT").ToList();
        Assert.Equal(4, annSteps.Count);
        Assert.All(annSteps, s => Assert.NotEqual(2, s.TargetValue));
    }

    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_LTSCENE", Scene.DayPrep)]
    [InlineData("AFTER_START", "AS_LTSCENE", Scene.DimFlight)]
    [InlineData("SHUTDOWN", "SD_LTSCENE", Scene.ParkingBright)]
    [InlineData("SECURE", "SC_LTSCENE", Scene.Off)]
    public void Scene_step_drives_its_scene_through_the_executor_pseudo_key(string flow, string id, Scene scene)
    {
        var s = Step(flow, id);
        Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
        Assert.Equal("COCKPIT_LIGHT_SCENE_" + scene.ToString().ToUpperInvariant(), s.EventName);
        Assert.Equal(FenixActionExecutor.CockpitLightSceneKey(scene), s.EventName);
        Assert.True(FenixActionExecutor.TryGetCockpitLightScene(s.EventName!, out var parsed));
        Assert.Equal(scene, parsed);
        Assert.Equal("Panel and integral brightness: SET", s.Label);
    }

    [Fact]
    public void Plain_lvar_keys_are_not_scene_keys()
    {
        Assert.False(FenixActionExecutor.TryGetCockpitLightScene("S_OH_INT_LT_DOME", out _));
        Assert.False(FenixActionExecutor.TryGetCockpitLightScene("COCKPIT_LIGHT_SCENE_", out _));
        Assert.False(FenixActionExecutor.TryGetCockpitLightScene("COCKPIT_LIGHT_SCENE_BRIGHT", out _));
    }

    /// <summary>FO-6/FO-7: every lighting line has a flow step behind it that writes it and
    /// names it, so a finished flow never latches a line it did not deliver.</summary>
    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU")]
    [InlineData("AFTER_START", "AS")]
    [InlineData("SHUTDOWN", "SD")]
    [InlineData("SECURE", "SC")]
    public void Every_lighting_line_has_a_delivering_step(string flow, string p)
    {
        foreach (var id in new[] { p + "_COCKPITLT", p + "_DOME", p + "_LTSCENE" })
        {
            Assert.Equal(id, Step(flow, id).CompletesChecklistItemId);
            Assert.NotNull(Item(id).CheckAction);
        }
    }

    /// <summary>The lighting block sits where the A32NX sets its cockpit lights: the step
    /// before the ANN step and the step after the block are the A32NX's neighbours of its own
    /// *_COCKPITLT step.</summary>
    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU")]
    [InlineData("AFTER_START", "AS")]
    [InlineData("SHUTDOWN", "SD")]
    [InlineData("SECURE", "SC")]
    public void Lighting_steps_sit_where_the_A32NX_sets_its_cockpit_lights(string flow, string p)
    {
        var fenix = FenixFlowDefinitions.Build().Single(f => f.Id == flow).Steps.Select(s => s.Id).ToList();
        var fbw = FbwA320FlowDefinitions.Build().Single(f => f.Id == flow).Steps.Select(s => s.Id).ToList();
        int i = fenix.IndexOf(p + "_COCKPITLT");
        int j = fbw.IndexOf(p + "_COCKPITLT");
        Assert.True(i >= 0 && j >= 0);
        Assert.Equal(new[] { p + "_COCKPITLT", p + "_DOME", p + "_LTSCENE" }, fenix.Skip(i).Take(3));
        Assert.Equal(j > 0 ? fbw[j - 1] : null, i > 0 ? fenix[i - 1] : null);
        Assert.Equal(j + 1 < fbw.Count ? fbw[j + 1] : null, i + 3 < fenix.Count ? fenix[i + 3] : null);
    }

    [Fact]
    public void Ann_step_labels_are_the_A32NX_labels()
    {
        var fbwSteps = FbwA320FlowDefinitions.Build().SelectMany(f => f.Steps).ToDictionary(s => s.Id);
        var fenixSteps = FenixFlowDefinitions.Build().SelectMany(f => f.Steps).ToDictionary(s => s.Id);
        foreach (var p in Phases)
            Assert.Equal(fbwSteps[p + "_COCKPITLT"].Label, fenixSteps[p + "_COCKPITLT"].Label);
    }

    /// <summary>The action lines carry the A32NX's ids and labels, in the A32NX's order (its
    /// standby-compass line left out), and are followed by the same line.</summary>
    [Theory]
    [InlineData("ELEC_POWER_UP", "EPU")]
    [InlineData("AFTER_START", "AS")]
    [InlineData("SHUTDOWN", "SD")]
    [InlineData("SECURE", "SC")]
    public void Lighting_lines_mirror_the_A32NX_lines(string group, string p)
    {
        var fbw = FbwA320ChecklistDefinitions.Build().Single(g => g.Id == group).Items;
        var fenix = FenixChecklistDefinitions.Build().Single(g => g.Id == group).Items;
        string[] ids = { p + "_COCKPITLT", p + "_DOME", p + "_LTSCENE" };

        Assert.Equal(fbw.Select(x => x.Id).Where(id => ids.Contains(id)),
            fenix.Select(x => x.Id).Where(id => ids.Contains(id)));

        var fenixIds = fenix.Select(x => x.Id).ToList();
        var fbwIds = fbw.Select(x => x.Id).ToList();
        int i = fenixIds.IndexOf(p + "_LTSCENE");
        int j = fbwIds.IndexOf(p + "_LTSCENE");
        Assert.Equal(ids, fenixIds.Skip(i - 2).Take(3));
        Assert.Equal(j + 1 < fbwIds.Count ? fbwIds[j + 1] : null, i + 1 < fenixIds.Count ? fenixIds[i + 1] : null);

        foreach (var id in ids)
            Assert.Equal(fbw.Single(x => x.Id == id).Label, fenix.Single(x => x.Id == id).Label);
    }

    [Theory]
    [InlineData("EPU_COCKPITLT", "S_OH_IN_LT_ANN_LT", 1)]
    [InlineData("EPU_DOME", "S_OH_INT_LT_DOME", 2)]
    [InlineData("AS_COCKPITLT", "S_OH_IN_LT_ANN_LT", 0)]
    [InlineData("AS_DOME", "S_OH_INT_LT_DOME", 1)]
    [InlineData("SD_COCKPITLT", "S_OH_IN_LT_ANN_LT", 1)]
    [InlineData("SD_DOME", "S_OH_INT_LT_DOME", 2)]
    [InlineData("SC_COCKPITLT", "S_OH_IN_LT_ANN_LT", 1)]
    [InlineData("SC_DOME", "S_OH_INT_LT_DOME", 0)]
    public void Ann_and_dome_lines_detect_their_own_position(string id, string field, int target)
    {
        var item = Item(id);
        Assert.Equal(ChecklistItemType.AutoDetectable, item.Type);
        Assert.Equal(field, item.StateFieldName);
        Assert.NotNull(item.CheckAction);
        for (int pos = 0; pos <= 2; pos++)
            Assert.Equal(pos == target, item.StateCondition!(pos));
        Assert.False(item.StateCondition!(double.NaN));
    }

    [Theory]
    [InlineData("EPU_LTSCENE")]
    [InlineData("AS_LTSCENE")]
    [InlineData("SD_LTSCENE")]
    [InlineData("SC_LTSCENE")]
    public void Scene_lines_are_manual_actions(string id)
    {
        var item = Item(id);
        Assert.Equal(ChecklistItemType.Actionable, item.Type);
        Assert.Null(item.StateFieldName);
        Assert.NotNull(item.CheckAction);
    }

    /// <summary>The Fenix has no standby-compass light: no compass line, no compass step.</summary>
    [Fact]
    public void No_standby_compass_lines()
    {
        Assert.DoesNotContain(FenixChecklistDefinitions.Build().SelectMany(g => g.Items),
            i => i.Id.EndsWith("_STBYCOMPASS", StringComparison.Ordinal));
        Assert.DoesNotContain(FenixFlowDefinitions.Build().SelectMany(f => f.Steps),
            s => s.Id.EndsWith("_STBYCOMPASS", StringComparison.Ordinal));
    }

    /// <summary>Both switches are OnRequest panel vars: the evaluator must poll them, or the
    /// Auto lines never read anything but NaN.</summary>
    [Fact]
    public void Ann_and_dome_switches_are_polled()
    {
        var poll = new FenixStateEvaluator().OnRequestPollFields;
        Assert.Contains("S_OH_IN_LT_ANN_LT", poll);
        Assert.Contains("S_OH_INT_LT_DOME", poll);
    }
}

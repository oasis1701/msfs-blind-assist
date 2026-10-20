using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using Scene = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.CockpitLightScene;
using FenixScene = MSFSBlindAssist.FirstOfficer.Fenix.FenixActionExecutor.CockpitLightScene;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-7 for the Headwind A330's cockpit-lighting action lines, the same change as the A32NX's
/// (<see cref="FbwA320LightingFlowTests"/>; FOA-3 keeps the two profiles' steps identical). Each of
/// the four lighting points (power-up, after start, shutdown, secure) has a step for the dome, the
/// standby-compass light and the scene behind the annunciator step, in the Fenix's step order (the
/// Fenix has no compass light). The A330 scene writes four brightness pots, not six
/// (<see cref="HwA330ActionExecutor.CockpitLightingKeys"/>): the step only delivers it.
/// </summary>
public class HwA330LightingFlowTests
{
    private static FlowStep<HwA330StateEvaluator> Step(string flow, string id)
        => HwA330FlowDefinitions.Build().Single(f => f.Id == flow).Steps.Single(s => s.Id == id);

    private static ChecklistItem<HwA330ActionExecutor, HwA330StateEvaluator> Item(string id)
        => HwA330ChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    /// <summary>The checklist source, read to find what a line's CheckAction writes: the delegate
    /// needs a connected executor to run, so the write it makes can only be read, not captured.</summary>
    private static string ChecklistSource()
        => File.ReadAllText(FoFbwUnclaimedEventKeyTests.ChecklistSourcePath("HWA330", "HwA330ChecklistDefinitions.cs"));

    private static (string Key, int Value) LineWrite(string id)
    {
        var m = Regex.Match(ChecklistSource(), "\"" + id + "\"[^;]*?e\\.Set\\(\"([A-Za-z0-9_]+)\",\\s*(\\d+)\\)");
        Assert.True(m.Success, $"{id}: no e.Set(\"KEY\", n) found in its checklist line");
        return (m.Groups[1].Value, int.Parse(m.Groups[2].Value));
    }

    private static string LineScene(string id)
    {
        var m = Regex.Match(ChecklistSource(), "\"" + id + "\"[^;]*?CockpitLightScene\\.(\\w+)\\)");
        Assert.True(m.Success, $"{id}: no SetCockpitLighting(CockpitLightScene.X) found in its checklist line");
        return m.Groups[1].Value;
    }

    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "ELEC_POWER_UP", "EPU")]
    [InlineData("AFTER_START", "AFTER_START", "AS")]
    [InlineData("SHUTDOWN", "SHUTDOWN", "SD")]
    [InlineData("SECURE", "SECURE", "SC")]
    public void Every_lighting_line_has_a_delivering_step(string flow, string group, string p)
    {
        foreach (var id in new[] { p + "_COCKPITLT", p + "_DOME", p + "_STBYCOMPASS", p + "_LTSCENE" })
        {
            Assert.Equal(id, Step(flow, id).CompletesChecklistItemId);
            Assert.Equal(group, Item(id).GroupId);
            Assert.NotNull(Item(id).CheckAction);
        }
    }

    /// <summary>The block follows the Fenix's step order after the annunciator: dome, then the
    /// compass light (the A330's own, as on the A32NX), then the scene, with nothing between them.</summary>
    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU")]
    [InlineData("AFTER_START", "AS")]
    [InlineData("SHUTDOWN", "SD")]
    [InlineData("SECURE", "SC")]
    public void The_lighting_block_is_annunciator_dome_compass_scene(string flow, string p)
    {
        var ids = HwA330FlowDefinitions.Build().Single(f => f.Id == flow).Steps.Select(s => s.Id).ToList();
        int i = ids.IndexOf(p + "_COCKPITLT");
        Assert.True(i >= 0);
        Assert.Equal(new[] { p + "_COCKPITLT", p + "_DOME", p + "_STBYCOMPASS", p + "_LTSCENE" },
            ids.Skip(i).Take(4));
    }

    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_DOME", 100, "Dome light: bright")]
    [InlineData("AFTER_START", "AS_DOME", 20, "Dome light: dim")]
    [InlineData("SHUTDOWN", "SD_DOME", 100, "Dome light: bright")]
    [InlineData("SECURE", "SC_DOME", 0, "Dome light: off")]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_STBYCOMPASS", 1, "Standby compass light: ON")]
    [InlineData("AFTER_START", "AS_STBYCOMPASS", 1, "Standby compass light: ON")]
    [InlineData("SHUTDOWN", "SD_STBYCOMPASS", 1, "Standby compass light: ON")]
    [InlineData("SECURE", "SC_STBYCOMPASS", 0, "Standby compass light: OFF")]
    public void Dome_and_compass_steps_make_the_write_their_lines_make(string flow, string id, int target, string label)
    {
        var step = Step(flow, id);
        var item = Item(id);
        string key = id.EndsWith("_DOME", StringComparison.Ordinal)
            ? "A32NX_OVHD_INTLT_DOME" : "A32NX_STBY_COMPASS_LIGHT_TOGGLE";

        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal(key, step.EventName);
        Assert.Equal(target, step.TargetValue);
        Assert.Equal(label, step.Label);

        // FO-3: the tick and the step fire the same write, and the line reads back what it writes.
        Assert.Equal((key, target), LineWrite(id));
        Assert.Equal(key, item.StateFieldName);
        Assert.True(item.StateCondition!(target));
    }

    /// <summary>Dome and compass steps skip quietly when the light already reads the line's own
    /// state, and an unread light never counts as "already set" (no data is not a reading).</summary>
    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_DOME")]
    [InlineData("AFTER_START", "AS_DOME")]
    [InlineData("SHUTDOWN", "SD_DOME")]
    [InlineData("SECURE", "SC_DOME")]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_STBYCOMPASS")]
    [InlineData("AFTER_START", "AS_STBYCOMPASS")]
    [InlineData("SHUTDOWN", "SD_STBYCOMPASS")]
    [InlineData("SECURE", "SC_STBYCOMPASS")]
    public void Dome_and_compass_steps_are_skip_guarded_and_unknown_is_not_already_set(string flow, string id)
    {
        var step = Step(flow, id);
        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new HwA330StateEvaluator()));
    }

    [Theory]
    [InlineData("ELECTRICAL_POWER_UP", "EPU_LTSCENE", Scene.DayPrep)]
    [InlineData("AFTER_START", "AS_LTSCENE", Scene.DimFlight)]
    [InlineData("SHUTDOWN", "SD_LTSCENE", Scene.ParkingBright)]
    [InlineData("SECURE", "SC_LTSCENE", Scene.Off)]
    public void Scene_step_drives_its_lines_scene_through_the_executor_pseudo_key(string flow, string id, Scene scene)
    {
        var s = Step(flow, id);
        Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
        Assert.Equal("Panel and integral brightness: SET", s.Label);

        Assert.Equal(scene.ToString(), LineScene(id));   // the scene the action line passes
        Assert.Equal("COCKPIT_LIGHT_SCENE_" + scene.ToString().ToUpperInvariant(), s.EventName);
        Assert.Equal(HwA330ActionExecutor.CockpitLightSceneKey(scene), s.EventName);
        Assert.True(HwA330ActionExecutor.TryGetCockpitLightScene(s.EventName!, out var parsed));
        Assert.Equal(scene, parsed);

        // Nothing readable says the knobs are at the scene's levels, so the step never skips.
        Assert.Null(s.SkipCondition);
    }

    /// <summary>The key is spelled as the Fenix spells it, scene for scene, from the same scene
    /// names, so the two A320-family flows read alike.</summary>
    [Fact]
    public void Scene_keys_are_spelled_as_the_Fenix_spells_them()
    {
        Assert.Equal(Enum.GetNames<FenixScene>(), Enum.GetNames<Scene>());
        foreach (Scene scene in Enum.GetValues<Scene>())
            Assert.Equal(FenixActionExecutor.CockpitLightSceneKey(Enum.Parse<FenixScene>(scene.ToString())),
                HwA330ActionExecutor.CockpitLightSceneKey(scene));
    }

    [Fact]
    public void Every_scene_has_its_own_key_and_the_executor_maps_each_back()
    {
        var keys = Enum.GetValues<Scene>().Select(HwA330ActionExecutor.CockpitLightSceneKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (Scene scene in Enum.GetValues<Scene>())
        {
            Assert.True(HwA330ActionExecutor.TryGetCockpitLightScene(HwA330ActionExecutor.CockpitLightSceneKey(scene), out var back));
            Assert.Equal(scene, back);
        }
    }

    [Fact]
    public void Plain_keys_are_not_scene_keys()
    {
        Assert.False(HwA330ActionExecutor.TryGetCockpitLightScene("A32NX_OVHD_INTLT_DOME", out _));
        Assert.False(HwA330ActionExecutor.TryGetCockpitLightScene("COCKPIT_LIGHT_SCENE_", out _));
        Assert.False(HwA330ActionExecutor.TryGetCockpitLightScene("COCKPIT_LIGHT_SCENE_BRIGHT", out _));
        Assert.False(HwA330ActionExecutor.TryGetCockpitLightScene("cockpit_light_scene_dayprep", out _));
    }

    /// <summary>
    /// FOA-4: the pseudo-key is not a control the definition knows, so it must be intercepted
    /// BEFORE the generic dispatch (whose ApplySilent would refuse or mis-write it). ExecuteStepAsync
    /// cannot be run here (every route into it is gated on a connected SimConnectManager), so the
    /// source is the only place to pin it, as FoFbwUnclaimedEventKeyTests does for ApplySilent.
    /// </summary>
    [Fact]
    public void The_executor_intercepts_the_scene_key_before_the_generic_dispatch()
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(
            FoFbwUnclaimedEventKeyTests.ExecutorSourcePath("HWA330", "HwA330ActionExecutor.cs"), "ExecuteStepAsync");

        int intercept = body.IndexOf("TryGetCockpitLightScene", StringComparison.Ordinal);
        int generic = body.IndexOf("DispatchAsync(step.EventName", StringComparison.Ordinal);
        Assert.True(intercept >= 0, "ExecuteStepAsync no longer maps the COCKPIT_LIGHT_SCENE_* pseudo-key.");
        Assert.True(generic >= 0);
        Assert.True(intercept < generic, "The scene key is intercepted only after the generic dispatch.");
        Assert.Contains("SetCockpitLighting", body);
    }

    /// <summary>Steps that were already there keep their place and wording: the annunciator step
    /// is unchanged, and the Fenix's step labels for the same lines are the A330's.</summary>
    [Theory]
    [InlineData("EPU")]
    [InlineData("AS")]
    [InlineData("SD")]
    [InlineData("SC")]
    public void Step_labels_are_the_Fenix_labels(string p)
    {
        var fbw = HwA330FlowDefinitions.Build().SelectMany(f => f.Steps).ToDictionary(s => s.Id);
        var fenix = FenixFlowDefinitions.Build().SelectMany(f => f.Steps).ToDictionary(s => s.Id);
        foreach (var id in new[] { p + "_COCKPITLT", p + "_DOME", p + "_LTSCENE" })
            Assert.Equal(fenix[id].Label, fbw[id].Label);
    }
}

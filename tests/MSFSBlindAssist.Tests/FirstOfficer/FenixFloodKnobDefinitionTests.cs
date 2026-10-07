using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.SimConnect;
using Xunit;
using Scene = MSFSBlindAssist.FirstOfficer.Fenix.FenixActionExecutor.CockpitLightScene;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix main-panel flood knob, A_MIP_LIGHTING_FLOOD_MAIN, and the other knobs the First
/// Officer's cockpit-lighting scene moves.
///
/// FenixA320Definition's variable dictionary initializer held this key twice: the "Main Panel
/// Flood Light" panel knob (OnRequest, Off and 10–100 %, Main Instrument Lights panel) and,
/// further down, an entry from the earlier bulk monitoring import ("LIGHTING FLOOD Main Pot
/// Position", Continuous, announced, Off/On). In an initializer the later entry silently
/// replaces the first, so the panel row offered only Off and On for a 0–1 knob, and every
/// change to the knob, the First Officer's scene write included, was spoken as a background
/// change. The panel knob is the intended one; the monitoring copy is gone.
/// </summary>
public class FenixFloodKnobDefinitionTests
{
    private const string Key = "A_MIP_LIGHTING_FLOOD_MAIN";

    [Fact]
    public void Flood_main_knob_is_defined_once()
    {
        string src = Regex.Replace(Regex.Replace(File.ReadAllText(DefinitionSource()),
            @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", "");
        int count = Regex.Matches(src, $@"\[\s*""{Key}""\s*\]\s*=\s*new\b").Count;
        Assert.True(count == 1,
            $"FenixA320Definition defines {Key} {count} times. In a dictionary initializer the last "
            + "one silently wins; keep the one panel knob.");
    }

    [Fact]
    public void Flood_main_knob_is_the_panel_knob_in_tenths()
    {
        var def = new FenixA320Definition().GetVariables()[Key];
        Assert.Equal(Key, def.Name);
        Assert.Equal("Main Panel Flood Light", def.DisplayName);
        Assert.Equal(SimVarType.LVar, def.Type);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.False(def.IsAnnounced);

        var steps = Enumerable.Range(0, 11).Select(i => i / 10.0).ToArray();
        Assert.Equal(steps.Length, def.ValueDescriptions.Count);
        foreach (double v in steps)
            Assert.Contains(def.ValueDescriptions.Keys, k => Math.Abs(k - v) < 1e-9);
        Assert.Equal("Off", def.ValueDescriptions[0.0]);
        Assert.Equal("100%", def.ValueDescriptions[1.0]);
    }

    /// <summary>Every knob a lighting scene writes is a quiet panel knob: the First Officer's
    /// own narration is the one voice for a scene, never a background announcement per knob.
    /// And every value a scene writes is one of that knob's panel positions, so the panel row
    /// shows it afterwards.</summary>
    [Fact]
    public void Scene_knobs_are_quiet_panel_knobs_that_list_every_scene_value()
    {
        var vars = new FenixA320Definition().GetVariables();
        foreach (Scene scene in Enum.GetValues<Scene>())
            foreach (var (key, value) in FenixActionExecutor.CockpitLightingWrites(scene))
            {
                var def = vars[key];
                Assert.True(def.UpdateFrequency == UpdateFrequency.OnRequest && !def.IsAnnounced,
                    $"{key} is {def.UpdateFrequency}{(def.IsAnnounced ? ", announced" : "")}: the {scene} scene "
                    + "write would be spoken as a background change.");
                Assert.True(def.ValueDescriptions.Keys.Any(k => Math.Abs(k - value) < 1e-9),
                    $"{key} has no panel position for {value} (the {scene} scene's value).");
            }
    }

    private static string DefinitionSource([CallerFilePath] string p = "")
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(p)!, "..", "..", "..",
            "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs"));
        Assert.True(File.Exists(path), path + " was not found. If the file moved, re-point this path.");
        return path;
    }
}

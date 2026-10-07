using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix FCU knob push/pull writers, and the First Officer's Before Start altitude push.
///
/// The Fenix FCU knobs (S_FCU_SPEED / S_FCU_HEADING / S_FCU_ALTITUDE) are relative encoders:
/// a push takes 1 off the L:var, a pull adds 1. The panel and the FCU hotkeys used to keep
/// their own running count per knob (rmpCounters) and write that count; the First Officer
/// (FenixActionExecutor.PushFcuManaged) reads the live value and writes value - 1. Two such
/// writers on one knob desync: after an FO push, the panel's next counted write can swallow
/// or double a press. Speed and heading moved to the atomic read-modify-write
/// (AdjustFcuPushPullCounter) first; the altitude knob follows here, before the First Officer
/// starts pushing it too.
///
/// The writers cannot run in a test (every write goes through SimConnectManager, whose
/// IsConnected setter is private), so the panel and hotkey call sites are pinned in source,
/// the FoFbwUnclaimedEventKeyTests technique.
/// </summary>
public class FenixFcuAltitudePushTests
{
    // =====================================================================
    // Panel + hotkeys: every FCU push/pull uses the atomic write
    // =====================================================================

    /// <summary>The panel (and the Fenix altitude window, which calls the same
    /// HandleUIVariableSet): PUSH is -1, PULL is +1, through the atomic helper.</summary>
    [Theory]
    [InlineData("S_FCU_SPEED")]
    [InlineData("S_FCU_HEADING")]
    [InlineData("S_FCU_ALTITUDE")]
    public void Panel_push_and_pull_use_the_atomic_write(string knob)
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionSource(), "HandleUIVariableSet");

        Assert.Equal($"AdjustFcuPushPullCounter(\"{knob}\", -1, simConnect);", PanelBranch(body, knob + "_PUSH"));
        Assert.Equal($"AdjustFcuPushPullCounter(\"{knob}\", 1, simConnect);", PanelBranch(body, knob + "_PULL"));
    }

    /// <summary>The FCU hotkeys write the same knobs, so they take the same atomic write.</summary>
    [Theory]
    [InlineData("FCUSpeed", "S_FCU_SPEED")]
    [InlineData("FCUHeading", "S_FCU_HEADING")]
    [InlineData("FCUAltitude", "S_FCU_ALTITUDE")]
    public void Hotkey_push_and_pull_use_the_atomic_write(string action, string knob)
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionSource(), "HandleHotkeyAction");

        Assert.Equal($"AdjustFcuPushPullCounter(\"{knob}\", -1, simConnect);", HotkeyCase(body, action + "Push"));
        Assert.Equal($"AdjustFcuPushPullCounter(\"{knob}\", 1, simConnect);", HotkeyCase(body, action + "Pull"));
    }

    /// <summary>No app-side counter writes these knobs anywhere in the definition: one counted
    /// write left behind is a second writer again.</summary>
    [Theory]
    [InlineData("S_FCU_SPEED")]
    [InlineData("S_FCU_HEADING")]
    [InlineData("S_FCU_ALTITUDE")]
    public void No_app_side_counter_writes_the_knob(string knob)
    {
        string src = StripComments(File.ReadAllText(DefinitionSource()));
        var counted = Regex.Matches(src, $@"\b(Increment|Decrement|Jump)Counter\(\s*""{knob}""");
        Assert.True(counted.Count == 0,
            $"FenixA320Definition still writes {knob} through an app-side counter ({counted.Count} call(s)). "
            + "The First Officer writes this knob too; use AdjustFcuPushPullCounter.");
    }

    /// <summary>The First Officer's push is the panel's push: a decrement of the live value.</summary>
    [Fact]
    public void First_officer_push_is_a_decrement_like_the_panel_push()
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(
            FoFbwUnclaimedEventKeyTests.ExecutorSourcePath("Fenix", "FenixActionExecutor.cs"), "PushFcuManaged");
        Assert.Contains("(L:{knobLVar}) 1 - (>L:{knobLVar})", body);
    }

    // =====================================================================
    // First Officer: the Before Start altitude push, as on the A32NX
    // =====================================================================

    [Fact]
    public void Executor_intercepts_the_altitude_push_pseudo_key()
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(
            FoFbwUnclaimedEventKeyTests.ExecutorSourcePath("Fenix", "FenixActionExecutor.cs"), "ExecuteStepAsync");
        Assert.Matches(new Regex(@"case\s+""FCU_PUSH_ALT_MANAGED""\s*:\s*return\s+PushFcuManaged\(\s*""S_FCU_ALTITUDE""\s*\)\s*;"),
            body);
    }

    /// <summary>Every FCU push pseudo-key a Fenix flow step writes is intercepted by the
    /// executor; an unintercepted one would fall through to a plain write of a dead L:var.</summary>
    [Fact]
    public void Every_fcu_push_pseudo_key_in_the_flows_is_intercepted()
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(
            FoFbwUnclaimedEventKeyTests.ExecutorSourcePath("Fenix", "FenixActionExecutor.cs"), "ExecuteStepAsync");
        var keys = FenixFlowDefinitions.Build().SelectMany(f => f.Steps)
            .Select(s => s.EventName)
            .Where(k => k != null && k.StartsWith("FCU_PUSH_", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.Contains("FCU_PUSH_ALT_MANAGED", keys);
        foreach (var key in keys)
            Assert.Contains($"case \"{key}\":", body);
    }

    [Fact]
    public void Before_start_flow_pushes_the_altitude_after_the_captain_sets_it()
    {
        var steps = FenixFlowDefinitions.Build().Single(f => f.Id == "BEFORE_START").Steps;
        int i = steps.FindIndex(s => s.Id == "BS_FCUALT");
        Assert.True(i > 0, "BS_FCUALT is missing from the Fenix Before Start flow.");

        var push = steps[i];
        Assert.Equal(FlowStepActionType.SetSwitch, push.ActionType);
        Assert.Equal("FCU_PUSH_ALT_MANAGED", push.EventName);
        Assert.Equal(1, push.TargetValue);
        Assert.Equal("FCU altitude: pushed", push.Label);
        Assert.Equal("BS_FCUALT", push.CompletesChecklistItemId);

        var captain = steps[i - 1];
        Assert.Equal("BS_ALT", captain.Id);
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Set cleared altitude on the FCU", captain.Label);
    }

    /// <summary>The two steps sit where the A32NX has them, with its labels.</summary>
    [Fact]
    public void Before_start_steps_sit_where_the_A32NX_has_them()
    {
        var fenix = FenixFlowDefinitions.Build().Single(f => f.Id == "BEFORE_START").Steps;
        var fbw = FbwA320FlowDefinitions.Build().Single(f => f.Id == "BEFORE_START").Steps;
        AssertSameSlot(fenix.Select(s => (s.Id, s.Label)).ToList(), fbw.Select(s => (s.Id, s.Label)).ToList());
    }

    [Fact]
    public void Before_start_has_the_altitude_lines()
    {
        var items = FenixChecklistDefinitions.Build().Single(g => g.Id == "BEFORE_START").Items;
        int i = items.FindIndex(x => x.Id == "BS_FCUALT");
        Assert.True(i > 0, "BS_FCUALT is missing from the Fenix Before Start action group.");

        var push = items[i];
        Assert.Equal("FCU altitude: pushed", push.Label);
        Assert.Equal(ChecklistItemType.Actionable, push.Type);
        Assert.Null(push.StateFieldName);
        Assert.NotNull(push.CheckAction);

        var reminder = items[i - 1];
        Assert.Equal("BS_ALT", reminder.Id);
        Assert.Equal(ChecklistItemType.CaptainReminder, reminder.Type);
        Assert.Equal("Set cleared altitude on the FCU", reminder.Label);
        Assert.Null(reminder.CheckAction);
    }

    [Fact]
    public void Before_start_lines_sit_where_the_A32NX_has_them()
    {
        var fenix = FenixChecklistDefinitions.Build().Single(g => g.Id == "BEFORE_START").Items;
        var fbw = FbwA320ChecklistDefinitions.Build().Single(g => g.Id == "BEFORE_START").Items;
        AssertSameSlot(fenix.Select(x => (x.Id, x.Label)).ToList(), fbw.Select(x => (x.Id, x.Label)).ToList());
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>BS_ALT then BS_FCUALT, with the A32NX's labels and its neighbours on both sides.</summary>
    private static void AssertSameSlot(List<(string Id, string Label)> fenix, List<(string Id, string Label)> fbw)
    {
        int i = fenix.FindIndex(x => x.Id == "BS_ALT");
        int j = fbw.FindIndex(x => x.Id == "BS_ALT");
        Assert.True(i > 0 && j > 0, "BS_ALT is missing (Fenix index " + i + ", A32NX index " + j + ").");
        Assert.True(i + 2 < fenix.Count && j + 2 < fbw.Count);

        Assert.Equal(fbw[j], fenix[i]);
        Assert.Equal(fbw[j + 1], fenix[i + 1]);
        Assert.Equal("BS_FCUALT", fenix[i + 1].Id);
        Assert.Equal(fbw[j - 1].Id, fenix[i - 1].Id);
        Assert.Equal(fbw[j + 2].Id, fenix[i + 2].Id);
    }

    /// <summary>The one statement inside <c>if (varKey == "KEY" &amp;&amp; value == 1) { ... return true; }</c>.</summary>
    private static string PanelBranch(string body, string key)
    {
        var m = Regex.Match(body,
            $@"if\s*\(\s*varKey\s*==\s*""{Regex.Escape(key)}""\s*&&\s*value\s*==\s*1\s*\)\s*\{{\s*(?<stmt>[^{{}}]*?)\s*return\s+true\s*;\s*\}}");
        Assert.True(m.Success, "HandleUIVariableSet has no `varKey == \"" + key + "\" && value == 1` branch.");
        return Normalize(m.Groups["stmt"].Value);
    }

    /// <summary>The one statement inside <c>case HotkeyAction.NAME: ... return true;</c>.</summary>
    private static string HotkeyCase(string body, string action)
    {
        var m = Regex.Match(body,
            $@"case\s+HotkeyAction\.{Regex.Escape(action)}\s*:\s*(?<stmt>[^{{}}]*?)\s*return\s+true\s*;");
        Assert.True(m.Success, "HandleHotkeyAction has no `case HotkeyAction." + action + ":`.");
        return Normalize(m.Groups["stmt"].Value);
    }

    private static string Normalize(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    /// <summary>Line and block comments out, so a comment naming an old call never counts.</summary>
    private static string StripComments(string src)
        => Regex.Replace(Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", "");

    private static string DefinitionSource([CallerFilePath] string p = "")
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(p)!, "..", "..", "..",
            "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs"));
        Assert.True(File.Exists(path), path + " was not found. If the file moved, re-point this path.");
        return path;
    }
}

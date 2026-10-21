// The First Officer window's 1 s poll must never re-read a variable MainForm speaks on EVERY
// delivery.
//
// MainForm.HandleSpecialAnnouncements answers the read-out hotkeys: a branch keyed on the
// variable alone speaks the delivery's own text (e.Description), because only the hotkey ever
// asked for that variable. A non-announced OnRequest variable fires SimVarUpdated on every
// response, changed or not (SimConnectManager.VarCache), so a variable polled each second by
// FirstOfficerForm over OnRequestPollFields is spoken each second. The A32NX and A330 read-back
// polled FUEL_QUANTITY_KG, the fuel hotkey's variable, and "Fuel on board ... kilograms" was
// spoken every second for as long as the First Officer window was open. MainForm has no test
// harness, so the spoken set is read from its source.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FoPollSilenceTests
{
    // A HandleSpecialAnnouncements branch whose body opens by speaking the delivery's own text.
    private static readonly Regex SpeaksDescription = new(
        @"if\s*\((?<cond>[^{]*?)\)\s*\{\s*announcer\.(?:AnnounceImmediate|Announce|AnnounceWithQueue)\(e\.Description\);",
        RegexOptions.Singleline);

    private static readonly Regex VarNameTest = new(@"e\.VarName\s*==\s*""(?<name>[^""]+)""");

    [Fact]
    public void No_first_officer_poll_reads_a_variable_MainForm_speaks_on_every_delivery()
    {
        var spoken = VarsSpokenOnEveryDelivery();
        // Guards the extraction itself: the fuel hotkey's variable is one of them.
        Assert.Contains("FUEL_QUANTITY_KG", spoken);

        var evaluators = typeof(LVarStateEvaluator).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(LVarStateEvaluator)) && !t.IsAbstract)
            .Select(t => (LVarStateEvaluator)Activator.CreateInstance(t)!)
            .ToList();
        Assert.True(evaluators.Count >= 5, $"only {evaluators.Count} LVarStateEvaluator types found");

        var offenders = evaluators
            .SelectMany(e => e.OnRequestPollFields.Where(spoken.Contains).Select(f => $"{e.GetType().Name} polls {f}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "MainForm.HandleSpecialAnnouncements speaks every delivery of these hotkey read-out variables, so the "
            + "First Officer's 1 s poll would speak them every second. Poll a silent key instead (the Fenix's "
            + "FUEL TOTAL QUANTITY WEIGHT, the A32NX's FUEL_TOTAL_WEIGHT_KG): " + string.Join(", ", offenders));
    }

    public static IEnumerable<object[]> FbwFuelAirframes() => new[]
    {
        new object[] { "A32NX" },
        new object[] { "A339X" },
    };

    [Theory]
    [MemberData(nameof(FbwFuelAirframes))]
    public void The_fuel_read_back_polls_a_silent_key(string airframe)
    {
        var (polled, registered) = airframe == "A32NX"
            ? (new MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320StateEvaluator().OnRequestPollFields,
               new FlyByWireA320Definition().GetVariables())
            : (new MSFSBlindAssist.FirstOfficer.HWA330.HwA330StateEvaluator().OnRequestPollFields,
               new HeadwindA330Definition().GetVariables());

        Assert.Contains("FUEL_TOTAL_WEIGHT_KG", polled);
        Assert.True(registered.TryGetValue("FUEL_TOTAL_WEIGHT_KG", out var def), airframe + ": FUEL_TOTAL_WEIGHT_KG is not registered");
        Assert.Equal("FUEL TOTAL QUANTITY WEIGHT", def!.Name);
        Assert.Equal("kilograms", def.Units);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.False(def.IsAnnounced);
        Assert.True(def.ExcludeFromMonitorManager);
    }

    private static HashSet<string> VarsSpokenOnEveryDelivery()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "MainForm.Announcers.cs"));
        int start = text.IndexOf("private bool HandleSpecialAnnouncements(", StringComparison.Ordinal);
        Assert.True(start >= 0, "HandleSpecialAnnouncements not found in MainForm.Announcers.cs");
        int end = text.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        if (end < 0) end = text.IndexOf("\n    }\r\n", start, StringComparison.Ordinal);
        Assert.True(end > start, "end of HandleSpecialAnnouncements not found");
        string body = text.Substring(start, end - start);

        return SpeaksDescription.Matches(body)
            .SelectMany(m => VarNameTest.Matches(m.Groups["cond"].Value).Select(n => n.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}

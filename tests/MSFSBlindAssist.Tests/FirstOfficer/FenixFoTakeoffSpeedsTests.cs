using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Before Start checklist's "Takeoff speeds and thrust: SET" line reads the Fenix's own
/// perf L:vars (<c>N_MISC_PERF_TO_V1/VR/V2/FLEX</c>), not the FenixQuartz ones, which v3 no
/// longer publishes. The speeds and flex come from the MCDU's PERF TAKEOFF page.
/// </summary>
public class FenixFoTakeoffSpeedsTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static ChecklistItem<FenixActionExecutor, FenixStateEvaluator> Item(string id)
        => FenixChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    private static T InCulture<T>(string name, Func<T> f)
    {
        var old = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
            return f();
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Takeoff_speeds_line_reads_the_native_perf_vars(string culture)
    {
        var e = new FenixStateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(("N_MISC_PERF_TO_V1", 142), ("N_MISC_PERF_TO_VR", 145),
            ("N_MISC_PERF_TO_V2", 149), ("N_MISC_PERF_TO_FLEX", 55)));
        Assert.Equal("V1 142, VR 145, V2 149, flex 55", InCulture(culture, () => Item("BSC_TOSPEEDS").ReadLiveValue(e)));
    }

    // Measured on v3 2026-10-21: all four read 0 until PERF TAKEOFF is filled in.
    [Fact]
    public void Takeoff_speeds_read_not_set_before_perf_is_entered()
    {
        var e = new FenixStateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(("N_MISC_PERF_TO_V1", 0), ("N_MISC_PERF_TO_VR", 0),
            ("N_MISC_PERF_TO_V2", 0), ("N_MISC_PERF_TO_FLEX", 0)));
        Assert.Equal("not set", Item("BSC_TOSPEEDS").ReadLiveValue(e));
    }

    [Fact]
    public void Takeoff_speeds_are_unknown_until_read()
    {
        var e = new FenixStateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With());
        Assert.Null(Item("BSC_TOSPEEDS").ReadLiveValue(e));
    }

    [Fact]
    public void The_perf_vars_are_polled_onto_the_cache()
    {
        var polled = new FenixStateEvaluator().OnRequestPollFields;
        foreach (var f in new[] { "N_MISC_PERF_TO_V1", "N_MISC_PERF_TO_VR", "N_MISC_PERF_TO_V2", "N_MISC_PERF_TO_FLEX" })
            Assert.Contains(f, polled);
    }

    [Fact]
    public void No_Fenix_first_officer_file_names_a_FenixQuartz_var()
    {
        string dir = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "FirstOfficer", "Fenix");
        var offenders = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("FNX2PLD_", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }
}

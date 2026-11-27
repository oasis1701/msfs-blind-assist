using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer.DA40;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests.FirstOfficer.DA40;

/// <summary>
/// The DA40 First Officer's flows and groups, built the way the window builds them. The read-back
/// pages come from the installed COWS package when it is there (<see cref="InstalledPages"/>), and
/// otherwise from a stand-in built from the read-back's own link table, so the tests run on CI; the
/// package-backed tests skip themselves without it, as NativeChecklistReaderTests do.
/// </summary>
internal static class DA40FoTestData
{
    public static IEnumerable<object[]> Variants() => new[]
    {
        new object[] { DA40Variant.NG }, new object[] { DA40Variant.XLS },
    };

    public static string Code(DA40Variant v) => v == DA40Variant.NG ? "COWS_DA40NG" : "COWS_DA40XLS";

    public static List<FlowDefinition<DA40FoStateEvaluator>> Flows(DA40Variant v) => DA40FoFlowDefinitions.Build(v);

    /// <summary>The aircraft's own pages, or null when the package is not installed.</summary>
    public static IReadOnlyList<NativeChecklistPage>? InstalledPages(DA40Variant v) => NativeChecklistReader.LoadPages(Code(v));

    /// <summary>
    /// A package-backed test skips only when the package is really absent: a file that is found and
    /// still loads nothing is the bug. "Null, so return" passed every one of these on a PC with the
    /// package while the Windows-1252 declaration made every load throw (DA40S-14).
    /// </summary>
    public static void AssertNotInstalled(DA40Variant v)
        => Xunit.Assert.True(NativeChecklistReader.FindChecklistFile(Code(v)) is null,
            $"{Code(v)}'s checklist is installed but did not load: {NativeChecklistReader.FindChecklistFile(Code(v))}");

    /// <summary>
    /// Stand-in pages: every line the variant's flows link, on its page and in its block, under the
    /// COWS titles, plus a line nothing links (a Captain line) and a note, and the two reference pages.
    /// </summary>
    public static IReadOnlyList<NativeChecklistPage> StandInPages(DA40Variant v)
    {
        var linked = Flows(v).SelectMany(f => f.Steps).SelectMany(s => s.LinkedChecklistItemIds).ToHashSet();
        var titles = new Dictionary<string, string>
        {
            [DA40FoReadback.BeforeStartPage] = "Before engine start",
            [DA40FoReadback.EngineStartPage] = "Engine start",
            [DA40FoReadback.AfterStartPage] = "After engine start",
            [DA40FoReadback.TaxiPage] = "TAXI",
            [DA40FoReadback.BeforeTakeoffPage] = v == DA40Variant.NG ? "Before take-off" : "Before Take off",
            [DA40FoReadback.LineupPage] = "LINE UP",
            [DA40FoReadback.ClimbPage] = "Climb",
            [DA40FoReadback.CruisePage] = "CRUISE",
            [DA40FoReadback.DescentPage] = "Descent",
            [DA40FoReadback.ApproachPage] = "Approach",
            [DA40FoReadback.FinalPage] = "Final",
            [DA40FoReadback.AfterLandingPage] = "After Landing",
            [DA40FoReadback.ShutdownPage] = "Shutdown",
        };

        var pages = new List<NativeChecklistPage>();
        foreach (var (page, title) in titles)
        {
            var lines = new List<NativeChecklistLine>
            {
                new("Note", "", new[] { "Edited for flight simulation." }, System.Array.Empty<string>(), null, true, false),
            };
            string? block = null;
            // One line per linked id (a page can match an id on two spellings; the first is enough).
            foreach (var link in DA40FoReadback.Links.Where(l => l.Page == page && linked.Contains(l.Id))
                         .GroupBy(l => l.Id).Select(g => g.First()))
            {
                if (link.Block != block && link.Block != null)
                    lines.Add(new NativeChecklistLine(link.Block, "", System.Array.Empty<string>(),
                        System.Array.Empty<string>(), null, false, true));
                block = link.Block;
                lines.Add(new NativeChecklistLine(link.Subject, link.Expectation, System.Array.Empty<string>(),
                    System.Array.Empty<string>(), link.Block, false, false));
            }
            lines.Add(new NativeChecklistLine("Engine instruments", "Monitor", System.Array.Empty<string>(),
                System.Array.Empty<string>(), null, false, false));
            pages.Add(new NativeChecklistPage(title, null, lines));
        }
        pages.Add(new NativeChecklistPage("Weights/Speeds", null, new[]
        {
            new NativeChecklistLine("Weights", "", System.Array.Empty<string>(), System.Array.Empty<string>(), null, false, true),
            new NativeChecklistLine("Vr", "67KIAS", System.Array.Empty<string>(), System.Array.Empty<string>(), "Weights", false, false),
        }));
        pages.Add(new NativeChecklistPage("Tips and help", null, new[]
        {
            new NativeChecklistLine("Warmup", "", new[] { "Idle for 2 min." }, System.Array.Empty<string>(), null, true, false),
        }));
        return pages;
    }

    /// <summary>The window's groups from the installed pages, else from the stand-in.</summary>
    public static List<ChecklistGroup<DA40FoActionExecutor, DA40FoStateEvaluator>> Groups(DA40Variant v)
        => DA40FoChecklistDefinitions.Build(v, InstalledPages(v) ?? StandInPages(v));

    public static List<ChecklistGroup<DA40FoActionExecutor, DA40FoStateEvaluator>> StandInGroups(DA40Variant v)
        => DA40FoChecklistDefinitions.Build(v, StandInPages(v));

    public static XDocument Doc(string xml) => XDocument.Parse(xml);
}

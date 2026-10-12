using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Reading the aeroplane's own checklist out of its package.
///
/// The parsing is tested against a document built here rather than against the installed
/// aircraft, so it runs on any machine; the one test that does touch the real package skips
/// itself when it is not there.
/// </summary>
public class NativeChecklistReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-checklist-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Roaming => Path.Combine(_root, "Roaming");
    private string Local => Path.Combine(_root, "Local");

    /// <summary>A UserCfg.opt for one simulator naming <paramref name="packages"/> as its packages root.</summary>
    private void WriteUserCfg(string simFolder, string packages, string key = "InstalledPackagesPath")
    {
        string dir = Path.Combine(Roaming, simFolder);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(packages);
        File.WriteAllLines(Path.Combine(dir, "UserCfg.opt"), new[] { "{Graphics", $"{key} \"{packages}\"", "}" });
    }

    /// <summary>An aircraft package under <paramref name="folder"/> carrying a checklist for <paramref name="simObject"/>.</summary>
    private static string WritePackage(string folder, string package, string simObject)
    {
        string dir = Path.Combine(folder, package, "SimObjects", "Airplanes", simObject, "Checklist");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "Checklist.xml");
        File.WriteAllText(file, "<Checklist/>");
        return file;
    }

    private static XDocument Sample() => XDocument.Parse("""
        <Checklist>
          <Step ChecklistStepId="PREFLIGHT_GATE">
            <Page SubjectTT="Before engine start">
              <Checkpoint>
                <CheckpointDesc SubjectTT="Parking Brake" ExpectationTT="Set"/>
                <Clue Name="Forwards = go, Backwards = stop"/>
              </Checkpoint>
              <Checkpoint>
                <CheckpointDesc SubjectTT="Alternate air" ExpectationTT="Closed"/>
              </Checkpoint>
            </Page>
          </Step>
          <Step ChecklistStepId="POSTFLIGHT">
            <Page SubjectTT="Tips and help">
              <Checkpoint>
                <CheckpointDesc SubjectTT="Warmup" ExpectationTT="Clue"/>
                <Clue Name="Idle for 2 min. Up to 50% load."/>
              </Checkpoint>
              <Block SubjectTT="Starting tips">
                <Checkpoint>
                  <CheckpointDesc SubjectTT="Priming" ExpectationTT="Clue"/>
                  <Clue Name="Use the same throttle position every time."/>
                </Checkpoint>
              </Block>
              <Checkpoint>
                <CheckpointDesc SubjectTT="After the block" ExpectationTT="Noted"/>
              </Checkpoint>
            </Page>
            <Page SubjectTT="Before engine start">
              <Checkpoint>
                <CheckpointDesc SubjectTT="Second page, same name" ExpectationTT="Noted"/>
              </Checkpoint>
            </Page>
          </Step>
        </Checklist>
        """);

    [Fact]
    public void APageBecomesACategoryAndACheckpointBecomesALine()
    {
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.Contains("[Before engine start]", text, StringComparison.Ordinal);
        Assert.Contains("Parking Brake ... Set", text, StringComparison.Ordinal);
        Assert.Contains("Alternate air ... Closed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACluesTextIsKeptBecauseItIsOftenTheOnlyPlaceADetailIsWritten()
    {
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.Contains("Forwards = go, Backwards = stop", text, StringComparison.Ordinal);
        Assert.Contains("Idle for 2 min. Up to 50% load.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteDoesNotReadAsAnExpectationOfTheWordClue()
    {
        // Asobo puts the literal word "Clue" in the expectation column of a note row.
        // Rendering it would give "Warmup ... Clue", which means nothing to anybody.
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.DoesNotContain("... Clue", text, StringComparison.Ordinal);
        Assert.Contains("Warmup", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPagesWithOneNameBothSurvive()
    {
        // The category dictionary downstream is keyed by NAME, so a duplicate would
        // silently swallow the first page's contents.
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.Contains("[Before engine start]", text, StringComparison.Ordinal);
        Assert.Contains("[Before engine start 2]", text, StringComparison.Ordinal);
        Assert.Contains("Second page, same name", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRealDA40ChecklistCarriesItsTipsPage()
    {
        // Skips itself when the aeroplane is not installed — a test that fails for being on
        // the wrong computer teaches people to ignore failures.
        string? text = NativeChecklistReader.Render("COWS_DA40NG");
        if (text is null) return;

        Assert.Contains("[Tips and help]", text, StringComparison.Ordinal);

        // The lines this whole exercise was for: the aeroplane's own operating knowledge.
        Assert.Contains("Idle for 2 min", text, StringComparison.Ordinal);
        Assert.Contains("Downwind", text, StringComparison.Ordinal);
        Assert.Contains("ECU test button for 10 seconds", text, StringComparison.Ordinal);

        // And the procedures themselves, not just the tips.
        Assert.Contains("[Before engine start]", text, StringComparison.Ordinal);
        Assert.True(text.Split('\n').Length > 100, "the whole checklist should be far longer");
    }

    [Fact]
    public void AnAircraftWithNoChecklistAnswersNullRatherThanGuessing()
    {
        Assert.Null(NativeChecklistReader.Render("NO_SUCH_AEROPLANE_12345"));
    }
    /// <summary>
    /// ⚠️ A CHECKPOINT INSIDE A &lt;Block&gt; USED TO BE THROWN AWAY. The reader walked only the
    /// page's DIRECT children, and both DA40 checklists put whole sections inside blocks:
    /// measured on the installed package, the XLS has 220 checkpoints of which 105 — nearly
    /// half — were lost, and the NG 29 of 137.
    /// </summary>
    [Fact]
    public void ACheckpointInsideABlockIsRendered()
    {
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.Contains("Use the same throttle position every time.", text, StringComparison.Ordinal);
    }

    /// <summary>The block's own subject is a real heading and is kept.</summary>
    [Fact]
    public void ABlockHeadingIsKept()
        => Assert.Contains("Starting tips", NativeChecklistReader.RenderFile(Sample()), StringComparison.Ordinal);

    /// <summary>
    /// A block inside a block keeps BOTH headings. Neither shipped file nests today, so
    /// this pins the shape rather than a measurement: a Descendants("Checkpoint") sweep
    /// would find the inner checkpoint and silently drop the inner heading, which is the
    /// half that says which table a row belongs to.
    /// </summary>
    [Fact]
    public void ANestedBlockKeepsItsOwnHeading()
    {
        string text = NativeChecklistReader.RenderFile(XDocument.Parse("""
            <Checklist>
              <Step>
                <Page SubjectTT="Cruise">
                  <Block SubjectTT="Power settings">
                    <Block SubjectTT="At 8000 feet">
                      <Checkpoint>
                        <CheckpointDesc SubjectTT="Manifold pressure" ExpectationTT="22 inches"/>
                      </Checkpoint>
                    </Block>
                  </Block>
                </Page>
              </Step>
            </Checklist>
            """));

        int outer = text.IndexOf("Power settings", StringComparison.Ordinal);
        int inner = text.IndexOf("At 8000 feet", StringComparison.Ordinal);
        int row = text.IndexOf("Manifold pressure ... 22 inches", StringComparison.Ordinal);

        Assert.True(outer >= 0 && inner > outer && row > inner,
            $"order was outer {outer}, inner {inner}, row {row}");
    }

    /// <summary>
    /// A page can mix loose checkpoints with blocks, so the walk is over the page's
    /// children IN ORDER and the author's sequence survives.
    /// </summary>
    [Fact]
    public void APageThatMixesLooseCheckpointsWithBlocksKeepsItsOrder()
    {
        string text = NativeChecklistReader.RenderFile(Sample());

        int warmup = text.IndexOf("Warmup", StringComparison.Ordinal);
        int block = text.IndexOf("Starting tips", StringComparison.Ordinal);
        int after = text.IndexOf("After the block", StringComparison.Ordinal);

        Assert.True(warmup >= 0 && block > warmup && after > block,
            $"order was warmup {warmup}, block {block}, after {after}");
    }

    /// <summary>
    /// The XLS's checklist is a DIFFERENT document from the NG's and carries the things the
    /// scanned POH puts in a chart: the cruise power table by altitude, the three start
    /// procedures, and the warning that the fuel gauge has a dead zone. Every one of those
    /// sits inside a Block, so this is the test that would have caught the lost half.
    /// </summary>
    [Fact]
    public void TheRealXlsChecklistCarriesItsPowerTableAndItsStartProcedures()
    {
        string? text = NativeChecklistReader.Render("COWS_DA40XLS");
        if (text is null) return;

        // Inside blocks, every one of them.
        Assert.Contains("Cold start", text, StringComparison.Ordinal);
        Assert.Contains("Flooded/Hot start", text, StringComparison.Ordinal);
        Assert.Contains("Cruise:65%", text, StringComparison.Ordinal);
        Assert.Contains("Starting tips", text, StringComparison.Ordinal);

        // A table row, to prove the block's contents came with its heading.
        Assert.Contains("2400RPM", text, StringComparison.Ordinal);
    }

    /// <summary>The NG's blocks carry its weights, its speeds and the high-altitude notes.</summary>
    [Fact]
    public void TheRealNgChecklistCarriesItsWeightsAndSpeeds()
    {
        string? text = NativeChecklistReader.Render("COWS_DA40NG");
        if (text is null) return;

        Assert.Contains("Weights", text, StringComparison.Ordinal);
        Assert.Contains("Operating speeds", text, StringComparison.Ordinal);
        Assert.Contains("High Altitude operations", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠️ THE READER ONLY KNEW THE FOUR DEFAULT COMMUNITY FOLDERS. The simulator asks where
    /// packages go on first launch, so a pilot who picked another drive — common — got "this
    /// aeroplane ships no checklist" for an aeroplane that does. The packages root is read from
    /// UserCfg.opt the way the scenery census reads it.
    /// </summary>
    [Fact]
    public void AChecklistInACustomPackagesFolderIsFound()
    {
        string packages = Path.Combine(_root, "D-drive", "MSFS");
        WriteUserCfg("Microsoft Flight Simulator 2024", packages);
        string expected = WritePackage(Path.Combine(packages, "Community"), "cows-da40", "COWS_DA40NG");

        Assert.Equal(expected, NativeChecklistReader.FindChecklistFile("COWS_DA40NG", Roaming, Local));
    }

    [Fact]
    public void TheFs2020PackagesFolderIsSearchedToo()
    {
        string packages = Path.Combine(_root, "E-drive", "MSFS2020");
        WriteUserCfg("Microsoft Flight Simulator", packages);
        string expected = WritePackage(Path.Combine(packages, "Community"), "cows-da40", "COWS_DA40XLS");

        Assert.Equal(expected, NativeChecklistReader.FindChecklistFile("COWS_DA40XLS", Roaming, Local));
    }

    /// <summary>A Marketplace copy lives under Official (OneStore or Steam), not Community.</summary>
    [Fact]
    public void AMarketplaceCopyUnderOfficialIsFound()
    {
        string packages = Path.Combine(_root, "MSFS");
        WriteUserCfg("Microsoft Flight Simulator 2024", packages);
        string expected = WritePackage(Path.Combine(packages, "Official", "OneStore"), "cows-da40", "COWS_DA40NG");

        Assert.Equal(expected, NativeChecklistReader.FindChecklistFile("COWS_DA40NG", Roaming, Local));
    }

    /// <summary>
    /// The simulator writes the NextBoot line the moment a new folder is PICKED, before
    /// anything has moved there — the folder it is loading from is the active key's.
    /// </summary>
    [Fact]
    public void TheNextBootFolderIsNotWhereTheAircraftIs()
    {
        string packages = Path.Combine(_root, "Pending");
        WriteUserCfg("Microsoft Flight Simulator 2024", packages, key: "InstalledPackagesPathNextBoot");
        WritePackage(Path.Combine(packages, "Community"), "cows-da40", "COWS_DA40NG");

        Assert.Null(NativeChecklistReader.FindChecklistFile("COWS_DA40NG", Roaming, Local));
    }

    /// <summary>A pilot with no readable UserCfg.opt still has the default folders searched.</summary>
    [Fact]
    public void TheDefaultCommunityFolderIsStillSearched()
    {
        string community = Path.Combine(Local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community");
        string expected = WritePackage(community, "cows-da40", "COWS_DA40NG");

        Assert.Equal(expected, NativeChecklistReader.FindChecklistFile("COWS_DA40NG", Roaming, Local));
    }

    /// <summary>
    /// Community is searched before Official on every simulator, so a pilot's own copy wins
    /// over a Marketplace one and the slow Official scan is paid only when nothing is found.
    /// </summary>
    [Fact]
    public void EveryCommunityFolderComesBeforeAnyOfficialOne()
    {
        string p2024 = Path.Combine(_root, "MSFS2024");
        string p2020 = Path.Combine(_root, "MSFS2020");
        WriteUserCfg("Microsoft Flight Simulator 2024", p2024);
        WriteUserCfg("Microsoft Flight Simulator", p2020);
        Directory.CreateDirectory(Path.Combine(p2024, "Official", "OneStore"));
        Directory.CreateDirectory(Path.Combine(p2020, "Official", "Steam"));

        var roots = NativeChecklistReader.PackageRoots(Roaming, Local).ToList();
        int lastCommunity = roots.FindLastIndex(r => Path.GetFileName(r) == "Community");
        int firstOfficial = roots.FindIndex(r => Path.GetFileName(Path.GetDirectoryName(r)!) == "Official");

        Assert.Contains(Path.Combine(p2024, "Community"), roots);
        Assert.Contains(Path.Combine(p2020, "Community"), roots);
        Assert.True(firstOfficial > lastCommunity, $"official at {firstOfficial}, last community at {lastCommunity}");
        Assert.Equal(roots.Count, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}


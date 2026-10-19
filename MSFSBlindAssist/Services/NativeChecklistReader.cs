using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The aircraft's OWN checklist, read out of the package it shipped in.
///
/// ⚠️ THIS EXISTS BECAUSE THE AEROPLANE HAD BEEN TELLING US THINGS NOBODY READ. Every MSFS
/// aircraft may ship a native checklist — plain XML under <c>SimObjects/Airplanes/&lt;model&gt;/
/// Checklist/</c> — and the COWS DA40's carries not only its two dozen procedures but a "Tips
/// and help" page holding the vendor's own operating knowledge: warm-up times, rotate speeds,
/// the traffic-pattern power settings, that fuel transfer stops itself above ~14 gallons, and
/// how to reset failures and charge the batteries. Days of this project were spent deriving
/// by probe what was written down in the package the whole time.
///
/// It was also the fix for a quieter fault. The old checklist window read a hand-written text
/// file per aircraft and fell back to the A320's when it had none — so on the DA40 it showed an
/// AIRBUS checklist. Reading the aeroplane's own is both more correct and less to maintain:
/// nothing to transcribe, and it follows the aircraft through updates. The DA40's First Officer
/// builds its read-back checklists and its reference pages from it (<see cref="LoadPages"/>).
///
/// THE SHAPE OF THE FILE, which is Asobo's rather than ours:
/// <code>
///   &lt;Step ChecklistStepId="PREFLIGHT_GATE"&gt;
///     &lt;Page SubjectTT="Before engine start"&gt;
///       &lt;Checkpoint&gt;
///         &lt;CheckpointDesc SubjectTT="Parking Brake" ExpectationTT="Set"/&gt;
///         &lt;Clue Name="Forwards = go, Backwards = stop"/&gt;
/// </code>
/// A Page becomes a category, a Checkpoint becomes "Subject … Expectation", and a Clue
/// becomes its own line underneath — because a clue is frequently the only place a real
/// operating detail is written down, and dropping it would throw away the best part.
/// </summary>
public static class NativeChecklistReader
{
    /// <summary>
    /// Every folder an aircraft package may sit in, Community before Official: the packages
    /// root each simulator's UserCfg.opt names, then the default Community folders of both
    /// simulators and the Store layout, which cover a config that is missing or unreadable.
    ///
    /// ⚠️ THE DEFAULTS ALONE MISSED EVERY PILOT WHO CHOSE WHERE PACKAGES GO — the simulator
    /// asks on first launch, so that is common — and the checklist window then said the
    /// aeroplane ships no checklist. The root is read the way the scenery census reads it:
    /// the ACTIVE key only, never <c>InstalledPackagesPathNextBoot</c>, because the
    /// simulator writes that line the moment a new folder is picked, before anything has
    /// moved there. Official holds a Marketplace copy (Official\OneStore, Official\Steam)
    /// and comes last: a pilot's own Community copy wins, and the scan of a large Official
    /// folder is paid only when nothing was found before it.
    /// </summary>
    internal static IEnumerable<string> PackageRoots(string roamingAppData, string localAppData)
    {
        var configured = new List<string>();
        foreach (string sim in new[] { "FS2024", "FS2020" })
        {
            string? root = MsfsPackagesLocator.TryGetInstalledPackagesPath(
                sim, roamingAppData, localAppData, includeNextBoot: false, out _);
            if (root != null) configured.Add(root);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in configured)
        {
            string community = Path.Combine(root, "Community");
            if (seen.Add(community)) yield return community;
        }

        foreach (string community in DefaultCommunityRoots(roamingAppData, localAppData))
            if (seen.Add(community)) yield return community;

        foreach (string root in configured)
            foreach (string official in SafeDirectories(Path.Combine(root, "Official")))
                if (seen.Add(official)) yield return official;
    }

    /// <summary>Where each simulator keeps Community when nobody chose otherwise.</summary>
    private static IEnumerable<string> DefaultCommunityRoots(string roamingAppData, string localAppData)
    {
        yield return Path.Combine(localAppData, "Packages",
            "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community");
        yield return Path.Combine(localAppData, "Packages",
            "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Community");
        yield return Path.Combine(roamingAppData, "Microsoft Flight Simulator 2024", "Packages", "Community");
        yield return Path.Combine(roamingAppData, "Microsoft Flight Simulator", "Packages", "Community");
    }

    /// <summary>
    /// Finds the checklist file for a SimObject folder — "COWS_DA40NG" and the like.
    ///
    /// Matched on the folder name rather than on the package name, because a package can
    /// hold several aeroplanes (this one holds two) with a checklist each, and the two are
    /// genuinely different documents.
    /// </summary>
    public static string? FindChecklistFile(string simObjectFolder)
        => FindChecklistFile(simObjectFolder,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Test seam: the two roots Windows would otherwise supply.</summary>
    internal static string? FindChecklistFile(string simObjectFolder, string roamingAppData, string localAppData)
    {
        foreach (string folder in PackageRoots(roamingAppData, localAppData))
        {
            if (!Directory.Exists(folder)) continue;

            foreach (string package in SafeDirectories(folder))
            {
                string dir = Path.Combine(package, "SimObjects", "Airplanes", simObjectFolder, "Checklist");
                if (!Directory.Exists(dir)) continue;

                // Sorted, so a package carrying more than one file reads the same one every time.
                string? file = SafeFiles(dir, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (file != null) return file;
            }
        }

        return null;
    }

    /// <summary>
    /// The checklist as text — "[Category]" followed by its lines — or null when the aircraft
    /// ships none, which is most of them.
    /// </summary>
    public static string? Render(string simObjectFolder)
    {
        string? path = FindChecklistFile(simObjectFolder);
        if (path == null) return null;

        try
        {
            return RenderFile(XDocument.Load(path));
        }
        catch (Exception ex)
        {
            // A malformed checklist must not take its reader down with it.
            Log.Debug("Checklist", $"Could not read {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The checklist as PAGES, for a reader that needs the structure rather than the text: the
    /// DA40's First Officer builds its read-back checklists from it. Null when the aircraft
    /// ships none or the file cannot be read.
    /// </summary>
    public static IReadOnlyList<NativeChecklistPage>? LoadPages(string simObjectFolder)
    {
        string? path = FindChecklistFile(simObjectFolder);
        if (path == null) return null;

        try
        {
            return ReadPages(XDocument.Load(path));
        }
        catch (Exception ex)
        {
            Log.Debug("Checklist", $"Could not read {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Exposed so the tests can render a document without touching the disk.</summary>
    public static string RenderFile(XDocument doc)
    {
        var sb = new StringBuilder();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in ReadPages(doc))
        {
            // Two pages can share a name across steps (a "Notes" page in more than one
            // phase); a category dictionary downstream keyed by name would silently swallow
            // the first one's contents.
            string unique = page.Title;
            for (int n = 2; !used.Add(unique); n++) unique = page.Title + " " + n;

            sb.Append('[').Append(unique).Append(']').Append('\n');
            foreach (var line in page.Lines)
            {
                if (line.IsHeading)
                {
                    sb.Append(line.Subject).Append('\n');
                    continue;
                }

                if (line.Subject.Length > 0)
                    sb.Append(line.Expectation.Length == 0 ? line.Subject : line.Subject + " ... " + line.Expectation)
                      .Append('\n');

                foreach (string clue in line.Clues) sb.Append("   ").Append(clue).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Every page of the document in order, its lines in the author's order: a block's heading,
    /// then the block's checkpoints. A page with no lines is left out.
    /// </summary>
    public static IReadOnlyList<NativeChecklistPage> ReadPages(XDocument doc)
    {
        var pages = new List<NativeChecklistPage>();

        foreach (var page in doc.Descendants("Page"))
        {
            string title = Attr(page, "SubjectTT");
            if (title.Length == 0) title = "Checklist";

            // The step a page belongs to (PREFLIGHT_GATE, FLIGHT_CRUISE ...): Asobo's phase.
            string step = page.Ancestors("Step").FirstOrDefault() is { } s ? Attr(s, "ChecklistStepId") : "";

            var lines = new List<NativeChecklistLine>();

            // ⚠️ A CHECKPOINT CAN SIT INSIDE A <Block>, AND WALKING ONLY THE PAGE'S DIRECT
            // CHILDREN THREW AWAY NEARLY HALF THE DOCUMENT. Measured against the two files
            // this reader was written for: the DA40-XLS has 220 checkpoints of which only
            // 115 are direct children — the other 105 are the cruise power table (four
            // blocks, 51 rows of manifold pressure and fuel flow by altitude), the three
            // start procedures (cold, flooded/hot, reprime), the weights, the stall and
            // operating speeds, the climb figures, and the whole Starting tips and Leaning
            // sets. The NG loses 29 of 137 the same way, including its own weights, speeds
            // and the High Altitude operations notes.
            //
            // Blocks are ONE LEVEL DEEP in both files and each carries its own SubjectTT,
            // which is a real heading ("Cruise:65%, 9.5gph (Best Power)") and reads as one.
            // The walk is over the page's children IN ORDER, so a page that mixes loose
            // checkpoints with blocks keeps the author's sequence.
            ReadChildren(page, null, lines);

            if (lines.Count == 0) continue;
            pages.Add(new NativeChecklistPage(title, step.Length == 0 ? null : step, lines));
        }

        return pages;
    }

    /// <summary>
    /// A page's or a block's children, in the author's own order: a block gives its heading
    /// line and then its own children, a checkpoint gives one line.
    ///
    /// Recursive rather than a Descendants("Checkpoint") sweep, which would find the same
    /// checkpoints but lose a nested block's heading. Both shipped files are one level
    /// deep today, so this costs nothing and is what keeps a deeper one readable.
    /// </summary>
    private static void ReadChildren(XElement parent, string? block, List<NativeChecklistLine> lines)
    {
        foreach (var element in parent.Elements())
        {
            if (element.Name == "Block")
            {
                string blockTitle = Attr(element, "SubjectTT");
                if (blockTitle.Length > 0)
                    lines.Add(new NativeChecklistLine(blockTitle, "", Array.Empty<string>(),
                        Array.Empty<string>(), block, IsNote: false, IsHeading: true));
                ReadChildren(element, blockTitle.Length > 0 ? blockTitle : block, lines);
                continue;
            }

            if (element.Name == "Checkpoint") ReadCheckpoint(element, block, lines);
        }
    }

    /// <summary>One checkpoint: its subject, its expectation, its clue lines and the cockpit
    /// instruments it highlights.</summary>
    private static void ReadCheckpoint(XElement checkpoint, string? block, List<NativeChecklistLine> lines)
    {
        var desc = checkpoint.Element("CheckpointDesc");
        string subject = desc != null ? Attr(desc, "SubjectTT") : "";
        string expect = desc != null ? Attr(desc, "ExpectationTT") : "";

        // "Clue" is Asobo's word for the expectation column on a NOTE row, so a checkpoint
        // whose expectation is literally "Clue" is a note and its subject is the heading
        // for the text that follows.
        bool isNote = string.Equals(expect, "Clue", StringComparison.OrdinalIgnoreCase);

        var clues = checkpoint.Elements("Clue").Select(c => Attr(c, "Name")).Where(t => t.Length > 0).ToList();
        var instruments = checkpoint.Descendants("Instrument").Select(i => Attr(i, "Id"))
            .Where(t => t.Length > 0).ToList();

        if (subject.Length == 0 && clues.Count == 0) return;
        lines.Add(new NativeChecklistLine(subject, isNote ? "" : expect, clues, instruments, block,
            isNote, IsHeading: false));
    }

    private static string Attr(XElement e, string name) => e.Attribute(name)?.Value.Trim() ?? "";

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try { return Directory.EnumerateDirectories(root); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string root, string pattern)
    {
        try { return Directory.EnumerateFiles(root, pattern); }
        catch (Exception) { return Array.Empty<string>(); }
    }
}

/// <summary>One page of an aircraft's own checklist: Asobo's <c>Page</c>, in its <c>Step</c>.</summary>
public sealed record NativeChecklistPage(string Title, string? StepId, IReadOnlyList<NativeChecklistLine> Lines);

/// <summary>
/// One line of a native checklist page: a checkpoint (subject, expectation, clue lines, the
/// cockpit instrument ids it highlights) or a block's heading. A note row has no expectation.
/// </summary>
public sealed record NativeChecklistLine(
    string Subject,
    string Expectation,
    IReadOnlyList<string> Clues,
    IReadOnlyList<string> Instruments,
    string? Block,
    bool IsNote,
    bool IsHeading);

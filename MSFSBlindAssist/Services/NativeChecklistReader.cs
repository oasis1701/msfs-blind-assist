using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
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
/// It is also the fix for a quieter fault. <see cref="Forms.ChecklistForm"/> reads a
/// hand-written text file per aircraft and falls back to the A320's when it has none — so on
/// the DA40 the checklist window was showing an AIRBUS checklist. Reading the aeroplane's own
/// is both more correct and less to maintain: nothing to transcribe, and it follows the
/// aircraft through updates.
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
    /// Where MSFS keeps Community packages. Both simulators, and the Store layout, because
    /// which one a pilot has is not something MSFSBA gets to choose.
    /// </summary>
    private static IEnumerable<string> CommunityRoots()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        yield return Path.Combine(local, "Packages",
            "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community");
        yield return Path.Combine(local, "Packages",
            "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Community");
        yield return Path.Combine(roaming, "Microsoft Flight Simulator 2024", "Packages", "Community");
        yield return Path.Combine(roaming, "Microsoft Flight Simulator", "Packages", "Community");
    }

    /// <summary>
    /// Finds the checklist file for a SimObject folder — "COWS_DA40NG" and the like.
    ///
    /// Matched on the folder name rather than on the package name, because a package can
    /// hold several aeroplanes (this one holds two) with a checklist each, and the two are
    /// genuinely different documents.
    /// </summary>
    public static string? FindChecklistFile(string simObjectFolder)
    {
        foreach (string community in CommunityRoots())
        {
            if (!Directory.Exists(community)) continue;

            foreach (string package in SafeDirectories(community))
            {
                string dir = Path.Combine(package, "SimObjects", "Airplanes", simObjectFolder, "Checklist");
                if (!Directory.Exists(dir)) continue;

                string? file = SafeFiles(dir, "*.xml").FirstOrDefault();
                if (file != null) return file;
            }
        }

        return null;
    }

    /// <summary>
    /// The checklist as <see cref="Forms.ChecklistForm"/>'s own text format — "[Category]"
    /// followed by its lines — or null when the aircraft ships none, which is most of them.
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
            // A malformed checklist must not take the window down with it — the bundled
            // text file is still there to fall back on.
            Log.Debug("Checklist", $"Could not read {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Exposed so the tests can render a document without touching the disk.</summary>
    public static string RenderFile(XDocument doc)
    {
        var sb = new StringBuilder();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in doc.Descendants("Page"))
        {
            string title = Attr(page, "SubjectTT");
            if (title.Length == 0) title = "Checklist";

            // Two pages can share a name across steps (a "Notes" page in more than one
            // phase); the category dictionary downstream is keyed by name, so a duplicate
            // would silently swallow the first one's contents.
            string unique = title;
            for (int n = 2; !used.Add(unique); n++) unique = title + " " + n;

            var lines = new List<string>();

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
            // which is a real heading ("Cruise:65%, 9.5gph (Best Power)") and renders as
            // one. The walk is over the page's children IN ORDER, so a page that mixes
            // loose checkpoints with blocks keeps the author's sequence.
            RenderChildren(page, lines);

            if (lines.Count == 0) continue;

            sb.Append('[').Append(unique).Append(']').Append('\n');
            foreach (string line in lines) sb.Append(line).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// A page's or a block's children, in the author's own order: a block renders its
    /// SubjectTT as a heading and then its own children, a checkpoint renders itself.
    ///
    /// Recursive rather than a Descendants("Checkpoint") sweep, which would find the same
    /// checkpoints but lose a nested block's heading. Both shipped files are one level
    /// deep today, so this costs nothing and is what keeps a deeper one readable.
    /// </summary>
    private static void RenderChildren(XElement parent, List<string> lines)
    {
        foreach (var element in parent.Elements())
        {
            if (element.Name == "Block")
            {
                string blockTitle = Attr(element, "SubjectTT");
                if (blockTitle.Length > 0) lines.Add(blockTitle);
                RenderChildren(element, lines);
                continue;
            }

            if (element.Name == "Checkpoint") RenderCheckpoint(element, lines);
        }
    }

    /// <summary>One checkpoint: its subject, its expectation, and any clue lines under it.</summary>
    private static void RenderCheckpoint(XElement checkpoint, List<string> lines)
    {
        var desc = checkpoint.Element("CheckpointDesc");
        string subject = desc != null ? Attr(desc, "SubjectTT") : "";
        string expect = desc != null ? Attr(desc, "ExpectationTT") : "";

        // "Clue" is Asobo's word for the expectation column on a NOTE row, so a checkpoint
        // whose expectation is literally "Clue" is a note and its subject is the heading
        // for the text that follows.
        bool isNote = string.Equals(expect, "Clue", StringComparison.OrdinalIgnoreCase);

        if (subject.Length > 0)
        {
            lines.Add(isNote || expect.Length == 0 ? subject : subject + " ... " + expect);
        }

        foreach (var clue in checkpoint.Elements("Clue"))
        {
            string text = Attr(clue, "Name");
            if (text.Length > 0) lines.Add("   " + text);
        }
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

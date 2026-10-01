using System;
using System.Collections.Generic;
using System.IO;

namespace MSFSBlindAssist.Services;

/// <summary>One heading of the checklist window and the lines under it, in file order.</summary>
public sealed record ChecklistSection(string Title, IReadOnlyList<string> Items);

/// <summary>
/// What the checklist window (output mode, Shift+C) shows, kept out of the form so it can be
/// tested: where the text comes from, how it splits into sections, and how a ticked item is
/// remembered.
///
/// THE ORDER IS THE AIRCRAFT'S OWN CHECKLIST FIRST (<see cref="NativeChecklistReader"/>), then
/// the text file MSFSBA ships for that aircraft, then a plain "there is none". The aircraft's
/// own is the vendor's, complete, and follows the aeroplane through updates; the bundled file
/// is what a pilot gets when the package cannot be found, or ships none.
///
/// ⚠️ NO FALLBACK TO ANOTHER AIRCRAFT'S FILE. An aeroplane with no checklist of its own used
/// to be shown the A320's — a wrong checklist that looks authoritative is worse than an empty
/// window.
/// </summary>
public static class ChecklistContent
{
    /// <summary>Aircraft code to the file under <c>Checklists\</c> next to the exe. Each one
    /// also needs its own copy entry in MSFSBlindAssist.csproj — there is no wildcard.</summary>
    public static IReadOnlyDictionary<string, string> BundledFiles { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["A320"] = "FBW_A320_Checklist.txt",
            ["HW_A330"] = "FBW_A330_Checklist.txt",
            ["FENIX_A320CEO"] = "Fenix_A320_Checklist.txt",
            ["FBW_A380"] = "FBW_A380_Checklist.txt",
            ["IFLY_737MAX8"] = "iFly_737MAX8_Checklist.txt",
            ["COWS_DA40NG"] = "COWS_DA40NG_Checklist.txt",
            ["COWS_DA40XLS"] = "COWS_DA40XLS_Checklist.txt",
        };

    /// <summary>Shown when the aircraft ships no checklist and MSFSBA carries none.</summary>
    public const string NoChecklistText =
        "[No checklist for this aircraft]\n" +
        "This aeroplane ships no checklist of its own and MSFSBA carries none " +
        "for it. Use the aircraft's own documentation.";

    /// <summary>The bundled file name for an aircraft, or null when MSFSBA carries none.</summary>
    public static string? BundledFileFor(string? aircraftCode)
        => aircraftCode != null && BundledFiles.TryGetValue(aircraftCode, out string? file) ? file : null;

    /// <summary>
    /// The checklist text for an aircraft. <paramref name="nativeReader"/> is
    /// <see cref="NativeChecklistReader.Render"/> in the app; <paramref name="checklistFolder"/>
    /// is the <c>Checklists</c> folder next to the exe.
    /// </summary>
    public static string Load(string aircraftCode, Func<string, string?> nativeReader, string checklistFolder)
    {
        string? native = nativeReader(aircraftCode);
        if (!string.IsNullOrWhiteSpace(native)) return native;

        string? file = BundledFileFor(aircraftCode);
        if (file == null) return NoChecklistText;

        string path = Path.Combine(checklistFolder, file);
        try
        {
            return File.Exists(path)
                ? File.ReadAllText(path)
                : $"[Error]\nChecklist file not found: {path}";
        }
        catch (Exception ex)
        {
            return $"[Error]\nError loading checklist: {ex.Message}";
        }
    }

    /// <summary>
    /// Splits "[Heading]" lines and the item lines under them, in order. Blank lines and
    /// lines before the first heading are dropped, and every line is trimmed.
    ///
    /// A heading used twice keeps BOTH sections, the second numbered ("Notes 2") the way
    /// <see cref="NativeChecklistReader"/> numbers a repeated page — the old parser started
    /// the heading's list again, throwing away everything already under it.
    /// </summary>
    public static IReadOnlyList<ChecklistSection> Parse(string text)
    {
        var sections = new List<ChecklistSection>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;

        foreach (string raw in text.Split('\r', '\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.Length > 1 && line[0] == '[' && line[^1] == ']')
            {
                string title = line[1..^1];
                string unique = title;
                for (int n = 2; !used.Add(unique); n++) unique = title + " " + n;

                current = new List<string>();
                sections.Add(new ChecklistSection(unique, current));
            }
            else
            {
                current?.Add(line);
            }
        }

        return sections;
    }

    /// <summary>
    /// The key a ticked item is remembered under for the session. Carries the aircraft,
    /// because two aircraft's checklists can share a heading and an item word for word (both
    /// DA40s open with the same preflight line) and a tick belongs to one of them.
    /// </summary>
    public static string ItemKey(string aircraftCode, string category, string item)
        => $"{aircraftCode}|{category}|{item}";
}

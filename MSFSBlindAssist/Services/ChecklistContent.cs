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
/// the text file MSFSBA ships for that aircraft (the definition names it). The
/// aircraft's own is the vendor's, complete, and follows the aeroplane through updates; the
/// bundled file is what a pilot gets when the package cannot be found, or ships none.
/// </summary>
public static class ChecklistContent
{
    /// <summary>
    /// The checklist text for an aircraft. <paramref name="checklistFileName"/> is the aircraft's
    /// own <c>IAircraftDefinition.ChecklistFileName</c> — main (#266) moved the file map onto the
    /// definitions, and an aircraft naming none opens no window at all, so there is no fallback
    /// here. <paramref name="nativeReader"/> is <see cref="NativeChecklistReader.Render"/> in the
    /// app; <paramref name="checklistFolder"/> is the <c>Checklists</c> folder next to the exe.
    /// </summary>
    public static string Load(string aircraftCode, string checklistFileName,
        Func<string, string?> nativeReader, string checklistFolder)
    {
        string? native = nativeReader(aircraftCode);
        if (!string.IsNullOrWhiteSpace(native)) return native;

        string path = Path.Combine(checklistFolder, checklistFileName);
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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ AN ANNOUNCEMENT REPORTS THE AEROPLANE. IT NEVER TELLS THE PILOT WHAT TO DO.
///
/// The pilot's ruling, and it is easy to breach by accident because the coaching sentence is
/// always the helpful-sounding one. Two shipped: "Turn the engine master off first" and
/// "Break the safety wire first", both attached to genuine refusals where the REASON was
/// welcome and the REMEDY was not.
///
/// ⚠️ THE SANCTIONED SHAPE OF A REFUSAL IS THE BLOCKING CONDITION, NOT THE FIX - "Will not
/// open at 34 knots. The limit is 30." A refused action must say WHY, so a pilot is never
/// left with a key that silently did nothing; it does not have to say what to press next.
///
/// This scans the DA40 sources for spoken literals rather than testing behaviour, because
/// the failure is in the WORDS and a behavioural test cannot see them. Comments are stripped
/// first - the reasoning around a message is allowed to say anything.
/// </summary>
public class CowsDA40MessageAuditTests
{
    private static readonly string[] CoachingPhrases =
    {
        "you should", "you must", "you need to", "you have to",
        "do not ", "don't ", "make sure", "be careful", "remember to",
        " first.", " first,", "instead of pressing", "try again",
    };

    private static IEnumerable<string> Sources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate the repository root.");
        return Directory.EnumerateFiles(
            Path.Combine(dir!.FullName, "MSFSBlindAssist", "Aircraft", "DA40"), "*.cs");
    }

    private static string StripComments(string src)
    {
        src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(src, @"//[^\n]*", "");
    }

    [Fact]
    public void NoAnnouncementTellsThePilotWhatToDo()
    {
        var offenders = new List<string>();

        foreach (string file in Sources())
        {
            string src = StripComments(File.ReadAllText(file));
            foreach (Match m in Regex.Matches(src, @"Announce(?:Immediate)?\(\s*\$?""([^""]*)"""))
            {
                string text = m.Groups[1].Value;
                foreach (string phrase in CoachingPhrases)
                    if (text.Contains(phrase, System.StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{Path.GetFileName(file)}: \"{text}\"");
                        break;
                    }
            }
        }

        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "audit_announce.txt"), offenders.Distinct());
        Assert.True(offenders.Count == 0,
            "These announcements instruct the pilot rather than reporting the aeroplane. A " +
            "refusal may name the blocking CONDITION; it must not name the remedy: " +
            string.Join(" | ", offenders.Distinct()));
    }

    [Fact]
    public void StateLabelsStayShortEnoughToHearTwentyTimes()
    {
        // ⚠️ A ValueDescriptions label is read every time the row is scanned, so a label
        // that explains prerequisites becomes a sentence a pilot hears on every pass. The
        // explanation belongs in docs/da40.md; the panel names the state and stops.
        var offenders = new List<string>();

        foreach (string file in Sources())
        {
            string src = StripComments(File.ReadAllText(file));
            foreach (Match m in Regex.Matches(src, @"\[\s*-?[\d.]+\s*\]\s*=\s*""([^""]*)"""))
            {
                string label = m.Groups[1].Value;
                if (label.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length > 6)
                    offenders.Add($"{Path.GetFileName(file)}: \"{label}\"");
            }
        }

        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "audit_labels.txt"), offenders.Distinct());
        Assert.True(offenders.Count == 0,
            "These state labels are long enough to be tiring on a scan; the detail belongs " +
            "in docs/da40.md: " + string.Join(" | ", offenders.Distinct()));
    }
}

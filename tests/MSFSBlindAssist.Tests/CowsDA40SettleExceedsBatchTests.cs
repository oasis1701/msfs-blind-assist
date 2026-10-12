using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ EVERY SETTLE TIMER MUST OUTLAST ONE CONTINUOUS-BATCH PERIOD, AND THREE DID NOT.
///
/// The batch samples at 1 Hz. A knob swept for several seconds therefore delivers a NEW value
/// once a second - so any settle shorter than that expires BETWEEN deliveries and announces
/// each intermediate value the knob passed through, instead of the one it stopped on.
///
/// It was found three times over before it was understood as one rule:
///   RadioSettleMs 300  - "it announces 700, 800" while tuning (those were real readings,
///                        a second apart, mid-sweep)
///   BaroSettleMs  300  - the same thing on the altimeter subscale
///   PowerSettleMs 900  - a bus still rising when the timer expired, read mid-climb, and the
///                        settled voltage never spoken at all
///
/// Just over the period is the whole trick: while the value is moving each delivery restarts
/// the timer, so nothing speaks; when it stops, no further delivery arrives and the timer runs
/// out on the resting value.
///
/// ⚠️ It COSTS LAG and nothing at 1 Hz avoids that - worst case is one batch period to notice
/// the last change plus the settle. Shortening one does not buy speed, it buys the intermediate
/// announcements back. The cure for the lag is a faster SAMPLE, which is a data-definition
/// budget question and not a tuning one.
///
/// ⚠️ THE RULE IS "OUTLAST THE SAMPLE", NOT "EXCEED ONE SECOND". A value moved onto a per-var
/// SIM_FRAME subscription (ExcludeFromBatch + HighFrequency, as the radio frequencies and both
/// altimeter subscales now are) arrives within a FRAME, so its settle should be SHORT - holding
/// it at 1200 ms would keep the very lag that made tuning feel a beat behind. Such a constant
/// carries a FAST-SAMPLED marker beside it, so the exemption is taken deliberately and sits
/// next to the reasoning rather than in this test.
///
/// ⚠️ HOLD durations are a different thing entirely and are deliberately not covered: a
/// momentary control needs a REPEATING write for a fixed duration (ECU test 26 s, fuel wire
/// 1.5 s, gyro cage 700 ms), which has nothing to do with when a value is read back.
/// </summary>
public class CowsDA40SettleExceedsBatchTests
{
    /// <summary>The continuous batch runs at PERIOD.SECOND.</summary>
    private const int BatchPeriodMs = 1000;

    private static string DefinitionFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate the repository root.");
        return Path.Combine(dir!.FullName, "MSFSBlindAssist", "Aircraft", "DA40");
    }

    [Fact]
    public void EverySettleConstantOutlastsOneBatchPeriod()
    {
        string folder = DefinitionFolder();
        Assert.True(Directory.Exists(folder), $"DA40 source folder not found at {folder}");

        // Matches "private const int XxxSettleMs = 1200;" - the SETTLE family only. HoldMs and
        // GraceMs are deliberately excluded; see the class comment.
        var rx = new Regex(@"const\s+int\s+(\w*SettleMs)\s*=\s*(\d+)\s*;", RegexOptions.Compiled);

        var offenders = new System.Collections.Generic.List<string>();
        int found = 0;

        foreach (string file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            string src = File.ReadAllText(file);
            foreach (Match m in rx.Matches(src))
            {
                found++;
                int ms = int.Parse(m.Groups[2].Value);
                if (ms > BatchPeriodMs) continue;

                // ⚠️ THE RULE IS "OUTLAST THE SAMPLE", NOT "EXCEED ONE SECOND". A variable
                // moved onto a per-var SIM_FRAME subscription (ExcludeFromBatch +
                // HighFrequency) is delivered within a frame, so its settle SHOULD be short -
                // forcing 1200 ms on it would reintroduce exactly the lag that made tuning
                // feel a beat behind. The opt-out is a marker in the 40 lines above the
                // constant, so it can only be taken deliberately and next to the reasoning.
                int at = m.Index;
                int from = Math.Max(0, at - 2200);
                if (src.Substring(from, at - from).Contains("FAST-SAMPLED")) continue;

                offenders.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value} = {ms}");
            }
        }

        Assert.True(found > 0, "No settle constants found - has the naming convention changed?");
        Assert.True(offenders.Count == 0,
            "These settle timers are shorter than one 1 Hz batch period, so they will announce " +
            "intermediate values instead of the resting one: " + string.Join("; ", offenders));
    }

    [Fact]
    public void AnOwnWriteGraceOutlastsTheSettleItProtects()
    {
        // A grace shorter than its settle cannot suppress the echo it exists for: the settle
        // fires after the grace has already lapsed, and MSFSBA announces its own write back.
        string folder = DefinitionFolder();
        string radio = File.ReadAllText(Path.Combine(folder, "CowsDA40Definition.RadioAnnounce.cs"));
        string baro = File.ReadAllText(Path.Combine(folder, "CowsDA40Definition.BaroAnnounce.cs"));

        int Read(string src, string name)
        {
            var m = Regex.Match(src, name + @"\s*=\s*(\d+)\s*;");
            Assert.True(m.Success, $"{name} not found");
            return int.Parse(m.Groups[1].Value);
        }

        Assert.True(Read(radio, "RadioOwnWriteGraceMs") > Read(radio, "RadioSettleMs"),
            "The radio grace must outlast the radio settle");
        Assert.True(Read(baro, "OwnWriteGraceMs") > Read(baro, "BaroSettleMs"),
            "The baro grace must outlast the baro settle");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A hotkey can only read what is in the CACHE, and this derives the list from the code
/// instead of trusting somebody to keep one by hand.
///
/// ⚠️ THE FAULT THIS EXISTS FOR, twice now. Batch membership is Continuous AND IsAnnounced
/// AND not ExcludeFromBatch; an OnRequest variable is never polled, so
/// GetCachedVariableValue returns null and the key answers "not available yet" for ever.
/// The first time it was B, F and W. The second time it was Alt+S and Shift+F, shipped
/// against thirteen OnRequest variables — so the engine-at-a-glance key reported nothing
/// available while the engine was running, and the fuel key reported a flow of zero out of
/// a burning engine.
///
/// The older test listed the keys to check by hand, which is why it passed through the
/// second one untouched. This one SCANS the hotkey source for every cache read, so a key
/// added tomorrow is covered without anybody remembering to add it here.
/// </summary>
/// ⚠️ CORRECTION: "CACHED" IS NOT THE SAME AS "BATCH-COVERED", AND THIS FILE USED TO CONFLATE
/// THEM. The predicate below was Continuous AND IsAnnounced AND NOT ExcludeFromBatch, which is
/// the test for riding the shared 1 Hz BATCH. The CACHE is wider: SetupDataDefinitions gives an
/// ExcludeFromBatch var its OWN periodic subscription, and its deliveries land in
/// ProcessIndividualVariableResponse, which writes lastVariableValues exactly as the batch path
/// does. So a per-var subscription caches too - verified in that method, not inferred.
///
/// It matters because the fast radio and altimeter subscales are ExcludeFromBatch ON PURPOSE:
/// that is what earns them a SIM_FRAME subscription instead of a 1 Hz sample. Under the old
/// predicate every one of them read as "not cached" and this test failed on a change that was
/// correct.
public class CowsDA40HotkeyCacheTests
{
    private static string HotkeySource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, "MSFSBlindAssist", "Aircraft", "DA40",
            "CowsDA40Definition.Hotkeys.cs");
        Assert.True(File.Exists(path), "Not found: " + path);
        return File.ReadAllText(path);
    }

    /// <summary>Every variable key the hotkeys read out of the cache, found in the source.</summary>
    private static List<string> KeysReadFromCache()
    {
        string src = HotkeySource();

        // Both roads into the cache: ReadNow(simConnect, "KEY") and the Add(...) helper,
        // which reads through ReadNow itself.
        var keys = Regex.Matches(src, @"ReadNow\(simConnect,\s*""([A-Z0-9_]+)""\)")
            .Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(src, @"Add\(bits,\s*simConnect,\s*""([A-Z0-9_]+)""")
                .Select(m => m.Groups[1].Value))
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(keys.Count > 10, "The scan found almost nothing — the pattern has drifted.");
        return keys;
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryKeyAHotkeyReadsIsActuallyCached(DA40Variant variant)
    {
        var vars = new CowsDA40Definition(variant).GetVariables();
        var broken = new List<string>();

        foreach (string key in KeysReadFromCache())
        {
            // A key that only exists on one airframe is correct, not a fault: the NG's
            // power lever and the XLS's magnetos are each absent from the other.
            if (!vars.TryGetValue(key, out var v)) continue;

            if (v.UpdateFrequency != UpdateFrequency.Continuous || !v.IsAnnounced)
            {
                broken.Add($"{key} ({v.UpdateFrequency}, announced={v.IsAnnounced}, " +
                           $"excluded={v.ExcludeFromBatch})");
            }
        }

        Assert.True(broken.Count == 0,
            $"{variant}: these are read from the cache by a hotkey but never reach it, so " +
            "that key answers \"not available yet\" for ever — " + string.Join("; ", broken));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoTwoBatchedVariablesShareOneSimVarName(DA40Variant variant)
    {
        // ⚠️ The continuous batch SORTS BY NAME, so two batched keys on one SimVar shift
        // every later variable's struct slot and quietly corrupt the whole read. Four of
        // the engine readouts have OnRequest twins on the same SimVar and two more (standby
        // airspeed and altitude) share theirs with variables that were already batched —
        // promoting either copy without checking would have broken far more than it fixed.
        var vars = new CowsDA40Definition(variant).GetVariables();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var clashes = new List<string>();

        foreach (var (key, v) in vars)
        {
            if (v.UpdateFrequency != UpdateFrequency.Continuous || !v.IsAnnounced)
            {
                continue;
            }

            if (seen.TryGetValue(v.Name, out string? first))
            {
                clashes.Add($"{v.Name} is batched as both {first} and {key}");
            }
            else
            {
                seen[v.Name] = key;
            }
        }

        Assert.True(clashes.Count == 0, $"{variant}: " + string.Join("; ", clashes));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPromotedReadoutIsSilentAndOutOfTheMonitorManager(DA40Variant variant)
    {
        // They carry IsAnnounced only to earn the batch place. A number that moves several
        // times a second and is spoken every time is unusable, and a Monitor Manager row
        // for something that was never going to speak is a row that lies.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (string key in CowsDA40Definition.HotkeyCachedReadoutKeys)
        {
            if (!vars.TryGetValue(key, out var v)) continue;

            Assert.True(v.ExcludeFromMonitorManager,
                key + " is a silent readout but still offers a Monitor Manager row.");
        }
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryFailureEitherSpeaksOrIsDeliberatelyGraded(DA40Variant variant)
    {
        // ⚠️ A FAILURE THAT CANNOT SPEAK IS THE WORST KIND OF SILENCE. Every DA40_FAIL_*
        // variable must either be an announced flag — which is how eighty-five of them
        // reach the pilot — or a graded percentage handled by NoteGradedFailure, or a reset
        // BUTTON with no state to announce. Anything else is a failure that happens quietly.
        var vars = new CowsDA40Definition(variant).GetVariables();
        var silent = new List<string>();

        foreach (var (key, v) in vars)
        {
            if (!key.StartsWith("DA40_FAIL", StringComparison.Ordinal)) continue;

            // A button: UpdateFrequency.Never, nothing to announce.
            if (v.UpdateFrequency == UpdateFrequency.Never) continue;

            if (CowsDA40Definition.GradedFailureKeys.Contains(key))
            {
                // Graded ones must still be POLLED, or the onset is never seen.
                Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
                continue;
            }

            if (!v.IsAnnounced) silent.Add(key);
        }

        Assert.True(silent.Count == 0,
            $"{variant}: these failures would happen in silence — " + string.Join(", ", silent));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryAlarmStateActuallyAnnounces(DA40Variant variant)
    {
        // These are the described states a sighted pilot is interrupted by — the engine
        // stopping, an ECU fault latching, the ECU test's result, the transfer pump
        // stopping itself. Each was silent, sitting in a panel scan, until it was listed.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (string key in CowsDA40Definition.AlarmStateKeys)
        {
            // A key absent on one airframe is correct, not a fault: the ECU states are
            // the Austro's FADEC and the XLS's Lycoming has none.
            if (!vars.TryGetValue(key, out var v)) continue;

            Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced, key + " would go back to being silent");
            Assert.False(v.ExcludeFromBatch, key + " is excluded from the batch");

            // Silencing one of these anywhere would undo the whole point of the list.
            Assert.DoesNotContain(key, CowsDA40Definition.SilentCachedReadoutKeys);
        }
    }

    [Fact]
    public void EveryEngineHealthReadingIsPolled()
    {
        // ⚠️ The aeroplane accumulates damage and it SURVIVES A RELOAD, so a health reading
        // that never reaches the cache means a pilot can fly a damaged engine and never
        // find out — the same trap the state-saving system sprang with flat batteries.
        var vars = new CowsDA40Definition(DA40Variant.NG).GetVariables();

        foreach (string key in CowsDA40Definition.HealthKeyNames)
        {
            Assert.True(vars.ContainsKey(key), key + " no longer exists");

            var v = vars[key];
            Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced, key + " would never be polled");
            Assert.False(v.ExcludeFromBatch);

            // The model publishes 0 to 1 and a pilot thinks in percent.
            Assert.Equal(100, v.Scale);
        }
    }

    [Fact]
    public void EveryAlarmStateExistsOnAtLeastOneAirframe()
    {
        var known = new CowsDA40Definition(DA40Variant.NG).GetVariables().Keys
            .Concat(new CowsDA40Definition(DA40Variant.XLS).GetVariables().Keys)
            .ToHashSet(StringComparer.Ordinal);

        var missing = CowsDA40Definition.AlarmStateKeys.Where(k => !known.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "alarm states that do not exist — " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryGradedFailureExists()
    {
        var known = new CowsDA40Definition(DA40Variant.NG).GetVariables().Keys
            .Concat(new CowsDA40Definition(DA40Variant.XLS).GetVariables().Keys)
            .ToHashSet(StringComparer.Ordinal);

        var missing = CowsDA40Definition.GradedFailureKeys.Where(k => !known.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "graded failures that do not exist — " + string.Join(", ", missing));
    }

    [Fact]
    public void ThePromotionListDoesNotNameAVariableThatDoesNotExist()
    {
        var known = new CowsDA40Definition(DA40Variant.NG).GetVariables().Keys
            .Concat(new CowsDA40Definition(DA40Variant.XLS).GetVariables().Keys)
            .ToHashSet(StringComparer.Ordinal);

        var missing = CowsDA40Definition.HotkeyCachedReadoutKeys
            .Where(k => !known.Contains(k))
            .ToList();

        Assert.True(missing.Count == 0,
            "promoted keys that no longer exist — " + string.Join(", ", missing));
    }
}

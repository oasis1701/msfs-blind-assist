using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Two guards over the NAMES the Fenix First Officer uses. A read of a name nothing registers is
/// never polled, so it reads NaN forever: a skip condition that never skips, a checklist line
/// that never ticks, and no error anywhere. v3 renamed, removed or orphaned several names the
/// First Officer's neighbours used, so these pin that the FO reads only what the Fenix
/// definition registers and names none of the v3-dead ones.
/// </summary>
public class FenixFoNameSweepTests
{
    // 0, 1 and 2: conditions short-circuit (an IsOn check ahead of an IsPosition one), so no single
    // seed reaches every read. 0 and 1 cover the on/off pairs, 2 the three-position switches. A read
    // that needs two different values at once is out of reach (N_FCU_EFIS1_BARO_INCH wants the QNH
    // light on and the baro unit inHg); such names are in OnRequestPollFields, which is checked too.
    private static readonly double[] Seeds = { 0, 1, 2 };

    [Fact]
    public void Every_cache_key_the_first_officer_reads_is_registered()
    {
        var registered = new HashSet<string>(new FenixA320Definition().GetVariables().Keys, StringComparer.Ordinal);
        var flows = FenixFlowDefinitions.Build();
        var groups = FenixChecklistDefinitions.Build();

        // key -> where it was read, so the failure names the step or line to fix.
        var strays = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var everRead = new HashSet<string>(StringComparer.Ordinal);

        foreach (double seed in Seeds)
        {
            var cache = RecordingSimConnectCache.With(registered.Select(k => (k, seed)).ToArray());
            var state = new FenixStateEvaluator();
            state.SetSimConnect(cache.Manager);

            // Runs one read-side member, then files what it asked of the cache. A member that throws
            // is reported with where and under which seed, never swallowed: the sweep would
            // otherwise stop looking at everything the member reads after the throw.
            void Probe(string where, Action read)
            {
                cache.Clear();
                try { read(); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"{where} threw {ex.GetType().Name} with every registered key seeded {seed}: {ex.Message}", ex);
                }
                foreach (var key in cache.Reads)
                {
                    everRead.Add(key);
                    if (registered.Contains(key))
                        continue;
                    if (!strays.TryGetValue(key, out var at))
                        strays[key] = at = new SortedSet<string>(StringComparer.Ordinal);
                    at.Add(where);
                }
            }

            foreach (var flow in flows)
                foreach (var step in flow.Steps)
                {
                    string at = $"flow {flow.Id} step {step.Id}";
                    if (step.SkipCondition != null) Probe($"{at} SkipCondition", () => step.SkipCondition(state));
                    if (step.LeaveAloneWhen != null) Probe($"{at} LeaveAloneWhen", () => step.LeaveAloneWhen(state));
                    if (step.TargetValueProvider != null) Probe($"{at} TargetValueProvider", () => step.TargetValueProvider(state));
                    if (step.ConditionFieldName != null) Probe($"{at} ConditionFieldName", () => state.GetValue(step.ConditionFieldName));
                    if (step.VerifyFieldName != null) Probe($"{at} VerifyFieldName", () => state.GetValue(step.VerifyFieldName));
                }

            foreach (var group in groups)
                foreach (var item in group.Items)
                {
                    string at = $"checklist {group.Id} item {item.Id}";
                    if (item.StateFieldName != null) Probe($"{at} StateFieldName", () => state.GetValue(item.StateFieldName));
                    foreach (var field in item.AdditionalStateFields)
                        Probe($"{at} AdditionalStateFields", () => state.GetValue(field));
                    // LiveValue itself, not ReadLiveValue: that one catches and logs a throw (a live value
                    // must never cost the pilot a tick), which would hide it from the promise above.
                    if (item.LiveValue != null) Probe($"{at} LiveValue", () => item.LiveValue(state));
                    if (item.LeaveAloneWhen != null) Probe($"{at} LeaveAloneWhen", () => item.LeaveAloneWhen(state));
                }
        }

        // A sweep that read nothing would pass for the wrong reason.
        Assert.True(everRead.Count > 0, "the sweep recorded no cache read at all: the recorder is not wired to the evaluator");

        var polledStrays = new FenixStateEvaluator().OnRequestPollFields.Where(f => !registered.Contains(f)).ToList();

        Assert.True(strays.Count == 0 && polledStrays.Count == 0,
            "The Fenix First Officer reads names the Fenix definition does not register (never polled, so they read NaN):"
            + string.Concat(strays.Select(s => $"{Environment.NewLine}  {s.Key}  read by {string.Join("; ", s.Value)}"))
            + string.Concat(polledStrays.Select(f => $"{Environment.NewLine}  {f}  listed in OnRequestPollFields")));
    }

    // FNX2PLD_: the 16 FenixQuartz L:vars, a discontinued third-party tool that v3 does not feed.
    // B_ELEC_BUS_POWER_DC_SERIVICE, I_OH_DOOR_VIDEO: renamed by Fenix.
    // I_OH_ELEC_COMMERCIAL_U, S_HYD_GRAVITY_GEAR_EXTEND_ROTATION: removed by v3.
    // I_MIP_AUTOLAND_: the AUTOLAND lights, which v3 splits into _U/_L; the old names may be dead
    // (a real autoland lighting the unsplit names is unproven), so the FO reads none of them.
    // S_OH_ELEC_GEN1: the generator 1 button, which the definition once confused with its _LINE
    // sibling, the EMER ELEC GEN 1 LINE button. The substring covers both: the FO drives
    // neither.
    private static readonly string[] RenamedRemovedOrDeadNames =
    {
        "FNX2PLD_",
        "B_ELEC_BUS_POWER_DC_SERIVICE",
        "I_OH_DOOR_VIDEO",
        "I_OH_ELEC_COMMERCIAL_U",
        "S_HYD_GRAVITY_GEAR_EXTEND_ROTATION",
        "I_MIP_AUTOLAND_",
        "S_OH_ELEC_GEN1",
    };

    [Fact]
    public void No_first_officer_file_names_a_name_v3_renamed_removed_or_left_dead()
    {
        string dir = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "FirstOfficer", "Fenix");
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        var offenders = new List<string>();
        foreach (var file in files)
        {
            string text = File.ReadAllText(file);
            foreach (var name in RenamedRemovedOrDeadNames)
                if (text.Contains(name, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)} names {name}");
        }

        Assert.True(offenders.Count == 0,
            "A Fenix First Officer file names a variable Fenix v3 renamed, removed or left dead:"
            + string.Concat(offenders.Select(o => $"{Environment.NewLine}  {o}")));
    }
}

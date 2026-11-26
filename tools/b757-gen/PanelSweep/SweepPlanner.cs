using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;

namespace PanelSweep;

/// <summary>What the sweep does with one control.</summary>
internal enum Disposition
{
    /// <summary>Read it, write every other position, read back, restore.</summary>
    Sweep,

    /// <summary>On the denylist (or the guard of / sharing a state var with a denied control): never written.</summary>
    Denied,

    /// <summary>A momentary or held button: a press has no position to read back.</summary>
    SkippedButton,

    /// <summary>A guard, or a control a guard covers: its writes need the app's guard sequence, which the tool does not link.</summary>
    SkippedGuarded,

    /// <summary>No state var, fewer than two positions, or not a positional kind: nothing to compare a read-back with.</summary>
    SkippedNoPositions,
}

internal sealed record ControlPlan(Bbs757Control Control, Disposition Disposition, string Reason)
{
    /// <summary>The words the TSV and the dry run use for a control that is not swept.</summary>
    public string Verdict => Disposition switch
    {
        Disposition.Denied => "denied",
        Disposition.SkippedButton => "skipped: button",
        Disposition.SkippedGuarded => "skipped: guarded",
        Disposition.SkippedNoPositions => "skipped: no readable positions",
        _ => "sweep",
    };
}

internal sealed record PanelPlan(string Section, string Panel, IReadOnlyList<ControlPlan> Controls)
{
    public string Name => $"{Section} / {Panel}";
}

internal sealed class SweepPlan
{
    public required IReadOnlyList<PanelPlan> Panels { get; init; }

    /// <summary>Every node the sweep must never write, with the reason, ignoring case.</summary>
    public required IReadOnlyDictionary<string, string> DeniedReasons { get; init; }

    /// <summary>Denylist lines that name no node in the map.</summary>
    public required IReadOnlyList<string> StaleDenylistEntries { get; init; }

    public bool IsDenied(string node) => DeniedReasons.ContainsKey(node);

    public IEnumerable<ControlPlan> AllControls => Panels.SelectMany(p => p.Controls);

    public int Count(Disposition d) => AllControls.Count(c => c.Disposition == d);
}

/// <summary>
/// The built command line a one-shot calculator write is sent as: the MobiFlight "Set" command, whose
/// RPN is made textually unique with the inert "{seq} 0 * " prefix. MobiFlight drops byte-identical
/// consecutive commands, so a second identical press would never run; this is the same idiom as
/// SimConnectManager.BuildUniqueCalcCode / ExecuteCalculatorCodeUnique ([SIM-17], [VAR-4]).
/// </summary>
internal static class CalcCommand
{
    public const string SetPrefix = "MF.SimVars.Set.";

    /// <summary>MobiFlight's command buffer is 1024 bytes including the terminating NUL; a longer command is cut off silently.</summary>
    public const int MaxChars = 1023;

    public static string Build(string rpn, long seq) =>
        SetPrefix + seq.ToString(CultureInfo.InvariantCulture) + " 0 * " + rpn;
}

/// <summary>
/// Everything the sweep DECIDES, kept in one static class so the dry run exercises exactly what the live
/// run does: which controls are swept, which positions are written, and what the denylist covers.
/// The writes themselves come from the app's own Bbs757Write.ToPosition, never from here.
/// </summary>
internal static class SweepPlanner
{
    /// <summary>The longest sequence number a sweep could reach, for the command-length audit.</summary>
    private const long AuditSeq = 9_999_999;

    /// <summary>Nodes from denylist lines: one per line, '#' starts a comment, blank lines ignored, case ignored.</summary>
    public static HashSet<string> ParseDenylist(IEnumerable<string> lines)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in lines)
        {
            string line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length > 0) set.Add(line);
        }
        return set;
    }

    /// <summary>
    /// The denylist grown to everything that must not be written: the listed nodes, then repeatedly the
    /// guard a denied control names in the map, any control whose map guard is denied, and any control
    /// sharing a state var with a denied one (a second node writing the same var is the same switch).
    /// </summary>
    public static Dictionary<string, string> ResolveDenied(IReadOnlyList<Bbs757Control> controls, IEnumerable<string> listed)
    {
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string node in listed) reasons[node] = "denylist";

        bool changed;
        do
        {
            changed = false;
            foreach (var c in controls)
            {
                if (reasons.ContainsKey(c.Node))
                {
                    if (!string.IsNullOrWhiteSpace(c.Guard) && reasons.TryAdd(c.Guard, $"guard of denied {c.Node}"))
                        changed = true;
                    if (!string.IsNullOrWhiteSpace(c.StateVar))
                    {
                        foreach (var other in controls)
                        {
                            if (!ReferenceEquals(other, c) && string.Equals(other.StateVar, c.StateVar, StringComparison.Ordinal)
                                && reasons.TryAdd(other.Node, $"shares {c.StateVar} with denied {c.Node}"))
                                changed = true;
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(c.Guard) && reasons.ContainsKey(c.Guard))
                {
                    reasons[c.Node] = $"covered by denied guard {c.Guard}";
                    changed = true;
                }
            }
        } while (changed);

        return reasons;
    }

    /// <summary>
    /// What the sweep does with a control. The denylist is asked FIRST, so a denied button or guard says
    /// "denied" and not "skipped". A guard or a control that carries one is "skipped: guarded" even when
    /// it is a momentary button: the guard is the reason it is not written, so it is asked before "button".
    /// </summary>
    public static ControlPlan Classify(Bbs757Control c, IReadOnlyDictionary<string, string> deniedReasons)
    {
        if (deniedReasons.TryGetValue(c.Node, out string? why))
            return new ControlPlan(c, Disposition.Denied, why);

        if (c.Kind == Bbs757Kinds.Guard || !string.IsNullOrWhiteSpace(c.Guard))
            return new ControlPlan(c, Disposition.SkippedGuarded, "its writes need the app's guard sequence, SetGuardedAsync, which this tool does not link");

        if (Bbs757Write.IsMomentary(c))
            return new ControlPlan(c, Disposition.SkippedButton, "a press has no position to read back");

        if (c.Kind is not (Bbs757Kinds.Toggle or Bbs757Kinds.Switch or Bbs757Kinds.Select))
            return new ControlPlan(c, Disposition.SkippedNoPositions, $"kind '{c.Kind}' is not positional");

        if (string.IsNullOrWhiteSpace(c.StateVar) || c.Positions.Count < 2)
            return new ControlPlan(c, Disposition.SkippedNoPositions, "no state var, or fewer than two positions");

        return new ControlPlan(c, Disposition.Sweep, "");
    }

    /// <summary>
    /// The positions the sweep writes for a control read at <paramref name="read"/>, in map order: every
    /// position except the one it reads at. A reading off every position writes them all.
    /// </summary>
    public static IReadOnlyList<Bbs757Position> TargetsFor(Bbs757Control c, double? read)
    {
        int at = read is double r && double.IsFinite(r) ? Bbs757Rpn.PositionIndex(c, r) : -1;
        return c.Positions.Where((_, i) => i != at).ToList();
    }

    public static SweepPlan Build(Bbs757ControlMap map, IReadOnlySet<string> listed, string? panelFilter)
    {
        var knownNodes = new HashSet<string>(map.Controls.Select(c => c.Node), StringComparer.OrdinalIgnoreCase);
        var stale = listed.Where(n => !knownNodes.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var reasons = ResolveDenied(map.Controls, listed);

        // Panels in the order the map lists them (the order a pilot meets them).
        var panels = map.Controls
            .GroupBy(c => (c.Section, c.Panel))
            .Select(g => (g.Key.Section, g.Key.Panel, Controls: g.ToList()))
            .ToList();

        if (!string.IsNullOrWhiteSpace(panelFilter))
        {
            var matched = MatchPanels(panels.Select(p => (p.Section, p.Panel)).ToList(), panelFilter);
            if (matched.Count == 0)
                throw new ArgumentException(
                    $"No panel matches '{panelFilter}'. Panels are: " +
                    string.Join("; ", panels.Select(p => $"{p.Section} / {p.Panel}")));
            panels = panels.Where(p => matched.Contains((p.Section, p.Panel))).ToList();
        }

        return new SweepPlan
        {
            Panels = panels
                .Select(p => new PanelPlan(p.Section, p.Panel, p.Controls.Select(c => Classify(c, reasons)).ToList()))
                .ToList(),
            DeniedReasons = reasons,
            StaleDenylistEntries = stale,
        };
    }

    /// <summary>
    /// The panels a --panel value names: an exact (ignoring case) section, panel or "Section / Panel"
    /// name; failing that, panels whose "Section / Panel" name contains the text.
    /// </summary>
    public static IReadOnlyList<(string Section, string Panel)> MatchPanels(
        IReadOnlyList<(string Section, string Panel)> all, string filter)
    {
        string f = filter.Trim();
        static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        var exact = all.Where(p => Eq(p.Panel, f) || Eq(p.Section, f) || Eq($"{p.Section} / {p.Panel}", f)).ToList();
        if (exact.Count > 0) return exact;
        return all.Where(p => $"{p.Section} / {p.Panel}".Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// The final safety check of the plan: every control it would write must be absent from the denied
    /// set, and none may be a guard or a button. Returns the violations (empty means clean).
    /// </summary>
    public static IReadOnlyList<string> Audit(SweepPlan plan)
    {
        var problems = new List<string>();
        foreach (var cp in plan.AllControls.Where(c => c.Disposition == Disposition.Sweep))
        {
            var c = cp.Control;
            if (plan.IsDenied(c.Node)) problems.Add($"{c.Node} is planned for a write but is denied ({plan.DeniedReasons[c.Node]})");
            if (!string.IsNullOrWhiteSpace(c.Guard) && plan.IsDenied(c.Guard))
                problems.Add($"{c.Node} is planned for a write but its guard {c.Guard} is denied");
            if (Bbs757Write.IsMomentary(c) || c.Kind == Bbs757Kinds.Guard)
                problems.Add($"{c.Node} is a {c.Kind} but is planned for a write");
        }
        return problems;
    }

    /// <summary>
    /// What the app's planner would send to go from one position to another (by index), exactly as the
    /// live run asks it. Used by the dry run to show the transport and to audit command lengths.
    /// </summary>
    public static Bbs757WritePlan PlanFor(Bbs757Control c, int fromIndex, int toIndex) =>
        Bbs757Write.ToPosition(c, c.Positions[fromIndex].Value, c.Positions[toIndex].Value);

    /// <summary>"press", "cw/ccw steps", "press cycles forward" or "direct L: set", read off the planner's own first call.</summary>
    public static string TransportOf(Bbs757Control c)
    {
        for (int to = 1; to < c.Positions.Count; to++)
        {
            var plan = PlanFor(c, 0, to);
            if (plan.Outcome != Bbs757WriteOutcome.Writes) continue;
            string first = plan.Calls[0];
            if (first == Bbs757Rpn.SetLVar(c.StateVar!, c.Positions[to].Value)) return "direct L: var set";
            if (!string.IsNullOrWhiteSpace(c.Cw) && !string.IsNullOrWhiteSpace(c.Ccw)
                && c.Kind == Bbs757Kinds.Switch) return "cw/ccw steps";
            return c.Kind == Bbs757Kinds.Switch ? "press, cycling forward" : "press";
        }
        return "no write";
    }

    /// <summary>The longest command any (from, to) pair of any swept control would send, and the ones over the buffer.</summary>
    public static (int Longest, string LongestNode, IReadOnlyList<string> OverLimit) AuditCommandLengths(SweepPlan plan)
    {
        int longest = 0;
        string longestNode = "";
        var over = new List<string>();
        foreach (var cp in plan.AllControls.Where(c => c.Disposition == Disposition.Sweep))
        {
            var c = cp.Control;
            for (int from = 0; from < c.Positions.Count; from++)
            {
                for (int to = 0; to < c.Positions.Count; to++)
                {
                    if (from == to) continue;
                    foreach (string call in PlanFor(c, from, to).Calls)
                    {
                        int len = CalcCommand.Build(call, AuditSeq).Length;
                        if (len > longest) { longest = len; longestNode = c.Node; }
                        if (len > CalcCommand.MaxChars) over.Add($"{c.Node} {c.Positions[from].Label} -> {c.Positions[to].Label}: {len} characters");
                    }
                }
            }
        }
        return (longest, longestNode, over);
    }

    /// <summary>
    /// A rough run time for a control read at its first position: each write is its calls 400 ms apart
    /// plus the 1.5 s read-back, and the restore is one more write back.
    /// </summary>
    public static double EstimateSeconds(Bbs757Control c)
    {
        double seconds = 0.2;   // the first read
        int last = 0;
        for (int to = 1; to < c.Positions.Count; to++)
        {
            seconds += StepSeconds(PlanFor(c, 0, to).Calls.Count);
            last = to;
        }
        seconds += StepSeconds(PlanFor(c, last, 0).Calls.Count);   // the restore
        return seconds;
    }

    private static double StepSeconds(int calls) =>
        Math.Max(0, calls - 1) * SweepTiming.StepGapMs / 1000.0 + SweepTiming.ReadBackMs / 1000.0 + 0.3;

    /// <summary>A position's value as the TSV and the dry run print it: invariant, at most six decimals.</summary>
    public static string FormatValue(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string PositionText(Bbs757Position p) => $"{p.Label} ({FormatValue(p.Value)})";
}

/// <summary>The sweep's timing, from the task brief.</summary>
internal static class SweepTiming
{
    /// <summary>Between the calls of one planned write.</summary>
    public const int StepGapMs = 400;

    /// <summary>From the last call of a write to the read-back.</summary>
    public const int ReadBackMs = 1500;
}

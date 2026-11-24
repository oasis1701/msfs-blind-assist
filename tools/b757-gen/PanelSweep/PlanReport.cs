using MSFSBlindAssist.Aircraft.BBS757;

namespace PanelSweep;

/// <summary>Prints a <see cref="SweepPlan"/>: the whole of --dry-run, and the summary the live run shows before it asks.</summary>
internal static class PlanReport
{
    /// <summary>The per-panel listing and the counts. Never touches the sim.</summary>
    public static void PrintDryRun(SweepPlan plan, TextWriter w, string? panelFilter)
    {
        w.WriteLine("PanelSweep dry run: nothing is connected to the sim and nothing is written.");
        w.WriteLine("A control marked sweep is read, every position it is not at is written in map order (the app's own planner, calls 400 ms apart, read back 1.5 s after the last), and it is put back where it was read.");
        w.WriteLine("The costs below are the calls to go from the control's first position to each other one.");
        if (!string.IsNullOrWhiteSpace(panelFilter)) w.WriteLine($"Panel filter: {panelFilter}");
        PrintStale(plan, w);

        foreach (var panel in plan.Panels)
        {
            w.WriteLine();
            w.WriteLine($"== {panel.Name} ({panel.Controls.Count} controls)");
            foreach (var cp in panel.Controls)
            {
                var c = cp.Control;
                w.WriteLine($"  {c.Label} | {c.Kind} | {c.Node}");
                w.WriteLine("    " + Describe(cp));
            }
        }

        w.WriteLine();
        PrintCounts(plan, w);
    }

    /// <summary>The denylist lines that name no node of the map.</summary>
    public static void PrintStale(SweepPlan plan, TextWriter w)
    {
        foreach (string stale in plan.StaleDenylistEntries)
            w.WriteLine($"WARNING: denylist entry '{stale}' names no node in the control map (stale: the node was renamed or removed).");
    }

    /// <summary>The counts, the time estimate and the two audits.</summary>
    public static void PrintCounts(SweepPlan plan, TextWriter w)
    {
        var sweep = plan.AllControls.Where(c => c.Disposition == Disposition.Sweep).ToList();
        int writesAtMost = sweep.Sum(c => c.Control.Positions.Count - 1);
        double seconds = sweep.Sum(c => SweepPlanner.EstimateSeconds(c.Control));

        w.WriteLine($"Controls in scope: {plan.AllControls.Count()}");
        w.WriteLine($"  to sweep: {sweep.Count} ({writesAtMost} position writes, each control put back afterwards; {Minutes(seconds)})");
        w.WriteLine($"  denied: {plan.Count(Disposition.Denied)}");
        w.WriteLine($"  skipped: button: {plan.Count(Disposition.SkippedButton)}");
        w.WriteLine($"  skipped: guarded: {plan.Count(Disposition.SkippedGuarded)}");
        w.WriteLine($"  skipped: no readable positions: {plan.Count(Disposition.SkippedNoPositions)}");
        w.WriteLine($"Denylist: {plan.DeniedReasons.Count} entries, with the guards and twins they pull in; {plan.StaleDenylistEntries.Count} of them stale.");

        var problems = SweepPlanner.Audit(plan);
        if (problems.Count == 0)
            w.WriteLine($"Audit: none of the {sweep.Count} controls planned for a write is denied, a guard, a guarded control or a button.");
        else
            foreach (string p in problems) w.WriteLine("AUDIT FAILED: " + p);

        var (longest, node, over) = SweepPlanner.AuditCommandLengths(plan);
        if (longest > 0)
            w.WriteLine($"Longest command: {longest} characters ({node}; the buffer takes {CalcCommand.MaxChars}).");
        if (over.Count == 0) w.WriteLine("Commands over the buffer: none.");
        else foreach (string o in over) w.WriteLine("COMMAND TOO LONG: " + o);
    }

    private static string Describe(ControlPlan cp)
    {
        var c = cp.Control;
        switch (cp.Disposition)
        {
            case Disposition.Sweep:
                return DescribeSweep(c);
            case Disposition.Denied:
                return $"denied ({cp.Reason})";
            default:
                return $"{cp.Verdict} ({cp.Reason})";
        }
    }

    private static string DescribeSweep(Bbs757Control c)
    {
        string positions = string.Join(", ", c.Positions.Select(SweepPlanner.PositionText));
        var costs = new List<string>();
        for (int to = 1; to < c.Positions.Count; to++)
        {
            var plan = SweepPlanner.PlanFor(c, 0, to);
            string cost = plan.Outcome switch
            {
                Bbs757WriteOutcome.Writes => plan.Calls.Count == 1 ? "1 call" : $"{plan.Calls.Count} calls",
                Bbs757WriteOutcome.AtPosition => "already there",
                _ => "REFUSED",
            };
            costs.Add($"{c.Positions[to].Label} = {cost}");
        }

        return $"sweep by {SweepPlanner.TransportOf(c)}; positions {positions}; from {c.Positions[0].Label}: {string.Join("; ", costs)}";
    }

    private static string Minutes(double seconds) =>
        seconds < 60 ? "under a minute" : $"about {Math.Ceiling(seconds / 60)} minutes";
}

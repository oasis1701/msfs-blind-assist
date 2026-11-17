using System.Globalization;
using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft.BBS757;

namespace PanelSweep;

/// <summary>
/// A live sweep of the BlueBird 757's positional panel controls. See README.md.
///
///   PanelSweep --dry-run [--panel NAME]     prints the plan; never connects to the sim
///   PanelSweep [--panel NAME]               asks, then sweeps
///
/// Exit codes: 0 done, 1 bad arguments, 2 map or denylist problem, 3 the sim cannot be reached,
/// 4 the pre-flight refused (nothing was written), 5 some control was not put back, 6 stopped on a
/// SimConnect fault (every control reached was put back).
/// </summary>
internal static class Program
{
    private sealed record Options(bool DryRun, bool Help, string? Panel, string? OutDir);

    private static int Main(string[] args)
    {
        Options opts;
        try
        {
            opts = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 1;
        }

        if (opts.Help)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        // The map, as the app loads it (the embedded resource, through Bbs757ControlMap.Load).
        var map = Bbs757ControlMap.Load();
        if (map.Controls.Count == 0)
        {
            Console.Error.WriteLine("The control map is empty: bbs757_control_map.json did not load.");
            return 2;
        }

        // The denylist is always the one shipped next to the exe. A missing or empty one stops everything:
        // never sweep without one.
        string denylistPath = Path.Combine(AppContext.BaseDirectory, "denylist.txt");
        HashSet<string> listed;
        try
        {
            listed = SweepPlanner.ParseDenylist(File.ReadAllLines(denylistPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot read the denylist {denylistPath}: {ex.Message}");
            return 2;
        }
        if (listed.Count == 0)
        {
            Console.Error.WriteLine($"The denylist {denylistPath} has no entries; refusing to go on without one.");
            return 2;
        }

        SweepPlan plan;
        try
        {
            plan = SweepPlanner.Build(map, listed, opts.Panel);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var problems = SweepPlanner.Audit(plan);
        if (problems.Count > 0)
        {
            foreach (string p in problems) Console.Error.WriteLine("AUDIT FAILED: " + p);
            return 2;
        }

        if (opts.DryRun)
        {
            Console.WriteLine($"Denylist: {denylistPath}");
            PlanReport.PrintDryRun(plan, Console.Out, opts.Panel);
            return 0;
        }

        return RunLive(plan, opts, denylistPath);
    }

    // The live path is its own method so that a dry run never loads anything that talks to the sim.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunLive(SweepPlan plan, Options opts, string denylistPath)
    {
        Console.WriteLine("PanelSweep: live sweep of the 757's panel controls.");
        Console.WriteLine($"Denylist: {denylistPath}");

        // A denylist line that names no node of the map means a node was renamed and lost its protection:
        // a live run does not start on one (--dry-run only warns). This runs before the prompt.
        if (plan.StaleDenylistEntries.Count > 0)
        {
            PlanReport.PrintStale(plan, Console.Error);
            Console.Error.WriteLine($"Refused: {plan.StaleDenylistEntries.Count} stale denylist " +
                (plan.StaleDenylistEntries.Count == 1 ? "entry" : "entries") +
                $" ({string.Join(", ", plan.StaleDenylistEntries)}). Fix {denylistPath} (and rebuild) before a live sweep.");
            return 2;
        }

        PlanReport.PrintCounts(plan, Console.Out);
        Console.WriteLine();
        Console.WriteLine("Before you go on:");
        Console.WriteLine("  - you are at the keyboard and watching (or listening to) the aircraft;");
        Console.WriteLine("  - the BlueBird 757 is loaded, on the ground, parked, engines OFF, APU running;");
        Console.WriteLine("  - MSFSBA is closed, so nothing else is writing to the cockpit;");
        Console.WriteLine("  - Ctrl+C stops after the step in progress and puts that control back.");
        Console.Write("Type YES and press Enter to start: ");
        string? answer = Console.ReadLine();
        if (!string.Equals(answer?.Trim(), "YES", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Not started.");
            return 0;
        }

        SimLink link;
        try
        {
            link = SimLink.Connect(Console.Out);
        }
        catch (SimLinkException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }

        using (link)
        {
            try
            {
                if (!SweepRunner.Preflight(link, Console.Out)) return 4;
            }
            catch (SimLinkException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 3;
            }

            string outDir = opts.OutDir ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(outDir);
            string tsvPath = Path.Combine(outDir, $"sweep-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.tsv");

            using var tsv = new TsvWriter(tsvPath);
            var runner = new SweepRunner(link, plan, tsv, Console.Out);

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;   // never let the default kill the process between a write and its restore
                if (runner.CancelRequested)
                {
                    Console.WriteLine($"Second Ctrl+C: stopping now. {runner.InProgress ?? "No control"} may need putting back by hand.");
                    Environment.Exit(130);
                }
                runner.RequestCancel();
                Console.WriteLine("Ctrl+C: stopping after this step; the control in progress will be put back. Press again to stop at once.");
            };

            runner.RunAll();
            runner.PrintSummary(Console.Out, tsvPath);
            if (runner.RestoreFailures.Count > 0) return 5;
            return link.Fault != null ? 6 : 0;
        }
    }

    private static Options ParseArgs(string[] args)
    {
        bool dryRun = false, help = false;
        string? panel = null, outDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--dry-run": dryRun = true; break;
                case "-h": case "--help": case "/?": help = true; break;
                case "--panel": panel = Value(args, ref i, a); break;
                case "--out": outDir = Value(args, ref i, a); break;
                default: throw new ArgumentException($"Unknown argument '{a}'.");
            }
        }
        return new Options(dryRun, help, panel, outDir);
    }

    private static string Value(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
        return args[++i];
    }

    private static void PrintUsage(TextWriter w)
    {
        w.WriteLine("PanelSweep: live sweep of the BlueBird 757's positional panel controls.");
        w.WriteLine();
        w.WriteLine("  PanelSweep --dry-run [--panel NAME]   print the plan; never connects to the sim");
        w.WriteLine("  PanelSweep [--panel NAME]             sweep (asks for YES first)");
        w.WriteLine();
        w.WriteLine("  --panel NAME      only this panel: a panel name (Fuel), a section (Overhead) or 'Section / Panel',");
        w.WriteLine("                    ignoring case; failing an exact match, any panel whose name contains the text");
        w.WriteLine("  --out DIR         where sweep-<date>.tsv goes (default: the current folder)");
        w.WriteLine("  --help            this text");
        w.WriteLine();
        w.WriteLine("See tools/b757-gen/PanelSweep/README.md.");
    }
}

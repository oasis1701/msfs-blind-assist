using System.Text;
using MSFSBlindAssist.Aircraft.BBS757;

namespace PanelSweep;

/// <summary>
/// The sweep's results file: one tab-separated row per position tried, and one row for each control not
/// swept. Flushed after every row, so a crash or a Ctrl+C leaves everything so far.
/// </summary>
internal sealed class TsvWriter : IDisposable
{
    public static readonly string[] Columns = { "node", "label", "position", "written", "read back", "verdict" };

    private readonly StreamWriter _w;

    public TsvWriter(string path)
    {
        _w = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        _w.WriteLine(string.Join('\t', Columns));
    }

    public void Row(Bbs757Control c, string position, string written, string readBack, string verdict) =>
        _w.WriteLine(string.Join('\t', Clean(c.Node), Clean(c.Label), Clean(position), Clean(written), Clean(readBack), Clean(verdict)));

    private static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public void Dispose() => _w.Dispose();
}

/// <summary>
/// The live sweep over a <see cref="SweepPlan"/>. For each control to sweep: read it, write every other
/// position through the app's own Bbs757Write.ToPosition (its calls 400 ms apart), read back 1.5 s after
/// the last call, and put it back where it was read. The denylist is asked before the first write to a
/// control, and again before every write. A SimConnect exception raised for one of a position's writes
/// makes its verdict "write error: ..."; a fault on the link's pump thread (<see cref="SimLink.Fault"/>)
/// stops the sweep as Ctrl+C does, once the control in progress has been put back.
/// </summary>
internal sealed class SweepRunner
{
    private const int RestoreAttempts = 3;
    private const double Tolerance = 0.001;

    private readonly SimLink _link;
    private readonly SweepPlan _plan;
    private readonly TsvWriter _tsv;
    private readonly TextWriter _out;

    private volatile bool _cancel;
    private volatile string? _inProgress;

    public int ControlsSwept { get; private set; }
    public int PositionsLanded { get; private set; }
    public int NotReached { get; private set; }
    public int WriteErrors { get; private set; }
    public List<string> Failures { get; } = new();
    public List<string> RestoreFailures { get; } = new();

    /// <summary>Why the sweep stops before the next step, or null to go on: a fault on the link, or Ctrl+C.</summary>
    private string? StopReason =>
        _link.Fault != null ? "stopped: SimConnect fault" : _cancel ? "stopped with Ctrl+C" : null;

    public SweepRunner(SimLink link, SweepPlan plan, TsvWriter tsv, TextWriter output)
    {
        _link = link;
        _plan = plan;
        _tsv = tsv;
        _out = output;
    }

    /// <summary>Ctrl+C: stop between steps (the control in progress is still put back).</summary>
    public void RequestCancel() => _cancel = true;

    public bool CancelRequested => _cancel;

    /// <summary>The node being swept right now, for the message a second Ctrl+C prints.</summary>
    public string? InProgress => _inProgress;

    /// <summary>
    /// Before anything is written: the sim must say the aircraft is on the ground with its engines
    /// off, and the MobiFlight calculator must run a write once (a nonce written to a private L:var of
    /// ours comes back on a fresh read). Positive evidence of danger refuses; a state that cannot be
    /// read only warns, since the owner confirmed the conditions.
    /// </summary>
    public static bool Preflight(SimLink link, TextWriter w)
    {
        double? onGround = link.ReadSimVar("SIM ON GROUND", "bool");
        double? eng1 = link.ReadSimVar("ENG COMBUSTION:1", "bool");
        double? eng2 = link.ReadSimVar("ENG COMBUSTION:2", "bool");

        if (onGround is 0)
        {
            w.WriteLine("Refused: the sim says the aircraft is not on the ground.");
            return false;
        }
        if (eng1 is > 0.5 || eng2 is > 0.5)
        {
            w.WriteLine("Refused: the sim says an engine is running.");
            return false;
        }
        if (onGround is null || eng1 is null || eng2 is null)
            w.WriteLine("Warning: could not read the ground or engine state from the sim; going on the owner's confirmation.");
        else
            w.WriteLine("On the ground, engines off.");

        // The calculator path: a one-shot write of a nonce into an L:var of the tool's own, then a read.
        // The var must exist before its definition is made, so the definition is made after the write.
        const string probe = "PANELSWEEP_PROBE";
        long nonce = Random.Shared.NextInt64(100_000, 9_000_000);
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            link.WriteCalc($"{nonce + attempt} (>L:{probe})");
            Thread.Sleep(SweepTiming.ReadBackMs);
            double? back = link.ReadLVar(probe, rebind: true);
            if (back is double b && Math.Abs(b - (nonce + attempt)) < Tolerance)
            {
                w.WriteLine("MobiFlight calculator path answers (a test value written and read back).");
                return true;
            }
        }
        w.WriteLine("Refused: a one-shot calculator write did not come back on a read. Is the MobiFlight WASM module installed and the aircraft loaded? Nothing was written to the cockpit.");
        return false;
    }

    public void RunAll()
    {
        int total = _plan.AllControls.Count(c => c.Disposition == Disposition.Sweep);
        int number = 0;

        foreach (var panel in _plan.Panels)
        {
            _out.WriteLine();
            _out.WriteLine($"== {panel.Name}");
            foreach (var cp in panel.Controls)
            {
                var c = cp.Control;
                if (cp.Disposition != Disposition.Sweep)
                {
                    _tsv.Row(c, "", "", "", cp.Verdict);
                    continue;
                }

                number++;
                if (StopReason is { } stop)
                {
                    _tsv.Row(c, "", "", "", $"not reached ({stop})");
                    NotReached++;
                    continue;
                }

                try
                {
                    SweepControl(panel.Name, c, number, total);
                }
                catch (Exception ex)
                {
                    // A fault outside the per-step handling (the sim gone, the denylist guard). The control
                    // was already restored if it was reached; say so plainly and carry on or stop.
                    string msg = $"{panel.Name} / {c.Label}: {ex.Message}";
                    _tsv.Row(c, "", "", "", "error: " + ex.Message);
                    Failures.Add(msg);
                    _out.WriteLine($"[{number}/{total}] {c.Label}: ERROR {ex.Message}");
                    if (_link.SimQuit || ex is InvalidOperationException) _cancel = true;
                }
                finally
                {
                    _inProgress = null;
                }
            }
        }
    }

    private void SweepControl(string panelName, Bbs757Control c, int number, int total)
    {
        GuardNotDenied(c);
        _inProgress = c.Node;

        double? read = _link.ReadLVar(c.StateVar!);
        if (read is not double original || !double.IsFinite(original))
        {
            _tsv.Row(c, "", "", "", "skipped: not read");
            Failures.Add($"{panelName} / {c.Label}: could not be read (no value delivered for L:{c.StateVar})");
            _out.WriteLine($"[{number}/{total}] {c.Label}: not read, skipped.");
            return;
        }

        int originalIndex = Bbs757Rpn.PositionIndex(c, original);
        var targets = SweepPlanner.TargetsFor(c, original);
        int landed = 0, failed = 0;
        bool wrote = false;

        try
        {
            foreach (var target in targets)
            {
                if (StopReason is { } stop)
                {
                    _tsv.Row(c, "", "", "", $"interrupted ({stop})");
                    NotReached++;
                    break;
                }

                var outcome = Step(panelName, c, target, ref wrote);
                if (outcome) landed++; else failed++;
            }
        }
        catch (Exception ex)
        {
            failed++;
            _tsv.Row(c, "", "", "", "error: " + ex.Message);
            Failures.Add($"{panelName} / {c.Label}: {ex.Message}");
            _out.WriteLine($"[{number}/{total}] {c.Label}: ERROR {ex.Message}");
        }
        finally
        {
            // The control is put back even after an error, a Ctrl+C or a link fault. Nothing in here looks at
            // _cancel or the fault: the restore always tries (a fault may leave its reads undelivered, and then
            // it says RESTORE FAILED and names the control).
            bool restored = Restore(c, original, originalIndex, wrote);
            if (!restored) RestoreFailures.Add($"{panelName} / {c.Label} ({c.Node}): read {SweepPlanner.FormatValue(original)} at the start, not put back");
        }

        ControlsSwept++;
        PositionsLanded += landed;
        _out.WriteLine($"[{number}/{total}] {c.Label}: {landed} of {landed + failed} positions landed" + (failed > 0 ? $", {failed} did NOT" : "") + ".");
    }

    /// <summary>One position: read, plan with the app's planner, send, read back, judge. True when it landed.</summary>
    private bool Step(string panelName, Bbs757Control c, Bbs757Position target, ref bool wrote)
    {
        string position = SweepPlanner.PositionText(target);
        double? before = _link.ReadLVar(c.StateVar!);
        if (before is not double was)
        {
            Fail(panelName, c, position, "", "", "not read before the write");
            return false;
        }

        var plan = Bbs757Write.ToPosition(c, was, target.Value);
        switch (plan.Outcome)
        {
            case Bbs757WriteOutcome.AtPosition:
                _tsv.Row(c, position, "", SweepPlanner.FormatValue(was), "already there");
                return true;
            case Bbs757WriteOutcome.Refused:
                Fail(panelName, c, position, "", SweepPlanner.FormatValue(was), "refused: the app's planner has no safe write from this reading");
                return false;
        }

        SendResult sent;
        try
        {
            sent = Send(c, plan.Calls);
            wrote = true;
        }
        catch (SimLinkException ex)
        {
            wrote = true;   // part of it may have gone: the restore must run
            Fail(panelName, c, position, string.Join(" | ", plan.Calls), "", "not sent: " + ex.Message);
            return false;
        }

        double? after = sent.After;
        string readBack = after is double a ? SweepPlanner.FormatValue(a) : "";
        string written = string.Join(" | ", plan.Calls);
        if (sent.WriteErrors.Count > 0)
        {
            // The sim refused a write of this position: whatever the read-back says, it is not the writer's
            // verdict ("no change" would blame the control).
            WriteErrors++;
            Fail(panelName, c, position, written, readBack, "write error: SimConnect " + string.Join(", ", sent.WriteErrors));
            return false;
        }

        string verdict = Judge(c, was, Bbs757Rpn.PositionIndex(c, target.Value), after);
        if (verdict == "landed")
        {
            _tsv.Row(c, position, written, readBack, verdict);
            return true;
        }
        Fail(panelName, c, position, written, readBack, verdict);
        return false;
    }

    /// <summary>What a write's calls came to: the control read 1.5 s after the last, and any SimConnect exceptions raised for the calls.</summary>
    private sealed record SendResult(double? After, IReadOnlyList<string> WriteErrors);

    /// <summary>
    /// Sends the calls, 400 ms apart, then waits 1.5 s after the last and reads the control again. By then the
    /// sim has answered any of the calls' packets with an exception, so they are collected with the read.
    /// </summary>
    private SendResult Send(Bbs757Control c, IReadOnlyList<string> calls)
    {
        GuardNotDenied(c);
        var sendIds = new List<uint>();
        for (int i = 0; i < calls.Count; i++)
        {
            if (i > 0) Thread.Sleep(SweepTiming.StepGapMs);
            GuardNotDenied(c);
            sendIds.Add(_link.WriteCalc(calls[i]));
        }
        Thread.Sleep(SweepTiming.ReadBackMs);
        double? after = _link.ReadLVar(c.StateVar!);
        return new SendResult(after, _link.WriteErrors(sendIds));
    }

    private static string Judge(Bbs757Control c, double before, int targetIndex, double? after)
    {
        if (after is not double a) return "not read after the write";
        int idx = Bbs757Rpn.PositionIndex(c, a);
        if (idx == targetIndex) return "landed";
        if (idx < 0) return $"read back off every position ({SweepPlanner.FormatValue(a)})";
        if (idx == Bbs757Rpn.PositionIndex(c, before)) return "no change";
        return $"wrong position: {c.Positions[idx].Label}";
    }

    /// <summary>
    /// Puts the control back where it was read: through the app's planner when that position is one of
    /// its own; by writing the raw value to the state var (a one-shot set) when the original reading was
    /// off every position. Up to three tries, each after a fresh read.
    /// </summary>
    private bool Restore(Bbs757Control c, double original, int originalIndex, bool wrote)
    {
        string label = originalIndex >= 0 ? SweepPlanner.PositionText(c.Positions[originalIndex]) : SweepPlanner.FormatValue(original) + " (off every position)";
        string position = "(restore) " + label;
        bool IsOriginal(double v) => originalIndex >= 0 ? Bbs757Rpn.PositionIndex(c, v) == originalIndex : Math.Abs(v - original) < Tolerance;

        string lastRead = "";
        for (int attempt = 1; attempt <= RestoreAttempts; attempt++)
        {
            double? now;
            try { now = _link.ReadLVar(c.StateVar!); }
            catch (SimLinkException) { now = null; }

            if (now is not double current)
            {
                Thread.Sleep(SweepTiming.ReadBackMs);
                continue;
            }
            lastRead = SweepPlanner.FormatValue(current);

            if (IsOriginal(current))
            {
                _tsv.Row(c, position, "", lastRead, wrote ? "restored" : "at start (nothing to restore)");
                return true;
            }

            IReadOnlyList<string> calls;
            if (originalIndex >= 0)
            {
                var plan = Bbs757Write.ToPosition(c, current, original);
                if (plan.Outcome != Bbs757WriteOutcome.Writes)
                {
                    // A reading between positions (still moving), or one the planner will not step from: wait and read again.
                    Thread.Sleep(SweepTiming.ReadBackMs);
                    continue;
                }
                calls = plan.Calls;
            }
            else
            {
                calls = new[] { Bbs757Rpn.SetLVar(c.StateVar!, original) };
            }

            try
            {
                var sent = Send(c, calls);
                if (sent.WriteErrors.Count > 0)
                    _out.WriteLine($"  restore of {c.Label}: write error: SimConnect {string.Join(", ", sent.WriteErrors)}");
                if (sent.After is double a && IsOriginal(a))
                {
                    _tsv.Row(c, position, string.Join(" | ", calls), SweepPlanner.FormatValue(a), "restored");
                    return true;
                }
                lastRead = sent.After is double r ? SweepPlanner.FormatValue(r) : "";
            }
            catch (SimLinkException ex)
            {
                _out.WriteLine($"  restore of {c.Label}: {ex.Message}");
            }
        }

        _tsv.Row(c, position, "", lastRead, "RESTORE FAILED: left at " + (lastRead.Length > 0 ? lastRead : "an unread value"));
        _out.WriteLine($"  RESTORE FAILED: {c.Label} ({c.Node}) was {label}; put it back by hand.");
        return false;
    }

    private void Fail(string panelName, Bbs757Control c, string position, string written, string readBack, string verdict)
    {
        _tsv.Row(c, position, written, readBack, verdict);
        Failures.Add($"{panelName} / {c.Label} ({c.Node}) -> {position}: {verdict}");
        _out.WriteLine($"  {c.Label} -> {position}: {verdict}");
    }

    /// <summary>The last line of defence: a denied node never reaches the writer, whatever called.</summary>
    private void GuardNotDenied(Bbs757Control c)
    {
        if (_plan.IsDenied(c.Node))
            throw new InvalidOperationException($"{c.Node} is denied ({_plan.DeniedReasons[c.Node]}); it is never written.");
    }

    public void PrintSummary(TextWriter w, string tsvPath)
    {
        w.WriteLine();
        w.WriteLine($"Done: {ControlsSwept} controls swept, {PositionsLanded} positions landed, {Failures.Count} problems, {NotReached} not reached.");
        w.WriteLine($"SimConnect exceptions: {_link.Exceptions} in all; {WriteErrors} positions with a write error.");
        if (_link.Fault is { } fault)
            w.WriteLine($"STOPPED on a SimConnect fault: {fault}" + (_link.Faults > 1 ? $" ({_link.Faults - 1} more after it)." : "."));
        if (Failures.Count > 0)
        {
            w.WriteLine("Problems (these go into the plan 12 handoff):");
            foreach (string f in Failures) w.WriteLine("  " + f);
        }
        if (RestoreFailures.Count > 0)
        {
            w.WriteLine("NOT PUT BACK (set these by hand):");
            foreach (string f in RestoreFailures) w.WriteLine("  " + f);
        }
        w.WriteLine($"Results: {tsvPath}");
    }
}

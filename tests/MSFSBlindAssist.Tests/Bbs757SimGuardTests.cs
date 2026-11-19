using System.ComponentModel;
using System.Diagnostics;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When the app may write BlueBird's settings (<see cref="Bbs757SimGuard"/>): only with no simulator process running,
/// a check that fails counting as unknown and as read-only as a running simulator, and so does a state nobody set; the
/// texts the Settings tab shows and speaks for each state, pinned to the spec's words; and what a failed check logs.
/// The process check is the internal overload, fed a counter of the test's own, so nothing here needs a simulator or
/// is disturbed by one. The real counter is pinned from both sides, since no test with a counter of its own can see it
/// go wrong: it finds the test host, which is running, and finds nothing for a name no process bears; and the one test
/// of the real check asks only that it can read the process list.
/// </summary>
public class Bbs757SimGuardTests
{
    private const string Sim2024 = "FlightSimulator2024";
    private const string Sim2020 = "FlightSimulator";

    // The Settings tab's status texts, as the spec words them (global constraints, "Exact texts").
    private const string ClosedText =
        "The simulator is closed. Changes are saved to BlueBird's settings at once and take effect when the 757 next loads.";
    private const string RunningText =
        "The simulator is running, so BlueBird's settings are read-only. Close the simulator to change them.";
    private const string UnknownText =
        "The app can't tell whether the simulator is running, so BlueBird's settings are read-only.";
    private const string NoSettingsText =
        "BlueBird hasn't created its settings yet. Load the 757 once, then close the simulator to change them.";

    // ---- the check --------------------------------------------------------------------------------

    [Fact]
    public void The_check_looks_for_both_simulators_processes()
    {
        Assert.Equal(new[] { Sim2024, Sim2020 }, Bbs757SimGuard.ProcessNames);
    }

    [Theory]
    [InlineData(Sim2024, 1)]
    [InlineData(Sim2020, 1)]
    [InlineData(Sim2020, 2)]   // more than one process of a name: still running
    public void Any_simulator_process_means_running(string runningName, int count)
    {
        var state = Bbs757SimGuard.Check(name => name == runningName ? count : 0);

        Assert.Equal(Bbs757SimState.Running, state);
    }

    [Fact]
    public void No_process_means_not_running()
    {
        var asked = new List<string>();

        var state = Bbs757SimGuard.Check(name =>
        {
            asked.Add(name);
            return 0;
        });

        Assert.Equal(Bbs757SimState.NotRunning, state);
        // Every name was looked for: a pilot may fly either simulator.
        Assert.Equal(Bbs757SimGuard.ProcessNames, asked);
    }

    [Theory]   // whatever the process list threw: the app can't tell
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(Win32Exception))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(NotSupportedException))]
    public void A_failed_check_is_unknown(Type failure)
    {
        var state = Bbs757SimGuard.Check(_ => throw (Exception)Activator.CreateInstance(failure)!);

        Assert.Equal(Bbs757SimState.Unknown, state);
    }

    [Theory]   // one of the two names fails to be checked, whichever it is
    // A simulator that was seen is running, whatever became of the other check.
    [InlineData(Sim2024, 1, Bbs757SimState.Running)]
    [InlineData(Sim2020, 1, Bbs757SimState.Running)]
    // With none seen and one check failed, the app can't tell.
    [InlineData(Sim2024, 0, Bbs757SimState.Unknown)]
    [InlineData(Sim2020, 0, Bbs757SimState.Unknown)]
    public void A_simulator_seen_outranks_a_failed_check(string failingName, int otherCount, Bbs757SimState expected)
    {
        var state = Bbs757SimGuard.Check(name => name == failingName ? throw new InvalidOperationException() : otherCount);

        Assert.Equal(expected, state);
    }

    // ---- the real counter ---------------------------------------------------------------------------

    [Fact]
    public void The_real_counter_finds_a_process_that_runs()
    {
        // The test host itself, which runs whatever else does. A counter that always said 0 would let a write through
        // while the simulator runs, and no test that feeds the guard a counter of its own could tell.
        using Process self = Process.GetCurrentProcess();

        Assert.True(Bbs757SimGuard.CountProcesses(self.ProcessName) >= 1);
    }

    [Fact]
    public void The_real_counter_finds_no_process_that_does_not_run()
    {
        // The other direction: a counter that always found one would keep the Settings tab read-only for good.
        Assert.Equal(0, Bbs757SimGuard.CountProcesses("Bbs757NoSuchProcess"));
    }

    [Fact]
    public void The_real_check_reads_the_process_list()
    {
        // Whether a simulator runs on this machine is not for the test to say. But a check that fell into its catch
        // would keep the Settings tab read-only for good, and the tests that feed the guard a counter of their own
        // cannot see that.
        Assert.NotEqual(Bbs757SimState.Unknown, Bbs757SimGuard.Check());
    }

    // ---- what a failed check logs -----------------------------------------------------------------

    [Fact]
    public void A_failure_that_lasts_is_logged_once_and_a_different_one_again()
    {
        var lines = new List<string>();
        // A message of its own: the failure logged last is kept for the whole test run.
        var lasting = new InvalidOperationException("the process list is blocked " + Guid.NewGuid());

        // The tab asks every second, and a failure that lasts is the same each time: one line, with its type and message.
        Bbs757SimGuard.LogFailure(lasting, lines.Add);
        Bbs757SimGuard.LogFailure(new InvalidOperationException(lasting.Message), lines.Add);
        Bbs757SimGuard.LogFailure(new InvalidOperationException(lasting.Message), lines.Add);

        string line = Assert.Single(lines);
        Assert.Contains(nameof(InvalidOperationException), line);
        Assert.Contains(lasting.Message, line);

        // A different failure is logged, and so is the first again after it: only the same one in a row is not.
        Bbs757SimGuard.LogFailure(new Win32Exception(5, "access denied " + Guid.NewGuid()), lines.Add);
        Bbs757SimGuard.LogFailure(lasting, lines.Add);
        Assert.Equal(3, lines.Count);
    }

    // ---- when writing is allowed --------------------------------------------------------------------

    [Theory]
    [InlineData(Bbs757SimState.NotRunning, true, true)]
    [InlineData(Bbs757SimState.NotRunning, false, false)]   // BlueBird has made no settings file: nothing to change
    [InlineData(Bbs757SimState.Running, true, false)]
    [InlineData(Bbs757SimState.Running, false, false)]
    [InlineData(Bbs757SimState.Unknown, true, false)]       // a failed check counts as running
    [InlineData(Bbs757SimState.Unknown, false, false)]
    public void Only_not_running_with_a_file_is_writable(Bbs757SimState state, bool settingsFileExists, bool writable)
    {
        Assert.Equal(writable, Bbs757SimGuard.SettingsWritable(state, settingsFileExists));
    }

    [Fact]
    public void A_state_nobody_set_is_unknown_and_not_writable()
    {
        // What a field of this type holds before anything has checked: it must never allow a write.
        Bbs757SimState unset = default;

        Assert.Equal(Bbs757SimState.Unknown, unset);
        Assert.False(Bbs757SimGuard.SettingsWritable(unset, settingsFileExists: true));
        Assert.Equal(UnknownText, Bbs757SimGuard.StatusText(unset, settingsFileExists: true, simulatorName: null));
    }

    // ---- what the tab says ------------------------------------------------------------------------

    [Theory]
    // No settings file: what the pilot needs to know, whatever the simulator is doing, a folder named or not.
    [InlineData(Bbs757SimState.NotRunning, false, null, NoSettingsText)]
    [InlineData(Bbs757SimState.NotRunning, false, "MSFS 2024", NoSettingsText)]
    [InlineData(Bbs757SimState.Running, false, null, NoSettingsText)]
    [InlineData(Bbs757SimState.Running, false, "MSFS 2024", NoSettingsText)]
    [InlineData(Bbs757SimState.Unknown, false, null, NoSettingsText)]
    [InlineData(Bbs757SimState.Unknown, false, "MSFS 2024", NoSettingsText)]
    // A simulator running, or a check that failed: read-only, and no folder is named, as nothing is being saved.
    [InlineData(Bbs757SimState.Running, true, null, RunningText)]
    [InlineData(Bbs757SimState.Running, true, "MSFS 2024", RunningText)]
    [InlineData(Bbs757SimState.Unknown, true, null, UnknownText)]
    [InlineData(Bbs757SimState.Unknown, true, "MSFS 2020", UnknownText)]
    // The simulator closed: changes are saved at once, and the folder is named only when the caller names one.
    [InlineData(Bbs757SimState.NotRunning, true, null, ClosedText)]
    [InlineData(Bbs757SimState.NotRunning, true, "MSFS 2024", ClosedText + " Changes go to the MSFS 2024 folder.")]
    [InlineData(Bbs757SimState.NotRunning, true, "MSFS 2020", ClosedText + " Changes go to the MSFS 2020 folder.")]
    public void The_status_texts_are_the_spec_s(Bbs757SimState state, bool settingsFileExists, string? simulatorName, string expected)
    {
        Assert.Equal(expected, Bbs757SimGuard.StatusText(state, settingsFileExists, simulatorName));
    }

    [Fact]
    public void The_spoken_texts_are_the_spec_s()
    {
        Assert.Equal("Close the simulator to change BlueBird's settings.", Bbs757SimGuard.LockedText);
        Assert.Equal("Simulator started: BlueBird's settings are read-only.", Bbs757SimGuard.SimStartedText);
        Assert.Equal("Simulator closed: BlueBird's settings can be changed.", Bbs757SimGuard.SimClosedText);
    }

    // ---- what the tab says for a change tried while read-only, and for a change of state --------------
    // The 757 EFB window's Settings page decides these (BBS757EfbForm.Settings.cs) from the guard's state and texts.

    private const string LockedText = "Close the simulator to change BlueBird's settings.";
    private const string StartedText = "Simulator started: BlueBird's settings are read-only.";
    private const string ClosedSpokenText = "Simulator closed: BlueBird's settings can be changed.";

    [Theory]
    // A simulator running: close it.
    [InlineData(Bbs757SimState.Running, true, LockedText)]
    // The check failing: closing the simulator may not be what is missing, so the status box's own words.
    [InlineData(Bbs757SimState.Unknown, true, UnknownText)]
    // No settings file, whatever the simulator does: the status box's words for that.
    [InlineData(Bbs757SimState.Running, false, NoSettingsText)]
    [InlineData(Bbs757SimState.Unknown, false, NoSettingsText)]
    [InlineData(Bbs757SimState.NotRunning, false, NoSettingsText)]
    public void A_change_tried_while_read_only_says_why(Bbs757SimState state, bool settingsFileExists, string expected)
    {
        Assert.Equal(expected, MSFSBlindAssist.Forms.BBS757.BBS757EfbForm.ReadOnlyAttemptText(state, settingsFileExists));
    }

    [Theory]
    [InlineData(Bbs757SimState.NotRunning, Bbs757SimState.Running, false, StartedText)]
    [InlineData(Bbs757SimState.Unknown, Bbs757SimState.Running, false, StartedText)]
    [InlineData(Bbs757SimState.Running, Bbs757SimState.NotRunning, true, ClosedSpokenText)]
    [InlineData(Bbs757SimState.Unknown, Bbs757SimState.NotRunning, true, ClosedSpokenText)]   // the check works again
    // Closed, but BlueBird has made no settings file: nothing can be changed, so nothing is said.
    [InlineData(Bbs757SimState.Running, Bbs757SimState.NotRunning, false, null)]
    [InlineData(Bbs757SimState.Unknown, Bbs757SimState.NotRunning, false, null)]
    // The check starts failing: the status box's words, said once.
    [InlineData(Bbs757SimState.NotRunning, Bbs757SimState.Unknown, false, UnknownText)]
    [InlineData(Bbs757SimState.Running, Bbs757SimState.Unknown, false, UnknownText)]
    // No change, nothing said: a failure that lasts too.
    [InlineData(Bbs757SimState.NotRunning, Bbs757SimState.NotRunning, true, null)]
    [InlineData(Bbs757SimState.Running, Bbs757SimState.Running, false, null)]
    [InlineData(Bbs757SimState.Unknown, Bbs757SimState.Unknown, false, null)]
    public void A_change_of_the_simulator_s_state_is_said_once(Bbs757SimState before, Bbs757SimState now, bool writable,
        string? expected)
    {
        Assert.Equal(expected, MSFSBlindAssist.Forms.BBS757.BBS757EfbForm.SimChangeText(before, now, writable));
    }
}

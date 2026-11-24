using System.Diagnostics;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>Whether a Microsoft Flight Simulator process runs, as <see cref="Bbs757SimGuard.Check()"/> finds it.</summary>
public enum Bbs757SimState
{
    /// <summary>
    /// The process list could not be read, so nothing is known. Treated as running: nothing is written. First on purpose:
    /// it is <c>default(Bbs757SimState)</c>, so a state that was never set is read-only, never writable.
    /// </summary>
    Unknown,

    /// <summary>No simulator process runs, so the 757 is not loaded and BlueBird's settings files may be written.</summary>
    NotRunning,

    /// <summary>A simulator process runs: the 757 may be loaded, and BlueBird may write its own copy over its files.</summary>
    Running,
}

/// <summary>
/// When the app may write BlueBird's settings files: only while no simulator process runs. BlueBird reads those files
/// once, when the 757 loads, and while it is loaded it writes its own copy back over them, so a value written then is
/// ignored and overwritten (measured 2026-10-27, [B757-17]). An aircraft can also stay loaded behind the
/// simulator's main menu, so the screen cannot say whether the 757 is loaded; the one safe moment is with no simulator
/// process at all, MSFS 2024's (<c>FlightSimulator2024</c>) or MSFS 2020's (<c>FlightSimulator</c>). A check that fails
/// is <see cref="Bbs757SimState.Unknown"/>, never "not running": the settings stay read-only, and debug.log says why,
/// once however often the settings window asks (a PC that blocks the process list would otherwise show "can't tell" and
/// nothing more). The guard asks Windows itself and not <c>Utils/SimulatorDetector</c>, which answers "Unknown" both
/// when no simulator runs and when its check fails, the two answers this guard must tell apart. A caller asks again
/// immediately before each write, as the simulator may have started since it last asked. Pure but for the real counter,
/// which lists the machine's processes and logs a failure.
/// </summary>
public static class Bbs757SimGuard
{
    /// <summary>
    /// The process names, as Windows reports them (no <c>.exe</c>), of the two simulators the app supports: MSFS 2024
    /// first, then MSFS 2020. A process of either one means a simulator runs.
    /// </summary>
    public static readonly IReadOnlyList<string> ProcessNames = Array.AsReadOnly(new[] { "FlightSimulator2024", "FlightSimulator" });

    /// <summary>What is said when a change is tried while the settings are read-only.</summary>
    public const string LockedText = "Close the simulator to change BlueBird's settings.";

    /// <summary>What is said, once, when a simulator starts while the 757 EFB settings window is open.</summary>
    public const string SimStartedText = "Simulator started: BlueBird's settings are read-only.";

    /// <summary>What is said, once, when the simulator closes while the 757 EFB settings window is open.</summary>
    public const string SimClosedText = "Simulator closed: BlueBird's settings can be changed.";

    // What the 757 EFB settings window's status box shows, one text for each case (StatusText picks one).
    private const string ClosedText =
        "The simulator is closed. Changes are saved to BlueBird's settings at once and take effect when the 757 next loads.";
    private const string RunningText =
        "The simulator is running, so BlueBird's settings are read-only. Close the simulator to change them.";
    private const string UnknownText =
        "The app can't tell whether the simulator is running, so BlueBird's settings are read-only.";
    private const string NoSettingsText =
        "BlueBird hasn't created its settings yet. Load the 757 once, then close the simulator to change them.";

    // The failure logged last, as "<type>: <message>" (LogFailure keeps it). The settings window asks every second, and a
    // failure that lasts is the same each time.
    private static string? _lastLoggedFailure;

    /// <summary>
    /// Whether a simulator runs now: <see cref="Process.GetProcessesByName(string)"/> for each of
    /// <see cref="ProcessNames"/>, the processes it returns disposed. <see cref="Bbs757SimState.Running"/> when any
    /// process is found, <see cref="Bbs757SimState.Unknown"/> when the list could not be read (the reason goes to
    /// debug.log, once: <see cref="CountProcesses"/>), else <see cref="Bbs757SimState.NotRunning"/>. Never throws.
    /// </summary>
    public static Bbs757SimState Check() => Check(CountProcesses);

    /// <summary>
    /// <see cref="Check()"/> with <paramref name="countProcesses"/>, how many processes bear a name, doing the asking:
    /// internal, so the tests answer for the machine without a simulator. A name with a count above 0 means
    /// <see cref="Bbs757SimState.Running"/>, whatever became of the other name; with none seen, a count that throws (any
    /// exception) means <see cref="Bbs757SimState.Unknown"/>, else <see cref="Bbs757SimState.NotRunning"/>. The answer
    /// does not depend on the order the names are asked in. Never throws for what <paramref name="countProcesses"/> throws.
    /// </summary>
    internal static Bbs757SimState Check(Func<string, int> countProcesses)
    {
        bool failed = false;
        foreach (string name in ProcessNames)
        {
            try
            {
                if (countProcesses(name) > 0) return Bbs757SimState.Running;
            }
            catch (Exception)
            {
                // Listing processes fails in several ways (Win32Exception, InvalidOperationException, ...): all of them
                // leave it unknown whether a simulator runs, and the other name may still show one. The real counter
                // has logged the reason already (CountProcesses).
                failed = true;
            }
        }
        return failed ? Bbs757SimState.Unknown : Bbs757SimState.NotRunning;
    }

    /// <summary>
    /// Whether a change may be saved: the simulator is known not to run (<see cref="Bbs757SimState.NotRunning"/>: a
    /// check that failed is not enough) and BlueBird's settings file exists (the app never creates one).
    /// </summary>
    public static bool SettingsWritable(Bbs757SimState state, bool settingsFileExists) =>
        state == Bbs757SimState.NotRunning && settingsFileExists;

    /// <summary>
    /// What the 757 EFB settings window's status box says. A missing settings file comes first, whatever the simulator does:
    /// there is nothing to change until BlueBird has made one, and its text sends the pilot to load the 757 once. Then a
    /// simulator that runs, then a check that failed (a state that is none of the three reads as failed), then, with the
    /// simulator closed, that changes are saved at once. That last text ends with the <paramref name="simulatorName"/>
    /// folder sentence when it is not null: the caller names the simulator only when more than one work folder exists (a
    /// pilot with both simulators), so the pilot knows which folder the changes go to. No other text names one: with
    /// those, nothing is saved.
    /// </summary>
    public static string StatusText(Bbs757SimState state, bool settingsFileExists, string? simulatorName)
    {
        if (!settingsFileExists) return NoSettingsText;
        return state switch
        {
            Bbs757SimState.Running => RunningText,
            Bbs757SimState.NotRunning when simulatorName is not null => ClosedText + $" Changes go to the {simulatorName} folder.",
            Bbs757SimState.NotRunning => ClosedText,
            _ => UnknownText,
        };
    }

    /// <summary>
    /// How many processes bear <paramref name="name"/>, asked of Windows: <see cref="Process.GetProcessesByName(string)"/>,
    /// each <see cref="Process"/> it returns disposed, as every one holds a handle. A failure is logged
    /// (<see cref="LogFailure"/>) and thrown on, for <see cref="Check(Func{string, int})"/> to turn into
    /// <see cref="Bbs757SimState.Unknown"/>. Internal, so a test can see that the real counter finds a process that runs
    /// and none that does not: one that always said 0 would let a write through while the simulator runs, and no test
    /// with a counter of its own could tell.
    /// </summary>
    internal static int CountProcesses(string name)
    {
        try
        {
            Process[] found = Process.GetProcessesByName(name);
            try
            {
                return found.Length;
            }
            finally
            {
                foreach (Process process in found) process.Dispose();
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex, static line => Log.Warn("BBS757", line));
            throw;
        }
    }

    /// <summary>
    /// Gives <paramref name="log"/> one line saying that the processes could not be listed and why (the exception's type
    /// and message), unless this is the failure logged last: the settings window asks every second, so a failure that
    /// lasts is logged once, not on every check. A different failure is logged, and so is the first one again after it. Internal,
    /// so the tests read what is logged without the app's process-wide logger. Safe to call from any thread.
    /// </summary>
    internal static void LogFailure(Exception failure, Action<string> log)
    {
        string reason = $"{failure.GetType().Name}: {failure.Message}";
        if (Interlocked.Exchange(ref _lastLoggedFailure, reason) == reason) return;
        log($"Could not list the simulator's processes, so BlueBird's settings stay read-only: {reason}");
    }
}

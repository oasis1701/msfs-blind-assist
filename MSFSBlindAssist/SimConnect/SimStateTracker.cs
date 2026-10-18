using MSFSBlindAssist.Services.SimPerformance;

namespace MSFSBlindAssist.SimConnect;

/// <summary>What a <see cref="SimStateTracker"/> delivery changed. Two can arrive together: opening
/// the Esc menu during active pause ends active pause and starts the menu pause in one delivery.</summary>
[Flags]
public enum SimStateChange
{
    None = 0,
    Paused = 1,
    Unpaused = 2,
    ActivePauseOn = 4,
    ActivePauseOff = 8,
    SlewOn = 16,
    SlewOff = 32,
}

/// <summary>
/// Turns the sim's pause, slew and sim-rate signals into the changes worth speaking: the "Pause_EX1"
/// system event's flags, the "Sim" system event (flight running or not), and the <c>IS SLEW ACTIVE</c>
/// and <c>SIMULATION RATE</c> simvars.
///
/// <para>Measured live on MSFS 2024 (SU build 282174, 2026-10-17, FS2020 key layout):</para>
/// <list type="bullet">
/// <item>Esc (the pause menu) reports <see cref="SimPauseFlag"/> (8); active pause (Pause/Break)
/// reports <see cref="ActivePauseFlag"/> (4); closing the menu reports 0 and ENDS an active pause that
/// was on before it.</item>
/// <item><see cref="FullPauseFlag"/> (1) only ever came from the sim itself: two 13 ms blips while a
/// flight loaded, and once about a second after entering the cockpit, held until the next Esc while the
/// sim clock kept running. It is ignored, never spoken.</item>
/// <item>The flags combine (9 = full | sim pause), so each kind is read as its own bit.</item>
/// <item>A flight load starts with "Sim" 0, then flags 0 (the menu pause the pilot left through ends),
/// then "Sim" 1: nothing changes aloud while "Sim" is 0.</item>
/// <item>SIMULATION RATE moves in steps (0.25, 0.5, 1, 2, 4) only when the rate really changes: the
/// sim-rate select key alone, or a step past the limit, delivers nothing.</item>
/// </list>
///
/// <para>Each subscription delivers the current state at once; the first delivery of each signal after
/// <see cref="Reset"/> only seeds it, so connecting to a paused or sped-up sim is silent. Not measured on
/// FS2020, where a pause may report only the full-pause bit and so go unspoken.</para>
/// </summary>
public sealed class SimStateTracker
{
    /// <summary>Pause_EX1 PAUSE_STATE_FLAG_PAUSE: a "full" pause. In MSFS 2024 only the sim's own (ignored).</summary>
    public const uint FullPauseFlag = 1;
    /// <summary>Pause_EX1 PAUSE_STATE_FLAG_ACTIVE_PAUSE: active pause.</summary>
    public const uint ActivePauseFlag = 4;
    /// <summary>Pause_EX1 PAUSE_STATE_FLAG_SIM_PAUSE: the Esc menu pause.</summary>
    public const uint SimPauseFlag = 8;

    // Rates are powers of two; anything closer than this is the same rate.
    private const double SameRate = 0.001;

    private bool? _paused;
    private bool? _activePause;
    private bool? _slew;
    private double? _simRate;
    // Assumed running until "Sim" says otherwise, so a missing "Sim" delivery can never mute the
    // announcements for a whole session.
    private bool _simRunning = true;

    /// <summary>Forgets every state: the next delivery of each signal seeds silently. Call per connection.</summary>
    public void Reset()
    {
        _paused = null;
        _activePause = null;
        _slew = null;
        _simRate = null;
        _simRunning = true;
    }

    /// <summary>The "Sim" system event: true while a flight runs. While false, changes are tracked silently.</summary>
    public void OnSimRunning(bool running) => _simRunning = running;

    /// <summary>A "Pause_EX1" delivery. Returns what changed aloud.</summary>
    public SimStateChange OnPauseFlags(uint flags)
    {
        bool paused = (flags & SimPauseFlag) != 0;
        bool activePause = (flags & ActivePauseFlag) != 0;

        var change = SimStateChange.None;
        if (_paused is bool wasPaused && wasPaused != paused)
            change |= paused ? SimStateChange.Paused : SimStateChange.Unpaused;
        if (_activePause is bool wasActive && wasActive != activePause)
            change |= activePause ? SimStateChange.ActivePauseOn : SimStateChange.ActivePauseOff;

        _paused = paused;
        _activePause = activePause;
        return _simRunning ? change : SimStateChange.None;
    }

    /// <summary>An <c>IS SLEW ACTIVE</c> delivery. Returns what changed aloud.</summary>
    public SimStateChange OnSlew(bool active)
    {
        var change = SimStateChange.None;
        if (_slew is bool was && was != active)
            change = active ? SimStateChange.SlewOn : SimStateChange.SlewOff;

        _slew = active;
        return _simRunning ? change : SimStateChange.None;
    }

    /// <summary>A <c>SIMULATION RATE</c> delivery. Returns the new rate when it changed aloud, else null.
    /// A rate of zero or less is not a rate and is ignored.</summary>
    public double? OnSimRate(double rate)
    {
        if (rate <= 0) return null;
        bool changed = _simRate is double was && Math.Abs(was - rate) >= SameRate;
        _simRate = rate;
        return changed && _simRunning ? rate : null;
    }

    /// <summary>The words for <paramref name="change"/>; null when nothing changed.</summary>
    public static string? Describe(SimStateChange change)
    {
        var parts = new List<string>(2);
        if (change.HasFlag(SimStateChange.Paused)) parts.Add("Sim paused");
        if (change.HasFlag(SimStateChange.Unpaused)) parts.Add("Sim unpaused");
        if (change.HasFlag(SimStateChange.ActivePauseOn)) parts.Add("Active pause on");
        if (change.HasFlag(SimStateChange.ActivePauseOff)) parts.Add("Active pause off");
        if (change.HasFlag(SimStateChange.SlewOn)) parts.Add("Slew on");
        if (change.HasFlag(SimStateChange.SlewOff)) parts.Add("Slew off");
        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    /// <summary>"Sim rate 4x", "Sim rate 0.5x": the Sim Performance window's wording for the rate.</summary>
    public static string DescribeSimRate(double rate) => $"Sim rate {SimPerformanceFormatter.SimRateText((float)rate)}";
}

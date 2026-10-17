namespace MSFSBlindAssist.SimConnect;

/// <summary>What a <see cref="SimPauseSlewTracker"/> delivery changed. Two can arrive together: opening
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
/// Turns the sim's pause and slew signals into the changes worth speaking: the "Pause_EX1" system
/// event's flags, the "Sim" system event (flight running or not) and the <c>IS SLEW ACTIVE</c> simvar.
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
/// </list>
///
/// <para>Each subscription delivers the current state at once; the first delivery of each signal after
/// <see cref="Reset"/> only seeds it, so connecting to a paused sim is silent. Not measured on FS2020,
/// where a pause may report only the full-pause bit and so go unspoken.</para>
/// </summary>
public sealed class SimPauseSlewTracker
{
    /// <summary>Pause_EX1 PAUSE_STATE_FLAG_PAUSE: a "full" pause. In MSFS 2024 only the sim's own (ignored).</summary>
    public const uint FullPauseFlag = 1;
    /// <summary>Pause_EX1 PAUSE_STATE_FLAG_ACTIVE_PAUSE: active pause.</summary>
    public const uint ActivePauseFlag = 4;
    /// <summary>Pause_EX1 PAUSE_STATE_FLAG_SIM_PAUSE: the Esc menu pause.</summary>
    public const uint SimPauseFlag = 8;

    private bool? _paused;
    private bool? _activePause;
    private bool? _slew;
    // Assumed running until "Sim" says otherwise, so a missing "Sim" delivery can never mute the
    // announcements for a whole session.
    private bool _simRunning = true;

    /// <summary>Forgets every state: the next delivery of each signal seeds silently. Call per connection.</summary>
    public void Reset()
    {
        _paused = null;
        _activePause = null;
        _slew = null;
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

    /// <summary>The words for <paramref name="change"/>, keeping only the kinds switched on; null when none is.</summary>
    public static string? Describe(SimStateChange change, bool announcePause, bool announceActivePause, bool announceSlew)
    {
        var parts = new List<string>(2);
        if (announcePause && change.HasFlag(SimStateChange.Paused)) parts.Add("Sim paused");
        if (announcePause && change.HasFlag(SimStateChange.Unpaused)) parts.Add("Sim unpaused");
        if (announceActivePause && change.HasFlag(SimStateChange.ActivePauseOn)) parts.Add("Active pause on");
        if (announceActivePause && change.HasFlag(SimStateChange.ActivePauseOff)) parts.Add("Active pause off");
        if (announceSlew && change.HasFlag(SimStateChange.SlewOn)) parts.Add("Slew on");
        if (announceSlew && change.HasFlag(SimStateChange.SlewOff)) parts.Add("Slew off");
        return parts.Count == 0 ? null : string.Join(". ", parts);
    }
}

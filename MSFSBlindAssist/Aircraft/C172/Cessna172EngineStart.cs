// MSFSBlindAssist/Aircraft/C172/Cessna172EngineStart.cs
namespace MSFSBlindAssist.Aircraft.C172;

/// <summary>
/// The Start Engine button's state machine. The definition sends MAGNETO1_SET 4 (START) after a
/// true <see cref="Begin"/>, feeds every combustion delivery and every 1 Hz batch tick through
/// <see cref="Tick"/>, and on any outcome other than None sends MAGNETO1_SET 3 (BOTH) and speaks
/// the outcome's sentence. <see cref="Cancel"/> answers whether the key must be returned to BOTH
/// because a start was abandoned (aircraft switch, shutdown).
///
/// Pure: time is a parameter (Environment.TickCount64 in production), combustion is a nullable
/// bool (null = no reading delivered yet, which keeps waiting rather than failing).
/// </summary>
public sealed class Cessna172EngineStart
{
    public const int TimeoutMs = 10_000;
    public const string RunningSentence = "Engine running";
    public const string FailedSentence = "Engine did not start";

    public enum Outcome { None, Running, Failed }

    private long _startedAtMs = -1;

    public bool IsInFlight => _startedAtMs >= 0;

    /// <summary>Arms a start at <paramref name="nowMs"/>. False when one is already in flight (ignored).</summary>
    public bool Begin(long nowMs)
    {
        if (IsInFlight) return false;
        _startedAtMs = nowMs;
        return true;
    }

    /// <summary>A sample. Running on the first true combustion; Failed once the timeout has elapsed without one.</summary>
    public Outcome Tick(long nowMs, bool? combustion)
    {
        if (!IsInFlight) return Outcome.None;
        if (combustion == true) { _startedAtMs = -1; return Outcome.Running; }
        if (nowMs - _startedAtMs >= TimeoutMs) { _startedAtMs = -1; return Outcome.Failed; }
        return Outcome.None;
    }

    /// <summary>Abandons a start. True when one was in flight, so the caller returns the key to BOTH.</summary>
    public bool Cancel()
    {
        bool was = IsInFlight;
        _startedAtMs = -1;
        return was;
    }
}

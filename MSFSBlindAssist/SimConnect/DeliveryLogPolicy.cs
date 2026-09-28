namespace MSFSBlindAssist.SimConnect;

/// <summary>Which debug.log line, if any, an individual-variable delivery writes.</summary>
internal enum DeliveryLogLine
{
    None,
    Firing,
    FirstUnchanged,
}

/// <summary>
/// The per-fire "Firing SimVarUpdated" line is skipped for HighFrequency (SIM_FRAME) vars, which
/// would churn the 5 MB rotation 30-60 times a second, and for an UNCHANGED periodic delivery of
/// any var beyond its first per cache lifetime: non-announced individual-def vars fire on every
/// response (the Fenix registers ~50 at 1 Hz), and logging each held only ~35 minutes of flying
/// in the whole 20 MB retention (measured 2026-09-27). A delivery someone is WAITING for is always
/// logged, changed or not — a forced read, or the answer to a fresh or seed read (ReadFreshAsync:
/// the MD-11 walker, every spoken read-back verdict) — because that line is what tells "the sim
/// answered with the old value" from "the sim never answered", and those reads are low volume.
/// Pure — <c>DeliveryLogPolicyTests</c>.
/// </summary>
internal static class DeliveryLogPolicy
{
    public static DeliveryLogLine For(
        bool highFrequency, bool hasChanged, bool isForceUpdate, bool targetedRead, bool unchangedAlreadyLogged)
    {
        if (highFrequency) return DeliveryLogLine.None;
        if (hasChanged || isForceUpdate || targetedRead) return DeliveryLogLine.Firing;
        return unchangedAlreadyLogged ? DeliveryLogLine.None : DeliveryLogLine.FirstUnchanged;
    }
}

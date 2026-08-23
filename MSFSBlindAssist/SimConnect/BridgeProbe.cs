namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Match rule for the MobiFlight calc-path liveness probe (L:MSFSBA_BRIDGE_PROBE).
///
/// ⚠️ The read-back LAGS THE WRITE BY ONE ROUND, so the PREVIOUS nonce must be accepted too.
/// The data-def request is issued immediately after the calc write, but the write only lands
/// when the MobiFlight WASM next runs, so the sim answers with the value from before it.
/// Measured live 2026-08-22: wrote 849 / delivered 848, wrote 850 / delivered 849, every round.
///
/// Comparing only against the CURRENT nonce misses by exactly one every time, and since each
/// miss picks a fresh nonce the probe can never converge — it had never once verified on that
/// machine. The cost was severe and entirely silent: CalcPathVerified stayed false, so SetLVar
/// fell back to the data-def write that is unreliable for FBW L:vars, and every dotted FBW event
/// fell back to a transport the FCU ignores. It surfaced as "the FCU won't accept".
///
/// Both nonces were written by us through the calculator, so either coming back proves the same
/// thing. Zero is never a match: it is what an undelivered def reads as.
/// </summary>
public static class BridgeProbe
{
    public static bool IsEcho(double cached, int nonce, int previousNonce)
    {
        int seen = (int)System.Math.Round(cached);
        if (seen == 0) return false;
        return seen == nonce || seen == previousNonce;
    }
}

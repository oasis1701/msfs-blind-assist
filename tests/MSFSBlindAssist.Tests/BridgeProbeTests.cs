// The MobiFlight calc-path liveness probe: write a nonce to L:MSFSBA_BRIDGE_PROBE through the
// calculator, read it back over the data-def path, and treat a match as proof the path is alive.
//
// ⚠️ The read-back LAGS THE WRITE BY ONE ROUND. The request is issued immediately after the calc
// write, but the write only lands when the MobiFlight WASM next runs, so the sim answers with the
// value from BEFORE it. Measured on a live machine 2026-08-22:
//
//     18:27:22.166  wrote 849        18:27:22.175  delivered 848
//     18:27:23.665  wrote 850        18:27:23.680  delivered 849
//
// Comparing only against the CURRENT nonce therefore misses by exactly one, every time, and
// because each miss picks a new nonce the probe can never converge. It had never verified on that
// machine — the nonce had climbed past 861 across sessions — which left CalcPathVerified false,
// so SetLVar fell back to the unreliable data-def write and every dotted FBW event fell back to a
// transport the FCU ignores. Reported as "the FCU won't accept".
//
// Accepting the PREVIOUS nonce as well is what closes it: both values were written by us through
// the calculator, so either one coming back proves the same thing.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class BridgeProbeTests
{
    [Fact]
    public void The_current_nonce_coming_back_proves_the_path()
    {
        Assert.True(BridgeProbe.IsEcho(cached: 850, nonce: 850, previousNonce: 849));
    }

    // The case that was failing live.
    [Fact]
    public void The_previous_nonce_coming_back_also_proves_it_because_the_read_lags_one_round()
    {
        Assert.True(BridgeProbe.IsEcho(cached: 849, nonce: 850, previousNonce: 849));
    }

    [Fact]
    public void A_value_we_never_wrote_proves_nothing()
    {
        Assert.False(BridgeProbe.IsEcho(cached: 4069, nonce: 850, previousNonce: 849));
    }

    // The var reads 0 when the def has never delivered at all — that must not look like a match
    // just because the probe happens to be starting up.
    [Fact]
    public void An_undelivered_read_is_not_a_match()
    {
        Assert.False(BridgeProbe.IsEcho(cached: 0, nonce: 1, previousNonce: 0));
    }

    // Rounding: the value arrives as a double over the data-def path.
    [Fact]
    public void A_float_rounding_wobble_still_matches()
    {
        Assert.True(BridgeProbe.IsEcho(cached: 849.0000001, nonce: 850, previousNonce: 849));
    }
}

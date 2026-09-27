namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// How long an A380 ECAM control-panel key (<c>L:A32NX_BTN_*</c>: TO CONFIG, CLR, RCL, C/L,
/// CHECK, UP, DOWN, ABN PROC) must be held — and then left released — for the flight warning
/// system to see exactly one press.
///
/// FBW #10934 ("athr off logic", a380x <c>27a73e219</c>, 2026-09-19) moved the FWS's
/// <c>UpdateThrottler(125)</c> check to the very top of <c>FwsCore.update()</c>, ahead of the block
/// that reads these keys — a block whose own comment still says it acquires them "at a higher
/// frequency". So each key is now sampled ONCE per FWS cycle, and the samples fall on sim frames:
/// 125 ms plus up to one frame apart. A press shorter than that is caught only when a sample
/// happens to land inside it (the checklist window's old 45 ms press: about one in three, with no
/// error anywhere), and the FWS turns a key into a pulse on its rising edge, so two presses with no
/// released sample between them merge into one. Hold a whole cycle with margin, release as long.
/// </summary>
public static class A380EcpKeyPulse
{
    /// <summary>The FWS cycle: <c>fwsUpdateThrottler = new UpdateThrottler(125)</c> (FwsCore.ts).</summary>
    public const int FwsCycleMs = 125;

    /// <summary>Key held down: two cycles, so a frame-late sample still lands inside it.</summary>
    public const int HoldMs = 250;

    /// <summary>Key released before the next press: two cycles, so a released sample always
    /// separates them.</summary>
    public const int ReleaseMs = 250;

    /// <summary>
    /// How long a press must still wait so the keys have been released <see cref="ReleaseMs"/>
    /// since <paramref name="lastReleaseTick"/> (an <c>Environment.TickCount64</c>; 0 = nothing
    /// pressed yet). A release still in the FUTURE — the last press is held — waits for it and then
    /// the full release time. Applied to EVERY press rather than left to each caller: the checklist
    /// window's bursts waited, but a press queued during its closing scrape followed the release by
    /// only 85 ms plus the scrape.
    /// </summary>
    public static int WaitBeforePressMs(long lastReleaseTick, long nowTick)
    {
        if (lastReleaseTick <= 0) return 0;
        long released = nowTick - lastReleaseTick;
        return released >= ReleaseMs ? 0 : (int)(ReleaseMs - released);
    }

    /// <summary>
    /// The ONE release clock every A380 ECP key press shares — the checklist window's keys and the
    /// ECAM Control Panel's buttons all reach the same FWS sampler. A press RESERVES its slot when it
    /// starts, recording when it WILL be released, so a press begun while another is still held
    /// waits for that release; recording the release only once it happened let the checklist
    /// window's close-time C/L go out on top of a C/L still held. Thread-safe: the close-time press
    /// runs on a pool thread.
    /// </summary>
    public sealed class PressClock
    {
        private readonly object _lock = new();
        private long _releaseAt;   // when the last reserved press is (or will be) released; 0 = none

        /// <summary>How long to wait before pressing; records this press's release as that wait
        /// plus <see cref="HoldMs"/> from <paramref name="nowTick"/>.</summary>
        public int Reserve(long nowTick)
        {
            lock (_lock)
            {
                int wait = WaitBeforePressMs(_releaseAt, nowTick);
                _releaseAt = nowTick + wait + HoldMs;
                return wait;
            }
        }

        /// <summary>A release that has just happened — later than scheduled, when a delay overshot.
        /// Never moves a later reservation back.</summary>
        public void MarkReleased(long nowTick)
        {
            lock (_lock) { if (nowTick > _releaseAt) _releaseAt = nowTick; }
        }
    }

    /// <summary>The app's one clock (see <see cref="PressClock"/>).</summary>
    public static readonly PressClock Shared = new();
}

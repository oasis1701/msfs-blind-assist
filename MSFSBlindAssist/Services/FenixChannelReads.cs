using System.Collections.Frozen;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One display key's read from the Fenix display channel: the display it takes its values from, and how it
/// composes the text to speak from a frame.
/// </summary>
/// <param name="Display">
/// The display the read speaks for. The read may speak only while the trust gate trusts this display.
/// </param>
/// <param name="Compose">
/// The text to speak from a frame, or null when the frame gives none. Called only for a display the gate
/// trusts, and with the latest frame. Whatever it throws, or leaves null, empty or blank, is answered with the
/// AI read.
/// </param>
internal sealed record FenixChannelRead(FenixChannelDisplay Display, Func<FenixChannelFrame, string?> Compose);

/// <summary>
/// The fallback switch of the Fenix display channel: the table of display keys that have a channel read, and the
/// pure choice between speaking that read and the AI read every display key has today. A key with no read, or
/// whose display the trust gate does not trust, keeps its AI read exactly as before: never speak an untrusted
/// value (FNX-22).
/// </summary>
internal static class FenixChannelReads
{
    /// <summary>
    /// The display keys that have a channel read, and the read of each. Empty in this work: every display key
    /// keeps its AI read. The table cannot be changed from outside, so a read joins it only by an edit here,
    /// with its own test and its own in-sim check.
    /// </summary>
    public static readonly IReadOnlyDictionary<HotkeyAction, FenixChannelRead> All =
        FrozenDictionary<HotkeyAction, FenixChannelRead>.Empty;

    /// <summary>
    /// The answer to a display key: the text to speak, or null for "use the AI read". The text is spoken only
    /// when all of these hold: <paramref name="reads"/> has a read for <paramref name="action"/>, a frame has
    /// come, <paramref name="verdictFor"/> trusts the read's own display, and the read composes text from the
    /// frame. Every other case answers null: no read for the key, no frame yet, the display not trusted, and a
    /// read that gives null, empty or blank text. Nothing throws out of it: a read or a verdict that throws is
    /// a read that did not come, and answers null too.
    /// </summary>
    /// <remarks>
    /// Pure: no state, no I/O and no log, so a caller that wants a line in the log decides what to write and
    /// when. A read is composed only for a display the gate trusts, so it never sees a frame whose display the
    /// gate refused.
    /// </remarks>
    /// <param name="action">The display key that was pressed.</param>
    /// <param name="reads">The table of reads: the app passes <see cref="All"/>, a test a stand-in of its own.</param>
    /// <param name="verdictFor">
    /// The trust gate's answer for a display, as <see cref="FenixChannelMonitor.VerdictFor"/> gives it. It is
    /// asked once, and only for the display of the read the key has.
    /// </param>
    /// <param name="latest">
    /// The monitor's latest frame, null before the first. It is an argument, not a function, so the caller has
    /// read it before the verdict is asked: a frame that latches the gate in between is then answered "not
    /// trusted", and never read as one the gate trusted.
    /// </param>
    public static string? Choose(HotkeyAction action, IReadOnlyDictionary<HotkeyAction, FenixChannelRead> reads,
        Func<FenixChannelDisplay, FenixDisplayVerdict> verdictFor, FenixChannelFrame? latest)
    {
        try
        {
            if (!reads.TryGetValue(action, out FenixChannelRead? read)) return null;
            if (latest is null) return null;
            if (!verdictFor(read.Display).Trusted) return null;

            string? text = read.Compose(latest);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            // A read or a verdict that throws is one that did not come: the key keeps its AI read, as it does
            // for every failure of the channel. Choose writes no log, so nothing here records the throw; the
            // tests of each read are what keep it from throwing.
            return null;
        }
    }
}

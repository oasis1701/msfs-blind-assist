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
/// trusts, and with the frame the gate judged in giving that verdict. Whatever it throws, or leaves null, empty
/// or blank, is answered with the AI read.
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
    /// when all of these hold: <paramref name="reads"/> has a read for <paramref name="action"/>, the snapshot
    /// of the read's own display trusts it and holds a frame, and the read composes text from that frame. Every
    /// other case answers null: no read for the key, the display not trusted, no frame yet, and a read that
    /// gives null, empty or blank text. Nothing throws out of it: a read or a snapshot that throws is a read
    /// that did not come, and answers null too.
    /// </summary>
    /// <remarks>
    /// Pure: no state, no I/O and no log, so a caller that wants a line in the log decides what to write and
    /// when. The verdict and the frame come from one snapshot, which the monitor takes at one locked instant, so
    /// the frame a read composes is the very frame the gate judged in giving that verdict: never an older frame
    /// beside a newer verdict, nor a newer frame beside an older one.
    /// </remarks>
    /// <param name="action">The display key that was pressed.</param>
    /// <param name="reads">The table of reads: the app passes <see cref="All"/>, a test a stand-in of its own.</param>
    /// <param name="snapshot">
    /// The trust gate's verdict for a display and the latest frame (null before the first), taken together, as
    /// <see cref="FenixChannelMonitor.Snapshot"/> gives them (with the rest of what it holds, which this choice does
    /// not use). It is asked once, and only for the display of the read the key has.
    /// </param>
    public static string? Choose(HotkeyAction action, IReadOnlyDictionary<HotkeyAction, FenixChannelRead> reads,
        Func<FenixChannelDisplay, FenixChannelSnapshot> snapshot)
    {
        try
        {
            if (!reads.TryGetValue(action, out FenixChannelRead? read)) return null;

            FenixChannelSnapshot taken = snapshot(read.Display);
            if (!taken.Verdict.Trusted || taken.Frame is null) return null;

            string? text = read.Compose(taken.Frame);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            // A read or a snapshot that throws is one that did not come: the key keeps its AI read, as it does
            // for every failure of the channel. Choose writes no log, so nothing here records the throw; the
            // tests of each read are what keep it from throwing.
            return null;
        }
    }
}

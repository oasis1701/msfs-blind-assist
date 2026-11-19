namespace MSFSBlindAssist.Services;

/// <summary>
/// The word for the APU bleed valve. The Fenix sends FailedOpen for the valve's 3 s in transit, both ways (measured
/// 2026-11-03; every other valve sends Transit), so a pilot reading the APU or BLEED page would hear "failed open" at every
/// APU bleed switch. The word says "in transit" until FailedOpen has stayed for <see cref="TransitMs"/>, and a valve that
/// really stays there then reads "failed open". One lives with the SD window, and only the UI thread calls it.
/// </summary>
internal sealed class FenixChannelApuBleedHold
{
    /// <summary>How long FailedOpen reads as a transit: the measured 3 s, and one of the window's refreshes.</summary>
    public const long TransitMs = 4000;

    // A page is composed about once a second while the SD shows it, so a gap longer than this means the valve went unwatched.
    private const long WatchedGapMs = 2000;

    private static readonly string FailedOpenWord = FenixChannelSdPages.ValveWord(FenixChannelFields.ValveStateEnum.FailedOpen);
    private static readonly string TransitWord = FenixChannelSdPages.ValveWord(FenixChannelFields.ValveStateEnum.Transit);

    private long? _sinceMs;
    private long _lastSeenMs;

    /// <summary>The word to say for <paramref name="word"/>, the valve's word as sent, read at <paramref name="atMs"/>.</summary>
    public string Word(string word, long atMs)
    {
        if (!string.Equals(word, FailedOpenWord, StringComparison.Ordinal))
        {
            _sinceMs = null;
            return word;
        }

        if (_sinceMs is null || atMs - _lastSeenMs > WatchedGapMs) _sinceMs = atMs;
        _lastSeenMs = atMs;

        return atMs - _sinceMs.Value < TransitMs ? TransitWord : word;
    }
}

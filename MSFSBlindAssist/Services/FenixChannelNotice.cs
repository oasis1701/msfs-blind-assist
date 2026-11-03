namespace MSFSBlindAssist.Services;

/// <summary>
/// The one line the Fenix display channel says to a pilot without being asked, once per Fenix load: that the channel
/// cannot be used, so every display uses the AI read and the FMA and E/WD announcements never start. Without it a pilot
/// would wait for announcements that are not coming. The notice judges the monitor's <see cref="FenixChannelSession"/>
/// against a clock reading and says nothing itself: the definition's sampler speaks what it returns (with
/// <c>Announce</c>, which does not interrupt) and writes the same text to the channel's log.
/// </summary>
/// <remarks>
/// <para>When. The notice is due when the build is known and the app has not checked the channel against it (every
/// display then uses the AI read, however much the channel streams); when the channel was refused (the Fenix said no,
/// and the channel is over for the session); and when no frame with data has come <see cref="NoDataAfterMs"/> after
/// the start. It is never due for a checked build whose channel has delivered, and never for a gap after the first
/// data: a stall of a few seconds, a reconnect or a display losing its trust are not news, the displays fall back on
/// their own meanwhile, and a pilot is not told about a session that was working. A build that is not known yet is no
/// news while data flows either: the build is asked for again every 30 s, and the displays wait for it.</para>
/// <para>What it says. "Fenix display channel not available for Fenix build 3.0.0.7613: displays use AI reading; FMA
/// and E/WD announcements are off." The build is spoken as the Fenix writes it; with no build known it is "for this
/// Fenix build". The sentence is made of strings alone, so the PC's culture cannot change a word of it.</para>
/// <para>Once. The first text it returns is the last of the load: it is spent when handed out, whatever the session
/// says next and whether or not it was heard. A new load, which is an aircraft switch and back, makes a new definition
/// with a new monitor and a new notice, so it starts fresh.</para>
/// <para>Threads. The notice remembers two things, that it has been spent and the moment the Fenix was first ready, and
/// its only caller is the sampler on the UI thread, so it takes no lock.</para>
/// </remarks>
internal sealed class FenixChannelNotice
{
    /// <summary>
    /// How long after the start (for <see cref="Sample"/>: after the Fenix is ready) the channel may say nothing before
    /// the notice is due, in milliseconds. At exactly this long it is due, and a millisecond before it is not.
    /// </summary>
    public const long NoDataAfterMs = 60_000;

    private const string WhatItMeans = "displays use AI reading; FMA and E/WD announcements are off.";

    // Whether the text has been handed out, and the first moment the Fenix was ready (Sample).
    private bool _spent;
    private long? _readyAtMs;

    /// <summary>
    /// The text to speak if the session calls for it and the notice has not been spent, else null; the first text it
    /// returns spends it. The session decides alone: the no-data minute is counted from
    /// <see cref="FenixChannelSession.StartedAtMs"/>, so whoever calls this decides what moment that is
    /// (<see cref="Sample"/> counts it from the Fenix being ready). It is due at once for a known build that is not
    /// checked and for a refused channel, and with no data it is due at <see cref="NoDataAfterMs"/> after
    /// <see cref="FenixChannelSession.StartedAtMs"/>. A build that is known is named, checked or not.
    /// </summary>
    /// <param name="s">The monitor's session.</param>
    /// <param name="nowMs">The clock reading, in the clock of the session
    /// (<see cref="Environment.TickCount64"/>).</param>
    public string? Due(FenixChannelSession s, long nowMs)
    {
        if (_spent) return null;

        bool buildNotChecked = s.Build is not null && !s.BuildChecked;
        bool noData = s.FirstDataAtMs is null && nowMs - s.StartedAtMs >= NoDataAfterMs;
        if (!s.Refused && !buildNotChecked && !noData) return null;

        _spent = true;
        string which = s.Build is null ? "this Fenix build" : "Fenix build " + s.Build;
        return "Fenix display channel not available for " + which + ": " + WhatItMeans;
    }

    /// <summary>
    /// <see cref="Due"/> for the sampler of the announcer host, which counts the no-data minute from the Fenix being
    /// ready and not from the monitor's start. The minute is "within 60 s of the Fenix being ready", and the monitor
    /// starts with the first sim update, which can come in the sim's main menu before any Fenix is loaded: counted
    /// from the start, the minute would run out there, speak a notice that is false and spend the one the load has. So
    /// the notice remembers the first <paramref name="nowMs"/> at which <paramref name="fenixReady"/> was true, keeps
    /// it even if the Fenix later stops being ready (its config reads unknown again), and counts from that moment;
    /// while the Fenix has never been ready the clock cannot run out. The unchecked-build and refused paths do not
    /// wait for ready: an answer on the channel proves a Fenix is there.
    /// </summary>
    /// <param name="s">The monitor's session; its <see cref="FenixChannelSession.StartedAtMs"/> is not used
    /// here.</param>
    /// <param name="fenixReady">Whether the Fenix has published its config, which is when the definition knows its
    /// airframe.</param>
    /// <param name="nowMs">The tick's clock reading, in the clock of the session
    /// (<see cref="Environment.TickCount64"/>).</param>
    public string? Sample(FenixChannelSession s, bool fenixReady, long nowMs)
    {
        if (fenixReady) _readyAtMs ??= nowMs;

        return Due(s with { StartedAtMs = _readyAtMs ?? nowMs }, nowMs);
    }
}

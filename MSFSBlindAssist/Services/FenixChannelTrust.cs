namespace MSFSBlindAssist.Services;

/// <summary>
/// The displays of the Fenix display channel that can be read to a pilot, each trusted or not on its own: the
/// primary flight display (PFD), the engine and warning display (E/WD), the system display (SD), the navigation
/// display (ND), the integrated standby instrument system (ISIS) and the datalink control and display unit (DCDU).
/// </summary>
internal enum FenixChannelDisplay { Pfd, Ewd, Sd, Nd, Isis, Dcdu }

/// <summary>
/// The gate's answer for one display: whether the values the channel gave for it may be read to the pilot.
/// <paramref name="Reason"/> is "trusted" when they may, and what stands in the way when they may not.
/// </summary>
internal readonly record struct FenixDisplayVerdict(bool Trusted, string Reason)
{
    /// <summary>
    /// Whether the channel is healthy and no powered unit shows this display: it is dark, or shown only on another unit
    /// (the first officer's PFD or ND, an ECAM on an ND). "display off" is judged only after every channel-wide condition
    /// held, so no channel fault ever reads as "display off".
    /// </summary>
    public bool IsDisplayOff => !Trusted && Reason == FenixChannelTrust.DisplayOffReason;
}

/// <summary>
/// The trust gate of the Fenix display channel: a pure state machine, with the time passed in and no I/O, that
/// decides display by display whether the values decoded from the channel may be read to the pilot. When in doubt a
/// display is not trusted, and its key falls back to the AI read (FNX-22), or opens its window on one status row while
/// no powered unit shows the display (<see cref="FenixDisplayVerdict.IsDisplayOff"/>).
/// </summary>
/// <remarks>
/// <para>A display is trusted only when every channel-wide condition and every condition of its own holds, and
/// <see cref="VerdictFor"/> gives the first one that fails, in this order: not connected; no data message since
/// the connection opened; the stream stalled; the build unknown; the build not one the app has checked; a
/// shape latch; the display off (no powered display unit shows it, or for the ISIS its own message does not say it
/// is powered); for the PFD, a frame with no FMA; a text code the app does not know in a line the display
/// covers.</para>
/// <para>The gate never compares a value of the channel with SimConnect's. It did until 2026-11-04, when test flights
/// showed that what a display draws can differ from SimConnect's value for no fault of the channel, which untrusted a
/// healthy display (FNX-22).</para>
/// <para>A display is live while a powered unit shows it (<see cref="FenixChannelFrame.PfdLive"/> and the others,
/// from the latest frame with its shapes); one shown only on another unit (the first officer's PFD or ND, an ECAM
/// on an ND) counts as off. The ISIS is on no unit: it is live while its own message says it is powered
/// (<see cref="FenixChannelFrame.IsisPowered"/>). A dark display reads "display off" whatever its values say: cold
/// and dark the channel still sends its altitude and heading. The SD, the ND, the ISIS and the DCDU have no other
/// condition of their own. The text of the ND, the ISIS and the DCDU is not judged here, but by each feature's
/// decoder.</para>
/// <para>A frame with no FMA slot at all is not a wrong shape: protobuf-net leaves out an empty or null array, so
/// an unpowered PFD can send none. The PFD alone reads "no FMA" until a frame brings its slots again; it never
/// latches, and no other display is touched by it.</para>
/// <para>A malformed message, or a field in the wrong shape, in a data message or an ECAM entries message
/// latches the whole channel until <see cref="OnConnected"/>: a wrong shape on a checked build means the check
/// itself cannot be trusted. The latch keeps its first cause. A push with no display data
/// (<see cref="FenixChannelFrame.HasData"/> false) is not a message at all and changes nothing. An unknown text code
/// untrusts a display too but never latches: it is an exact condition, not a noisy one.</para>
/// <para>Not thread-safe: the monitor locks around every call.</para>
/// </remarks>
internal sealed class FenixChannelTrust
{
    /// <summary>How long after the last data message the stream counts as stalled.</summary>
    public const long StreamStaleMs = 2_000;

    private const string TrustedReason = "trusted";

    internal const string DisplayOffReason = "display off";

    private const string NoFmaReason = "no FMA";

    private readonly IReadOnlySet<string> _verifiedBuilds;

    // Everything below belongs to one connection: OnConnected replaces it.
    private bool _connected;
    private string? _disconnectReason;
    private string? _build;
    private bool _haveFrame;
    private long _lastFrameMs;
    private string? _shapeReason;
    private bool _pfdLive;                   // a powered unit shows the PFD, in the latest good frame
    private bool _ewdLive;                   // the same for the E/WD
    private bool _sdLive;                    // and for the SD
    private bool _ndLive;                    // and for the ND
    private bool _dcduLive;                  // and for the DCDU
    private bool _isisLive;                  // the ISIS says it is powered, in the latest good frame: it is on no unit
    private bool _noFma;                     // the latest good frame had no FMA slot: the PFD has nothing to read
    private string? _fmaUnknownCode;         // the reason for the first unknown code in the latest good frame's FMA
    private string? _ewdUnknownCode;         // the same for the lines the E/WD covers

    /// <param name="verifiedBuilds">
    /// The Fenix builds the channel has been checked against, compared exactly as given (the set's own
    /// comparer). A build outside it is not trusted.
    /// </param>
    public FenixChannelTrust(IReadOnlySet<string> verifiedBuilds)
    {
        ArgumentNullException.ThrowIfNull(verifiedBuilds);
        _verifiedBuilds = verifiedBuilds;
    }

    /// <summary>
    /// The data call is open. Starts the connection fresh: the build unknown, no data message yet, no display live
    /// and no latch. <paramref name="nowMs"/> goes unused: "no data yet" is judged by whether a message has come, not
    /// by how long ago the call opened.
    /// </summary>
    public void OnConnected(long nowMs)
    {
        _connected = true;
        _disconnectReason = null;
        _build = null;
        _haveFrame = false;
        _lastFrameMs = 0;
        _shapeReason = null;
        _pfdLive = false;
        _ewdLive = false;
        _sdLive = false;
        _ndLive = false;
        _dcduLive = false;
        _isisLive = false;
        _noFma = false;
        _fmaUnknownCode = null;
        _ewdUnknownCode = null;
    }

    /// <summary>The data call is not answering, for <paramref name="reason"/>: no display is trusted until the next <see cref="OnConnected"/>.</summary>
    public void OnDisconnected(string reason)
    {
        _connected = false;
        _disconnectReason = reason;
    }

    /// <summary>The build the build query answered, or null when it did not, which leaves the build unknown.</summary>
    public void OnBuild(string? build) => _build = build;

    /// <summary>
    /// One decoded data message, received at <paramref name="nowMs"/>: refreshes the stream, latches a bad shape, and
    /// takes which displays are live and what their lines hold. A frame that is not
    /// <see cref="FenixChannelFrame.ShapesOk"/> gives no values: the latch already holds every display.
    /// </summary>
    public void OnFrame(FenixChannelFrame frame, long nowMs)
    {
        // A push with nothing in it is neither damaged nor a sign of life: no shape check and no stream refresh, so
        // five seconds of them leave a trusted gate stalled but never latched.
        if (!frame.HasData) return;

        _haveFrame = true;
        _lastFrameMs = nowMs;

        if (!frame.ShapesOk)
        {
            Latch(frame.Malformed ? "malformed message" : $"field {frame.WrongShapeFields[0]} has the wrong shape");
            return;
        }

        ReadFrame(frame);
    }

    /// <summary>
    /// One decoded ECAM entries message. Only its shape matters here: a bad one latches the whole channel, with
    /// a reason of its own so it cannot be taken for a fault of the display data. It is not a data message, so
    /// it neither refreshes the stream nor ends "no data yet".
    /// </summary>
    public void OnEcamEntries(FenixEcamEntries entries)
    {
        if (entries.ShapesOk) return;

        Latch(entries.Malformed
            ? "malformed ECAM entries message"
            : $"ECAM entries field {entries.WrongShapeFields[0]} has the wrong shape");
    }

    /// <summary>
    /// Whether <paramref name="display"/> may be read at <paramref name="nowMs"/>, and the first reason it
    /// may not. A display the gate does not know is not trusted.
    /// </summary>
    public FenixDisplayVerdict VerdictFor(FenixChannelDisplay display, long nowMs)
    {
        string? reason = ChannelReason(nowMs) ?? DisplayReason(display);
        return reason is null ? new FenixDisplayVerdict(true, TrustedReason) : new FenixDisplayVerdict(false, reason);
    }

    // ---- The verdict ----------------------------------------------------------------------------

    // The first channel-wide condition that fails, or null when they all hold.
    private string? ChannelReason(long nowMs)
    {
        if (!_connected)
        {
            return string.IsNullOrWhiteSpace(_disconnectReason)
                ? "channel not answering"
                : $"channel not answering: {_disconnectReason}";
        }

        if (!_haveFrame) return "no data yet";
        if (nowMs - _lastFrameMs > StreamStaleMs) return "stream stalled";
        if (_build is null) return "build unknown";
        if (!_verifiedBuilds.Contains(_build)) return $"build {_build} not checked";
        return _shapeReason;
    }

    // The first of the display's own conditions that fails, or null when they all hold.
    private string? DisplayReason(FenixChannelDisplay display)
    {
        bool live;
        string? nothingToRead;
        string? unknownCode;
        switch (display)
        {
            case FenixChannelDisplay.Pfd:
                live = _pfdLive;
                nothingToRead = _noFma ? NoFmaReason : null;
                unknownCode = _fmaUnknownCode;
                break;
            case FenixChannelDisplay.Ewd:
                live = _ewdLive;
                nothingToRead = null;
                unknownCode = _ewdUnknownCode;
                break;
            case FenixChannelDisplay.Sd:
                // No text to read here: being live is its one condition.
                live = _sdLive;
                nothingToRead = null;
                unknownCode = null;
                break;
            case FenixChannelDisplay.Nd:
                // As for the SD: being live is its one condition, and its text is its feature's decoder's to judge.
                live = _ndLive;
                nothingToRead = null;
                unknownCode = null;
                break;
            case FenixChannelDisplay.Isis:
                // As for the SD: being live (powered) is its one condition, and its text is its feature's.
                live = _isisLive;
                nothingToRead = null;
                unknownCode = null;
                break;
            case FenixChannelDisplay.Dcdu:
                // As for the SD: being live is its one condition, and its text is its feature's.
                live = _dcduLive;
                nothingToRead = null;
                unknownCode = null;
                break;
            default:
                return "not a display of the channel";
        }

        // First: a display no powered unit shows has nothing a pilot could hear, whatever its values say (cold and
        // dark, the channel still sends its altitude and heading).
        if (!live) return DisplayOffReason;

        // Before the text codes: a PFD frame with no FMA has no text to look at.
        return nothingToRead ?? unknownCode;
    }

    // The first cause stays: a later fault does not replace it.
    private void Latch(string reason) => _shapeReason ??= reason;

    // Takes which displays are live and the first unknown code of each display's lines, from a frame that has its
    // shapes.
    private void ReadFrame(FenixChannelFrame frame)
    {
        _pfdLive = frame.PfdLive;
        _ewdLive = frame.EwdLive;
        _sdLive = frame.SdLive;
        _ndLive = frame.NdLive;
        _dcduLive = frame.DcduLive;
        _isisLive = frame.IsisPowered;

        _noFma = frame.Fma.Count == 0;
        _fmaUnknownCode = FirstUnknownCode(frame.Fma.Select(slot => slot.Text), "FMA");
        _ewdUnknownCode = FirstUnknownCode(frame.UpperEcamLeft.Concat(frame.UpperEcamRight), "upper ECAM")
            ?? UnknownCodeIn(FenixChannelText.Parse(frame.FobText), "FOB line");
    }

    // ---- Unknown text codes ---------------------------------------------------------------------

    // The reason for the first letter the text parser does not know, over the lines in order, or null.
    private static string? FirstUnknownCode(IEnumerable<string?> lines, string where)
    {
        foreach (string? line in lines)
        {
            string? reason = UnknownCodeIn(FenixChannelText.Parse(line), where);
            if (reason is not null) return reason;
        }

        return null;
    }

    private static string? UnknownCodeIn(FenixTextLine line, string where) =>
        line.UnknownCodes.Count == 0 ? null : $"unknown code '{line.UnknownCodes[0]}' in the {where}";
}

using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Utils;

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
internal readonly record struct FenixDisplayVerdict(bool Trusted, string Reason);

/// <summary>
/// The values the channel gives that SimConnect also knows, which the gate compares: the two flight director
/// lights (SimConnect's raw value, 0 or 1), the true altitude in feet (PLANE ALTITUDE, never the indicated altitude,
/// FNX-25), the magnetic heading in degrees and the total fuel in kilograms.
/// </summary>
internal enum FenixComparison { FdLightCapt, FdLightFo, AltitudeFt, MagHeadingDeg, FuelKg }

/// <summary>
/// The trust gate of the Fenix display channel: a pure state machine, with the time passed in and no I/O, that
/// decides display by display whether the values decoded from the channel may be read to the pilot. Its whole
/// purpose is that a Fenix update can never make the app speak a wrong value: when in doubt a display is not
/// trusted, and its key falls back to the AI read (FNX-22).
/// </summary>
/// <remarks>
/// <para>A display is trusted only when every channel-wide condition and every condition of its own holds, and
/// <see cref="VerdictFor"/> gives the first one that fails, in this order: not connected; no data message since
/// the connection opened; the stream stalled; the build unknown; the build not one the app has checked; a
/// shape latch; the display off (no powered display unit shows it, or for the ISIS its own message does not say it
/// is powered); the display's cross-checks, in their order; for the PFD, a frame with no FMA; a text code the app
/// does not know in a line the display covers.</para>
/// <para>A display is live while a powered unit shows it (<see cref="FenixChannelFrame.PfdLive"/> and the others,
/// from the latest frame with its shapes); one shown only on another unit (the first officer's PFD or ND, an ECAM
/// on an ND) counts as off. The ISIS is on no unit: it is live while its own message says it is powered
/// (<see cref="FenixChannelFrame.IsisPowered"/>). A dark display reads "display off" whatever its values say: cold
/// and dark the channel still sends the sim's altitude and heading, which agree. The SD, the ISIS and the DCDU have
/// no other condition of their own. The ND has the heading cross-check the PFD has, the one check and not a copy of
/// it, so a heading that disagrees untrusts both and a heading that latched holds both; it has no other. The text
/// of the ND, the ISIS and the DCDU is not judged here, but by each feature's decoder. The cross-checks run on every
/// frame, live or not, so a disagreement or a latch built while a display was off shows as soon as it is live.</para>
/// <para>A frame with no FMA slot at all is not a wrong shape: protobuf-net leaves out an empty or null array, so
/// an unpowered PFD can send none. The PFD alone reads "no FMA" until a frame brings its slots again; it never
/// latches, and no other display is touched by it.</para>
/// <para>A malformed message, or a field in the wrong shape, in a data message or an ECAM entries message
/// latches the whole channel until <see cref="OnConnected"/>: a wrong shape on a checked build means the check
/// itself cannot be trusted. The latch keeps its first cause. A push with no display data
/// (<see cref="FenixChannelFrame.HasData"/> false) is not a message at all and changes nothing.</para>
/// <para>A cross-check compares a value of the channel with SimConnect's on every data message. SimConnect
/// delivers about once a second, so the channel's value is the one it showed when SimConnect's arrived (the
/// kept message nearest that arrival, for <see cref="ComparisonWindowMs"/>), not the one it shows now: a turn
/// or a climb moves both, and the newest message would make that lag a disagreement. A SimConnect value older
/// than the window has not changed, since an unchanged value is not delivered again, and is compared with the
/// channel's value now. Agreement marks the check passed, and a display whose check has never passed is not
/// trusted.</para>
/// <para>A disagreement untrusts its display, and stays in force until a message agrees or the channel
/// reconnects. A value that is missing makes the check wait: SimConnect has not delivered it, an FD light reads
/// neither 0 nor 1 or the annunciator light test is on (<see cref="OnLightTest"/>), a channel value is not finite,
/// or the FOB text is not a whole number of KG or LBS. Waiting neither passes nor fails and does not end a
/// disagreement, and a check that passed before with none in force still counts as passed. Once a disagreement
/// has lasted <see cref="MismatchHoldMs"/> without a break it latches until reconnect. A waiting value is a break,
/// and so is a stall of the stream (a data message more than <see cref="StreamStaleMs"/> after the one before it):
/// either one only restarts that count. An unknown text code untrusts a display too but never latches: it is an
/// exact condition, not a noisy one.</para>
/// <para>Not thread-safe: the monitor locks around every call.</para>
/// </remarks>
internal sealed class FenixChannelTrust
{
    /// <summary>How long after the last data message the stream counts as stalled.</summary>
    public const long StreamStaleMs = 2_000;

    /// <summary>
    /// How long a disagreement must last, without a break, to latch its display until reconnect. A waiting
    /// value or a stall of the stream is a break: it restarts the count, and the disagreement stays in force.
    /// </summary>
    public const long MismatchHoldMs = 3_000;

    /// <summary>
    /// How far from a data message SimConnect's value may have arrived to be compared with the channel as it
    /// was then. SimConnect's batch comes about once a second, so a value up to a second old is the normal
    /// case. A value older than this has not changed: it is compared with the channel now.
    /// </summary>
    public const long ComparisonWindowMs = 1_500;

    // The starting tolerances, pinned only after the in-sim measurements. A Fenix update never loosens one to
    // make a build pass (FNX-23).

    /// <summary>The altitude the channel and SimConnect may differ by, in feet.</summary>
    public const double AltitudeToleranceFt = 100;

    /// <summary>The heading they may differ by on the circle, in degrees: 359 and 1 are 2 apart.</summary>
    public const double HeadingToleranceDeg = 3;

    /// <summary>The fuel they may differ by, in kilograms, whenever that is more than the percentage.</summary>
    public const double FuelToleranceKg = 100;

    /// <summary>The fuel they may differ by, as a percentage of SimConnect's value, whenever that is more.</summary>
    public const double FuelTolerancePercent = 2;

    /// <summary>Pounds in a kilogram, to read a FOB line given in LBS in the kilograms SimConnect's fuel is in.</summary>
    public const double PoundsPerKilogram = 2.20462262;

    private const string TrustedReason = "trusted";

    private const string DisplayOffReason = "display off";

    private const string NoFmaReason = "no FMA";

    // The amount and unit of the FOB line, run over the line's text with its format codes taken out, as in
    // "FOB:   2940  KG". The amount is whole digits only: a decimal point or a grouping separator ("12,340",
    // "12 340") does not match, so a FOB of ten tonnes or more written with one reads as missing and never as
    // a wrong value (a lone comma would read as a decimal: 12.34).
    private static readonly Regex FobPattern =
        new(@"FOB:\s*([0-9]+)\s*(KG|LBS)", RegexOptions.CultureInvariant);

    // The checks of each display, in the order they are judged. The ND has the PFD's heading check: one Check, so what
    // it finds holds both. The SD, the ISIS and the DCDU have none in this set.
    private static readonly FenixComparison[] PfdChecks =
    {
        FenixComparison.FdLightCapt, FenixComparison.FdLightFo, FenixComparison.AltitudeFt, FenixComparison.MagHeadingDeg
    };

    private static readonly FenixComparison[] EwdChecks = { FenixComparison.FuelKg };

    private static readonly FenixComparison[] NdChecks = { FenixComparison.MagHeadingDeg };

    private static readonly FenixComparison[] NoChecks = Array.Empty<FenixComparison>();

    private static readonly int ComparisonCount = Enum.GetValues<FenixComparison>().Length;

    private readonly IReadOnlySet<string> _verifiedBuilds;

    // SimConnect's side of each comparison and when it arrived, indexed by FenixComparison; NaN is missing. A
    // reconnect keeps both, since SimConnect keeps delivering through it (and does not repeat a value that has
    // not changed).
    private readonly double[] _sim = Missing();
    private readonly long[] _simAtMs = new long[ComparisonCount];

    // SimConnect's too: whether the annunciator light test is on (OnLightTest). Kept across reconnects for the same
    // reason: a switch that stays at Test is not delivered again.
    private bool _lightTest;

    // SimConnect's deliveries since DescribeSimDeliveries last ran: how many came, and the longest interval between
    // one of them and the delivery before it (null while no gap has been measured). The time of the latest delivery
    // ever (null before the first) carries over those calls, so a gap that spans one counts in the next period. Not
    // reset by a reconnect: they are SimConnect's, not the channel's.
    private int _deliveries;
    private long? _longestDeliveryGapMs;
    private long? _lastDeliveryMs;

    // Everything below belongs to one connection: OnConnected replaces it.
    private bool _connected;
    private string? _disconnectReason;
    private string? _build;
    private bool _haveFrame;
    private long _lastFrameMs;
    private string? _shapeReason;
    private double[] _channel = Missing();   // the channel's side of each comparison, from the latest good frame
    private bool _pfdLive;                   // a powered unit shows the PFD, in the latest good frame
    private bool _ewdLive;                   // the same for the E/WD
    private bool _sdLive;                    // and for the SD
    private bool _ndLive;                    // and for the ND
    private bool _dcduLive;                  // and for the DCDU
    private bool _isisLive;                  // the ISIS says it is powered, in the latest good frame: it is on no unit
    private bool _noFma;                     // the latest good frame had no FMA slot: the PFD has nothing to read
    private string? _fmaUnknownCode;         // the reason for the first unknown code in the latest good frame's FMA
    private string? _ewdUnknownCode;         // the same for the lines the E/WD covers
    private Check[] _checks = NewChecks();

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
    /// The data call is open. Starts the connection fresh: the build unknown, no data message yet, no display live,
    /// no latch and no check passed. SimConnect's values stay. <paramref name="nowMs"/> goes unused: "no data yet"
    /// is judged by whether a message has come, not by how long ago the call opened.
    /// </summary>
    public void OnConnected(long nowMs)
    {
        _connected = true;
        _disconnectReason = null;
        _build = null;
        _haveFrame = false;
        _lastFrameMs = 0;
        _shapeReason = null;
        _channel = Missing();
        _pfdLive = false;
        _ewdLive = false;
        _sdLive = false;
        _ndLive = false;
        _dcduLive = false;
        _isisLive = false;
        _noFma = false;
        _fmaUnknownCode = null;
        _ewdUnknownCode = null;
        _checks = NewChecks();
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
    /// One decoded data message, received at <paramref name="nowMs"/>: refreshes the stream, latches a bad
    /// shape, and runs each cross-check against SimConnect's latest values. A message that comes more than
    /// <see cref="StreamStaleMs"/> after the one before it first restarts every hold clock (a disagreement
    /// stays in force). A frame that is not <see cref="FenixChannelFrame.ShapesOk"/> gives no values: the latch
    /// already holds every display, and the defaults it carries would only disagree.
    /// </summary>
    public void OnFrame(FenixChannelFrame frame, long nowMs)
    {
        // A push with nothing in it is neither damaged nor a sign of life: no shape check, no stream refresh
        // and no cross-check, so five seconds of them leave a trusted gate stalled but never latched.
        if (!frame.HasData) return;

        // A stall is a break in what the gate has seen, as a waiting value is: the run a disagreement has built
        // toward MismatchHoldMs counts only what was seen without a gap, so it starts again. The same limit as
        // the verdict's "stream stalled". Only the hold clocks restart: a disagreement stays in force until a
        // message agrees, a check that passed stays passed and a latch stays latched.
        if (_haveFrame && nowMs - _lastFrameMs > StreamStaleMs)
        {
            foreach (Check check in _checks)
            {
                check.HoldStartMs = null;
            }
        }

        _haveFrame = true;
        _lastFrameMs = nowMs;

        if (!frame.ShapesOk)
        {
            Latch(frame.Malformed ? "malformed message" : $"field {frame.WrongShapeFields[0]} has the wrong shape");
            return;
        }

        ReadFrame(frame, nowMs);
        for (int i = 0; i < ComparisonCount; i++)
        {
            Evaluate((FenixComparison)i, nowMs);
        }
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
    /// SimConnect's latest value for one comparison, which arrived at <paramref name="nowMs"/>; NaN means it
    /// has none. The gate runs the checks on the next data message, against the channel as it was when this
    /// arrived (<see cref="ComparisonWindowMs"/>). Kept, with its arrival time, across reconnects. Each arrival is
    /// counted for <see cref="DescribeSimDeliveries"/>.
    /// </summary>
    public void OnComparison(FenixComparison kind, double value, long nowMs)
    {
        int index = (int)kind;
        if ((uint)index < (uint)_sim.Length)
        {
            _sim[index] = value;
            _simAtMs[index] = nowMs;

            if (_lastDeliveryMs is long lastMs)
            {
                long gapMs = Math.Max(0, nowMs - lastMs);
                _longestDeliveryGapMs = Math.Max(_longestDeliveryGapMs ?? 0, gapMs);
            }

            _lastDeliveryMs = nowMs;
            _deliveries++;
        }
    }

    /// <summary>
    /// Whether the annunciator light test is on, from SimConnect's light switch. The test lights both FD lamps
    /// whatever the FD buttons say, while the channel's FD flags follow the buttons, so while it is on both FD checks
    /// wait: the test's lamp is never a disagreement, a check that passed stays passed, and, as for any waiting
    /// value, a disagreement already in force stays in force and its hold clock restarts. No other check is touched.
    /// Kept across reconnects, as SimConnect's values are.
    /// </summary>
    public void OnLightTest(bool testing) => _lightTest = testing;

    /// <summary>
    /// Whether <paramref name="display"/> may be read at <paramref name="nowMs"/>, and the first reason it
    /// may not. A display the gate does not know is not trusted.
    /// </summary>
    public FenixDisplayVerdict VerdictFor(FenixChannelDisplay display, long nowMs)
    {
        string? reason = ChannelReason(nowMs) ?? DisplayReason(display);
        return reason is null ? new FenixDisplayVerdict(true, TrustedReason) : new FenixDisplayVerdict(false, reason);
    }

    /// <summary>
    /// One line for each comparison, in the invariant culture, with the channel's latest value beside
    /// SimConnect's latest, as both stand now (not aligned in time, as the checks compare them):
    /// "altitude: channel 1,023 ft, sim 1,020 ft". A value that is missing reads "missing".
    /// </summary>
    public IReadOnlyList<string> DescribeComparisons()
    {
        var lines = new string[ComparisonCount];
        for (int i = 0; i < lines.Length; i++)
        {
            var kind = (FenixComparison)i;
            lines[i] = $"{NameOf(kind)}: {Pair(kind, _channel[i], _sim[i])}";
        }

        return lines;
    }

    /// <summary>
    /// SimConnect's deliveries since the last call, for the comparisons line: "SimConnect: 42 values, longest gap
    /// 1.1 s". The count is every arrival of the five comparisons (<see cref="OnComparison"/>) in the period. The gap
    /// is the longest interval between an arrival of the period and the arrival before it, in seconds to a tenth, in
    /// the invariant culture. The arrival before the period's first is the previous period's last, so a stall that
    /// spans a call shows in the next line ("SimConnect: 1 value, longest gap 6.0 s"). "no gap" when the period has
    /// no arrival ("SimConnect: 0 values, no gap"), or its only arrival is the first ever ("SimConnect: 1 value, no
    /// gap"). The gate treats an old SimConnect value as unchanged, so a long gap (a UI stall in a turn) could become
    /// a false disagreement: this is how a flight measures it. A value is delivered only when it changes ([SIM-7]),
    /// so a long gap is a stall only while a compared value is moving (the heading in a turn, the fuel with the
    /// engines running); parked, or cold and dark, long gaps are normal. Side effect: each call starts a new period,
    /// so the next call counts only what arrives after this one; the time of the last arrival carries over.
    /// </summary>
    public string DescribeSimDeliveries()
    {
        string count = _deliveries == 1
            ? "1 value"
            : _deliveries.ToString(CultureInfo.InvariantCulture) + " values";
        string gap = _longestDeliveryGapMs is long gapMs
            ? "longest gap " + (gapMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : "no gap";

        _deliveries = 0;
        _longestDeliveryGapMs = null;
        return "SimConnect: " + count + ", " + gap;
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
        FenixComparison[] checks;
        string? nothingToRead;
        string? unknownCode;
        switch (display)
        {
            case FenixChannelDisplay.Pfd:
                live = _pfdLive;
                checks = PfdChecks;
                nothingToRead = _noFma ? NoFmaReason : null;
                unknownCode = _fmaUnknownCode;
                break;
            case FenixChannelDisplay.Ewd:
                live = _ewdLive;
                checks = EwdChecks;
                nothingToRead = null;
                unknownCode = _ewdUnknownCode;
                break;
            case FenixChannelDisplay.Sd:
                // Nothing in this set to cross-check and no text to read: being live is its one condition.
                live = _sdLive;
                checks = NoChecks;
                nothingToRead = null;
                unknownCode = null;
                break;
            case FenixChannelDisplay.Nd:
                // The heading check the PFD has, and no more: its text is its feature's decoder's to judge, not the
                // gate's.
                live = _ndLive;
                checks = NdChecks;
                nothingToRead = null;
                unknownCode = null;
                break;
            case FenixChannelDisplay.Isis:
                // As for the SD: being live (powered) is its one condition, and its text is its feature's.
                live = _isisLive;
                checks = NoChecks;
                nothingToRead = null;
                unknownCode = null;
                break;
            case FenixChannelDisplay.Dcdu:
                // As for the SD: being live is its one condition, and its text is its feature's.
                live = _dcduLive;
                checks = NoChecks;
                nothingToRead = null;
                unknownCode = null;
                break;
            default:
                return "not a display of the channel";
        }

        // First: a display no powered unit shows has nothing a pilot could hear, whatever its values say (cold and
        // dark, the channel still sends the sim's altitude and heading). Its checks have run all the same, so what
        // they found shows from its first live frame.
        if (!live) return DisplayOffReason;

        // The first check that is not OK gives the reason, whatever kind of fault it is: latched, else a
        // disagreement in force, else never passed. A check that passed with no disagreement in force is OK,
        // waiting or not.
        foreach (FenixComparison kind in checks)
        {
            Check check = _checks[(int)kind];
            if (check.Latched) return $"{NameOf(kind)} latched until reconnect: {check.Pair}";
            if (check.Disagreeing) return $"{NameOf(kind)} disagrees: {check.Pair}";
            if (!check.Passed) return $"{NameOf(kind)} not checked yet";
        }

        // After the cross-checks and before the text codes: a PFD frame with no FMA has no text to look at.
        return nothingToRead ?? unknownCode;
    }

    // ---- The checks -----------------------------------------------------------------------------

    // The first cause stays: a later fault does not replace it.
    private void Latch(string reason) => _shapeReason ??= reason;

    // Takes which displays are live, the channel's side of every comparison, and the first unknown code of each
    // display's lines, from a frame that has its shapes, and keeps each comparison's value for ComparisonWindowMs.
    private void ReadFrame(FenixChannelFrame frame, long nowMs)
    {
        _pfdLive = frame.PfdLive;
        _ewdLive = frame.EwdLive;
        _sdLive = frame.SdLive;
        _ndLive = frame.NdLive;
        _dcduLive = frame.DcduLive;
        _isisLive = frame.IsisPowered;

        FenixTextLine fob = FenixChannelText.Parse(frame.FobText);

        _channel[(int)FenixComparison.FdLightCapt] = frame.Fd.FdOnCapt ? 1 : 0;
        _channel[(int)FenixComparison.FdLightFo] = frame.Fd.FdOnFo ? 1 : 0;
        _channel[(int)FenixComparison.AltitudeFt] = frame.Altitude;
        _channel[(int)FenixComparison.MagHeadingDeg] = frame.MagHeading;
        _channel[(int)FenixComparison.FuelKg] = FuelKgOf(fob);

        for (int i = 0; i < ComparisonCount; i++)
        {
            Keep(_checks[i], nowMs, _channel[i]);
        }

        _noFma = frame.Fma.Count == 0;
        _fmaUnknownCode = FirstUnknownCode(frame.Fma.Select(slot => slot.Text), "FMA");
        _ewdUnknownCode = FirstUnknownCode(frame.UpperEcamLeft.Concat(frame.UpperEcamRight), "upper ECAM")
            ?? UnknownCodeIn(fob, "FOB line");
    }

    // One check, on one data message: waiting, agreeing or disagreeing. A disagreement is a state of its own
    // (Disagreeing) that only an agreeing message ends. The hold clock is what a waiting message or a stall
    // restarts: it counts how long the disagreeing messages have run unbroken.
    private void Evaluate(FenixComparison kind, long nowMs)
    {
        Check check = _checks[(int)kind];
        if (check.Latched) return;   // until reconnect: nothing that follows changes it, not even the values its reason names

        double sim = _sim[(int)kind];
        double channel = ChannelAt(check, _channel[(int)kind], _simAtMs[(int)kind], nowMs);

        if (IsWaiting(kind, channel, sim))
        {
            check.HoldStartMs = null;   // a break in the run; a disagreement in force stays in force
            return;
        }

        if (Agrees(kind, channel, sim))
        {
            check.Passed = true;
            check.Disagreeing = false;
            check.HoldStartMs = null;
            return;
        }

        check.Disagreeing = true;
        check.Pair = Pair(kind, channel, sim);
        check.HoldStartMs ??= nowMs;
        if (nowMs - check.HoldStartMs.Value >= MismatchHoldMs)
        {
            check.Latched = true;
        }
    }

    // The channel's value to compare with SimConnect's latest one, arrived at simAtMs. SimConnect delivers about
    // once a second, so when that arrived within ComparisonWindowMs of this message it is what the channel showed
    // in the kept message nearest its arrival (the later one on a tie), not what it shows now: a turn or a climb
    // moves both. An older SimConnect value has not changed, since an unchanged value is not delivered again, and
    // the channel's value now is the one to compare. The kept values include this message's own.
    private static double ChannelAt(Check check, double current, long simAtMs, long nowMs)
    {
        if (nowMs - simAtMs > ComparisonWindowMs) return current;

        double nearest = current;
        long nearestGapMs = long.MaxValue;
        foreach ((long ms, double value) in check.Recent)
        {
            long gapMs = Math.Abs(ms - simAtMs);
            if (gapMs <= nearestGapMs)
            {
                nearestGapMs = gapMs;
                nearest = value;
            }
        }

        return nearest;
    }

    // Keeps the channel's value from this message for ComparisonWindowMs, and drops what is older.
    private static void Keep(Check check, long nowMs, double value)
    {
        check.Recent.Add((nowMs, value));

        int stale = 0;
        while (stale < check.Recent.Count && check.Recent[stale].Ms < nowMs - ComparisonWindowMs)
        {
            stale++;
        }

        check.Recent.RemoveRange(0, stale);
    }

    // Either value is missing (SimConnect has not delivered it, or a value is not finite), or an FD light says
    // nothing about its flight director: it reads anything but exactly 0 or 1, or the annunciator light test is on,
    // which lights both lamps whatever the FD buttons say.
    private bool IsWaiting(FenixComparison kind, double channel, double sim) =>
        !double.IsFinite(channel) || !double.IsFinite(sim)
        || (kind is FenixComparison.FdLightCapt or FenixComparison.FdLightFo && (_lightTest || (sim != 0 && sim != 1)));

    // Both values present, and within the tolerance.
    private static bool Agrees(FenixComparison kind, double channel, double sim) => kind switch
    {
        FenixComparison.FdLightCapt or FenixComparison.FdLightFo => channel == sim,
        FenixComparison.AltitudeFt => Math.Abs(channel - sim) <= AltitudeToleranceFt,
        FenixComparison.MagHeadingDeg => CircleDifference(channel, sim) <= HeadingToleranceDeg,
        FenixComparison.FuelKg => Math.Abs(channel - sim)
            <= Math.Max(FuelToleranceKg, Math.Abs(sim) * FuelTolerancePercent / 100),
        _ => false
    };

    // The angle between two headings on the circle, 0 to 180.
    private static double CircleDifference(double a, double b)
    {
        double difference = Math.Abs(a - b) % 360;
        return Math.Min(difference, 360 - difference);
    }

    // The FOB line's amount in kilograms, or NaN when the line holds no amount and unit the app can read.
    private static double FuelKgOf(FenixTextLine fobLine)
    {
        Match match = FobPattern.Match(fobLine.JoinedText);
        if (!match.Success || !NumberParsing.TryParseExternal(match.Groups[1].Value, out double amount))
        {
            return double.NaN;
        }

        return match.Groups[2].Value == "LBS" ? amount / PoundsPerKilogram : amount;
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

    // ---- Wording --------------------------------------------------------------------------------

    private static string NameOf(FenixComparison kind) => kind switch
    {
        FenixComparison.FdLightCapt => "FD captain",
        FenixComparison.FdLightFo => "FD first officer",
        FenixComparison.AltitudeFt => "altitude",
        FenixComparison.MagHeadingDeg => "heading",
        FenixComparison.FuelKg => "fuel",
        _ => kind.ToString()
    };

    // Both sides of one comparison: "channel 1,023 ft, sim 1,020 ft".
    private static string Pair(FenixComparison kind, double channel, double sim) =>
        $"channel {Format(kind, channel)}, sim {Format(kind, sim)}";

    // One value in the invariant culture, never the PC's: altitude (ft) and fuel (kg) whole, heading (deg) to a
    // tenth, each with its unit, a flight director as on or off. A light that reads neither 0 nor 1 shows its
    // raw value.
    private static string Format(FenixComparison kind, double value)
    {
        if (!double.IsFinite(value)) return "missing";

        switch (kind)
        {
            case FenixComparison.FdLightCapt:
            case FenixComparison.FdLightFo:
                if (value == 1) return "on";
                if (value == 0) return "off";
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            case FenixComparison.AltitudeFt:
                return value.ToString("N0", CultureInfo.InvariantCulture) + " ft";
            case FenixComparison.MagHeadingDeg:
                return value.ToString("0.0", CultureInfo.InvariantCulture) + " deg";
            case FenixComparison.FuelKg:
                return value.ToString("N0", CultureInfo.InvariantCulture) + " kg";
            default:
                return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    // ---- State ----------------------------------------------------------------------------------

    private static double[] Missing()
    {
        var values = new double[ComparisonCount];
        Array.Fill(values, double.NaN);
        return values;
    }

    private static Check[] NewChecks()
    {
        var checks = new Check[ComparisonCount];
        for (int i = 0; i < checks.Length; i++)
        {
            checks[i] = new Check();
        }

        return checks;
    }

    // One cross-check's state for one connection.
    private sealed class Check
    {
        /// <summary>The values have agreed at least once.</summary>
        public bool Passed;

        /// <summary>A disagreement is in force: until a message agrees, whatever else the messages say.</summary>
        public bool Disagreeing;

        /// <summary>
        /// When the run of disagreeing messages now counting toward <see cref="MismatchHoldMs"/> began; null when
        /// none is running. A waiting message or a stall of the stream ends the run, not the disagreement.
        /// </summary>
        public long? HoldStartMs;

        /// <summary>A disagreement lasted <see cref="MismatchHoldMs"/>: not trusted until reconnect.</summary>
        public bool Latched;

        /// <summary>"channel 1,023 ft, sim 2,050 ft": the values last compared in the disagreement, or that latched it.</summary>
        public string Pair = "";

        /// <summary>The channel's value in each message of the last <see cref="ComparisonWindowMs"/>, with its time.</summary>
        public readonly List<(long Ms, double Value)> Recent = new();
    }
}

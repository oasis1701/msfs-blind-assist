using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The displays of the Fenix display channel that can be read to a pilot, each trusted or not on its own: the
/// primary flight display, the engine and warning display (E/WD) and the system display (SD).
/// </summary>
internal enum FenixChannelDisplay { Pfd, Ewd, Sd }

/// <summary>
/// The gate's answer for one display: whether the values the channel gave for it may be read to the pilot.
/// <paramref name="Reason"/> is "trusted" when they may, and what stands in the way when they may not.
/// </summary>
internal readonly record struct FenixDisplayVerdict(bool Trusted, string Reason);

/// <summary>
/// The values the channel gives that SimConnect also knows, which the gate compares: the two flight director
/// lights (SimConnect's raw value, 0 or 1), the indicated altitude in feet, the magnetic heading in degrees
/// and the total fuel in kilograms.
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
/// shape latch; the display's cross-checks, in their order; a text code the app does not know in a line the
/// display covers.</para>
/// <para>A malformed message, or a field in the wrong shape, in a data message or an ECAM entries message
/// latches the whole channel until <see cref="OnConnected"/>: a wrong shape on a checked build means the check
/// itself cannot be trusted. The latch keeps its first cause. A push with no display data
/// (<see cref="FenixChannelFrame.HasData"/> false) is not a message at all and changes nothing.</para>
/// <para>A cross-check compares a value of the channel with SimConnect's on every data message. Agreement marks
/// it passed, and a display whose check has never passed is not trusted. A value that is missing makes the
/// check wait: SimConnect has not delivered it, an FD light reads neither 0 nor 1, a channel value is not
/// finite, or the FOB text does not parse. Waiting neither passes nor fails, it ends a pending disagreement,
/// and a check that has passed before still counts as passed. A disagreement untrusts its display while it
/// lasts, and once it has lasted <see cref="MismatchHoldMs"/> without a break it latches until reconnect. A
/// waiting value is a break, and so is a stall of the stream: a data message that comes more than
/// <see cref="StreamStaleMs"/> after the one before it ends every pending disagreement (a check that passed
/// stays passed, a latch stays latched). An unknown text code untrusts a display too but never latches: it is
/// an exact condition, not a noisy one.</para>
/// <para>Not thread-safe: the monitor locks around every call.</para>
/// </remarks>
internal sealed class FenixChannelTrust
{
    /// <summary>How long after the last data message the stream counts as stalled.</summary>
    public const long StreamStaleMs = 2_000;

    /// <summary>
    /// How long a disagreement must last, without a break (a waiting value or a stall of the stream is one), to
    /// latch its display until reconnect.
    /// </summary>
    public const long MismatchHoldMs = 3_000;

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

    // The amount and unit of the FOB line, run over the line's text with its format codes taken out, as in
    // "FOB:   2940  KG".
    private static readonly Regex FobPattern =
        new(@"FOB:\s*([0-9]+(?:[.,][0-9]+)?)\s*(KG|LBS)", RegexOptions.CultureInvariant);

    // The checks of each display, in the order they are judged. The SD has none in this set.
    private static readonly FenixComparison[] PfdChecks =
    {
        FenixComparison.FdLightCapt, FenixComparison.FdLightFo, FenixComparison.AltitudeFt, FenixComparison.MagHeadingDeg
    };

    private static readonly FenixComparison[] EwdChecks = { FenixComparison.FuelKg };

    private static readonly int ComparisonCount = Enum.GetValues<FenixComparison>().Length;

    private readonly IReadOnlySet<string> _verifiedBuilds;

    // SimConnect's side of each comparison, indexed by FenixComparison; NaN is missing. A reconnect keeps them,
    // since SimConnect keeps delivering through it.
    private readonly double[] _sim = Missing();

    // Everything below belongs to one connection: OnConnected replaces it.
    private bool _connected;
    private string? _disconnectReason;
    private string? _build;
    private bool _haveFrame;
    private long _lastFrameMs;
    private string? _shapeReason;
    private double[] _channel = Missing();   // the channel's side of each comparison, from the latest good frame
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
    /// The data call is open. Starts the connection fresh: the build unknown, no data message yet, no latch and
    /// no check passed. SimConnect's values stay. <paramref name="nowMs"/> goes unused: "no data yet" is judged
    /// by whether a message has come, not by how long ago the call opened.
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
    /// <see cref="StreamStaleMs"/> after the one before it first ends every pending disagreement. A frame that
    /// is not <see cref="FenixChannelFrame.ShapesOk"/> gives no values: the latch already holds every display,
    /// and the defaults it carries would only disagree.
    /// </summary>
    public void OnFrame(FenixChannelFrame frame, long nowMs)
    {
        // A push with nothing in it is neither damaged nor a sign of life: no shape check, no stream refresh
        // and no cross-check, so five seconds of them leave a trusted gate stalled but never latched.
        if (!frame.HasData) return;

        // A stall is a break in what the gate has seen, as a waiting value is: a disagreement that began before
        // the gap does not count toward MismatchHoldMs, which counts only what was seen without one. The same
        // limit as the verdict's "stream stalled". Only the pending clocks end: a check that passed stays
        // passed and a latch stays latched.
        if (_haveFrame && nowMs - _lastFrameMs > StreamStaleMs)
        {
            foreach (Check check in _checks)
            {
                check.DisagreeSinceMs = null;
            }
        }

        _haveFrame = true;
        _lastFrameMs = nowMs;

        if (!frame.ShapesOk)
        {
            Latch(frame.Malformed ? "malformed message" : $"field {frame.WrongShapeFields[0]} has the wrong shape");
            return;
        }

        ReadFrame(frame);
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
    /// SimConnect's latest value for one comparison; NaN means it has none. The gate runs the checks on the
    /// next data message. Kept across reconnects.
    /// </summary>
    public void OnComparison(FenixComparison kind, double value)
    {
        int index = (int)kind;
        if ((uint)index < (uint)_sim.Length)
        {
            _sim[index] = value;
        }
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

    /// <summary>
    /// One line for each comparison, in the invariant culture, with the channel's latest value beside
    /// SimConnect's: "altitude: channel 1,023 ft, sim 1,020 ft". A value that is missing reads "missing".
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
        FenixComparison[] checks;
        string? unknownCode;
        switch (display)
        {
            case FenixChannelDisplay.Pfd:
                checks = PfdChecks;
                unknownCode = _fmaUnknownCode;
                break;
            case FenixChannelDisplay.Ewd:
                checks = EwdChecks;
                unknownCode = _ewdUnknownCode;
                break;
            case FenixChannelDisplay.Sd:
                // Nothing in this set to cross-check and no text to read: the channel-wide conditions decide.
                return null;
            default:
                return "not a display of the channel";
        }

        // The first check that is not OK gives the reason, whatever kind of fault it is: latched, else
        // disagreeing now, else never passed. A check that passed and is waiting now is still OK.
        foreach (FenixComparison kind in checks)
        {
            Check check = _checks[(int)kind];
            if (check.Latched) return $"{NameOf(kind)} latched until reconnect: {check.Pair}";
            if (check.DisagreeSinceMs is not null) return $"{NameOf(kind)} disagrees: {check.Pair}";
            if (!check.Passed) return $"{NameOf(kind)} not checked yet";
        }

        return unknownCode;
    }

    // ---- The checks -----------------------------------------------------------------------------

    // The first cause stays: a later fault does not replace it.
    private void Latch(string reason) => _shapeReason ??= reason;

    // Takes the channel's side of every comparison, and the first unknown code of each display's lines, from a
    // frame that has its shapes.
    private void ReadFrame(FenixChannelFrame frame)
    {
        FenixTextLine fob = FenixChannelText.Parse(frame.FobText);

        _channel[(int)FenixComparison.FdLightCapt] = frame.Fd.FdOnCapt ? 1 : 0;
        _channel[(int)FenixComparison.FdLightFo] = frame.Fd.FdOnFo ? 1 : 0;
        _channel[(int)FenixComparison.AltitudeFt] = frame.Altitude;
        _channel[(int)FenixComparison.MagHeadingDeg] = frame.MagHeading;
        _channel[(int)FenixComparison.FuelKg] = FuelKgOf(fob);

        _fmaUnknownCode = FirstUnknownCode(frame.Fma.Select(slot => slot.Text), "FMA");
        _ewdUnknownCode = FirstUnknownCode(frame.UpperEcamLeft.Concat(frame.UpperEcamRight), "upper ECAM")
            ?? UnknownCodeIn(fob, "FOB line");
    }

    // One check, on one data message: waiting, agreeing or disagreeing.
    private void Evaluate(FenixComparison kind, long nowMs)
    {
        Check check = _checks[(int)kind];
        if (check.Latched) return;   // until reconnect: nothing that follows changes it, not even the values its reason names

        double channel = _channel[(int)kind];
        double sim = _sim[(int)kind];

        if (IsWaiting(kind, channel, sim))
        {
            check.DisagreeSinceMs = null;
            return;
        }

        if (Agrees(kind, channel, sim))
        {
            check.Passed = true;
            check.DisagreeSinceMs = null;
            return;
        }

        check.Pair = Pair(kind, channel, sim);
        check.DisagreeSinceMs ??= nowMs;
        if (nowMs - check.DisagreeSinceMs.Value >= MismatchHoldMs)
        {
            check.Latched = true;
        }
    }

    // Either value is missing (SimConnect has not delivered it, or a value is not finite), or an FD light reads
    // anything but exactly 0 or 1 (the annunciator light test, for one).
    private static bool IsWaiting(FenixComparison kind, double channel, double sim) =>
        !double.IsFinite(channel) || !double.IsFinite(sim)
        || (kind is FenixComparison.FdLightCapt or FenixComparison.FdLightFo && sim != 0 && sim != 1);

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

        /// <summary>When the disagreement now pending began; null when none is.</summary>
        public long? DisagreeSinceMs;

        /// <summary>A disagreement lasted <see cref="MismatchHoldMs"/>: not trusted until reconnect.</summary>
        public bool Latched;

        /// <summary>"channel 1,023 ft, sim 2,050 ft": the values of the disagreement pending or that latched it.</summary>
        public string Pair = "";
    }
}

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// What the PMDG 737 and 777 CALCULATED flap maneuvering speeds share
/// (<see cref="Pmdg737FlapSpeeds"/>, <see cref="Pmdg777FlapSpeeds"/>): the spoken readout and
/// the output-mode hotkey that reads the gross weight fresh and speaks it. The iFly's
/// <see cref="IFly737FlapSpeeds"/> predates it and keeps its own.
/// </summary>
public static class FlapManeuverSpeeds
{
    /// <summary>Spoken when no believable gross weight (or variant) is available.</summary>
    public const string Unavailable = "Flap speeds unavailable.";

    private const int FreshReadTimeoutMs = 1000;

    /// <summary>Spoken name for a schedule's flap label ("Flaps up", "Flaps 5", ...).</summary>
    public static string FlapName(string flap) => flap == "up" ? "Flaps up" : $"Flaps {flap}";

    /// <summary>The readout ("Flaps 5 maneuver speed 160 knots, calculated").</summary>
    public static string Sentence(string flap, int knots) =>
        $"{FlapName(flap)} maneuver speed {knots} knots, calculated";

    /// <summary>
    /// Reads <paramref name="weightKey"/> fresh (spoken only on a DELIVERED value, never a cache
    /// left by an earlier press) and speaks <paramref name="describe"/>'s sentence for it, which
    /// gets null when nothing was delivered. Call on the UI thread and discard the task: it never
    /// faults, since any exception is logged and spoken as <see cref="Unavailable"/>.
    /// </summary>
    public static async Task AnnounceAsync(SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, string weightKey, string logCategory, Func<double?, string> describe)
    {
        try
        {
            double? lbs = await simConnect.ReadFreshAsync(weightKey, FreshReadTimeoutMs);
            announcer.AnnounceImmediate(describe(lbs));
        }
        catch (Exception ex)
        {
            Log.Warn(logCategory, $"Flap speed readout failed: {ex.Message}");
            try { announcer.AnnounceImmediate(Unavailable); }
            catch { /* the announcer itself failed; nothing left to tell the pilot with */ }
        }
    }
}

/// <summary>
/// VREF by gross weight from points read off an FMC's APPROACH REF page (typed gross weight,
/// in thousands of pounds as the page takes it): linear between the points, and
/// <see cref="Beyond"/> outside them.
/// </summary>
public sealed class MeasuredVrefTable
{
    /// <summary>What a weight outside the measured points reads.</summary>
    public enum Beyond
    {
        /// <summary>The square-root law (VREF grows with the square root of weight) from the nearer end point.</summary>
        SquareRootLaw,

        /// <summary>The nearer end point's own value.</summary>
        HoldEndValue,
    }

    private readonly Beyond _beyond;
    private readonly (double Klb, int Knots)[] _points;

    /// <param name="points">(gross weight in klb, VREF in knots), ascending by weight.</param>
    public MeasuredVrefTable(Beyond beyond, params (double Klb, int Knots)[] points)
    {
        _beyond = beyond;
        _points = points;
    }

    /// <summary>VREF in whole knots (midpoints away from zero).</summary>
    public int Knots(double grossWeightLbs) =>
        (int)Math.Round(Interpolate(grossWeightLbs / 1000.0), MidpointRounding.AwayFromZero);

    private double Interpolate(double klb)
    {
        if (klb <= _points[0].Klb) return Extend(_points[0], klb);
        for (int i = 1; i < _points.Length; i++)
        {
            var (aKlb, aKnots) = _points[i - 1];
            var (bKlb, bKnots) = _points[i];
            if (klb <= bKlb) return aKnots + (bKnots - aKnots) * (klb - aKlb) / (bKlb - aKlb);
        }
        return Extend(_points[^1], klb);
    }

    private double Extend((double Klb, int Knots) end, double klb) =>
        _beyond == Beyond.SquareRootLaw ? end.Knots * Math.Sqrt(klb / end.Klb) : end.Knots;
}

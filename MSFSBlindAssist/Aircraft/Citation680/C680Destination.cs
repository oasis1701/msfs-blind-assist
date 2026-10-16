using System.Globalization;
using System.Text.Json;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Output D's sentence from the MFD agent's dest() reply (the FMS's own leg distances) and the
/// SimConnect ETEs. Pure, so it is testable without the aircraft.
/// </summary>
public static class C680Destination
{
    private sealed class Reply
    {
        public bool ok { get; set; }
        public bool plan { get; set; }
        public string? dest { get; set; }
        public double? remainingNm { get; set; }
        public double? nextNm { get; set; }
        public string? next { get; set; }
    }

    /// <summary>
    /// "Destination EBBR, 540 miles, 1 hour 14 minutes; next waypoint ITVIP, 404.9 miles, 55 minutes".
    /// An empty or failed reply falls back to the next waypoint from SimConnect, and says the
    /// destination could not be read rather than inventing a number.
    /// </summary>
    public static string Compose(string? agentJson, double? destEteSec, double? nextNmSim, double? nextEteSec)
    {
        Reply? r = null;
        if (!string.IsNullOrWhiteSpace(agentJson))
        {
            try { r = JsonSerializer.Deserialize<Reply>(agentJson); } catch (JsonException) { r = null; }
        }
        if (r is { ok: true, plan: false }) return "No active flight plan";

        string destPart = r is { ok: true, remainingNm: not null }
            ? $"Destination {(string.IsNullOrEmpty(r.dest) ? "" : r.dest + ", ")}{Miles(r.remainingNm.Value, "0")}, {Hm(destEteSec)}"
            : "Destination distance not available from the MFD";
        double? nextNm = r?.nextNm ?? nextNmSim;
        string nextName = string.IsNullOrWhiteSpace(r?.next) ? "" : r!.next!.Trim() + ", ";
        string nextPart = nextNm == null ? "next waypoint not read" : $"next waypoint {nextName}{Miles(nextNm.Value, "0.0")}, {Hm(nextEteSec)}";
        return destPart + "; " + nextPart;
    }

    private static string Miles(double nm, string format) => nm.ToString(format, CultureInfo.InvariantCulture) + " miles";

    /// <summary>"1 hour 14 minutes", "55 minutes", or "time not available".</summary>
    public static string Hm(double? seconds)
    {
        if (seconds == null || seconds <= 0 || double.IsNaN(seconds.Value)) return "time not available";
        var t = TimeSpan.FromSeconds(seconds.Value);
        int h = (int)t.TotalHours;
        string m = t.Minutes == 1 ? "1 minute" : $"{t.Minutes} minutes";
        return h >= 1 ? $"{h} {(h == 1 ? "hour" : "hours")} {m}" : m;
    }
}

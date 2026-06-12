namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure state + sentence composer for FBW TCAS resolution-advisory spoken guidance,
/// shared by the FlyByWire A32NX and A380X definitions (the logic was previously
/// duplicated character-for-character in both). Holds the cached detail vars
/// (corrective flag, up/down advisory status, rate to maintain, green/red V/S bands)
/// and composes the "what to fly" sentence; the OWNING definition keeps the
/// announcer, the Ctrl+M mute check, and the deferred-compose timer (the timer is
/// per-def so its disposal rides each def's StopAllMotion).
/// Enum semantics from FBW TcasConstants.ts (UpDownAdvisoryStatus).
/// </summary>
public sealed class TcasRaGuidance
{
    /// <summary>A32NX_TCAS_STATE: 0 none, 1 TA, 2 RA.</summary>
    public int AdvisoryState;
    public bool Corrective;
    public int UpAdvisory, DownAdvisory;     // UpDownAdvisoryStatus 0-5
    public double RateToMaintain;            // fpm
    public double GreenMin, GreenMax, RedMin, RedMax;
    private string _lastSpoken = "";

    /// <summary>
    /// Routes a TCAS detail-var update into the cached state.
    /// Returns true when the var was one of ours (caller then recomposes + returns
    /// true from ProcessSimVarUpdate to suppress the generic announce).
    /// </summary>
    public bool TryHandleDetailVar(string varName, double value)
    {
        switch (varName)
        {
            case "A32NX_TCAS_RA_CORRECTIVE": Corrective = value >= 0.5; return true;
            case "A32NX_TCAS_RA_UP_ADVISORY_STATUS": UpAdvisory = (int)value; return true;
            case "A32NX_TCAS_RA_DOWN_ADVISORY_STATUS": DownAdvisory = (int)value; return true;
            case "A32NX_TCAS_RA_RATE_TO_MAINTAIN": RateToMaintain = value; return true;
            case "A32NX_TCAS_VSPEED_GREEN:1": GreenMin = value; return true;
            case "A32NX_TCAS_VSPEED_GREEN:2": GreenMax = value; return true;
            case "A32NX_TCAS_VSPEED_RED:1": RedMin = value; return true;
            case "A32NX_TCAS_VSPEED_RED:2": RedMax = value; return true;
            default: return false;
        }
    }

    /// <summary>Clears the change-dedup so the next compose speaks (RA onset / RA end).</summary>
    public void ResetSpoken() => _lastSpoken = "";

    /// <summary>
    /// Composes the guidance and returns it ONLY when in an RA and the sentence
    /// changed since the last spoken one (covers strengthening/reversal); null
    /// otherwise. Corrective RA → the green fly-to band; preventive RA → the
    /// do-not limits and/or the rate to maintain.
    /// </summary>
    public string? ComposeIfChanged()
    {
        if (AdvisoryState != 2) return null;
        string text = Compose();
        if (text.Length == 0 || text == _lastSpoken) return null;
        _lastSpoken = text;
        return text;
    }

    private string Compose()
    {
        bool greenBand = Math.Abs(GreenMin) >= 1 || Math.Abs(GreenMax) >= 1;
        if (Corrective && greenBand)
        {
            string action = GreenMin >= -1 ? "Climb"
                          : GreenMax <= 1 ? "Descend"
                          : "Adjust vertical speed";
            return $"TCAS: {action}. Fly vertical speed {FmtSignedFpm(GreenMin)} to {FmtSignedFpm(GreenMax)} feet per minute.";
        }
        var parts = new List<string>();
        string? up = AdvisoryPhrase(UpAdvisory, "climb");
        string? down = AdvisoryPhrase(DownAdvisory, "descend");
        if (up != null) parts.Add(up);
        if (down != null) parts.Add(down);
        if (Math.Abs(RateToMaintain) >= 1)
            parts.Add($"Maintain {FmtSignedFpm(RateToMaintain)} feet per minute");
        return parts.Count == 0 ? "" : "TCAS: " + string.Join(". ", parts) + ".";
    }

    private static string? AdvisoryPhrase(int status, string verb) => status switch
    {
        1 => verb == "climb" ? "Climb" : "Descend",
        2 => $"Do not {verb}",
        3 => $"Do not {verb} more than 500 feet per minute",
        4 => $"Do not {verb} more than 1000 feet per minute",
        5 => $"Do not {verb} more than 2000 feet per minute",
        _ => null
    };

    private static string FmtSignedFpm(double v) =>
        Math.Abs(v) < 1 ? "0" : $"{(v > 0 ? "plus" : "minus")} {Math.Abs(v):0}";
}

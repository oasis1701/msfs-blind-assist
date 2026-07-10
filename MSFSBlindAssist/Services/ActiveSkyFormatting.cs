using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure text builders for the ActiveSky readouts. Everything here is a static
/// function of its inputs — no I/O, no settings reads — so the whole class is
/// directly characterization-tested in CI (ActiveSkyFormattingTests).
/// </summary>
public static class ActiveSkyFormatting
{
    /// <summary>
    /// Parses the /GetMode body, e.g. "Live Real time mode (Active) (2026/7/10 1935z)"
    /// → ("Live Real time mode", "2026/7/10 1935z"). Unknown strings pass through
    /// verbatim as the mode name (never crash, never hide); empty → "unknown".
    /// </summary>
    internal static (string ModeName, string? WeatherTimeZ) ParseModeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("unknown", null);
        string s = raw.Trim();

        // The trailing "(...z)" group is the AS weather clock.
        string? time = null;
        var m = Regex.Match(s, @"\(([^()]*z)\)\s*$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            time = m.Groups[1].Value.Trim();
            s = s[..m.Index].Trim();
        }

        // Drop a trailing "(Active)" / "(Inactive)" marker.
        s = Regex.Replace(s, @"\s*\((?:In)?active\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        return (s.Length > 0 ? s : "unknown", time);
    }

    /// <summary>Weather Radar status line: "ActiveSky: Live Real time mode, weather time 1935Z".</summary>
    internal static string FormatModeLine(string? raw)
    {
        var (mode, time) = ParseModeText(raw);
        if (time == null) return $"ActiveSky: {mode}";
        string clock = time.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].ToUpperInvariant();
        return $"ActiveSky: {mode}, weather time {clock}";
    }

    /// <summary>
    /// "Temperature/dew point: 36 / 12°C" from the AS position METAR — the dew
    /// point exists nowhere else (no SimConnect dew SimVar; the AS JSON ambient
    /// block has no dew field). Null when the METAR has no temperature group.
    /// DecodeMetar yields temperature and dew point strictly together (both or
    /// neither), so there is deliberately no temperature-only rendering path; if
    /// the decoder ever gains a temp-without-dew path, this method needs revisiting.
    /// </summary>
    internal static string? BuildTempDewLine(string? positionMetar)
    {
        if (string.IsNullOrWhiteSpace(positionMetar)) return null;
        var d = ActiveSkyWeatherMonitor.DecodeMetar(positionMetar);
        if (d.TemperatureC.HasValue && d.DewPointC.HasValue)
            return $"Temperature/dew point: {d.TemperatureC} / {d.DewPointC}°C";
        return null;
    }

    /// <summary>Forecast offsets for the METAR form's combo. AS synthesizes a
    /// forecast METAR from current METAR + TAF at the given offset (live-verified:
    /// distinct output at +4h/+12h/+24h).</summary>
    internal static readonly (string Label, int OffsetSeconds)[] ForecastPresets =
    {
        ("Now", 0),
        ("+1 hour", 3600),
        ("+2 hours", 7200),
        ("+4 hours", 14400),
        ("+6 hours", 21600),
    };

    /// <summary>Caption for the AS METAR box stating which offset it shows.</summary>
    internal static string BuildAsMetarCaption(int presetIndex)
    {
        var p = ForecastPresets[Math.Clamp(presetIndex, 0, ForecastPresets.Length - 1)];
        return p.OffsetSeconds == 0 ? "ActiveSky METAR:" : $"ActiveSky METAR ({p.Label}):";
    }
}

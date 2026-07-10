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

    /// <summary>The Winds Aloft box's altitude set: ±5000 ft of the aircraft in
    /// 1000-ft steps, clamped at 0 — kept identical to the Open-Meteo path's
    /// window (WeatherService.ParseWindsAloft) so switching source never
    /// changes which levels the pilot hears.</summary>
    internal static int[] WindsAloftAltitudes(int aircraftAltFt)
    {
        int lowAlt = (int)Math.Max(0, Math.Round((aircraftAltFt - 5000) / 1000.0) * 1000);
        int highAlt = (int)Math.Round((aircraftAltFt + 5000) / 1000.0) * 1000;
        var list = new List<int>();
        for (int a = lowAlt; a <= highAlt; a += 1000) list.Add(a);
        return list.ToArray();
    }

    /// <summary>AS-sourced Winds Aloft text — same layout as the Open-Meteo path
    /// plus per-level temperature and the source tag line.</summary>
    internal static string BuildWindsAloftText(int aircraftAltFt, IReadOnlyList<ActiveSkyClient.AtmosphereLevel> levels)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Aircraft: {aircraftAltFt:N0} ft  |  forecast winds:");
        sb.AppendLine(new string('─', 36));
        foreach (var w in levels)
        {
            string marker = Math.Abs(w.AltitudeFt - aircraftAltFt) < 500 ? " (nearest)" : "";
            sb.AppendLine($"{w.AltitudeFt:N0} ft:  {w.WindDirection:F0}° / {w.WindSpeed:F0} kts, {w.TemperatureC:F0}°C{marker}");
        }
        sb.AppendLine("Source: ActiveSky");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Curated vertical-profile briefing (design doc 2026-07-10 §3.2): every cloud
    /// layer with base/top/coverage plus icing/precip/turbulence phrases only when
    /// present; winds/temps at the layers nearest the standard levels (surface,
    /// 5,000, 10,000, 18,000, 24,000, 34,000 ft) plus the layer nearest the
    /// aircraft, deduplicated, ascending. Unknown enum values render as no phrase.
    /// </summary>
    internal static string BuildProfileNarrative(ActiveSkyClient.VerticalProfile p, int aircraftAltFt)
    {
        var sb = new System.Text.StringBuilder();

        var realClouds = p.CloudLayers.Where(c => CoverageWord(c.CoverageOktas) != null)
                                      .OrderBy(c => c.BaseFt).ToList();
        if (realClouds.Count == 0)
        {
            sb.AppendLine("No cloud layers reported below FL560.");
        }
        else
        {
            sb.AppendLine("Cloud layers:");
            foreach (var c in realClouds)
            {
                var line = new System.Text.StringBuilder(
                    $"{CoverageWord(c.CoverageOktas)}, {c.BaseFt:N0} to {c.TopFt:N0} feet");
                if (SeverityWord(c.IcingEnum) is { } icing) line.Append($", {icing} icing");
                if (PrecipWord(c.PrecipType) is { } precip) line.Append($", {precip}");
                if (SeverityWord(c.TurbulenceEnum) is { } turb) line.Append($", {turb} turbulence");
                sb.AppendLine(line.ToString());
            }
        }

        var curated = SelectCuratedLevels(p.WindLayers, aircraftAltFt);
        if (curated.Count > 0)
        {
            sb.AppendLine("Winds and temperatures aloft:");
            foreach (var w in curated)
            {
                string label = w.IsSurface ? "Surface" : $"{w.AltitudeFt:N0} feet";
                var line = new System.Text.StringBuilder(
                    $"{label}: {(int)Math.Round(w.DirectionDeg):000} at {w.SpeedKts:F0}");
                if (w.IsSurface && w.GustKts > 0) line.Append($", gusting {w.GustKts:F0}");
                line.Append($", {TempWord(w.TemperatureC)}");
                if (SeverityWord(w.TurbulenceEnum) is { } turb) line.Append($", {turb} turbulence");
                sb.AppendLine(line.ToString());
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Oktas → METAR coverage word; null = not a reportable layer.</summary>
    internal static string? CoverageWord(int oktas) => oktas switch
    {
        1 or 2 => "Few",
        3 or 4 => "Scattered",
        >= 5 and <= 7 => "Broken",
        8 => "Overcast",
        _ => null,
    };

    /// <summary>FSX-style severity enum (0-4) → word; null = none/unknown (omit).</summary>
    internal static string? SeverityWord(int e) => e switch
    {
        1 => "light",
        2 => "moderate",
        3 => "heavy",
        4 => "severe",
        _ => null,
    };

    /// <summary>PrecipType enum → word; null = none/unknown (omit).</summary>
    internal static string? PrecipWord(int t) => t switch
    {
        1 => "rain",
        2 => "snow",
        _ => null,
    };

    /// <summary>Spoken-friendly whole-degree temperature ("minus 37" / "15").</summary>
    internal static string TempWord(double c)
    {
        int r = (int)Math.Round(c);
        return r < 0 ? $"minus {-r}" : $"{r}";
    }

    /// <summary>Nearest layer to each standard level + the aircraft's level, deduped, ascending.</summary>
    internal static List<ActiveSkyClient.ProfileWindLayer> SelectCuratedLevels(
        IReadOnlyList<ActiveSkyClient.ProfileWindLayer> layers, int aircraftAltFt)
    {
        var result = new List<ActiveSkyClient.ProfileWindLayer>();
        if (layers.Count == 0) return result;
        int[] targets = { 0, 5000, 10000, 18000, 24000, 34000 };
        foreach (int t in targets)
        {
            var nearest = layers.OrderBy(l => Math.Abs(l.AltitudeFt - t)).First();
            if (!result.Contains(nearest)) result.Add(nearest);
        }
        var nearCurrent = layers.OrderBy(l => Math.Abs(l.AltitudeFt - aircraftAltFt)).First();
        if (!result.Contains(nearCurrent)) result.Add(nearCurrent);
        return result.OrderBy(l => l.AltitudeFt).ToList();
    }
}

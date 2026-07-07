using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Background poller that announces ActiveSky weather updates as they happen.
/// AS doesn't push notifications, so we poll on a fixed interval and announce
/// only when AS has actually pulled fresh weather data.
///
/// <para><b>Change detection (primary): JSON `TimeStamp` field.</b></para>
///
/// `/GetWeatherAreaJson` returns a `TimeStamp` (Unix epoch seconds). When
/// AS refreshes its underlying weather data (every 5 / 10 / 15 minutes per
/// AS settings), this value advances. When the user is sitting still and
/// AS hasn't refreshed, the value stays the same across polls. We compare
/// the timestamp across polls and announce only when it advances. This is
/// the closest thing the AS API exposes to a "weather download cadence"
/// signal — without it, the announcer was firing on superficial changes
/// (interpolated wind / temp / pressure drift) that don't correspond to a
/// real refresh, so announcements landed at irregular intervals.
///
/// <para><b>Change detection (fallback): METAR-content comparison.</b></para>
///
/// If TimeStamp is absent or behaves as request-time (advances on every
/// poll, which would announce every minute and defeat the purpose), we fall
/// back to comparing a normalized form of the METAR — strip timestamp,
/// wind, temp/dew, pressure (all interpolated continuously) and compare the
/// remaining tokens (visibility, clouds, weather, CAVOK, etc.) which only
/// change on a real AS refresh.
///
/// <para><b>Polling interval.</b></para>
///
/// 60 s. AS refreshes at 5–15 min intervals, so 60 s gives sub-minute
/// detection latency without hammering the local HTTP API. The cost when
/// AS is down is one ~1.2 s parallel-port probe per minute (cheap), so the
/// monitor runs unconditionally.
///
/// <para><b>Announcement format.</b></para>
///
/// Decoded conditions, screen-reader friendly, no METAR-isms. Example:
/// <c>"Active sky weather updated. Decoded weather at EGLL. Wind: 123 at
/// 4 knots. Visibility: 10 kilometres or more. Clouds: Few at 1,500 feet,
/// broken at 3,000 feet, overcast at 5,000 feet. Precipitation: None.
/// Temperature: 20. Dew point: 10. Altimeter: 1013 (29.92 inches)."</c>
///
/// We pull the closest-station METAR for the airport ICAO label, fall back
/// to the position METAR labelled "your position" if that endpoint is
/// unavailable. Conditions JSON gives surface wind/QNH (also redundantly
/// in the METAR but JSON is easier to parse for those fields).
///
/// <para><b>First-poll silence and AS-came-back behavior.</b></para>
///
/// First successful poll establishes a baseline silently. If AS goes
/// unreachable mid-flight, we reset the baseline so the next poll after
/// AS returns is also silent (avoids "weather update" right after sim
/// resume).
/// </summary>
public class ActiveSkyWeatherMonitor : IDisposable
{
    private const int POLL_INTERVAL_MS = 60_000;

    private readonly ActiveSkyClient _activeSky;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>Last seen JSON TimeStamp. 0 = no baseline yet.</summary>
    private long _lastTimeStamp;

    /// <summary>Normalized METAR last seen (for fallback when TimeStamp is unreliable).</summary>
    private string? _lastNormalizedMetar;

    /// <summary>Latches on the first successful poll so we don't announce the baseline.</summary>
    private bool _hasBaseline;

    /// <summary>Re-entry guard for slow polls.</summary>
    private bool _polling;

    /// <summary>Stops further work after Dispose so a pending tick can't fire announcements late.</summary>
    private bool _disposed;

    /// <summary>Wall-clock UTC of the last announcement fired by this monitor. Used by the user-configurable throttle.</summary>
    private DateTime _lastAnnouncedAt = DateTime.MinValue;

    /// <summary>
    /// User-configurable minimum minutes between announcements. 0 = no extra
    /// throttle (announce whenever the smart change-detection sees a refresh).
    /// Positive values add a hard floor on TOP of the existing detection —
    /// useful when teleporting/repositioning makes the position METAR change
    /// spatially in ways the detection can't tell from a real AS download.
    /// When throttled, the baseline is preserved so a still-pending change
    /// fires as soon as the throttle window passes.
    /// </summary>
    public int IntervalMinutes { get; set; }

    public ActiveSkyWeatherMonitor(ActiveSkyClient activeSky, ScreenReaderAnnouncer announcer)
    {
        _activeSky = activeSky;
        _announcer = announcer;
        _timer = new System.Windows.Forms.Timer { Interval = POLL_INTERVAL_MS };
        _timer.Tick += OnTickAsync;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>
    /// User-facing toggle: enable/disable the announcement feature without
    /// disposing the monitor. When disabled we don't even poll.
    /// </summary>
    public bool Enabled
    {
        get => _timer.Enabled;
        set { if (value) Start(); else Stop(); }
    }

    private async void OnTickAsync(object? sender, EventArgs e)
    {
        if (_disposed || _polling) return;
        _polling = true;
        try
        {
            // Re-check AS each tick. If AS goes away, reset baseline so the
            // user doesn't get a stale announcement when AS comes back.
            if (!await _activeSky.IsRunningAsync())
            {
                Log.Debug("Services", "tick: AS not detected; baseline reset");
                _lastTimeStamp = 0;
                _lastNormalizedMetar = null;
                _hasBaseline = false;
                // Drop the throttle stamp too — if AS comes back later the user
                // shouldn't be artificially held up by a stale "last announced"
                // timestamp from a previous AS session.
                _lastAnnouncedAt = DateTime.MinValue;
                return;
            }

            // Pull the structured conditions (TimeStamp + ambient/surface fields)
            // and the position METAR in parallel. The closest-station METAR is
            // pulled separately because it's only used for the announcement
            // label — failing that is non-fatal.
            var conditionsTask = _activeSky.GetCurrentConditionsAsync();
            var positionMetarTask = _activeSky.GetPositionMetarAsync();
            await Task.WhenAll(conditionsTask, positionMetarTask);

            var conditions = await conditionsTask;
            string? positionMetar = await positionMetarTask;

            if (conditions == null || string.IsNullOrWhiteSpace(positionMetar))
            {
                Log.Debug("Services", "tick: AS detected but data fetch failed");
                return;
            }

            // Decide whether this poll represents a real AS refresh.
            string normalized = NormalizeMetar(positionMetar);
            bool isRefresh = DetectRefresh(conditions.TimeStamp, normalized);

            if (!_hasBaseline)
            {
                _lastTimeStamp = conditions.TimeStamp;
                _lastNormalizedMetar = normalized;
                _hasBaseline = true;
                Log.Debug("Services", 
                    $"baseline: ts={_lastTimeStamp} normalized='{normalized}'");
                return;
            }

            if (!isRefresh)
            {
                Log.Debug("Services", 
                    $"tick: unchanged (ts={conditions.TimeStamp}, last={_lastTimeStamp})");
                return;
            }

            Log.Debug("Services", 
                $"tick: REFRESH detected ts {_lastTimeStamp}→{conditions.TimeStamp}");

            // User-configurable hard floor on announcement rate. Applied on TOP
            // of the smart change-detection so a positive setting strictly
            // limits how often the user hears an update, regardless of how
            // many genuine refreshes AS reports. Throttled poll keeps the
            // OLD baseline so a still-pending change announces immediately
            // once the throttle window passes (instead of being silently
            // absorbed).
            if (IntervalMinutes > 0
                && _lastAnnouncedAt != DateTime.MinValue
                && DateTime.UtcNow - _lastAnnouncedAt < TimeSpan.FromMinutes(IntervalMinutes))
            {
                Log.Debug("Services", 
                    $"tick: refresh detected but throttled — last announce was {(DateTime.UtcNow - _lastAnnouncedAt).TotalMinutes:F1}m ago, interval={IntervalMinutes}m");
                return;
            }

            _lastTimeStamp = conditions.TimeStamp;
            _lastNormalizedMetar = normalized;

            if (_disposed) return;

            // For the announcement label we want a real airport ICAO. The
            // closest-station METAR provides that; if the endpoint isn't
            // available we fall back to the position METAR with a "your
            // position" label.
            string? closestMetar = await _activeSky.GetClosestStationMetarAsync();
            string spoken = BuildAnnouncement(closestMetar, positionMetar, conditions);

            if (!_disposed && !string.IsNullOrEmpty(spoken))
            {
                Log.Debug("Services", $"announcing: \"{spoken}\"");
                _announcer.Announce(spoken);
                _lastAnnouncedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"tick error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>
    /// Decide whether this poll represents a fresh AS data pull. Primary
    /// signal: JSON TimeStamp advanced. Sanity guard: also require some
    /// METAR content to differ — if the timestamp keeps advancing on every
    /// poll (request-time semantics) without any actual content change, we
    /// don't fire. Fallback signal: if the JSON TimeStamp is missing/zero,
    /// rely purely on the normalized METAR comparison.
    /// </summary>
    private bool DetectRefresh(long currentTimeStamp, string currentNormalized)
    {
        bool tsAvailable = currentTimeStamp > 0 && _lastTimeStamp > 0;
        bool tsAdvanced = tsAvailable && currentTimeStamp != _lastTimeStamp;
        bool normalizedChanged = currentNormalized != _lastNormalizedMetar;

        if (tsAvailable)
        {
            // Use TimeStamp + sanity check on content. If TimeStamp keeps
            // advancing every poll (request-time semantics), ignore those
            // micro-advances unless content also changed.
            return tsAdvanced && normalizedChanged;
        }

        // No TimeStamp signal — fall back to METAR-content comparison.
        return normalizedChanged;
    }

    // -----------------------------------------------------------------
    //  Announcement construction
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds the spoken announcement. Wording is intentionally screen-reader
    /// friendly: numbers as digits, no METAR-isms.
    /// </summary>
    private static string BuildAnnouncement(
        string? closestStationMetar,
        string positionMetar,
        ActiveSkyClient.Conditions conditions)
    {
        // Prefer the closest-station METAR for the announcement: it has a
        // real ICAO at the head and is what real-world ATIS would speak. If
        // that's not available, fall back to the position METAR labelled
        // "your position".
        string metarToUse = !string.IsNullOrWhiteSpace(closestStationMetar)
            ? closestStationMetar!
            : positionMetar;

        var decoded = DecodeMetar(metarToUse);
        string locationLabel = decoded.IsPositionMetar
            ? "your position"
            : decoded.Station;

        var parts = new List<string>
        {
            "Active sky weather updated.",
            $"Decoded weather at {locationLabel}."
        };

        // Wind. Prefer the METAR's wind (it's the SOURCE of AS's wind value
        // and matches what controllers/ATIS would speak); fall back to the
        // surface-wind from JSON if the METAR didn't parse.
        string windText = decoded.WindText
                          ?? FormatWindFromConditions(conditions);
        if (!string.IsNullOrEmpty(windText))
            parts.Add($"Wind: {windText}.");

        // Visibility — prefer METAR (it has the original token; ICAO METARs
        // give meters, US METARs give SM).
        if (!string.IsNullOrEmpty(decoded.VisibilityText))
            parts.Add($"Visibility: {decoded.VisibilityText}.");
        else if (conditions.SurfaceVisibility > 0)
        {
            // SurfaceVisibility is in statute miles per the AS JSON API.
            // CLAUDE.md requires both km and SM in both source paths, so
            // speak SM verbatim with the km equivalent alongside.
            double sm = conditions.SurfaceVisibility;
            int km = (int)Math.Round(sm * 1.609);
            string vis = sm >= 6
                ? "10 statute miles or more (16 kilometres or more)"
                : $"{(int)Math.Round(sm)} statute miles ({km} kilometres)";
            parts.Add($"Visibility: {vis}.");
        }

        // Clouds — entirely from METAR, the JSON only gives a single ceiling.
        if (!string.IsNullOrEmpty(decoded.CloudsText))
            parts.Add($"Clouds: {decoded.CloudsText}.");
        else if (conditions.CloudCeilingFtAgl > 0)
            parts.Add($"Clouds: ceiling at {FormatThousands(conditions.CloudCeilingFtAgl)} feet.");

        // Precipitation — METAR weather group decoded by the shared helper.
        // None when the METAR has no weather token.
        string precip = WeatherRadarFormPrecipShim.ParsePrecipFromMetar(metarToUse);
        if (!string.IsNullOrEmpty(precip))
            parts.Add($"Precipitation: {Capitalise(precip)}.");
        else
            parts.Add("Precipitation: None.");

        // Temperature / dew point. Prefer METAR (more decimals in JSON might
        // be misleading at typical taxi/ATIS reporting precision).
        if (decoded.TemperatureC.HasValue)
            parts.Add($"Temperature: {decoded.TemperatureC.Value}.");
        else if (conditions.SurfaceTemperature != 0)
            parts.Add($"Temperature: {(int)Math.Round(conditions.SurfaceTemperature)}.");

        if (decoded.DewPointC.HasValue)
            parts.Add($"Dew point: {decoded.DewPointC.Value}.");

        // Altimeter — METAR Q (hPa) or A (inHg) — convert to give both.
        if (decoded.QnhMb.HasValue)
        {
            int hpa = decoded.QnhMb.Value;
            double inHg = hpa * 0.0295299830714;
            parts.Add($"Altimeter: {hpa} ({inHg:F2} inches).");
        }
        else if (conditions.QnhMb > 0)
        {
            int hpa = (int)Math.Round(conditions.QnhMb);
            double inHg = hpa * 0.0295299830714;
            parts.Add($"Altimeter: {hpa} ({inHg:F2} inches).");
        }

        return string.Join(" ", parts);
    }

    private static string FormatWindFromConditions(ActiveSkyClient.Conditions c)
    {
        if (c.SurfaceWindSpeed <= 0) return "";
        string text = $"{(int)Math.Round(c.SurfaceWindDirection):D3} at {(int)Math.Round(c.SurfaceWindSpeed)} knots";
        if (c.SurfaceGustSpeed > 0)
            text += $", gusting {(int)Math.Round(c.SurfaceGustSpeed)}";
        return text;
    }

    private static string FormatThousands(double value)
        => ((int)Math.Round(value)).ToString("N0", CultureInfo.InvariantCulture);

    private static string Capitalise(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    // -----------------------------------------------------------------
    //  METAR decoding
    // -----------------------------------------------------------------

    /// <summary>Parsed METAR fields used for the spoken announcement.</summary>
    internal sealed class DecodedMetar
    {
        public string Station = "";
        public bool IsPositionMetar;            // true when station == "@POS"
        public string? WindText;                // "123 at 4 knots[, gusting 15]" / "variable at 3 knots"
        public string? VisibilityText;          // "10 kilometres or more" / "5 kilometres" / "800 metres" / "10 statute miles" / "half a statute mile"
        public string? CloudsText;              // "Few at 1,500 feet, broken at 3,000 feet" or "Clear" etc.
        public int? TemperatureC;
        public int? DewPointC;
        public int? QnhMb;                      // hPa (converted from inHg if needed)
    }

    internal static DecodedMetar DecodeMetar(string metar)
    {
        var d = new DecodedMetar();
        if (string.IsNullOrWhiteSpace(metar)) return d;

        // Drop annotations after the first newline — AS appends "(Cloned by: …)"
        // on a second line for some stations.
        string firstLine = metar.Split('\r', '\n')
                                .Select(l => l.Trim())
                                .FirstOrDefault(l => l.Length > 0) ?? "";
        if (string.IsNullOrEmpty(firstLine)) return d;

        // Drop everything from " RMK " onward — remarks aren't part of the
        // operational decode.
        int rmkIdx = firstLine.IndexOf(" RMK ", StringComparison.OrdinalIgnoreCase);
        if (rmkIdx > 0) firstLine = firstLine.Substring(0, rmkIdx);

        string[] tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return d;

        // Token 0 is the station. AS position-METAR uses "@POS"; real
        // station METARs use the 4-letter ICAO.
        d.Station = tokens[0].ToUpperInvariant();
        d.IsPositionMetar = d.Station == "@POS";

        var clouds = new List<string>();
        bool sawCavok = false;

        for (int i = 1; i < tokens.Length; i++)
        {
            string t = tokens[i].ToUpperInvariant();

            if (IsTimestampToken(t)) continue;

            if (d.WindText is null && IsWindToken(t))
            {
                d.WindText = DecodeWind(t);
                continue;
            }

            // Wind variability "140V200" — append to wind text if present.
            if (IsWindVariability(t) && d.WindText != null)
            {
                d.WindText += $", varying between {t.Substring(0, 3)} and {t.Substring(4, 3)}";
                continue;
            }

            if (t == "CAVOK")
            {
                sawCavok = true;
                d.VisibilityText ??= "10 kilometres or more";
                clouds.Clear();
                clouds.Add("clear");
                continue;
            }

            if (d.VisibilityText is null && IsVisibilityToken(t))
            {
                d.VisibilityText = DecodeVisibility(t);
                continue;
            }

            // US METAR composite visibility: "1 1/2SM" arrives as two tokens
            // ("1" and "1/2SM"). Without lookahead, the integer is silently
            // dropped and we'd announce "half a statute mile" when actual
            // visibility is 1.5 SM. Detect a plain integer followed by a
            // bare "N/MSM" fraction and combine. P/M prefixes never compose
            // in US METAR (P6SM already saturates) so this is safe.
            if (d.VisibilityText is null
                && i + 1 < tokens.Length
                && t.Length > 0
                && t.All(char.IsDigit)
                && IsBareFractionalSmToken(tokens[i + 1].ToUpperInvariant()))
            {
                d.VisibilityText = DecodeCompositeStatuteMiles(t, tokens[i + 1].ToUpperInvariant());
                i++; // consume the fractional follower
                continue;
            }

            if (IsCloudToken(t))
            {
                string cloud = DecodeCloud(t);
                if (!string.IsNullOrEmpty(cloud)) clouds.Add(cloud);
                continue;
            }

            if (t is "NSC" or "NCD" or "CLR" or "SKC")
            {
                clouds.Clear();
                clouds.Add(t == "NSC" ? "no significant clouds" : "clear");
                continue;
            }

            if (d.TemperatureC is null && IsTempDewToken(t))
            {
                var (temp, dew) = DecodeTempDew(t);
                d.TemperatureC = temp;
                d.DewPointC = dew;
                continue;
            }

            if (d.QnhMb is null && IsPressureToken(t))
            {
                d.QnhMb = DecodePressure(t);
                continue;
            }
        }

        if (clouds.Count > 0)
            d.CloudsText = Capitalise(string.Join(", ", clouds));
        else if (sawCavok)
            d.CloudsText = "Clear";

        return d;
    }

    // ---- token classifiers ----

    private static bool IsTimestampToken(string t)
    {
        if (t.Length != 7 || t[6] != 'Z') return false;
        for (int i = 0; i < 6; i++)
            if (!char.IsDigit(t[i])) return false;
        return true;
    }

    private static bool IsWindToken(string t)
    {
        if (!t.EndsWith("KT", StringComparison.Ordinal) || t.Length < 7) return false;
        bool dirOk = (t.StartsWith("VRB", StringComparison.Ordinal))
                     || (char.IsDigit(t[0]) && char.IsDigit(t[1]) && char.IsDigit(t[2]));
        if (!dirOk) return false;
        for (int i = 3; i < t.Length - 2; i++)
        {
            char c = t[i];
            if (!(char.IsDigit(c) || c == 'G' || c == 'P')) return false;
        }
        return true;
    }

    private static bool IsWindVariability(string t)
    {
        // DDDvDDD — three digits, "V", three digits.
        if (t.Length != 7) return false;
        if (t[3] != 'V') return false;
        for (int i = 0; i < 3; i++) if (!char.IsDigit(t[i])) return false;
        for (int i = 4; i < 7; i++) if (!char.IsDigit(t[i])) return false;
        return true;
    }

    private static bool IsVisibilityToken(string t)
    {
        // 9999, 4-digit metres, "10SM", "P6SM", "1/2SM", "M1/4SM"
        if (t == "9999") return true;
        if (t.Length == 4 && t.All(char.IsDigit)) return true;
        if (t.EndsWith("SM", StringComparison.Ordinal))
        {
            string head = t.Substring(0, t.Length - 2);
            // Allow optional leading "P" (greater than) or "M" (less than)
            if (head.Length > 0 && (head[0] == 'P' || head[0] == 'M')) head = head.Substring(1);
            // Either an integer or "X/Y"
            if (head.All(char.IsDigit) && head.Length > 0) return true;
            int slash = head.IndexOf('/');
            if (slash > 0 && slash < head.Length - 1)
            {
                string num = head.Substring(0, slash);
                string den = head.Substring(slash + 1);
                if (num.All(char.IsDigit) && den.All(char.IsDigit) && num.Length > 0 && den.Length > 0)
                    return true;
            }
        }
        return false;
    }

    private static bool IsCloudToken(string t)
    {
        if (t.Length < 6) return false;
        string head = t.Substring(0, 3);
        if (head is not ("FEW" or "SCT" or "BKN" or "OVC" or "VV ")) return false;
        // Three following digits (altitude in hundreds of feet).
        if (t.Length < 6) return false;
        for (int i = 3; i < 6; i++) if (!char.IsDigit(t[i])) return false;
        // After the digits there can be a cloud-type suffix (CB, TCU) — we
        // don't speak those; just accept the token.
        return true;
    }

    private static bool IsTempDewToken(string t)
    {
        int slash = t.IndexOf('/');
        if (slash < 1 || slash >= t.Length - 1) return false;
        return IsTempPart(t.AsSpan(0, slash)) && IsTempPart(t.AsSpan(slash + 1));
        static bool IsTempPart(ReadOnlySpan<char> s)
        {
            if (s.Length == 0) return false;
            int i = 0;
            if (s[0] == 'M') i = 1;
            if (i >= s.Length) return false;
            for (; i < s.Length; i++)
                if (!char.IsDigit(s[i])) return false;
            return true;
        }
    }

    private static bool IsPressureToken(string t)
    {
        if (t.Length != 5) return false;
        if (t[0] != 'Q' && t[0] != 'A') return false;
        for (int i = 1; i < 5; i++)
            if (!char.IsDigit(t[i])) return false;
        return true;
    }

    // ---- token decoders ----

    private static string DecodeWind(string t)
    {
        // Token forms: DDDsskKT, VRBsskKT, DDDssGgsKT, DDDsspKT (P = exceeds).
        string body = t.Substring(0, t.Length - 2); // strip "KT"
        string dir;
        int idx = 3;
        if (body.StartsWith("VRB", StringComparison.Ordinal))
        {
            dir = "variable";
        }
        else
        {
            dir = body.Substring(0, 3); // 3-digit direction, leading zeros OK
        }

        // Speed: digits up to 'G' or 'P' or end of body.
        int gIdx = body.IndexOfAny(new[] { 'G', 'P' }, 3);
        int speedEnd = gIdx > 0 ? gIdx : body.Length;
        string speed = body.Substring(idx, speedEnd - idx);
        if (!int.TryParse(speed, out int speedKts)) speedKts = 0;

        string text = $"{dir} at {speedKts} knots";

        if (gIdx > 0 && body[gIdx] == 'G')
        {
            string gust = body.Substring(gIdx + 1);
            if (int.TryParse(gust, out int gustKts))
                text += $", gusting {gustKts}";
        }
        return text;
    }

    private static string DecodeVisibility(string t)
    {
        // 9999 → 10 km or more
        if (t == "9999") return "10 kilometres or more";

        // 4-digit metres: NNNN
        if (t.Length == 4 && t.All(char.IsDigit))
        {
            int metres = int.Parse(t, CultureInfo.InvariantCulture);
            if (metres >= 1000) return $"{metres / 1000} kilometres";
            return $"{metres} metres";
        }

        // Statute miles (including P##SM, M1/2SM, fractional). US METARs report
        // visibility in SM; preserve that unit rather than converting to km, so
        // the spoken value matches the source token a US controller would say.
        if (t.EndsWith("SM", StringComparison.Ordinal))
        {
            string head = t.Substring(0, t.Length - 2);
            string prefix = "";
            if (head.Length > 0 && head[0] == 'P') { prefix = "more than "; head = head.Substring(1); }
            else if (head.Length > 0 && head[0] == 'M') { prefix = "less than "; head = head.Substring(1); }

            int slash = head.IndexOf('/');
            if (slash > 0)
            {
                if (!int.TryParse(head.Substring(0, slash), out int num)) return "";
                if (!int.TryParse(head.Substring(slash + 1), out int den) || den == 0) return "";
                // Common METAR fractions get natural English so screen readers
                // don't read "1/2" as "one slash two".
                string fracText = (num, den) switch
                {
                    (1, 2) => "half a statute mile",
                    (1, 4) => "a quarter of a statute mile",
                    (3, 4) => "three quarters of a statute mile",
                    _ => $"{num}/{den} statute miles"
                };
                return prefix + fracText;
            }

            if (!double.TryParse(head, NumberStyles.Any, CultureInfo.InvariantCulture, out double sm))
                return "";
            string valueText = Math.Abs(sm - Math.Round(sm)) < 0.001
                ? ((int)Math.Round(sm)).ToString(CultureInfo.InvariantCulture)
                : sm.ToString("0.0", CultureInfo.InvariantCulture);
            string unit = Math.Abs(sm - 1.0) < 0.001 ? "statute mile" : "statute miles";
            return prefix + $"{valueText} {unit}";
        }

        return "";
    }

    /// <summary>
    /// Bare "N/MSM" — fraction with SM suffix and no P/M prefix. Used as the
    /// follower in composite US-METAR visibility tokens like "1 1/2SM".
    /// </summary>
    private static bool IsBareFractionalSmToken(string t)
    {
        if (!t.EndsWith("SM", StringComparison.Ordinal)) return false;
        string head = t.Substring(0, t.Length - 2);
        int slash = head.IndexOf('/');
        if (slash <= 0 || slash >= head.Length - 1) return false;
        string num = head.Substring(0, slash);
        string den = head.Substring(slash + 1);
        return num.Length > 0 && den.Length > 0
               && num.All(char.IsDigit) && den.All(char.IsDigit);
    }

    /// <summary>
    /// Decodes composite "WHOLE FRAC/DENSM" — e.g. "1 1/2SM" → "1 and a half
    /// statute miles". Whole part is a plain integer; frac is N/M with SM
    /// suffix (already validated by <see cref="IsBareFractionalSmToken"/>).
    /// </summary>
    private static string DecodeCompositeStatuteMiles(string whole, string frac)
    {
        if (!int.TryParse(whole, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w))
            return "";
        string head = frac.Substring(0, frac.Length - 2);
        int slash = head.IndexOf('/');
        if (!int.TryParse(head.Substring(0, slash), out int num)) return "";
        if (!int.TryParse(head.Substring(slash + 1), out int den) || den == 0) return "";
        string fracText = (num, den) switch
        {
            (1, 2) => "and a half",
            (1, 4) => "and a quarter",
            (3, 4) => "and three quarters",
            _ => $"and {num}/{den}"
        };
        return $"{w} {fracText} statute miles";
    }

    private static string DecodeCloud(string t)
    {
        string code = t.Substring(0, 3);
        string altStr = t.Substring(3, 3);
        if (!int.TryParse(altStr, out int hundreds)) return "";
        int feet = hundreds * 100;
        string label = code switch
        {
            "FEW" => "few",
            "SCT" => "scattered",
            "BKN" => "broken",
            "OVC" => "overcast",
            "VV " => "vertical visibility",
            _ => ""
        };
        if (string.IsNullOrEmpty(label)) return "";
        // Comma-thousands so "1500" is spoken as "one thousand five hundred",
        // not "fifteen hundred". Screen readers handle it correctly with
        // the comma.
        return $"{label} at {feet.ToString("N0", CultureInfo.InvariantCulture)} feet";
    }

    private static (int temp, int dew) DecodeTempDew(string t)
    {
        int slash = t.IndexOf('/');
        return (DecodeTempValue(t.Substring(0, slash)),
                DecodeTempValue(t.Substring(slash + 1)));
    }

    private static int DecodeTempValue(string s)
    {
        bool negative = s.Length > 0 && s[0] == 'M';
        string digits = negative ? s.Substring(1) : s;
        if (!int.TryParse(digits, out int v)) return 0;
        return negative ? -v : v;
    }

    private static int DecodePressure(string t)
    {
        // Q1013 → 1013 hPa direct.
        // A2992 → 29.92 inHg → 1013 hPa.
        string digits = t.Substring(1);
        if (!int.TryParse(digits, out int n)) return 0;
        if (t[0] == 'Q') return n;
        // A: digits represent inHg × 100. Convert to hPa.
        double inHg = n / 100.0;
        return (int)Math.Round(inHg * 33.8638866667);
    }

    // -----------------------------------------------------------------
    //  Normalised METAR (for fallback change detection)
    // -----------------------------------------------------------------

    /// <summary>
    /// Comparison key built from ONLY the discrete, weather-meaningful tokens
    /// in the METAR. Drops every continuously-interpolated or housekeeping
    /// token so we can detect a real refresh by content alone — used as a
    /// sanity guard against TimeStamp drift, and as the sole signal when
    /// TimeStamp isn't available.
    ///
    /// Kept tokens: visibility, weather phenomena, cloud groups, CAVOK,
    /// wind variability.
    ///
    /// Stripped tokens: timestamp, station/@POS, wind, temp/dewpoint,
    /// pressure, RMK and beyond.
    /// </summary>
    internal static string NormalizeMetar(string metar)
    {
        if (string.IsNullOrWhiteSpace(metar)) return "";
        string firstLine = metar.Split('\r', '\n')
                                .Select(l => l.Trim())
                                .FirstOrDefault(l => l.Length > 0) ?? "";
        firstLine = firstLine.ToUpperInvariant();

        int rmkIdx = firstLine.IndexOf(" RMK ", StringComparison.Ordinal);
        if (rmkIdx > 0) firstLine = firstLine.Substring(0, rmkIdx);

        var keep = new List<string>();
        foreach (string t in firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsTimestampToken(t))   continue;
            if (IsWindToken(t))        continue;
            if (IsTempDewToken(t))     continue;
            if (IsPressureToken(t))    continue;
            if (t == "@POS")           continue;
            keep.Add(t);
        }
        return string.Join(" ", keep);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTickAsync;
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Tiny shim that exposes <c>WeatherRadarForm.ParsePrecipFromMetar</c> to the
/// monitor without making the form's helper public. The METAR-token decoder
/// is form-agnostic but historically lived in the form file; rather than
/// move it (which would touch every caller), we re-host the same logic here
/// as a pure static. Keep in sync with the WeatherRadarForm copy if either
/// is changed — they implement the same WMO/ICAO weather group decoding.
/// </summary>
internal static class WeatherRadarFormPrecipShim
{
    public static string ParsePrecipFromMetar(string metar)
    {
        if (string.IsNullOrWhiteSpace(metar)) return "";
        string firstLine = metar.Split('\r', '\n')[0].ToUpperInvariant();
        string[] tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string t in tokens)
        {
            string token = t;
            string intensity = "moderate";
            if (token.StartsWith("-")) { intensity = "light"; token = token[1..]; }
            else if (token.StartsWith("+")) { intensity = "heavy"; token = token[1..]; }
            else if (token.StartsWith("VC")) { intensity = "in vicinity"; token = token[2..]; }

            string descriptor = "";
            if (token.Length >= 2)
            {
                string head = token[..2];
                if (head is "TS" or "SH" or "FZ" or "BL" or "DR" or "MI" or "BC" or "PR")
                {
                    descriptor = head;
                    token = token[2..];
                }
            }

            string phenom = token.Length >= 2 ? token[..2] : "";
            string phenomName = phenom switch
            {
                "RA" => "rain",
                "SN" => "snow",
                "GR" => "hail",
                "GS" => "small hail",
                "PL" => "ice pellets",
                "IC" => "ice crystals",
                "UP" => "unknown precipitation",
                "DZ" => "drizzle",
                "SG" => "snow grains",
                _ => ""
            };
            if (string.IsNullOrEmpty(phenomName)) continue;

            string descriptorName = descriptor switch
            {
                "TS" => "thunderstorm with ",
                "SH" => "showers of ",
                "FZ" => "freezing ",
                _ => ""
            };
            return $"{intensity} {descriptorName}{phenomName}";
        }
        return "";
    }
}

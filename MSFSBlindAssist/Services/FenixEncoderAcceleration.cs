namespace MSFSBlindAssist.Services;

/// <summary>
/// Whether the Fenix's Encoder Acceleration option is on, for the typed FCU sets ([FNX-20]): read from the
/// settings query (<see cref="FenixDataService.GetSettingsAsync"/>) with a short cache, so a set does not wait on
/// a query every time. Null is unread (no answer, no such option, a value that is not true or false), which
/// the sets treat as on: their chunked path lands either way, only more slowly. Reads this one key and logs no
/// setting value ([FNX-19]).
/// </summary>
internal sealed class FenixEncoderAcceleration
{
    /// <summary>The longest a set waits on the settings query; past it the option is unread.</summary>
    internal const int ReadTimeoutMs = 1500;
    /// <summary>How long an "off" is kept: a stale off lets one whole-difference write through before the set
    /// sees it multiplied, so it is kept briefly.</summary>
    internal const int OffCacheMs = 5000;
    /// <summary>How long an "on" or an unread answer is kept: both take the chunked path, which a change of the
    /// option cannot hurt, and a gateway that is down is not asked again on every set.</summary>
    internal const int OnCacheMs = 60000;

    private readonly Func<CancellationToken, Task<IReadOnlyList<FenixSetting>>> _read;
    private readonly Func<long> _nowMs;
    private readonly int _readTimeoutMs;
    private readonly object _gate = new();
    private bool _hasAnswer;
    private bool? _answer;
    private long _answeredAtMs;

    /// <param name="read">The settings query (in the app, the data service's <c>GetSettingsAsync</c>).</param>
    /// <param name="nowMs">A monotonic clock in milliseconds.</param>
    /// <param name="readTimeoutMs">For the tests; the app uses <see cref="ReadTimeoutMs"/>.</param>
    internal FenixEncoderAcceleration(Func<CancellationToken, Task<IReadOnlyList<FenixSetting>>> read, Func<long> nowMs,
        int readTimeoutMs = ReadTimeoutMs)
    {
        _read = read;
        _nowMs = nowMs;
        _readTimeoutMs = readTimeoutMs;
    }

    /// <summary>The option's value in a settings answer: true or false in any case, else null.</summary>
    internal static bool? Parse(IReadOnlyList<FenixSetting>? settings)
    {
        FenixSetting? row = settings?.FirstOrDefault(s => string.Equals(s.Key, FenixOptionKeys.EncoderAcceleration, StringComparison.Ordinal));
        return row?.Value is string value && bool.TryParse(value.Trim(), out bool on) ? on : null;
    }

    /// <summary>The option now: from the cache while it is fresh, else from one settings query. Never throws.</summary>
    internal async Task<bool?> ReadAsync()
    {
        lock (_gate)
        {
            if (_hasAnswer && _nowMs() - _answeredAtMs < (_answer == false ? OffCacheMs : OnCacheMs))
                return _answer;
        }

        bool? answer;
        try
        {
            using var timeout = new CancellationTokenSource(_readTimeoutMs);
            answer = Parse(await _read(timeout.Token));
        }
        catch (Exception)
        {
            answer = null;   // a time-out or a failing query: unread, the safe path
        }

        lock (_gate)
        {
            _hasAnswer = true;
            _answer = answer;
            _answeredAtMs = _nowMs();
        }
        return answer;
    }
}

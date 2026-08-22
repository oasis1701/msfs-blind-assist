namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure decision logic for the ActiveSky mode-change announcement, extracted
/// from the weather monitor so the silence rules are pinned in CI
/// (ActiveSkyModeTrackerTests). Baseline-first: the first successful read is
/// silent; only a genuine parsed-mode change announces. The baseline
/// deliberately SURVIVES unreachable gaps — AS coming back in a different mode
/// is a change the pilot must hear (design doc 2026-07-10 §3.3).
/// </summary>
internal sealed class ActiveSkyModeTracker
{
    private string? _baselineMode;

    /// <summary>Call only after a successful liveness probe, with the fresh
    /// LastModeText. Returns the announcement, or null for silence.</summary>
    public string? Observe(string? rawModeText)
    {
        if (string.IsNullOrWhiteSpace(rawModeText)) return null;   // failed body read ≠ change
        string mode = ActiveSkyFormatting.ParseModeText(rawModeText).ModeName;
        if (mode == "unknown") return null;
        if (_baselineMode == null) { _baselineMode = mode; return null; }
        if (mode == _baselineMode) return null;
        _baselineMode = mode;
        return $"ActiveSky weather mode changed to {mode}.";
    }
}

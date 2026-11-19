namespace MSFSBlindAssist.Services;

/// <summary>
/// Keeps a shown Engines value still until the raw value moves <see cref="Band"/> of a display step
/// away from it, so a value sitting on a rounding edge (116.49/116.51) does not rewrite its row, and
/// make the screen reader re-read a focused row, every second. UI thread only.
/// </summary>
public sealed class FenixEngineDisplayHold
{
    public const double Band = 0.6;

    private readonly Dictionary<string, double> _shown = new(StringComparer.Ordinal);

    /// <summary>
    /// The value to show for <paramref name="key"/> at <paramref name="decimals"/> places. An unknown
    /// value forgets the key, so the next known one rounds afresh.
    /// </summary>
    public double? Hold(string key, double? raw, int decimals)
    {
        if (raw is not double value)
        {
            _shown.Remove(key);
            return null;
        }

        if (!_shown.TryGetValue(key, out double shown)
            || Math.Abs(value - shown) >= Band * Math.Pow(10, -decimals))
        {
            shown = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            _shown[key] = shown;
        }
        return shown;
    }
}

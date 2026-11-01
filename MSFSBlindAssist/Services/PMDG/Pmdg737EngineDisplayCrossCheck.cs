namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The PMDG SDK's tank quantities (pounds), read fresh from the data manager: left is tank 1, right tank 2.</summary>
public readonly record struct EngineDisplaySdkSnapshot(double FuelLeftLb, double FuelCenterLb, double FuelRightLb);

/// <summary>
/// The engine display gate's checks against what the app already trusts. The tanks: each SDK quantity must
/// match at least one memory sample from the last <see cref="WindowMs"/> within <see cref="ToleranceLb"/>
/// (the SDK lags; tanks may match different samples). Measured live on 2026-10-24 with the APU burning
/// tank 1: memory and SDK agree to 0.02 lb, both PMDG's indicated quantity, so the tolerance is the 1 lb
/// floor. The fingerprint: the EFIS object's display units carry their own indexes 0, 1, 2… in order, which a
/// wrong object never does.
/// </summary>
public static class Pmdg737EngineDisplayCrossCheck
{
    public const long WindowMs = 3_000;
    public const double ToleranceLb = 1.0;

    public static bool Passes(EngineDisplaySdkSnapshot sdk, IEnumerable<(long AtMs, EngineDisplaySample Sample)> history, long nowMs)
    {
        bool center = false, left = false, right = false;
        foreach (var (atMs, s) in history)
        {
            if (nowMs - atMs > WindowMs || s.FuelQuantity.Count < 3) continue;
            center |= Math.Abs(s.FuelQuantity[0] - sdk.FuelCenterLb) <= ToleranceLb;
            left |= Math.Abs(s.FuelQuantity[1] - sdk.FuelLeftLb) <= ToleranceLb;
            right |= Math.Abs(s.FuelQuantity[2] - sdk.FuelRightLb) <= ToleranceLb;
        }
        return center && left && right;
    }

    /// <summary>True when the display units' indexes are exactly 0, 1, 2… (six on the 737).</summary>
    public static bool Fingerprint(EngineDisplaySample s) =>
        s.UnitIndices.Count >= 6 && s.UnitIndices.Select((index, k) => index == k).All(ok => ok);
}

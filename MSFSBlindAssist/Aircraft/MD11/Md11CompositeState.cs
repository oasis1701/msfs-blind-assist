using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The spoken state of a COMPOSITE control (<see cref="Md11CompositeSpec"/>, the generator's
/// <c>composite</c> block) — TFDi's three engine fire handles and its Elevator Feel knob, and
/// nothing else today.
///
/// The knob (spec D13) is the simpler one: its MANUAL latch (<c>MD11_OVHD_FLTCTL_ELEVFEEL_BT</c>)
/// says "Auto" at 0, and at 1 it hands over to the knob's own five reference-speed words. The engine
/// handles' tooltip reads the PULL (<c>MD11_AOVHD_ENGnFIRE_SW</c>: 0 Normal, 1 Generator Field
/// Disconnect) and, fully pulled (2), the handle's own ROTATION (<c>MD11_AOVHD_ENGnFIRE_KB</c>:
/// 0 Bottle 1, 1 Fuel and Hydraulic Disconnect, 2 Bottle 2). The map used to give the row the pull
/// var with the rotation's words, so a stowed handle read "Bottle 1", and the row's walker turned the
/// bottle-discharge wheel while it read the pull — a walk that could never land, and could only risk
/// a discharge click. So the row is READ-ONLY (<c>BuildControlVariable</c>), its key keeps reading the
/// pull, the rotation is read under <see cref="InnerKeyFor"/>, and <c>SetControl</c> refuses the
/// control with <see cref="RefusalSentence"/>. Explicit pull, push and rotate actions are a
/// follow-up: they need a live fire to verify detents and polarity.
///
/// Pure, so the composition is pinned without a sim; the definition hands it the two cached values.
/// </summary>
public static class Md11CompositeState
{
    /// <summary>
    /// Suffix of the variable KEY the inner var is registered under — the control's own key already
    /// reads the outer var. A key is never a SimConnect name; the definition's Name is the inner var.
    /// </summary>
    public const string InnerKeySuffix = "__INNER";

    /// <summary>A reading counts as a position within this of the position's key (the nearest wins).</summary>
    public const double PositionTolerance = 0.5;

    /// <summary>The key the definition registers <paramref name="nodeId"/>'s inner var under.</summary>
    public static string InnerKeyFor(string nodeId) => nodeId + InnerKeySuffix;

    /// <summary>
    /// TFDi's word for the control, composed as its tooltip composes it: the outer word, or at the
    /// delegate position the inner word. Null — nothing truthful to say — without an outer reading,
    /// for an outer reading that is no listed position, and at the delegate position without an inner
    /// reading or for one that is no listed position. A handle short of the delegate needs no inner
    /// reading at all.
    /// </summary>
    public static string? Describe(Md11CompositeSpec? spec, double? outer, double? inner)
    {
        if (spec == null || outer is not double o) return null;
        var outerKey = NearestKey(spec.OuterWords.Keys.Append(spec.Delegate), o);
        if (outerKey == null) return null;
        if (outerKey != spec.Delegate) return spec.OuterWords[outerKey];
        if (inner is not double i) return null;
        var innerKey = NearestKey(spec.InnerWords.Keys, i);
        return innerKey == null ? null : spec.InnerWords[innerKey];
    }

    /// <summary>
    /// The outer words as ValueDescriptions — the delegate position has no word of its own: the
    /// handles' Normal / Generator Field Disconnect, the knob's lone "Auto". They are the words shown
    /// before the definition has a cache to compose from. They do NOT decide how MainForm builds the
    /// row: a read-only row with StateVariables is its status box whatever its description count
    /// (<c>Utils.PanelRowRules</c>) — by count alone, the knob's one word sent it past every branch to
    /// a plain Button that writes its var.
    /// </summary>
    public static Dictionary<double, string> OuterDescriptions(Md11CompositeSpec spec)
    {
        var d = new Dictionary<double, string>();
        foreach (var (key, word) in spec.OuterWords)
            if (TryParseKey(key, out var v)) d[v] = word;
        return d;
    }

    /// <summary>What <c>SetControl</c> says when it is asked to operate a composite control.</summary>
    public static string RefusalSentence(string label) => $"{label} cannot be operated from this panel yet.";

    private static string? NearestKey(IEnumerable<string> keys, double value)
    {
        string? best = null;
        double bestDistance = PositionTolerance;
        foreach (var key in keys)
        {
            if (!TryParseKey(key, out var v)) continue;
            double distance = Math.Abs(v - value);
            if (distance < bestDistance) { best = key; bestDistance = distance; }
        }
        return best;
    }

    private static bool TryParseKey(string key, out double value)
        => double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The side-console circuit breakers, one switch per row of the generated
/// <see cref="C680BreakerTable"/> (tools/c680-gen/gen-breakers.js reads them out of the
/// package's SOV_Circuit_Breakers.xml). A pull is the model's own click code: flip the
/// breaker's L:var AND toggle the electrical line it guards.
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildBreakerVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        foreach (var r in C680BreakerTable.Rows)
            AddSwitch(v, r.Key, r.LVar, r.Label, "In", "Pulled");
        return v;
    }

    private static readonly List<string> BreakersControls = C680BreakerTable.Rows.Select(r => r.Key).ToList();

    private static readonly Dictionary<string, (string LVar, string Line)> BreakerByKey =
        C680BreakerTable.Rows.ToDictionary(r => r.Key, r => (r.LVar, r.Line), StringComparer.Ordinal);

    /// <summary>The model's own click: flip the L:var and toggle the line, only when the pick differs from the live state.</summary>
    private static bool HandleBreakerSet(string varKey, double value, SimConnectManager sc)
    {
        if (!BreakerByKey.TryGetValue(varKey, out var b)) return false;
        sc.ExecuteCalculatorCode($"(L:{b.LVar}, bool) {(value > 0.5 ? 0 : 1)} == if{{ {Rpn(value)} (>L:{b.LVar}) '{b.Line}'_n (>K:ELECTRICAL_LINE_BREAKER_TOGGLE) }}");
        return true;
    }
}

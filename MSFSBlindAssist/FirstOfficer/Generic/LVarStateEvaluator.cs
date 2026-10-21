using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.Generic;

/// <summary>
/// Aircraft-agnostic First Officer state evaluator for L:var-driven aircraft. Reads the
/// SimConnect variable cache (GetCachedVariableValue); an uncached field returns NaN —
/// the ChecklistManager treats NaN as indeterminate (no auto-tick, no revert), the same
/// contract as the PMDG evaluators' CDA-not-ready NaN.
///
/// Most add-on control vars are registered OnRequest (panel-only) and never land in the
/// continuous batch cache; subclasses list those in OnRequestPollFields and the
/// FirstOfficerForm's 1 s timer polls them onto the cache via RequestVariable.
/// </summary>
public abstract class LVarStateEvaluator : IFoStateEvaluator
{
    private SimConnectManager? _sc;
    private int _takeoffFlaps = -1;
    // NaN until the window's first N2 answer, never 0: a guessed 0 reads "engine stopped" and
    // lets an engines-off guard or line pass over a running engine (the iFly evaluator's rule).
    private double _eng1N2 = double.NaN;
    private double _eng2N2 = double.NaN;

    protected SimConnectManager? Sc => _sc;

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;

    public bool IsAvailable => _sc is { IsConnected: true };

    /// <summary>OnRequest-registered fields the FO window must poll onto the cache each
    /// second so auto-detection can read them. Empty by default.</summary>
    public virtual IReadOnlyList<string> OnRequestPollFields => Array.Empty<string>();

    /// <summary>Per-aircraft synthetic fields (e.g. FO_ENG1_N2). Return false to fall
    /// through to the cache read.</summary>
    protected virtual bool TryGetSyntheticValue(string field, out double value)
    {
        value = double.NaN;
        return false;
    }

    /// <summary>A two-input synthetic field: NaN when either input is unknown, else 1/0 from
    /// <paramref name="test"/>. The "unknown is not false" contract every FO state read keeps.</summary>
    protected static double Both(double a, double b, Func<double, double, bool> test)
        => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : test(a, b) ? 1.0 : 0.0;

    /// <summary>A two-input "either is on" field: 1 when either reads above 0.5 (one lit source is
    /// enough with the other unread), 0 when both read off, NaN otherwise.</summary>
    protected static double AnyLit(double a, double b)
        => a > 0.5 || b > 0.5 ? 1.0 : double.IsNaN(a) || double.IsNaN(b) ? double.NaN : 0.0;

    public double GetValue(string field)
    {
        if (TryGetSyntheticValue(field, out double synthetic))
            return synthetic;
        return _sc?.GetCachedVariableValue(field) ?? double.NaN;
    }

    public bool IsOn(string field)
    {
        double v = GetValue(field);
        return !double.IsNaN(v) && v > 0.5;
    }

    public bool IsPosition(string field, int position)
    {
        double v = GetValue(field);
        return !double.IsNaN(v) && Math.Abs(v - position) < 0.5;
    }

    // 0 or less (a plan without one, or a plan forgotten) is "not loaded".
    public void SetTakeoffFlaps(int flaps) => _takeoffFlaps = flaps > 0 ? flaps : -1;

    /// <summary>SimBrief takeoff flaps (A320: 1..3), or -1 when not loaded.</summary>
    public int GetTakeoffFlaps() => _takeoffFlaps;

    public void SetEngineN2(double eng1N2, double eng2N2)
    {
        Volatile.Write(ref _eng1N2, eng1N2);
        Volatile.Write(ref _eng2N2, eng2N2);
    }

    protected double Eng1N2 => Volatile.Read(ref _eng1N2);
    protected double Eng2N2 => Volatile.Read(ref _eng2N2);

    /// <summary>Stored for parity; L:var Airbus pressurization is automatic — no consumer.</summary>
    public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
}

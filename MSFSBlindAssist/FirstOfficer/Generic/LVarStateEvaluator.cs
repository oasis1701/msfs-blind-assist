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
    private double _eng1N2;
    private double _eng2N2;

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

    public void SetTakeoffFlaps(int flaps) => _takeoffFlaps = flaps;

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

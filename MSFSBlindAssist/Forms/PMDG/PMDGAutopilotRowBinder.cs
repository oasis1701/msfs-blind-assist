using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG;

/// <summary>
/// Binds the pure <see cref="ApRowSpec"/> tables to live UI rows.
/// <para>
/// Actuation always goes through the def's own HandleUIVariableSet (passed in as
/// <c>setValue</c>), never a direct SendPMDGEvent. That is load-bearing, not a
/// convenience: the 777's F/D and A/T Arm switches ignore position values entirely and
/// require MOUSE_FLAG_LEFTSINGLE simulation plus an already-at-target guard, momentary
/// controls need CDA parameter 1, and ValueDescriptions controls need the IsReady guard.
/// Firing events directly here would silently break the 777's F/D and A/T Arm buttons.
/// </para>
/// </summary>
public static class PMDGAutopilotRowBinder
{
    /// <summary>Shown when the CDA snapshot has not arrived. GetFieldValue returns 0.0
    /// for every field until then, which must never render as a real position.</summary>
    private const string NotReady = "--";

    public static (List<ToggleButtonDef> Buttons, List<SelectorRowDef> Selectors) Bind(
        IReadOnlyList<ApRowSpec> specs,
        IReadOnlyDictionary<string, SimVarDefinition> vars,
        SimConnectManager simConnect,
        Action<string, double> suppressEcho,
        Func<string, double, SimVarDefinition, bool> setValue)
    {
        var buttons = new List<ToggleButtonDef>();
        var selectors = new List<SelectorRowDef>();

        foreach (var spec in specs)
        {
            if (!vars.TryGetValue(spec.VarKey, out var varDef)) continue;

            if (spec.Kind == ApRowKind.Selector)
            {
                selectors.Add(new SelectorRowDef(
                    spec.Label,
                    varDef.ValueDescriptions,
                    () => ReadRaw(simConnect, spec),
                    target =>
                    {
                        // A selector's expected resulting value IS the chosen position.
                        suppressEcho(spec.VarKey, target);
                        setValue(spec.VarKey, target, varDef);
                    }));
                continue;
            }

            buttons.Add(new ToggleButtonDef(
                spec.Label,
                () => RenderState(simConnect, spec, varDef),
                () =>
                {
                    double current = ReadRaw(simConnect, spec) ?? 0;
                    double expected = current == 0 ? 1 : 0;

                    // Mark the EXPECTED RESULT, not the press parameter: the echo gate is
                    // value-matched, so marking a press with 1 would suppress an engage
                    // but let the matching disengage (1 -> 0) leak through.
                    suppressEcho(spec.VarKey, expected);

                    // Momentary controls are dispatched by HandleUIVariableSet on the
                    // RenderAsButton/IsMomentary branch, which always sends CDA parameter
                    // 1 regardless of the value passed; toggles take the target position.
                    setValue(spec.VarKey, spec.Kind == ApRowKind.Momentary ? 1 : expected, varDef);
                }));
        }

        return (buttons, selectors);
    }

    /// <summary>Raw CDA field read, or null when the row has no state field or the
    /// data manager has not yet received a snapshot.</summary>
    private static double? ReadRaw(SimConnectManager simConnect, ApRowSpec spec)
    {
        if (spec.StateField.Length == 0) return null;
        var dm = simConnect.PMDGDataManager;
        if (dm == null || !dm.IsReady) return null;
        return dm.GetFieldValue(spec.StateField);
    }

    /// <summary>Button state suffix: "" for a stateless control (the disconnects),
    /// "--" before the first CDA snapshot, the varDef's own label when it has one,
    /// else a plain Engaged/Off.</summary>
    private static string RenderState(SimConnectManager simConnect, ApRowSpec spec, SimVarDefinition varDef)
    {
        if (spec.StateField.Length == 0) return string.Empty;

        double? raw = ReadRaw(simConnect, spec);
        if (!raw.HasValue) return NotReady;

        if (varDef.ValueDescriptions.TryGetValue(raw.Value, out string? label)) return label;
        return raw.Value != 0 ? "Engaged" : "Off";
    }
}

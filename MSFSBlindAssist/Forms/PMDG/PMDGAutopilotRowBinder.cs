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
/// controls need their aircraft's momentary dispatch (CDA parameter 1 on the 777, a
/// LEFTSINGLE+RELEASE pair on the 737), and ValueDescriptions controls need the IsReady
/// guard. Firing events directly here would silently break the 777's F/D and A/T Arm
/// buttons.
/// </para>
/// </summary>
public static class PMDGAutopilotRowBinder
{
    /// <summary>Shown when the CDA snapshot has not arrived. GetFieldValue returns 0.0
    /// for every field until then, which must never render as a real position.</summary>
    private const string NotReady = "--";

    /// <summary>Inserts the WinForms mnemonic marker before the first case-insensitive
    /// occurrence of <paramref name="mnemonic"/> ("CMD A", 'A' -> "CMD &amp;A").
    /// '\0' or a letter not present returns the label unchanged — the row-table tests
    /// pin that every ASSIGNED letter does occur, so unchanged-return is safety
    /// degradation, not an expected path.</summary>
    public static string ApplyMnemonic(string label, char mnemonic)
    {
        if (mnemonic == '\0') return label;
        int at = label.IndexOf(char.ToString(mnemonic), StringComparison.OrdinalIgnoreCase);
        return at < 0 ? label : label.Insert(at, "&");
    }

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
                        MarkEcho(suppressEcho, spec, target);
                        setValue(spec.VarKey, target, varDef);
                    },
                    spec.Mnemonic));
                continue;
            }

            buttons.Add(new ToggleButtonDef(
                // Pre-decorated with the Alt-key mnemonic, the same convention the
                // ValueInputForm dialog toggles use ("&Approach"). The state suffix
                // never contains '&', so the marker stays unique in the button Text.
                ApplyMnemonic(spec.Label, spec.Mnemonic),
                () => RenderState(simConnect, spec, varDef),
                () =>
                {
                    double current = ReadRaw(simConnect, spec) ?? 0;
                    double expected = current == 0 ? 1 : 0;

                    // Mark the EXPECTED RESULT, not the press parameter: the echo gate is
                    // value-matched, so marking a press with 1 would suppress an engage
                    // but let the matching disengage (1 -> 0) leak through.
                    MarkEcho(suppressEcho, spec, expected);

                    // Momentary controls are dispatched by HandleUIVariableSet's
                    // momentary branch, which ignores the value passed (the 777 sends
                    // CDA parameter 1, the 737 a LEFTSINGLE+RELEASE TransmitClientEvent
                    // pair); toggles take the target position.
                    setValue(spec.VarKey, spec.Kind == ApRowKind.Momentary ? 1 : expected, varDef);
                })
            {
                // A Toggle press computes its flip target from the current state, so it
                // must not fire while that state is unreadable (pre-snapshot, or a
                // dropped data manager): the 737's 2-position branch has no IsReady
                // guard and would command position 1 regardless of the real switch.
                // Momentary presses ignore the value and the stateless disconnects read
                // nothing, so only Toggle rows gate — same rule as the selector combo,
                // which disables on a null read.
                IsEnabled = spec.Kind == ApRowKind.Toggle
                    ? () => ReadRaw(simConnect, spec).HasValue
                    : null,
            });
        }

        return (buttons, selectors);
    }

    /// <summary>
    /// Marks the UI-set echo for a row under EVERY variable key that can announce it.
    /// <para>
    /// Both keys are needed because the two aircraft are ASYMMETRIC. On the 777 the row's
    /// VarKey IS the announced variable: the dictionary entry is keyed "MCP_AP_L" and its
    /// varDef.Name is the CDA field "MCP_annunAP_0", so one entry covers switch and state
    /// and marking VarKey suffices. On the 737 the switch and its annunciator are two
    /// SEPARATE dictionary entries: "MCP_CmdA" is declared Momentary with
    /// UpdateFrequency.Never and never raises an event at all — an echo under it is dead —
    /// while the announcement comes from "MCP_annunCMD_A", which is exactly the row's
    /// StateField. Marking VarKey alone therefore leaves the 737's CMD A/B and CWS A/B
    /// double-announcing in both directions, silently inconsistent with the 777.
    /// </para>
    /// <para>
    /// Do NOT "simplify" this back to a single key. Marking the extra key is inert
    /// wherever it is not itself a variable-dictionary key (the 777's annunciator CDA
    /// field names are varDef.Name values, not keys, so the entry never matches an event
    /// VarName), and wherever it IS a key it is by construction the announcing variable
    /// for the control the pilot just pressed — precisely what the echo gate exists to
    /// suppress.
    /// </para>
    /// </summary>
    private static void MarkEcho(Action<string, double> suppressEcho, ApRowSpec spec, double expected)
    {
        suppressEcho(spec.VarKey, expected);
        if (spec.StateField.Length > 0 && spec.StateField != spec.VarKey)
            suppressEcho(spec.StateField, expected);
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

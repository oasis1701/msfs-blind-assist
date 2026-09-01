// Fleet-wide structural invariants for aircraft definitions.
//
// (a) BATCH NAME COLLISIONS. Two var keys may share one underlying `Name` — that is
// routine and deliberate (e.g. PFD_VLS and A32NX_SPEEDS_VLS both read A32NX_SPEEDS_VLS,
// one for the PFD readout and one for the monitor). It is only a bug when BOTH keys ride
// the continuous batch, because SetupDataDefinitions sorts that batch by full name to
// mirror SimConnect's own ordering: two identical names there shift every later var's
// struct slot, so unrelated readouts silently return each other's values. Excluding
// either copy from the batch, or giving one an OnRequest frequency, resolves it.
//
// (b) PANEL KEYS RESOLVE. Every key listed in GetPanelControls must be a registered
// variable, or the panel renders a control that can never read or write anything. This
// catches a var rename that misses one of its panel references — exactly what FBW's
// FG-into-PRIM move (#10855) forced across the A380 EFIS and FCU panels.
//
// (c) PANEL LABELS ARE DISTINCT. Two controls in one panel must not share a
// DisplayName. The label is what the screen reader speaks and what the panel row
// shows, so two identical labels are two controls a blind pilot cannot tell apart
// - PR #223 fixed exactly that on the 777 Pressurization panel, where the aft
// valve's mode switch and its manual selector were both "Outflow Valve Aft" and
// both announce a value of "Auto". Cross-panel duplicates are fine: the panel
// name supplies the context (Captain vs First Officer pairs rely on this).

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class VarNameCollisionTests
{
    // Pre-existing, NOT deliberate: the autobrake mode is registered twice on the A320 and
    // A330 (AUTOBRAKE_MODE + A32NX_AUTOBRAKES_ARMED_MODE), both continuous and both batched.
    // Listed so this test pins today's shape rather than failing on unrelated work; it is a
    // real drift risk on those two airframes and wants fixing on its own branch. The A380
    // deliberately has none — its FG alerts are ONE var decoded into two spoken alerts.
    private static readonly HashSet<string> KnownBatchCollisions = new()
    {
        "A320/A32NX_AUTOBRAKES_ARMED_MODE",
        "HW_A330/A32NX_AUTOBRAKES_ARMED_MODE",
    };

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Batched_vars_do_not_share_an_underlying_name(IAircraftDefinition aircraft)
    {
        var offenders = aircraft.GetVariables()
            .Where(kv => kv.Value.Type != SimVarType.Event
                         && kv.Value.UpdateFrequency == UpdateFrequency.Continuous
                         && !kv.Value.ExcludeFromBatch)
            .GroupBy(kv => kv.Value.Name)
            .Where(g => g.Count() > 1)
            .Select(g => $"{aircraft.AircraftCode}/{g.Key}")
            .Where(id => !KnownBatchCollisions.Contains(id))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Continuous batched vars sharing one Name (data-definition position drift): "
            + string.Join(", ", offenders));
    }

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Every_panel_control_key_is_a_registered_variable(IAircraftDefinition aircraft)
    {
        var vars = aircraft.GetVariables();
        var dangling = aircraft.GetPanelControls()
            .SelectMany(p => p.Value.Select(k => (panel: p.Key, key: k)))
            .Where(x => !vars.ContainsKey(x.key))
            .Select(x => $"{aircraft.AircraftCode} panel '{x.panel}' -> '{x.key}'")
            .ToList();

        Assert.True(dangling.Count == 0,
            "Panel control keys with no registered variable: " + string.Join(", ", dangling));
    }

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Panel_rows_do_not_share_a_spoken_name(IAircraftDefinition aircraft)
    {
        var vars = aircraft.GetVariables();
        var offenders = new List<string>();

        foreach (var panel in aircraft.GetPanelControls())
        {
            var groups = panel.Value
                .Distinct(StringComparer.Ordinal)
                .Where(vars.ContainsKey)
                .Select(k => vars[k].DisplayName)
                .Where(n => !string.IsNullOrEmpty(n))
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var g in groups)
                offenders.Add($"{aircraft.AircraftCode} panel '{panel.Key}' label \"{g.Key}\"");
        }

        Assert.True(offenders.Count == 0,
            "Two controls in one panel answering to one spoken name: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The panel label column sizes to its content, so a runaway DisplayName widens
    /// the whole panel. 50 characters is roughly twice the longest label that existed
    /// when the column was fixed at 140px, and comfortably clears the longest today
    /// ("Center 1 Primary Electric Pump FAULT Light", 42). This is a bound on the
    /// data, not a rendering check - the rendered appearance is not verifiable by
    /// this project's testers and is deliberately not asserted here.
    /// </summary>
    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Panel_labels_stay_within_a_sane_width(IAircraftDefinition aircraft)
    {
        const int MaxLabelChars = 50;
        var vars = aircraft.GetVariables();

        var tooLong = aircraft.GetPanelControls()
            .SelectMany(p => p.Value)
            .Distinct(StringComparer.Ordinal)
            .Where(vars.ContainsKey)
            .Select(k => vars[k].DisplayName)
            .Where(n => !string.IsNullOrEmpty(n) && n.Length > MaxLabelChars)
            .Distinct(StringComparer.Ordinal)
            .Select(n => $"{aircraft.AircraftCode}: \"{n}\" ({n.Length} chars)")
            .ToList();

        Assert.True(tooLong.Count == 0,
            $"Panel labels over {MaxLabelChars} characters widen the auto-sized label column: "
            + string.Join(", ", tooLong));
    }
}

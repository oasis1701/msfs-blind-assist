using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Ctrl+M Monitor Manager is a FLAT list: MonitorManagerFormBase adds
/// row.Label alone, with no panel heading to disambiguate. Two rows sharing a
/// label are two checkboxes a pilot cannot tell apart, and a screen reader
/// speaks them identically.
///
/// The allowlist below is measured, pre-existing debt on other airframes - most
/// of it Captain/First Officer pairs that want a side tag. It should only ever
/// shrink. The PMDG 777 is deliberately absent: it was cleaned up in 2026-09 and
/// must stay clean.
/// </summary>
public class MonitorRowLabelUniquenessTests
{
    private static readonly HashSet<string> KnownMonitorLabelCollisions =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // A320 / Headwind A330 - Captain vs First Officer EFIS sides, plus two
        // genuine two-source pairs (Autopilot N light vs active, PTU memo vs pb).
        "A320/ARPT Filter", "A320/Autopilot 1", "A320/Autopilot 2", "A320/CSTR Filter",
        "A320/EFIS Mode Control", "A320/EFIS Range Control", "A320/ILS",
        "A320/Navaid 1 Selector", "A320/Navaid 2 Selector", "A320/NDB Filter",
        "A320/PTU", "A320/VORD Filter", "A320/WPT Filter",
        "HW_A330/ARPT Filter", "HW_A330/Autopilot 1", "HW_A330/Autopilot 2",
        "HW_A330/CSTR Filter", "HW_A330/EFIS Mode Control", "HW_A330/EFIS Range Control",
        "HW_A330/ILS", "HW_A330/Navaid 1 Selector", "HW_A330/Navaid 2 Selector",
        "HW_A330/NDB Filter", "HW_A330/PTU", "HW_A330/VORD Filter", "HW_A330/WPT Filter",

        // A380 - RMP 1 vs RMP 2, and two state-vs-mode pairs.
        "FBW_A380/Autobrake", "FBW_A380/Cargo BULK Isolation Valve",
        "FBW_A380/Cargo FWD Isolation Valve", "FBW_A380/Runway Overrun Protection",
        "FBW_A380/VHF 1 Receive", "FBW_A380/VHF 1 Transmit",
        "FBW_A380/VHF 2 Receive", "FBW_A380/VHF 2 Transmit",
        "FBW_A380/VHF 3 Receive", "FBW_A380/VHF 3 Transmit",

        // HS787 - stock ground speed alongside the aircraft's own.
        "HS_787/Ground Speed",

        // iFly 737 - display unit 0 vs unit 1.
        "IFLY_737MAX8/Baro Units", "IFLY_737MAX8/Left VOR ADF Selector",
        "IFLY_737MAX8/Minimums Reference", "IFLY_737MAX8/Navigation Display Mode",
        "IFLY_737MAX8/Navigation Display Range", "IFLY_737MAX8/Right VOR ADF Selector",

        // PMDG 737 - MCP value vs its annunciator, and a switch vs its annunciator.
        "PMDG_737/Landing Altitude", "PMDG_737/Speed",
        "PMDG_737/Vertical Speed", "PMDG_737/Yaw Damper",
    };

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Monitor_manager_rows_have_distinct_labels(IAircraftDefinition aircraft)
    {
        var offenders = MonitorRowBuilder.Build(aircraft.GetVariables())
            .GroupBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{aircraft.AircraftCode}/{g.Key}")
            .Where(id => !KnownMonitorLabelCollisions.Contains(id))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Two Ctrl+M rows answering to one label: " + string.Join(", ", offenders));
    }
}

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A Ctrl+M row must be able to silence something. The variables below are consumed
/// before any mute gate runs: MainForm.HandleSpecialAnnouncements returns for them at
/// Step 2 of OnSimVarUpdated, ahead of the announcer.Suppressed wrap and the Step-6
/// per-aircraft disabled-set check (INDICATED_ALTITUDE, GROUND_VELOCITY, G_FORCE,
/// PLANE_TOUCHDOWN_NORMAL_VELOCITY), or their ProcessSimVarUpdate branch is cache-only
/// and never speaks (HS787_GroundSpeed). A row for any of them is a checkbox that mutes
/// nothing. They stay Continuous + IsAnnounced - that is what puts them on the continuous
/// batch - and are hidden with ExcludeFromMonitorManager instead.
/// </summary>
public class MonitorRowMuteReachabilityTests
{
    private static readonly string[] ConsumedBeforeTheMuteGate =
    {
        "INDICATED_ALTITUDE",
        "GROUND_VELOCITY",
        "G_FORCE",
        "PLANE_TOUCHDOWN_NORMAL_VELOCITY",
        "HS787_GroundSpeed",
    };

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Variables_consumed_before_the_mute_gate_get_no_row(IAircraftDefinition aircraft)
    {
        var listed = MonitorRowBuilder.Build(aircraft.GetVariables())
            .Select(r => r.Key)
            .Where(k => ConsumedBeforeTheMuteGate.Contains(k, StringComparer.Ordinal))
            .ToList();

        Assert.True(listed.Count == 0,
            $"{aircraft.AircraftCode} lists a Ctrl+M row that can never silence anything: "
            + string.Join(", ", listed));
    }
}

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Simulation: the Crew Seat setting (which touchscreens the windows open on and which
/// altimeter the readouts use). The vendor EFB's options are set in the EFB window's Settings page.
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildSimulationVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        AddSelector(v, "C680_SEAT", "C680_SEAT", "Crew Seat", new[] { "Pilot", "Copilot" },
            "Which touchscreens the windows open on and which altimeter the readouts use. Saved between sessions.");
        v["C680_SEAT"].UpdateFrequency = UpdateFrequency.OnRequest;
        v["C680_SEAT"].IsAnnounced = false;

        return v;
    }

    private static readonly List<string> SeatControls = new() { "C680_SEAT" };

    private bool HandleSimulationSet(string varKey, double value, SimConnectManager sc)
    {
        if (varKey == "C680_SEAT")
        {
            Settings.SettingsManager.Current.C680CrewSeat = value > 0.5 ? 2 : 1;
            Settings.SettingsManager.Save();
            sc.RequestVariable("C680_SEAT", forceUpdate: true);
            return true;
        }
        return false;
    }

    private bool TrySimulationDisplay(string varKey, out string displayText)
    {
        if (varKey == "C680_SEAT")
        {
            displayText = CurrentSeat == C680Seat.Side.Copilot ? "Copilot" : "Pilot";
            return true;
        }
        displayText = string.Empty;
        return false;
    }
}

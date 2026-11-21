using System;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Major cockpit sections and instruments targeted for camera head movement in Streamer Mode.
/// </summary>
public enum CockpitCameraTarget
{
    /// <summary>Default forward-looking pilot eye level (cockpit view type 1, index 0).</summary>
    Forward,

    /// <summary>Overhead panel (electrical, lights, fuel, hydraulics, APU, etc.).</summary>
    Overhead,

    /// <summary>Glareshield (FCU / MCP, EFIS panels).</summary>
    Glareshield,

    /// <summary>Center pedestal (throttles, radios, transponder, trim, radar).</summary>
    Pedestal,

    /// <summary>Main instrument panel (PFD, ND, EICAS, gear, standby instruments).</summary>
    MainPanel,

    /// <summary>Multi-Control Display Unit / Flight Management Computer (Captain side).</summary>
    MCDU,

    /// <summary>Electronic Flight Bag / tablet (flyPad, EFB).</summary>
    EFB
}

/// <summary>
/// Helper methods for mapping cockpit section names to <see cref="CockpitCameraTarget"/>.
/// </summary>
public static class CockpitCameraTargetExtensions
{
    /// <summary>
    /// Resolves one or more candidate strings (e.g. section name, item label, event name) to a <see cref="CockpitCameraTarget"/>.
    /// Checks candidates in order and returns the first resolved target.
    /// </summary>
    public static CockpitCameraTarget? ResolveSection(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            if (candidate.Contains("Overhead", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("_OH_", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Battery", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Ext Power", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("External Power", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Standby Power", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("APU", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Fuel Pump", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Fuel", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Hydraulic", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Bleed", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Air Cond", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Pressurization", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Oxygen", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Wiper", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Anti-ice", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Anti Ice", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Seatbelt", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Landing Light", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Strobe", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Beacon", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Nav Light", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Taxi Light", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Logo Light", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("IRS", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("ADIRS", StringComparison.OrdinalIgnoreCase))
                return CockpitCameraTarget.Overhead;

            if (candidate.Contains("Glareshield", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("FCU", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("MCP", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("_MCP_", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("_FCU_", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Flight Director", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Autopilot", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Autothrottle", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("A/THR", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("EFIS", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Altimeter", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Baro", StringComparison.OrdinalIgnoreCase))
                return CockpitCameraTarget.Glareshield;

            if (candidate.Contains("Pedestal", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("_PED_", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Engine Mode", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Master Switch", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Engine Master", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Throttle", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Thrust", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Speedbrake", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Spoiler", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Flap", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Parking Brake", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Trim", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Transponder", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("XPNDR", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("TCAS", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Radio", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Weather Radar", StringComparison.OrdinalIgnoreCase))
                return CockpitCameraTarget.Pedestal;

            if (candidate.Contains("Main", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Forward", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Center", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Display", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Instrument", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Cockpit", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("MIP", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("_MIP_", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Autobrake", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Landing Gear", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Gear", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Clock", StringComparison.OrdinalIgnoreCase))
                return CockpitCameraTarget.MainPanel;

            if (candidate.Contains("MCDU", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("CDU", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("FMC", StringComparison.OrdinalIgnoreCase))
                return CockpitCameraTarget.MCDU;

            if (candidate.Contains("EFB", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("flyPad", StringComparison.OrdinalIgnoreCase))
                return CockpitCameraTarget.EFB;
        }

        return null;
    }
}


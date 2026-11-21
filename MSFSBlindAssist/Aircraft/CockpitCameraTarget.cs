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
    /// Resolves a section name or panel identifier to a <see cref="CockpitCameraTarget"/>.
    /// </summary>
    public static CockpitCameraTarget? ResolveSection(string? sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName)) return null;

        if (sectionName.Contains("Overhead", StringComparison.OrdinalIgnoreCase))
            return CockpitCameraTarget.Overhead;

        if (sectionName.Contains("Glareshield", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("FCU", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("MCP", StringComparison.OrdinalIgnoreCase))
            return CockpitCameraTarget.Glareshield;

        if (sectionName.Contains("Pedestal", StringComparison.OrdinalIgnoreCase))
            return CockpitCameraTarget.Pedestal;

        if (sectionName.Contains("Main", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("Forward", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("Center", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("Display", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("Instrument", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("Cockpit", StringComparison.OrdinalIgnoreCase))
            return CockpitCameraTarget.MainPanel;

        if (sectionName.Contains("MCDU", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("CDU", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("FMC", StringComparison.OrdinalIgnoreCase))
            return CockpitCameraTarget.MCDU;

        if (sectionName.Contains("EFB", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ||
            sectionName.Contains("flyPad", StringComparison.OrdinalIgnoreCase))
            return CockpitCameraTarget.EFB;

        return null;
    }
}


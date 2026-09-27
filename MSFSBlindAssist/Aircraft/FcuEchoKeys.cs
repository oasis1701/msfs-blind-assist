namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The five FCU value vars the hardware-dial announcer listens to on one airframe (PR #140).
/// One record per airframe so every echo key, phrase switch and readout names a source the same way.
/// </summary>
internal sealed record FcuSources(string Heading, string Speed, string Altitude, string VerticalSpeed, string FlightPathAngle)
{
    /// <summary>FlyByWire A32NX and the Headwind A330, which inherits it.</summary>
    public static readonly FcuSources A32nx = new(
        "A32NX_AUTOPILOT_HEADING_SELECTED", "A32NX_AUTOPILOT_SPEED_SELECTED",
        "A32NX_FCU_SELECTED_ALTITUDE", "A32NX_FCU_SELECTED_VERTICAL_SPEED", "A32NX_FCU_SELECTED_FPA");

    /// <summary>FlyByWire A380X: the shared shims, the stock FCU altitude, PRIM 1's selected words.</summary>
    public static readonly FcuSources A380 = new(
        "A32NX_AUTOPILOT_HEADING_SELECTED", "A32NX_AUTOPILOT_SPEED_SELECTED",
        "FCU_ALT_VALUE", "A32NX_PRIM_1_SELECTED_VERTICAL_SPEED", "A32NX_PRIM_1_SELECTED_FPA");
}

/// <summary>What confirms an MSFSBA-origin FCU write to the pilot, apart from the dial callout.</summary>
internal enum FcuConfirmation
{
    /// <summary>Only the always-on managed/selected mode monitors (or the setter's own readback).</summary>
    None,
    /// <summary>MainForm's press feedback speaks the resulting MODE (GetButtonStateMapping).</summary>
    ModeFeedback,
    /// <summary>A readout speaks the resulting VALUE (A380 panel buttons, FireFCUButton readback:true).</summary>
    ValueReadout,
}

/// <summary>
/// The FCU value vars an MSFSBA-origin FCU event moves AND that something else already confirms —
/// the keys whose next change must not be spoken as a hardware turn. Exact event names only: a
/// substring match muted unrelated events ("ALT_INCREMENT", "METRIC_ALT"). Pinned by FcuEchoKeysTests.
/// </summary>
internal static class FcuEchoKeys
{
    public static IReadOnlyList<string> For(string evt, FcuSources sources, FcuConfirmation confirmation)
    {
        switch (evt)
        {
            case "A32NX.FCU_HDG_PUSH":
            case "A32NX.FCU_HDG_PULL":
            case "A32NX.FCU_HDG_SET":
                return new[] { sources.Heading };
            case "A32NX.FCU_SPD_PUSH":
            case "A32NX.FCU_SPD_PULL":
            case "A32NX.FCU_SPD_SET":
                return new[] { sources.Speed };
            case "A32NX.FCU_ALT_SET":
                return new[] { sources.Altitude };
            case "A32NX.FCU_VS_SET":
                return new[] { sources.VerticalSpeed, sources.FlightPathAngle };
            case "A32NX.FCU_TRK_FPA_TOGGLE_PUSH":
                // The flip re-syncs the heading window onto the track as well as the vertical channel.
                return new[] { sources.Heading, sources.VerticalSpeed, sources.FlightPathAngle };
            case "A32NX.FCU_VS_PUSH":
            case "A32NX.FCU_VS_PULL":
                // Nothing announces a V/S level-off (the FMA stays V/S, there is no mode feedback), so
                // unless a readout will say the value, the dial callout is the only confirmation.
                return confirmation == FcuConfirmation.ValueReadout
                    ? new[] { sources.VerticalSpeed, sources.FlightPathAngle }
                    : Array.Empty<string>();
            case "A32NX.FCU_SPD_MACH_TOGGLE_PUSH":
                // The window's silent button has nothing else to say the target in the new unit.
                return confirmation == FcuConfirmation.None ? Array.Empty<string>() : new[] { sources.Speed };
            default:
                // ALT push/pull move the vertical mode, not the selected altitude; AP/ATHR/LOC/APPR/
                // EXPED/EFIS buttons move no FCU value at all.
                return Array.Empty<string>();
        }
    }
}

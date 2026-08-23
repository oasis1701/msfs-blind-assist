namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A380 FCU's managed-vs-selected ALTITUDE state, derived from the FMA.
///
/// ⚠️ Do NOT go back to reading <c>L:A32NX_FCU_ALT_MANAGED</c>. FBW #10855 (a380x 1bbd304,
/// "add FG part to PRIM") deleted the TypeScript FCU that published it and replaced it with a
/// WASM shim that hardcodes the var, in <c>updatePrimFgShim</c>, every frame:
/// <code>
///     const bool lvlChManaged = false;
///     simConnectInterface.sendEvent(Events::AP_ALTITUDE_SLOT_INDEX_SET, lvlChManaged ? 2 : 1, …);
///     idFcuShimAltManaged->set(lvlChManaged);
/// </code>
/// So the L:var — and the stock <c>AUTOPILOT ALTITUDE SLOT INDEX</c> beside it — read 0/1
/// forever. Measured live at FL360: <c>altManaged=0, altSlot=1</c> while the sibling flags
/// (<c>hdgDashes</c>, <c>spdDot</c>, <c>vsManaged</c>) all read correctly — altitude is the only
/// one #10855 stranded, which is why only this readout went wrong.
///
/// The formula below is the DELETED FCU's own, transcribed from
/// <c>fbw-a380x/src/systems/instruments/src/FCU/Managers/AltitudeManager.ts</c> at
/// <c>1bbd304c4^</c>, so this is not a guess at what "managed altitude" means — it is the
/// definition the var carried:
/// <code>
///     (verticalArmed &amp; (AltCst|Clb|Des|Gs)) > 0 || MANAGED_MODES.includes(verticalMode)
/// </code>
/// Both inputs are still published by the shim (<c>A32NX_FMA_VERTICAL_MODE</c> /
/// <c>A32NX_FMA_VERTICAL_ARMED</c>) and were already monitored by the A380 def, so nothing new
/// is registered to get them.
/// </summary>
public static class AltitudeManagedState
{
    /// <summary>
    /// The armed modes that mean the FMS profile — not the FCU altitude — is what the aircraft
    /// will fly. Bit values are FBW's <c>FgVerticalArmedFlags</c>: Alt=1, AltCst=2, Clb=4,
    /// Des=8, Gs=16, Final=32, Tcas=64.
    ///
    /// ALT (1) is deliberately OUT: it arms the FCU's OWN selected altitude, which is the
    /// definition of selected. FINAL (32) and TCAS (64) are out because the deleted FCU left
    /// them out — FINAL only ever arms alongside DES, and a TCAS RA overrides both modes.
    ///
    /// AltCst (2) is kept for fidelity with that formula even though the #10855 shim never sets
    /// it (<c>verticalArmed = altArmed | (clbArmed &lt;&lt; 2) | (desArmed &lt;&lt; 3) |
    /// (gsArmed &lt;&lt; 4) | (finalArmed &lt;&lt; 5) | (tcasArmed &lt;&lt; 6)</c> — bit 1 is
    /// skipped): an ALT CST arm reaches us through the ALT CST vertical MODE instead, and the
    /// bit costs nothing if FBW restores it.
    /// </summary>
    public const int ManagedArmedMask = 2 | 4 | 8 | 16;

    /// <summary>
    /// True when the vertical guidance is following the FMS profile rather than the FCU's own
    /// selected altitude.
    ///
    /// <paramref name="verticalMode"/> is <c>A32NX_FMA_VERTICAL_MODE</c>,
    /// <paramref name="verticalArmed"/> is <c>A32NX_FMA_VERTICAL_ARMED</c>, and
    /// <paramref name="lateralMode"/> is <c>A32NX_FMA_LATERAL_MODE</c> — see
    /// <see cref="IsAutolandLateralMode"/> for why the lateral mode is needed at all.
    /// </summary>
    public static bool IsManaged(int verticalMode, int verticalArmed, int lateralMode) =>
        (verticalArmed & ManagedArmedMask) != 0
        || IsManagedVerticalMode(verticalMode)
        || IsAutolandLateralMode(lateralMode);

    /// <summary>
    /// The vertical modes the deleted FCU counted as managed: ALT CST (20), ALT CST* (21),
    /// CLB (22), DES (23), FINAL (24), then G/S capture (30) through ROLL OUT (34). Everything
    /// below 20 flies the FCU altitude (ALT, ALT*, OP CLB, OP DES, V/S, FPA) and everything
    /// above 34 is SRS / SRS GA / TCAS.
    /// </summary>
    private static bool IsManagedVerticalMode(int verticalMode) =>
        verticalMode is >= 20 and <= 24 or >= 30 and <= 34;

    /// <summary>
    /// LAND (32) / FLARE (33) / ROLL OUT (34) read off the LATERAL mode.
    ///
    /// This is not a liberty: the #10855 shim's vertical-mode if-chain assigns
    /// <c>lateralMode</c> in its 32/33/34 branches — a verbatim copy-paste of the three
    /// branches in the lateral chain directly above it — so <c>A32NX_FMA_VERTICAL_MODE</c>
    /// structurally CANNOT report an autoland and falls through to 0 ("None") once G/S track
    /// drops. Without this the readout would flip to Selected during the flare and, being a
    /// change, ANNOUNCE it — a spurious call-out at the worst moment of the approach.
    ///
    /// It stays correct if FBW fixes that chain: on a real autoland the lateral and vertical
    /// LAND/FLARE/ROLL OUT modes engage together, so the same three numbers appear on the
    /// lateral mode either way. LOC capture/track (30/31) are NOT included — localizer tracking
    /// says nothing about the vertical side.
    /// </summary>
    private static bool IsAutolandLateralMode(int lateralMode) => lateralMode is >= 32 and <= 34;

    /// <summary>Spoken/display text, matching the wording the dead var's ValueDescriptions used.</summary>
    public static string Text(bool managed) => managed ? "Managed" : "Selected";
}

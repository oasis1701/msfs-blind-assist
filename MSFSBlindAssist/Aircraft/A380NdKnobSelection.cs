namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A380 EFIS-CP ND MODE and ND RANGE knobs.
///
/// ⚠️ <c>A32NX_EFIS_{L,R}_ND_MODE</c> and <c>A32NX_EFIS_{L,R}_ND_RANGE</c> are FCU-shim
/// OUTPUTS, not inputs. <c>fbw.wasm</c>'s <c>FlyByWireInterface::updateFcu</c> decodes the
/// FCU's ARINC <c>efis_discrete_word_1</c> and writes both L:vars EVERY FRAME
/// (<c>idFcuShimLeftNdMode-&gt;set(getNdMode(...))</c>), so a direct L:var write — which is
/// what the <c>A32NX_EFIS_</c> prefix catch-all in HandleUIVariableSet does — is overwritten
/// within one frame and the knob never moves. Live-measured on a380x 2026-09-03: wrote 3 to
/// <c>A32NX_EFIS_L_ND_MODE</c> through the calculator path, read it back 2 immediately.
/// That dead write is what made the First Officer's "EFIS mode: ARC" and "EFIS range: 40"
/// checklist items revert and speak "Unable to complete", and it left both panel combos
/// inert too.
///
/// The knobs are <c>ASOBO_GT_Knob_Infinite</c> firing
/// <c>A32NX.FCU_EFIS_#SIDE#_#TYPE#_{INC,DEC}</c> (efis-cp.xml), and the FCU additionally
/// accepts the ABSOLUTE <c>..._SET</c> — which is what FBW's own in-flight init uses
/// (<c>sendEvent(A32NX_FCU_EFIS_L_MODE_SET, 3)</c>). A380FcuComputer.cpp:2184 assigns the
/// event's parameter straight into <c>pMode</c>/<c>pRange</c>, so the SET parameter IS the
/// Simulink enum. Live-verified: <c>A32NX.FCU_EFIS_L_RANGE_SET</c> with 6 moved the
/// published range from 1 to 2, where the L:var write could not move it at all.
///
/// MODE and RANGE differ in one critical way, and it is the trap:
/// <list type="bullet">
/// <item>MODE — the SET enum (<c>a380_efis_mode_selection</c>) and the published enum
/// (<c>getNdMode</c>) are THE SAME: 0 Rose ILS, 1 Rose VOR, 2 Rose Nav, 3 ARC, 4 Plan. The
/// value passes straight through.</item>
/// <item>RANGE — they are NOT. <c>a380_efis_range_selection</c> begins with five OANS zoom
/// levels (0 = 0.2, 1 = 0.5, 2 = 1, 3 = 2, 4 = 5 NM) and only then RANGE_10..RANGE_640 at
/// 5..11. The published readback (<c>getNdRange</c>) collapses every zoom level to a single
/// 0 and reports 1..7 for 10..640. So "40 NM" READS as 3 and must be SET as 7. Sending the
/// read value verbatim would select 2 NM OANS zoom.</item>
/// </list>
/// The published enum is the one the app speaks and displays (it is what the var actually
/// holds), so the remap belongs here, on the write, and no panel or First Officer
/// definition has to know the FCU's internal numbering.
/// </summary>
public static class A380NdKnobSelection
{
    /// <summary>Published <c>A32NX_EFIS_{side}_ND_RANGE</c> value meaning "an OANS zoom level
    /// is selected". Not settable — see <see cref="ZoomUnsupportedMessage"/>.</summary>
    public const int RangeZoom = 0;

    /// <summary>Offset from the published range index (1 = 10 NM … 7 = 640 NM) to
    /// <c>a380_efis_range_selection</c> (5 = RANGE_10 … 11 = RANGE_640).</summary>
    private const int RangeSetOffset = 4;

    /// <summary>Highest published range index (640 NM).</summary>
    private const int MaxPublishedRange = 7;

    /// <summary>Highest <c>a380_efis_mode_selection</c> value (PLAN).</summary>
    private const int MaxMode = 4;

    /// <summary>
    /// The FCU event and parameter that move a knob to <paramref name="value"/>, expressed in
    /// the SAME enum the matching <c>A32NX_EFIS_{side}_ND_{MODE,RANGE}</c> L:var publishes, or
    /// null when <paramref name="varKey"/> is not one of those four keys — leaving every other
    /// EFIS key to the direct-write catch-all, which is correct for them.
    ///
    /// Returns null for an out-of-range value too, including the published range 0 (zoom):
    /// there are five distinct zoom levels behind that single readback, so no one SET value
    /// reproduces it.
    /// </summary>
    public static (string EventName, uint Parameter)? SetEvent(string varKey, double value)
    {
        string? side = varKey switch
        {
            "A32NX_EFIS_L_ND_MODE" or "A32NX_EFIS_L_ND_RANGE" => "L",
            "A32NX_EFIS_R_ND_MODE" or "A32NX_EFIS_R_ND_RANGE" => "R",
            _ => null
        };
        if (side == null) return null;

        int v = (int)Math.Round(value);
        if (varKey.EndsWith("_ND_MODE", StringComparison.Ordinal))
        {
            if (v < 0 || v > MaxMode) return null;
            return ($"A32NX.FCU_EFIS_{side}_MODE_SET", (uint)v);
        }

        if (v < 1 || v > MaxPublishedRange) return null;
        return ($"A32NX.FCU_EFIS_{side}_RANGE_SET", (uint)(v + RangeSetOffset));
    }

    /// <summary>True when the pilot picked the "Zoom" position on a ND RANGE combo. The
    /// readback legitimately shows Zoom (the OANS is zoomed in), so the position has to stay in
    /// the value list, but it cannot be commanded back.</summary>
    public static bool IsZoomAttempt(string varKey, double value) =>
        varKey.EndsWith("_ND_RANGE", StringComparison.Ordinal)
        && (int)Math.Round(value) == RangeZoom;

    /// <summary>Spoken when Zoom is picked on a ND RANGE combo. A control that silently does
    /// nothing is worse for a blind pilot than one that explains itself (the
    /// <see cref="NdFilterSelection.ClearUnsupportedMessage"/> precedent).</summary>
    public static string ZoomUnsupportedMessage =>
        "Zoom is an OANS range and cannot be selected here. Choose a range in nautical miles.";
}

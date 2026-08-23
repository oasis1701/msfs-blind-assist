using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Which flavour of armed ALT the FMA is showing — plain altitude, an FMS altitude CONSTRAINT,
/// or (A380 only) the cruise altitude. Shared by the A380 and the A32NX, which agree on the
/// SEMANTIC and differ only in how the aircraft carries the qualifier: see
/// <see cref="ConstraintApplicable"/> (A380, a discrete bit) against
/// <see cref="ConstraintApplicableFromConstraintWord"/> (A32NX, an SSM).
///
/// ⚠️ Do NOT go looking for an "ALT CST armed" bit. The A380 PRIM FG has none: its armed-modes
/// bus (<c>base_prim_armed_modes</c>) carries <c>alt_acq_armed</c>, <c>alt_acq_arm_possible</c>,
/// <c>glide</c>, <c>app_des</c>, <c>clb</c>, <c>des</c>, <c>op_clb</c>, <c>tcas</c>,
/// <c>nav</c>, <c>loc</c>, <c>rwy</c>, <c>land</c> — and that is the whole list. The
/// constraint is a SEPARATE qualifier, <c>alt_cstr_applicable</c>, and the PFD's own
/// armed-vertical cell (<c>FMA.tsx</c> <c>B2Cell</c>) combines the two:
/// <code>
///     altAcqArmed &amp;&amp; altCstrApplicable -> "ALT" rendered MAGENTA
///     altAcqArmed &amp;&amp; altIsCrzAlt       -> "ALT CRZ"
///     altAcqArmed                            -> "ALT" rendered CYAN
/// </code>
/// So on the A380 the constraint case is signalled by COLOUR ALONE — the text stays "ALT" —
/// which is exactly the difference a blind pilot has no other channel for. (The A320 writes
/// "ALT CST" in the text, which is why the older MSFSBA bit table had a name for it.)
///
/// This is also why the "Altitude constraint" entry that used to sit at bit 2 of
/// <c>A32NX_FMA_VERTICAL_ARMED</c> could never fire and has been removed rather than repaired:
/// FBW's shim builds that bitmask as
/// <c>altArmed | (clbArmed &lt;&lt; 2) | (desArmed &lt;&lt; 3) | (gsArmed &lt;&lt; 4) |
/// (finalArmed &lt;&lt; 5) | (tcasArmed &lt;&lt; 6)</c> — bit 1 is skipped because there is no
/// signal to put there. The constraint refines the name of the ALT bit instead.
///
/// ⚠️ Neither qualifier is an armed state on its own and neither may ever announce by itself.
/// Measured live at FL360 straight after a step climb: <c>alt_cstr_applicable</c> was TRUE while
/// <c>A32NX_FMA_VERTICAL_ARMED</c> was 0 and nothing at all was armed.
/// </summary>
public static class ArmedAltitudeMode
{
    /// <summary>ARINC 429 bit of <c>alt_cstr_applicable</c> in PRIM FG discrete word 3.</summary>
    private const int ConstraintApplicableBit = 28;

    /// <summary>ARINC 429 bit of <c>altIsCrzAlt</c> in PRIM FG discrete word 3 — the same bit the
    /// "Cruise Altitude Mode" readout already decodes.</summary>
    private const int CruiseAltitudeBit = 29;

    /// <summary>
    /// The name to speak for the armed ALT bit. <paramref name="altConstraintApplicable"/> wins
    /// over <paramref name="altIsCruiseAltitude"/> because that is the order the PFD's own
    /// if-chain tests them in.
    /// </summary>
    public static string Name(bool altConstraintApplicable, bool altIsCruiseAltitude) =>
        altConstraintApplicable ? "Altitude constraint"
        : altIsCruiseAltitude ? "Cruise altitude"
        : "Altitude";

    /// <summary>
    /// <c>alt_cstr_applicable</c> off a raw PRIM FG discrete word 3. SSM-gated by
    /// <see cref="Arinc429Word.BitValueOr"/>: a word that is not Normal Operation / Functional
    /// Test reads false, so a failed word degrades to the plain "Altitude" call-out rather than
    /// inventing a constraint.
    /// </summary>
    public static bool ConstraintApplicable(double rawWord) =>
        new Arinc429Word(rawWord).BitValueOr(ConstraintApplicableBit, false);

    /// <summary><c>altIsCrzAlt</c> off a raw PRIM FG discrete word 3, SSM-gated the same way.</summary>
    public static bool IsCruiseAltitude(double rawWord) =>
        new Arinc429Word(rawWord).BitValueOr(CruiseAltitudeBit, false);

    /// <summary>
    /// The SAME qualifier on the A32NX, which carries it by a different route: its FMGC encodes
    /// <c>alt_cstr_applicable</c> as the SSM of the constraint VALUE word rather than as a
    /// discrete bit (<c>FmgcComputer.cpp:4898</c>):
    /// <code>
    ///     if (alt_cstr_applicable) fmgc_a_bus.fm_alt_constraint_ft.SSM = NormalOperation;
    ///     else                     fmgc_a_bus.fm_alt_constraint_ft.SSM = NoComputedData;
    /// </code>
    /// So "is there a constraint?" IS "is that word in Normal Operation?" — which is exactly
    /// what the A32NX PFD reads (<c>FMA.tsx</c>: <c>altAcqArmed &amp;&amp; !clbArmed &amp;&amp;
    /// altConstraint.isNormalOperation()</c>). Pass the raw
    /// <c>A32NX_FMGC_{1,2}_FM_ALTITUDE_CONSTRAINT</c>.
    ///
    /// ⚠️ Normal Operation ONLY — deliberately stricter than <see cref="Arinc429Word.BitValueOr"/>,
    /// which also accepts Functional Test. Matching the PFD exactly is the point; a lamp test
    /// must not manufacture an altitude constraint.
    ///
    /// The A32NX PFD's extra <c>!clbArmed</c> term is NOT reproduced here, on either airframe.
    /// That term decides which single label to draw in one text slot, not whether the armed
    /// altitude is a constraint — the A380's own COLOUR rule (<c>B2Cell.classSub</c>, the rule
    /// this helper mirrors) has no such term. MSFSBA announces each newly-armed mode separately
    /// rather than only the top-priority one, so the display-priority question does not arise.
    /// </summary>
    public static bool ConstraintApplicableFromConstraintWord(double rawConstraintWord) =>
        new Arinc429Word(rawConstraintWord).IsNormalOperation;
}

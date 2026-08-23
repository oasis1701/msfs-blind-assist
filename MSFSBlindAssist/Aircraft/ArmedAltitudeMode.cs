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
    /// ⚠️ <c>IsNormalOperation</c>, not <see cref="Arinc429Word.BitValueOr"/> — and that is NOT
    /// this code choosing to be stricter than the A380 half above. The two aircraft carry the
    /// qualifier differently and each mirrors ITS OWN PFD's accessor: the A380 reads a discrete
    /// BIT, so it uses FBW's <c>bitValueOr</c> semantics (Normal Operation OR Functional Test),
    /// while the A32NX reads a value word's VALIDITY, for which <c>isNormalOperation()</c> is the
    /// only sensible accessor and is exactly what its PFD calls. Do not "harmonize" them.
    ///
    /// The A32NX PFD's extra <c>!clbArmed</c> term is NOT reproduced here, on either airframe.
    /// That term decides which single label to draw in one text slot, not whether the armed
    /// altitude is a constraint — the A380's own COLOUR rule (<c>B2Cell.classSub</c>, the rule
    /// this helper mirrors) has no such term. MSFSBA announces each newly-armed mode separately
    /// rather than only the top-priority one, so the display-priority question does not arise.
    /// </summary>
    public static bool ConstraintApplicableFromConstraintWord(double rawConstraintWord) =>
        new Arinc429Word(rawConstraintWord).IsNormalOperation;

    /// <summary>
    /// An armed-vertical-mode bit table with the ALT entry named for the qualifiers currently in
    /// force. Shared by both FBW airframes, which differ only in HOW they read the qualifier —
    /// keeping the transform here is what stops the "find the ALT entry by VALUE, never by
    /// position" rule from existing in two copies that can drift.
    ///
    /// Returns a NEW array; <paramref name="bits"/> is typically a shared static table and must
    /// never be mutated, or a rename would outlive the constraint that caused it.
    /// </summary>
    public static (int bit, string name)[] NameAltArmedBit(
        (int bit, string name)[] bits, bool altConstraintApplicable, bool altIsCruiseAltitude)
    {
        var named = ((int bit, string name)[])bits.Clone();
        for (int i = 0; i < named.Length; i++)
            if (named[i].bit == AltArmedBit)   // the ALT bit — found by VALUE, never by position
                named[i].name = Name(altConstraintApplicable, altIsCruiseAltitude);
        return named;
    }

    /// <summary>
    /// The ALT bit within <c>A32NX_FMA_VERTICAL_ARMED</c>. A raw bit VALUE, not a shift
    /// position — the <c>_vertArmedBits</c> tables are written as values (1 Altitude, 4 Climb,
    /// 8 Descent, 16 Glideslope, 32 Final, 64 TCAS), and <see cref="NameAltArmedBit"/> finds its
    /// entry by comparing against this same value.
    /// </summary>
    public const int AltArmedBit = 1;

    /// <summary>
    /// The newly-armed bits that can be announced IMMEDIATELY — everything except ALT.
    ///
    /// ALT is the only armed mode with a qualifier (an FMS altitude constraint, or on the A380
    /// the cruise altitude), and that qualifier is always dispatched to the definition AFTER the
    /// armed bitmask itself: on the A380 it rides a different continuous batch, on the A32NX it
    /// sorts a few slots later in the same one. Naming ALT inline therefore reads the PREVIOUS
    /// tick's qualifier, and because the call-out is edge-triggered the wrong name is permanent.
    /// So ALT is held and flushed once the qualifier has settled; every other mode is unaffected
    /// and keeps its immediate call-out.
    /// </summary>
    public static int ImmediateArmedBits(int newlyArmed) => newlyArmed & ~AltArmedBit;

    /// <summary>Whether a newly-armed bitmask contains ALT and therefore needs the hold.</summary>
    public static bool ShouldHoldAltAnnouncement(int newlyArmed) => (newlyArmed & AltArmedBit) != 0;

    /// <summary>
    /// Whether a held ALT announcement is still worth speaking when the hold flushes: ALT must
    /// still be armed in the CURRENT bitmask. An ALT that arms and disarms inside the hold
    /// window is dropped rather than announced after it stopped being true.
    /// </summary>
    public static bool HeldAltStillArmed(int currentArmed) => (currentArmed & AltArmedBit) != 0;
}

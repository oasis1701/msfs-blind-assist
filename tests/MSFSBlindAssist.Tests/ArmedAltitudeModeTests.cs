// Which flavour of armed ALT the A380 FMA is showing.
//
// The A380 PRIM FG has NO "ALT CST armed" bit — its armed-modes bus (base_prim_armed_modes)
// carries alt_acq_armed, glide, app_des, clb, des, op_clb, tcas, nav, loc, rwy, land and
// nothing else. The constraint is a SEPARATE qualifier, alt_cstr_applicable, and the PFD's
// own armed-vertical cell (FMA.tsx B2Cell) combines the two:
//
//     altAcqArmed   = PRIM_1_FG_DISCRETE_WORD_2 bit 11   (reaches us as VERTICAL_ARMED bit 1)
//     altCstrApplicable = PRIM_1_FG_DISCRETE_WORD_3 bit 28
//     altIsCrzAlt       = PRIM_1_FG_DISCRETE_WORD_3 bit 29
//
//     altAcqArmed && altCstrApplicable -> "ALT" rendered MAGENTA
//     altAcqArmed && altIsCrzAlt       -> "ALT CRZ"
//     altAcqArmed                      -> "ALT" rendered CYAN
//
// so on the A380 the constraint case is signalled by COLOUR ALONE — the text stays "ALT" —
// which is precisely the difference a blind pilot has no other way to get.

using System.Reflection;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class ArmedAltitudeModeTests
{
    [Fact]
    public void Plain_altitude_when_neither_qualifier_is_set()
    {
        Assert.Equal("Altitude", ArmedAltitudeMode.Name(altConstraintApplicable: false, altIsCruiseAltitude: false));
    }

    [Fact]
    public void Constraint_qualifier_names_the_constraint()
    {
        Assert.Equal("Altitude constraint", ArmedAltitudeMode.Name(altConstraintApplicable: true, altIsCruiseAltitude: false));
    }

    [Fact]
    public void Cruise_qualifier_names_the_cruise_altitude()
    {
        Assert.Equal("Cruise altitude", ArmedAltitudeMode.Name(altConstraintApplicable: false, altIsCruiseAltitude: true));
    }

    [Fact]
    public void Constraint_outranks_cruise_exactly_as_the_pfd_orders_them()
    {
        // FMA.tsx tests `altAcqArmed && altCstrApplicable` BEFORE `altAcqArmed && altIsCrzAlt`.
        Assert.Equal("Altitude constraint", ArmedAltitudeMode.Name(altConstraintApplicable: true, altIsCruiseAltitude: true));
    }

    // ---- The qualifiers are read off an ARINC 429 word, SSM-gated ----

    [Fact]
    public void Qualifiers_are_read_from_the_live_word()
    {
        // Measured live at FL360 after a step climb: SSM 3 (Normal Operation), payload
        // 0x4D804000 — bit 28 set, bit 29 clear. Nothing was armed at the time, which is the
        // whole point: the constraint qualifier stands on its own and must never be mistaken
        // for an armed state (see Name's contract).
        double raw = (3.0 * 4294967296.0) + 0x4D804000;
        Assert.True(ArmedAltitudeMode.ConstraintApplicable(raw));
        Assert.False(ArmedAltitudeMode.IsCruiseAltitude(raw));
    }

    [Fact]
    public void A_word_that_is_not_normal_operation_reads_both_qualifiers_as_false()
    {
        // SSM 0 = Failure Warning. Degrading to the plain "Altitude" call-out is the safe
        // answer; inventing a constraint from a failed word is not.
        double failed = (0.0 * 4294967296.0) + 0x4D804000;
        Assert.False(ArmedAltitudeMode.ConstraintApplicable(failed));
        Assert.False(ArmedAltitudeMode.IsCruiseAltitude(failed));
        Assert.Equal("Altitude", ArmedAltitudeMode.Name(
            ArmedAltitudeMode.ConstraintApplicable(failed), ArmedAltitudeMode.IsCruiseAltitude(failed)));
    }

    [Fact]
    public void Cruise_bit_is_read_at_the_right_position()
    {
        // Bit 29 alone, SSM Normal Operation.
        double raw = (3.0 * 4294967296.0) + (1u << 28);
        Assert.False(ArmedAltitudeMode.ConstraintApplicable(raw));
        Assert.True(ArmedAltitudeMode.IsCruiseAltitude(raw));
    }

    [Fact]
    public void Constraint_bit_is_read_at_the_right_position()
    {
        // Bit 28 ALONE, SSM Normal Operation. The live-capture test above uses a payload with
        // six bits set, so it passes for five WRONG bit indices; this is what actually pins 28.
        double raw = (3.0 * 4294967296.0) + (1u << 27);
        Assert.True(ArmedAltitudeMode.ConstraintApplicable(raw));
        Assert.False(ArmedAltitudeMode.IsCruiseAltitude(raw));
    }

    [Fact]
    public void NameAltArmedBit_renames_only_the_alt_entry_and_leaves_the_source_untouched()
    {
        var source = new (int bit, string name)[]
            { (1, "Altitude"), (4, "Climb"), (16, "Glideslope") };

        var named = ArmedAltitudeMode.NameAltArmedBit(source, altConstraintApplicable: true, altIsCruiseAltitude: false);

        Assert.Equal("Altitude constraint", named[0].name);
        Assert.Equal("Climb", named[1].name);
        Assert.Equal("Glideslope", named[2].name);
        // The shared static table must NOT be mutated — a rename that leaked into it would
        // survive the constraint clearing and mislabel every later arm for the session.
        Assert.Equal("Altitude", source[0].name);
    }

    [Fact]
    public void NameAltArmedBit_finds_the_alt_entry_by_value_not_by_position()
    {
        var source = new (int bit, string name)[] { (4, "Climb"), (1, "Altitude") };
        var named = ArmedAltitudeMode.NameAltArmedBit(source, altConstraintApplicable: false, altIsCruiseAltitude: true);
        Assert.Equal("Climb", named[0].name);
        Assert.Equal("Cruise altitude", named[1].name);
    }

    // ---- Splitting a newly-armed bitmask into "say now" and "hold" ----
    //
    // The ALT entry is the only one with a qualifier (constraint / cruise altitude), and its
    // qualifier is always DISPATCHED AFTER the armed bitmask, so naming it inline reads the
    // previous tick's value. It is held; everything else is announced at once.

    [Fact]
    public void Immediate_bits_strip_only_the_alt_bit()
    {
        // ALT(1) + CLB(4) + DES(8) armed together: CLB and DES speak now, ALT is held.
        Assert.Equal(4 | 8, ArmedAltitudeMode.ImmediateArmedBits(1 | 4 | 8));
    }

    [Fact]
    public void Immediate_bits_pass_a_mask_without_alt_through_unchanged()
    {
        Assert.Equal(4 | 16, ArmedAltitudeMode.ImmediateArmedBits(4 | 16));
    }

    [Fact]
    public void Immediate_bits_are_empty_when_alt_armed_alone()
    {
        Assert.Equal(0, ArmedAltitudeMode.ImmediateArmedBits(1));
    }

    [Theory]
    [InlineData(1, true)]        // ALT alone
    [InlineData(1 | 4, true)]    // ALT with CLB
    [InlineData(4, false)]       // CLB alone
    [InlineData(0, false)]       // nothing newly armed
    public void Hold_is_needed_exactly_when_the_alt_bit_is_newly_armed(int newlyArmed, bool expected)
    {
        Assert.Equal(expected, ArmedAltitudeMode.ShouldHoldAltAnnouncement(newlyArmed));
    }

    [Theory]
    [InlineData(1, true)]        // still armed at flush time
    [InlineData(1 | 8, true)]    // still armed, alongside DES
    [InlineData(8, false)]       // ALT disarmed inside the hold window
    [InlineData(0, false)]       // everything disarmed
    [InlineData(-1, false)]      // the "no baseline yet" sentinel — NOT an armed mask
    public void A_held_announcement_survives_only_while_alt_is_still_armed(int currentArmed, bool expected)
    {
        // An ALT that arms and disarms inside the hold window must be DROPPED, not spoken late.
        //
        // -1 is the sentinel BOTH defs write to _prevVertArmed in ResetAnnouncementBaselines and
        // in the field initialiser, and it is what this guard is handed. It means "no baseline
        // taken", not "everything armed" — but -1 & 1 == 1, so the naive bit test says ALT is
        // armed and a hold surviving a re-baseline would speak a phantom call-out with nothing
        // armed at all. That is the exact invention the "neither qualifier may ever announce
        // alone" rule exists to prevent.
        Assert.Equal(expected, ArmedAltitudeMode.HeldAltStillArmed(currentArmed));
    }

    [Theory]
    // pending, muted, currentArmed  -> speak?
    [InlineData(true,  false, 1,  true)]     // the ordinary case: held, audible, still armed
    [InlineData(false, false, 1,  false)]    // nothing held — a batch delivery with no arm behind it
    [InlineData(true,  true,  1,  false)]    // Ctrl+M muted DURING the hold window
    [InlineData(true,  false, 8,  false)]    // ALT disarmed before the qualifier landed
    [InlineData(true,  false, -1, false)]    // re-baselined mid-hold
    public void A_held_alt_call_out_is_spoken_only_when_all_three_gates_agree(
        bool pending, bool muted, int currentArmed, bool expected)
    {
        // The three gates are checked in ONE shared place because both airframes must agree on
        // them: the A32NX flush runs outside MainForm's announcer.Suppressed wrap and so has to
        // consult the mute itself, and both must drop a hold whose ALT is no longer armed.
        Assert.Equal(expected, ArmedAltitudeMode.ShouldSpeakHeldAlt(pending, muted, currentArmed));
    }

    [Theory]
    [InlineData(typeof(FlyByWireA320Definition))]
    [InlineData(typeof(FlyByWireA380Definition))]
    public void Each_fbw_vert_armed_table_puts_exactly_one_entry_on_the_alt_bit(Type definitionType)
    {
        // The whole hold/flush machinery (ImmediateArmedBits / ShouldHoldAltAnnouncement /
        // HeldAltStillArmed) is keyed on AltArmedBit, so a DEFINITION whose own table moved
        // Altitude off that bit would desync speech from what the FMA decoder announces.
        // Asserting the entry's NAME would pin nothing — NameAltArmedBit overwrites it on every
        // use — so the load-bearing assertion is that the bit is present exactly once.
        var bits = VertArmedBits(definitionType);
        Assert.Single(bits, b => b.bit == ArmedAltitudeMode.AltArmedBit);
    }

    [Theory]
    [InlineData(typeof(FlyByWireA320Definition))]
    [InlineData(typeof(FlyByWireA380Definition))]
    public void Bit_one_of_each_fbw_lateral_table_is_nav_not_altitude(Type definitionType)
    {
        // Why ImmediateArmedBits is applied ONLY to a vertical mask: the same bit VALUE that
        // means Altitude vertically means NAV laterally. Stripping it from a lateral mask would
        // silently and permanently drop "NAV armed", which has no other channel. This pins the
        // collision that makes that guard necessary, so the reason survives a refactor.
        var bits = ArmedBitsField(definitionType, "_latArmedBits");
        Assert.Equal("NAV", bits.Single(b => b.bit == ArmedAltitudeMode.AltArmedBit).name);
    }

    private static (int bit, string name)[] VertArmedBits(Type definitionType) =>
        ArmedBitsField(definitionType, "_vertArmedBits");

    private static (int bit, string name)[] ArmedBitsField(Type definitionType, string fieldName)
    {
        var field = definitionType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field != null,
            $"{definitionType.Name} has no private static {fieldName}. If it was renamed or hoisted "
            + "into a base class, update this test — it is what stops the two airframes' bit tables "
            + "drifting from the AltArmedBit the hold machinery is keyed on.");
        return ((int bit, string name)[])field!.GetValue(null)!;
    }
}

/// <summary>
/// The A32NX carries the SAME qualifier by a different route. Its FMGC encodes
/// <c>alt_cstr_applicable</c> as the SSM of the constraint VALUE word rather than as a
/// discrete bit (<c>FmgcComputer.cpp:4898</c>):
/// <code>
///     if (alt_cstr_applicable) fmgc_a_bus.fm_alt_constraint_ft.SSM = NormalOperation;
///     else                     fmgc_a_bus.fm_alt_constraint_ft.SSM = NoComputedData;
/// </code>
/// which is exactly what the A32NX PFD reads (<c>FMA.tsx</c>: <c>altAcqArmed &amp;&amp;
/// !clbArmed &amp;&amp; altConstraint.isNormalOperation()</c>). Its FMGC has no
/// <c>alt_cst_armed</c> bit either — <c>base_fmgc_armed_modes</c> carries
/// <c>alt_acq_armed</c> / <c>alt_acq_arm_possible</c> and no constraint member — and there is
/// no cruise-altitude branch on that airframe at all.
/// </summary>
public class ArmedAltitudeModeConstraintWordTests
{
    private const double NoComputedData = 1.0 * 4294967296.0;
    private const double FunctionalTest = 2.0 * 4294967296.0;
    private const double NormalOperation = 3.0 * 4294967296.0;
    private const double FailureWarning = 0.0;

    // 8000 ft as an IEEE-754 float payload, the shape fm_alt_constraint_ft actually carries.
    private static readonly uint Payload8000Ft = BitConverter.SingleToUInt32Bits(8000f);

    [Fact]
    public void A_constraint_word_in_normal_operation_means_the_constraint_applies()
    {
        Assert.True(ArmedAltitudeMode.ConstraintApplicableFromConstraintWord(NormalOperation + Payload8000Ft));
    }

    [Fact]
    public void No_computed_data_means_no_constraint()
    {
        // This is the FMGC's own "else" branch — not a failure, just nothing constrained.
        Assert.False(ArmedAltitudeMode.ConstraintApplicableFromConstraintWord(NoComputedData + Payload8000Ft));
    }

    [Fact]
    public void A_failed_word_means_no_constraint()
    {
        Assert.False(ArmedAltitudeMode.ConstraintApplicableFromConstraintWord(FailureWarning + Payload8000Ft));
    }

    [Fact]
    public void Functional_test_is_not_normal_operation()
    {
        // Deliberately stricter than Arinc429Word.BitValueOr, which accepts Functional Test:
        // the A32NX PFD gates on isNormalOperation() alone, and this must not diverge from it.
        Assert.False(ArmedAltitudeMode.ConstraintApplicableFromConstraintWord(FunctionalTest + Payload8000Ft));
    }

    [Fact]
    public void The_a320_never_claims_a_cruise_altitude()
    {
        // No ALT CRZ branch exists in the A32NX FMA, so the A320 always passes false and the
        // shared namer must fall through to the constraint/plain pair.
        Assert.Equal("Altitude constraint", ArmedAltitudeMode.Name(
            ArmedAltitudeMode.ConstraintApplicableFromConstraintWord(NormalOperation + Payload8000Ft),
            altIsCruiseAltitude: false));
        Assert.Equal("Altitude", ArmedAltitudeMode.Name(
            ArmedAltitudeMode.ConstraintApplicableFromConstraintWord(NoComputedData + Payload8000Ft),
            altIsCruiseAltitude: false));
    }
}

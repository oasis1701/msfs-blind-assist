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
}

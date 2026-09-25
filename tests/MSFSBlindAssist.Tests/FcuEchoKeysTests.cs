// The echo keys an MSFSBA-origin FCU write mutes (PR #140 review). One EXPLICIT table keyed by
// event name and by how the write is confirmed, replacing two copied substring classifiers.
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FcuEchoKeysTests
{
    private static readonly FcuSources A32 = FcuSources.A32nx;
    private static readonly FcuSources A380 = FcuSources.A380;

    // A public [Theory] method cannot take an internal enum as its own parameter type (CS0051),
    // even with InternalsVisibleTo — FcuConfirmation stays internal per the interface, so every
    // enum-parameterized theory below carries the value boxed as int and casts it back inside.
    public static IEnumerable<object[]> AllConfirmations() =>
        Enum.GetValues<FcuConfirmation>().Select(c => new object[] { (int)c });

    [Theory]
    [MemberData(nameof(AllConfirmations))]
    public void Heading_and_speed_writes_mute_their_own_value(int confirmationValue)
    {
        var c = (FcuConfirmation)confirmationValue;
        foreach (var s in new[] { A32, A380 })
        {
            foreach (string evt in new[] { "A32NX.FCU_HDG_PUSH", "A32NX.FCU_HDG_PULL", "A32NX.FCU_HDG_SET" })
                Assert.Equal(new[] { s.Heading }, FcuEchoKeys.For(evt, s, c));
            foreach (string evt in new[] { "A32NX.FCU_SPD_PUSH", "A32NX.FCU_SPD_PULL", "A32NX.FCU_SPD_SET" })
                Assert.Equal(new[] { s.Speed }, FcuEchoKeys.For(evt, s, c));
        }
    }

    [Theory]
    [MemberData(nameof(AllConfirmations))]
    public void Only_an_altitude_SET_moves_the_altitude(int confirmationValue)
    {
        var c = (FcuConfirmation)confirmationValue;
        // ALT push/pull change the vertical mode, never the selected altitude (FBW FcuComputer moves
        // it only on a set or a knob turn), so muting it would swallow a hardware turn for nothing.
        foreach (var s in new[] { A32, A380 })
        {
            Assert.Equal(new[] { s.Altitude }, FcuEchoKeys.For("A32NX.FCU_ALT_SET", s, c));
            Assert.Empty(FcuEchoKeys.For("A32NX.FCU_ALT_PUSH", s, c));
            Assert.Empty(FcuEchoKeys.For("A32NX.FCU_ALT_PULL", s, c));
        }
    }

    [Fact]
    public void A_vs_push_or_pull_is_muted_only_when_a_readout_will_say_the_value()
    {
        // Nothing else confirms a V/S level-off (no button-state mapping, the FMA stays V/S), so
        // without a readout the dial callout is the only confirmation the pilot gets.
        foreach (var s in new[] { A32, A380 })
            foreach (string evt in new[] { "A32NX.FCU_VS_PUSH", "A32NX.FCU_VS_PULL" })
            {
                Assert.Empty(FcuEchoKeys.For(evt, s, FcuConfirmation.None));
                Assert.Empty(FcuEchoKeys.For(evt, s, FcuConfirmation.ModeFeedback));
                Assert.Equal(new[] { s.VerticalSpeed, s.FlightPathAngle },
                    FcuEchoKeys.For(evt, s, FcuConfirmation.ValueReadout));
            }
    }

    [Fact]
    public void A_vs_set_mutes_both_vertical_words()
    {
        foreach (var s in new[] { A32, A380 })
            Assert.Equal(new[] { s.VerticalSpeed, s.FlightPathAngle },
                FcuEchoKeys.For("A32NX.FCU_VS_SET", s, FcuConfirmation.None));
    }

    [Fact]
    public void Spd_mach_is_muted_whenever_something_else_speaks_the_change()
    {
        foreach (var s in new[] { A32, A380 })
        {
            // The window's silent SPD/MACH button: the dial is the only thing that says the new unit.
            Assert.Empty(FcuEchoKeys.For("A32NX.FCU_SPD_MACH_TOGGLE_PUSH", s, FcuConfirmation.None));
            // The panel's "Mach mode on" feedback, or a readout, already confirms it.
            Assert.Equal(new[] { s.Speed }, FcuEchoKeys.For("A32NX.FCU_SPD_MACH_TOGGLE_PUSH", s, FcuConfirmation.ModeFeedback));
            Assert.Equal(new[] { s.Speed }, FcuEchoKeys.For("A32NX.FCU_SPD_MACH_TOGGLE_PUSH", s, FcuConfirmation.ValueReadout));
        }
    }

    [Theory]
    [MemberData(nameof(AllConfirmations))]
    public void A_trk_fpa_flip_mutes_the_heading_and_both_vertical_words(int confirmationValue)
    {
        var c = (FcuConfirmation)confirmationValue;
        foreach (var s in new[] { A32, A380 })
            Assert.Equal(new[] { s.Heading, s.VerticalSpeed, s.FlightPathAngle },
                FcuEchoKeys.For("A32NX.FCU_TRK_FPA_TOGGLE_PUSH", s, c));
    }

    [Theory]
    [InlineData("A32NX.FCU_AP_1_PUSH")]
    [InlineData("A32NX.FCU_ATHR_PUSH")]
    [InlineData("A32NX.FCU_EXPED_PUSH")]
    [InlineData("A32NX.FCU_ALT_INCREMENT_TOGGLE")]    // contains "ALT" — the old substring match muted it
    [InlineData("A32NX.FCU_METRIC_ALT_TOGGLE_PUSH")]  // contains "ALT"
    [InlineData("A32NX.FCU_EFIS_L_VORD_PUSH")]
    [InlineData("XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT")] // not an FCU event at all
    [InlineData("A32NX_FCU_SELECTED_ALTITUDE")]
    public void Events_that_move_no_value_mute_nothing(string evt)
    {
        foreach (FcuConfirmation c in Enum.GetValues<FcuConfirmation>())
        {
            Assert.Empty(FcuEchoKeys.For(evt, A32, c));
            Assert.Empty(FcuEchoKeys.For(evt, A380, c));
        }
    }

    [Fact]
    public void Every_echo_key_is_a_var_its_airframe_announces()
    {
        // A key the announcer does not listen to mutes nothing and the write's own echo is spoken.
        var a32 = new FlyByWireA320Definition();
        foreach (string key in new[] { A32.Heading, A32.Speed, A32.Altitude, A32.VerticalSpeed, A32.FlightPathAngle })
            Assert.True(a32.TryComposeFcuValuePhrase(key, 0.0, out _), key);
        var a380 = new FlyByWireA380Definition();
        foreach (string key in new[] { A380.Heading, A380.Speed, A380.Altitude, A380.VerticalSpeed, A380.FlightPathAngle })
            Assert.True(a380.TryComposeFcuValuePhrase(key, 0.0, out _), key);
    }
}

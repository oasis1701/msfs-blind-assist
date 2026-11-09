using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The A300 flight mode annunciator, decoded from the aircraft's own mode numbers. Every expected
/// word here was read off the A300's own PFD drawing routine (PFD::drawFMA, A300 v1.0.11), run
/// against chosen mode numbers during the 2026-10-03 investigation; the words are the readable
/// forms of what it draws ("P.CLB" is "Profile climb").
/// </summary>
public class A300FmaTests
{
    /// <summary>A powered, aligned aircraft with autopilot 1 on, the autothrottle on and both
    /// master switches on, in SPD / V/S / HDG with nothing armed.</summary>
    private static A300FmaInputs Flying() => new()
    {
        PitchMode = 8,
        RollMode = 3,
        AtMode = 1,
        AtOn = true,
        AtMasterSwitch1 = true,
        AtMasterSwitch2 = true,
        Ap1 = true,
        Irs1Aligned = true,
        Irs2Aligned = true,
        Irs3Aligned = true,
        PitchTrim1 = true,
        PitchTrim2 = true,
    };

    [Fact]
    public void A_flying_aircraft_reads_each_column()
    {
        var fma = A300Fma.Read(Flying());
        Assert.Equal("Speed", fma.Thrust);
        Assert.Equal("Vertical speed", fma.Pitch);
        Assert.Equal("Heading hold", fma.Roll);
        Assert.Null(fma.Common);
        Assert.Empty(fma.Armed);
        Assert.Equal("CMD 1", fma.Autopilot);
    }

    [Theory]
    [InlineData(1, "SRS")]
    [InlineData(2, "Speed")]
    [InlineData(3, "Profile climb")]
    [InlineData(4, "Speed")]
    [InlineData(5, "Profile descent")]
    [InlineData(6, "Altitude")]
    [InlineData(7, "Glide slope")]
    [InlineData(8, "Vertical speed")]
    [InlineData(12, "Profile altitude")]
    [InlineData(13, "Profile descent")]
    [InlineData(14, "Profile altitude")]
    [InlineData(15, "Profile altitude")]
    [InlineData(16, "Profile descent")]
    [InlineData(20, "Profile altitude")]
    [InlineData(22, "Altitude capture")]
    [InlineData(24, "Profile climb")]
    [InlineData(25, "Profile descent")]
    [InlineData(26, "Profile altitude")]
    [InlineData(27, "Profile climb")]
    [InlineData(28, "Altitude")]
    [InlineData(29, "Altitude")]
    public void Pitch_modes_read_as_the_pfd_draws_them(int mode, string words)
    {
        Assert.Equal(words, A300Fma.Read(Flying() with { PitchMode = mode }).Pitch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(17)]
    [InlineData(30)]
    public void Pitch_modes_the_pfd_leaves_blank_read_as_nothing(int mode)
    {
        Assert.Null(A300Fma.Read(Flying() with { PitchMode = mode }).Pitch);
    }

    [Theory]
    [InlineData(3, "Profile climb")]
    [InlineData(24, "Profile climb")]
    [InlineData(5, "Profile descent")]
    [InlineData(25, "Profile descent")]
    public void Pitch_mode_9_repeats_the_last_profile_mode(int last, string words)
    {
        Assert.Equal(words, A300Fma.Read(Flying() with { PitchMode = 9, LastPitchMode = last }).Pitch);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void A_speed_pitch_mode_reads_mach_in_mach(int mode)
    {
        Assert.Equal("Mach", A300Fma.Read(Flying() with { PitchMode = mode, IsMach = true }).Pitch);
    }

    [Theory]
    [InlineData(1, "NAV")]
    [InlineData(2, "Heading select")]
    [InlineData(3, "Heading hold")]
    [InlineData(4, "Runway")]
    [InlineData(5, "Localizer")]
    [InlineData(7, "VOR")]
    [InlineData(8, "VOR capture")]
    public void Roll_modes_read_as_the_pfd_draws_them(int mode, string words)
    {
        Assert.Equal(words, A300Fma.Read(Flying() with { RollMode = mode }).Roll);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(9)]
    public void Roll_modes_the_pfd_leaves_blank_read_as_nothing(int mode)
    {
        Assert.Null(A300Fma.Read(Flying() with { RollMode = mode }).Roll);
    }

    [Theory]
    [InlineData(1, "Land")]
    [InlineData(2, "Flare")]
    [InlineData(3, "Rollout")]
    [InlineData(4, "Go around")]
    public void A_combined_mode_replaces_pitch_and_roll(int mode, string words)
    {
        var fma = A300Fma.Read(Flying() with { CommonMode = mode, PitchArmed = 6, RollArmed = 5 });
        Assert.Equal(words, fma.Common);
        Assert.Null(fma.Pitch);
        Assert.Null(fma.Roll);
        Assert.Empty(fma.Armed);   // the pitch and roll armed modes go with them
    }

    [Theory]
    [InlineData(0, true, true, "Manual thrust")]
    [InlineData(1, true, true, "Speed")]
    [InlineData(2, true, true, "Autothrottle")]
    [InlineData(3, true, true, "Thrust")]
    [InlineData(5, true, true, "Thrust")]
    [InlineData(9, true, true, "Retard")]
    [InlineData(12, true, true, "Thrust")]
    [InlineData(13, true, true, "Thrust lock")]
    [InlineData(0, true, false, "Manual thrust")]
    [InlineData(0, false, true, "Manual thrust")]
    public void Thrust_modes_read_as_the_pfd_draws_them(int mode, bool switch1, bool switch2, string words)
    {
        var fma = A300Fma.Read(Flying() with { AtMode = mode, AtMasterSwitch1 = switch1, AtMasterSwitch2 = switch2 });
        Assert.Equal(words, fma.Thrust);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(11)]
    public void Thrust_modes_the_pfd_leaves_blank_read_as_nothing(int mode)
    {
        Assert.Null(A300Fma.Read(Flying() with { AtMode = mode }).Thrust);
    }

    [Fact]
    public void Mode_0_with_both_master_switches_off_reads_nothing()
    {
        Assert.Null(A300Fma.Read(Flying() with { AtMode = 0, AtMasterSwitch1 = false, AtMasterSwitch2 = false }).Thrust);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void With_the_autothrottle_off_and_a_master_switch_on_the_column_reads_manual_thrust(int mode)
    {
        Assert.Equal("Manual thrust", A300Fma.Read(Flying() with { AtMode = mode, AtOn = false }).Thrust);
    }

    [Theory]
    [InlineData(1, "Speed")]
    [InlineData(3, "Thrust")]
    public void With_the_autothrottle_and_both_switches_off_the_mode_still_reads(int mode, string words)
    {
        var fma = A300Fma.Read(Flying() with { AtMode = mode, AtOn = false, AtMasterSwitch1 = false, AtMasterSwitch2 = false });
        Assert.Equal(words, fma.Thrust);
    }

    [Fact]
    public void Speed_reads_mach_in_mach()
    {
        Assert.Equal("Mach", A300Fma.Read(Flying() with { IsMach = true }).Thrust);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(22)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    public void Speed_reads_profile_speed_in_a_profile_pitch_mode_even_in_mach(int pitch)
    {
        Assert.Equal("Profile speed", A300Fma.Read(Flying() with { PitchMode = pitch, IsMach = true }).Thrust);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(27)]
    public void Speed_stays_speed_in_the_other_profile_words(int pitch)
    {
        Assert.Equal("Speed", A300Fma.Read(Flying() with { PitchMode = pitch }).Thrust);
    }

    [Theory]
    [InlineData(3, "Profile speed")]
    [InlineData(8, "Speed")]
    public void Speed_in_pitch_mode_9_follows_the_last_pitch_mode(int last, string words)
    {
        Assert.Equal(words, A300Fma.Read(Flying() with { PitchMode = 9, LastPitchMode = last }).Thrust);
    }

    [Theory]
    [InlineData(3, "Profile thrust")]
    [InlineData(12, "Profile thrust")]
    [InlineData(5, "Thrust")]
    public void Thrust_reads_profile_thrust_in_profile(int mode, string words)
    {
        Assert.Equal(words, A300Fma.Read(Flying() with { AtMode = mode, IsProfile = true }).Thrust);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_toga_lock_or_thrust_latch_reads_thrust_lock_over_any_mode(bool togaLock, bool latch)
    {
        var fma = A300Fma.Read(Flying() with { AtMode = 0, TogaLock = togaLock, ForceThrustLatch = latch });
        Assert.Equal("Thrust lock", fma.Thrust);
    }

    [Theory]
    [InlineData(2, "Speed")]
    [InlineData(3, "Profile climb")]
    [InlineData(5, "Profile descent")]
    [InlineData(6, "Altitude")]
    [InlineData(7, "Glide slope")]
    [InlineData(13, "Profile descent")]
    [InlineData(21, "Profile descent")]
    [InlineData(22, "Profile altitude")]
    public void Pitch_armed_modes(int armed, string words)
    {
        Assert.Equal(new[] { words }, A300Fma.Read(Flying() with { PitchArmed = armed }).Armed);
    }

    [Theory]
    [InlineData(7, "Glide slope")]
    [InlineData(13, "Profile descent")]
    public void The_second_pitch_armed_slot(int armed, string words)
    {
        Assert.Equal(new[] { words }, A300Fma.Read(Flying() with { PitchArmed2 = armed }).Armed);
    }

    [Theory]
    [InlineData(1, "NAV")]
    [InlineData(2, "Heading select")]
    [InlineData(5, "Localizer")]
    [InlineData(7, "VOR")]
    public void Roll_armed_modes(int armed, string words)
    {
        Assert.Equal(new[] { words }, A300Fma.Read(Flying() with { RollArmed = armed }).Armed);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(24)]
    [InlineData(25)]
    public void Altitude_armed_is_hidden_in_a_profile_climb_or_descent(int pitch)
    {
        Assert.Empty(A300Fma.Read(Flying() with { PitchMode = pitch, PitchArmed = 6 }).Armed);
    }

    [Fact]
    public void Armed_modes_read_in_column_order()
    {
        var fma = A300Fma.Read(Flying() with { PitchArmed = 6, PitchArmed2 = 7, RollArmed = 5 });
        Assert.Equal(new[] { "Altitude", "Glide slope", "Localizer" }, fma.Armed);
    }

    [Fact]
    public void The_combined_armed_slot_reads_altitude_beside_a_combined_mode()
    {
        var fma = A300Fma.Read(Flying() with { CommonMode = 4, CommonArmed = 6 });
        Assert.Equal(new[] { "Altitude" }, fma.Armed);
    }

    [Fact]
    public void Altitude_armed_twice_reads_once()
    {
        Assert.Equal(new[] { "Altitude" }, A300Fma.Read(Flying() with { PitchArmed = 6, CommonArmed = 6 }).Armed);
    }

    [Theory]
    [InlineData(true, false, "CMD 1")]
    [InlineData(false, true, "CMD 2")]
    [InlineData(true, true, "Dual")]
    [InlineData(false, false, null)]
    public void The_autopilot_column(bool ap1, bool ap2, string? words)
    {
        Assert.Equal(words, A300Fma.Read(Flying() with { Ap1 = ap1, Ap2 = ap2 }).Autopilot);
    }

    [Theory]
    [InlineData(true, true, false, true, true)]    // trim 2 alone, bus 2 powered
    [InlineData(true, false, true, true, true)]    // trim 1 with the captain's FD source switch
    [InlineData(true, false, true, false, false)]  // trim 1 without it
    [InlineData(false, true, true, true, false)]   // no IRS aligned
    public void The_guidance_columns_show_only_when_the_pfd_draws_them(bool irsAligned, bool trim2, bool trim1, bool fdSource, bool shown)
    {
        var inputs = Flying() with
        {
            Irs1Aligned = false, Irs2Aligned = false, Irs3Aligned = irsAligned,
            PitchTrim1 = trim1, PitchTrim2 = trim2, CaptainFdSourceSwitch = fdSource,
            PitchArmed = 6,
        };
        var fma = A300Fma.Read(inputs);
        Assert.Equal(shown, fma.IsShown);
        Assert.Equal(shown ? "Vertical speed" : null, fma.Pitch);
        Assert.Equal(shown ? "Heading hold" : null, fma.Roll);
        Assert.Equal(shown ? "Speed" : null, fma.Thrust);
        Assert.Equal(shown ? 1 : 0, fma.Armed.Count);
    }

    [Fact]
    public void Essential_bus_2_off_blanks_the_guidance_columns()
    {
        var fma = A300Fma.Read(Flying() with { EssentialBus2Off = true });
        Assert.False(fma.IsShown);
        Assert.Null(fma.Pitch);
    }

    [Fact]
    public void The_autopilot_column_reads_even_while_the_guidance_columns_are_blank()
    {
        Assert.Equal("CMD 1", A300Fma.Read(Flying() with { PitchTrim1 = false, PitchTrim2 = false }).Autopilot);
    }
}

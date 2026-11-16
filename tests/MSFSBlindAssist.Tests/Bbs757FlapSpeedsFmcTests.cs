using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Shift+1 to Shift+6 from the FMC's own VREF (plan 27, Task 6b; the owner's word, 2026-11-16): while the ADI reader's
/// newest good reading has a VREF 30 above 0, the keys speak from it, BlueBird's own schedule over VREF 30 and no
/// "calculated", since these are the numbers APPROACH REF prints. Otherwise the readout stays as it was: the measured
/// table by the simulator's weight (<see cref="Bbs757FlapSpeedsTests"/>). The live reading behind the numbers: ZFW 150.0
/// on PERF INIT, VREF 30 121 and VREF 25 123 (docs/b757/evidence/displays-adi.md, "The FMC's reference speeds").
/// </summary>
public class Bbs757FlapSpeedsFmcTests
{
    private const string Unavailable = "Flap speeds unavailable.";

    /// <summary>Shift+1 to Shift+6 at VREF 30 121 and VREF 25 123.</summary>
    private static readonly string[] FromFmc =
    {
        "Flaps up maneuver speed 201 knots",
        "Flaps 1 maneuver speed 181 knots",
        "Flaps 5 maneuver speed 161 knots",
        "Flaps 15 maneuver speed 141 knots",
        "Flaps 20 maneuver speed 141 knots",
        "VREF 30 121 knots, VREF 25 123 knots",
    };

    private static readonly HotkeyAction[] Keys =
    {
        HotkeyAction.ReadSpeedGD, HotkeyAction.ReadSpeedS, HotkeyAction.ReadSpeedF, HotkeyAction.ReadSpeedVLS,
        HotkeyAction.ReadSpeedVS, HotkeyAction.ReadSpeedVFE,
    };

    private static string?[] Sentences(double? vref30, double? vref25) =>
        Enumerable.Range(0, 6).Select(i => Bbs757FlapSpeeds.DescribeFromFmc(vref30, vref25, i)).ToArray();

    // ---- the sentences ------------------------------------------------------------------------------------------

    [Fact]
    public void The_six_sentences_from_the_fmcs_numbers()
    {
        Assert.Equal(FromFmc, Sentences(121, 123));
        // The aircraft's own numbers: never "calculated".
        Assert.All(Sentences(121, 123), s => Assert.DoesNotContain("calculated", s));
        // The same shapes as the calculated readout, which keeps the word.
        for (int i = 0; i < 6; i++)
            Assert.Equal(FromFmc[i] + ", calculated", Bbs757FlapSpeeds.Describe(170_000, i));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_sentences_are_the_same_under_a_comma_decimal_culture(string culture)
    {
        Assert.Equal(FromFmc, TestCulture.Run(culture, () => Sentences(121, 123)));
        Assert.Equal(new[] { "Flaps up maneuver speed 232 knots", "VREF 30 152 knots, VREF 25 154 knots" },
            TestCulture.Run(culture, () => new[]
            {
                Bbs757FlapSpeeds.DescribeFromFmc(152.0, 154.0, 0), Bbs757FlapSpeeds.DescribeFromFmc(152.0, 154.0, 5),
            }));
    }

    [Fact]
    public void A_weight_outside_the_measured_table_still_reads_from_the_fmc()
    {
        // The FMC's table runs from 134,000 lb, below the measured points' 140,000: its VREF 30 is spoken as it is.
        Assert.Equal("Flaps 5 maneuver speed 147 knots", Bbs757FlapSpeeds.DescribeFromFmc(107, 109, 2));
        Assert.Equal("VREF 30 107 knots, VREF 25 109 knots", Bbs757FlapSpeeds.DescribeFromFmc(107, 109, 5));
    }

    [Theory]
    [InlineData(null, null)]                       // no reading, or the part missing
    [InlineData(null, 123.0)]
    [InlineData(121.0, null)]
    [InlineData(0.0, 0.0)]                         // PERF INIT not done, or the weight outside the FMC's table
    [InlineData(0.0, 123.0)]
    [InlineData(121.0, 0.0)]                       // a VREF 25 of 0 falls back too
    [InlineData(-1.0, 123.0)]
    [InlineData(double.NaN, 123.0)]
    [InlineData(121.0, double.NaN)]
    [InlineData(double.PositiveInfinity, 123.0)]
    public void Without_a_vref_above_zero_there_is_no_readout_from_the_fmc(double? vref30, double? vref25)
    {
        Assert.All(Sentences(vref30, vref25), Assert.Null);
    }

    [Fact]
    public void A_fraction_is_spoken_as_the_nearest_knot()
    {
        Assert.Equal("VREF 30 121 knots, VREF 25 123 knots", Bbs757FlapSpeeds.DescribeFromFmc(121.4, 122.5, 5));
        Assert.Equal("Flaps up maneuver speed 202 knots", Bbs757FlapSpeeds.DescribeFromFmc(121.5, 123, 0));
    }

    // ---- through the definition's keys --------------------------------------------------------------------------

    [Fact]
    public void The_keys_speak_the_fmcs_numbers_while_the_adi_reader_has_them()
    {
        using var rig = new Bbs757AdiWiringTests.Rig();
        rig.RunAdi();   // the gate: VREF 30 121, VREF 25 123

        for (int i = 0; i < Keys.Length; i++)
        {
            Assert.Equal(FromFmc[i], rig.Def.FlapSpeedsFromFmc(i));
            Assert.True(rig.Press(Keys[i]));
        }

        // Each answered at once, interrupting, as a pilot's own request is; nothing else is said.
        Assert.Equal(FromFmc, rig.Speech.Interrupts);
        Assert.Equal(FromFmc, rig.Speech.All);
    }

    [Fact]
    public void The_numbers_follow_the_fmc()
    {
        using var rig = new Bbs757AdiWiringTests.Rig();
        rig.RunAdi();
        Bbs757AdiReaderTests.SetReferenceSpeeds(rig.AdiBytes!, 140, 142);   // a heavier landing weight on APPROACH REF
        rig.Tick();

        rig.Press(HotkeyAction.ReadSpeedF);
        rig.Press(HotkeyAction.ReadSpeedVFE);

        Assert.Equal(new[] { "Flaps 5 maneuver speed 180 knots", "VREF 30 140 knots, VREF 25 142 knots" }, rig.Speech.All);
    }

    [Fact]
    public void A_dark_adi_still_gives_the_fmcs_numbers()
    {
        // The reference speeds are read whatever the display's power: on the ground with the ADI dark, the FMC has them.
        using var rig = new Bbs757AdiWiringTests.Rig();
        rig.RunAdi(Bbs757AdiReaderTests.Gate(b => b[Bbs757AdiReaderTests.Pfd + Bbs757AdiLayoutTests.Sample().Powered] = 0));
        Assert.Equal(new[] { "ADI display not powered" }, rig.Def.Adi.View());
        Assert.Null(rig.Def.Adi.Latest);

        rig.Press(HotkeyAction.ReadSpeedVFE);

        Assert.Equal(new[] { FromFmc[5] }, rig.Speech.All);
    }

    [Fact]
    public void No_reading_falls_back_to_the_calculated_readout()
    {
        // The ADI reader never started: the calculated path runs as it always did. Here the simulator is not connected,
        // so no weight is read and it says so.
        using var rig = new Bbs757AdiWiringTests.Rig();
        for (int i = 0; i < Keys.Length; i++)
        {
            Assert.Null(rig.Def.FlapSpeedsFromFmc(i));
            Assert.True(rig.Press(Keys[i]));
        }
        Assert.Equal(Enumerable.Repeat(Unavailable, 6), rig.Speech.All);

        // Unavailable (no ADI layout in the file) alike.
        using var failed = new Bbs757AdiWiringTests.Rig();
        failed.FailAdi();
        Assert.Null(failed.Def.FlapSpeedsFromFmc(2));
        failed.Press(HotkeyAction.ReadSpeedF);
        Assert.Equal(new[] { Unavailable }, failed.Speech.Interrupts);
    }

    [Fact]
    public void A_missing_part_falls_back_to_the_calculated_readout()
    {
        using var rig = new Bbs757AdiWiringTests.Rig();
        rig.RunAdi(layout: Bbs757AdiLayoutTests.Sample() with { ReferenceSpeeds = null, ReferenceSpeedsMissing = "reference speeds: no VREF getter" });
        Assert.Equal("Running", rig.Def.Adi.StateName);   // the ADI itself reads: only the part is missing
        Assert.NotNull(rig.Def.Adi.Latest);

        Assert.Null(rig.Def.FlapSpeedsFromFmc(5));
        rig.Press(HotkeyAction.ReadSpeedVFE);

        Assert.Equal(new[] { Unavailable }, rig.Speech.All);
    }

    [Theory]
    [InlineData(0.0, 0.0)]      // before PERF INIT
    [InlineData(0.0, 123.0)]
    [InlineData(121.0, 0.0)]
    public void A_vref_of_zero_falls_back_to_the_calculated_readout(double vref30, double vref25)
    {
        using var rig = new Bbs757AdiWiringTests.Rig();
        var gate = Bbs757AdiReaderTests.Gate();
        Bbs757AdiReaderTests.SetReferenceSpeeds(gate, vref30, vref25);
        rig.RunAdi(gate);

        for (int i = 0; i < Keys.Length; i++) Assert.Null(rig.Def.FlapSpeedsFromFmc(i));
        rig.Press(HotkeyAction.ReadSpeedGD);

        Assert.Equal(new[] { Unavailable }, rig.Speech.All);

        // Once the FMC has computed them, the next press speaks them.
        Bbs757AdiReaderTests.SetReferenceSpeeds(gate, 121, 123);
        rig.Tick();
        rig.Press(HotkeyAction.ReadSpeedGD);
        Assert.Equal(new[] { Unavailable, FromFmc[0] }, rig.Speech.All);
    }

    [Fact]
    public void A_reader_that_stopped_no_longer_gives_the_fmcs_numbers()
    {
        // Numbers from a reading the reader can no longer refresh are never spoken as the aircraft's own.
        using var rig = new Bbs757AdiWiringTests.Rig();
        rig.RunAdi();
        Assert.Equal(FromFmc[2], rig.Def.FlapSpeedsFromFmc(2));

        rig.Def.Adi.Stop();

        Assert.Null(rig.Def.FlapSpeedsFromFmc(2));
        rig.Press(HotkeyAction.ReadSpeedF);
        Assert.Equal(new[] { Unavailable }, rig.Speech.All);
    }

    [Fact]
    public void A_corrupt_reading_is_never_the_source()
    {
        // The ADI turns corrupt (a value out of range) while the FMC's VREF changes: the keys keep the newest good
        // reading's numbers, never the corrupt one's.
        using var rig = new Bbs757AdiWiringTests.Rig();
        rig.RunAdi();
        var bytes = rig.AdiBytes!;
        Bbs757AdiReaderTests.SetReferenceSpeeds(bytes, double.NaN, 500);
        rig.Def.Adi.Tick();
        Assert.Equal("Running", rig.Def.Adi.StateName);

        Assert.Equal(FromFmc[5], rig.Def.FlapSpeedsFromFmc(5));
    }
}

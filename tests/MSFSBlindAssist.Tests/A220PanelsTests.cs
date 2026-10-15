using System.Globalization;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the A220 panel helpers (Plan B): the speedbrake lever's cockpit
/// write and detent read-back ([A220-40]) and the spoken press confirmations (CORE-7). CI runs
/// en-US, so every number that reaches the sim or the screen reader is also checked under a
/// comma-decimal culture.
/// </summary>
public class A220PanelsTests
{
    [Theory]
    [InlineData(0, "0 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(1, "0.2 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(2, "0.4 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(3, "0.6 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(4, "0.8 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(5, "1 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(-3, "0 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(9, "1 0.5 - 32768 * (>K:SPOILERS_SET)")]
    public void SpeedbrakeCalc_IsTheCockpitLeversOwnWrite(int detent, string expected)
        => Assert.Equal(expected, A220Panels.SpeedbrakeCalc(detent));

    [Fact]
    public void SpeedbrakeCalc_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("0.6 0.5 - 32768 * (>K:SPOILERS_SET)", A220Panels.SpeedbrakeCalc(3));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.09, 0)]
    [InlineData(0.13, 1)]
    [InlineData(0.31, 2)]
    [InlineData(0.6, 3)]
    [InlineData(0.79, 4)]
    [InlineData(1.0, 5)]
    [InlineData(-0.02, 0)]   // read-back a hair below RET
    [InlineData(1.03, 5)]    // a hair above MAX
    public void SpeedbrakeDetentForTravel_PicksTheNearestDetent(double travel, double detent)
        => Assert.Equal(detent, A220Panels.SpeedbrakeDetentForTravel(travel));

    [Fact]
    public void SpeedbrakeDetentForTravel_TreatsNaNAsRetracted()
        => Assert.Equal(0, A220Panels.SpeedbrakeDetentForTravel(double.NaN));

    [Fact]
    public void SpeedbrakeDetents_HaveOneLabelEach()
    {
        Assert.Equal(6, A220Panels.SpeedbrakeDetentTravel.Count);
        Assert.Equal(A220Panels.SpeedbrakeDetentTravel.Count, A220Panels.SpeedbrakeDetentLabels.Count);
        Assert.Equal("Retracted", A220Panels.SpeedbrakeDetentLabels[0]);
        Assert.Equal("Max (emergency)", A220Panels.SpeedbrakeDetentLabels[5]);
    }

    [Theory]
    [InlineData("Left Engine Fire (guarded)", "Left Engine Fire pressed")]
    [InlineData("CVR Test", "CVR Test pressed")]
    [InlineData("Mechanic Call", "Mechanic Call pressed")]
    public void PressedText_DropsTheGuardNote(string displayName, string expected)
        => Assert.Equal(expected, A220Panels.PressedText(displayName));

    [Theory]
    [InlineData(1, "Transmit VHF 1")]
    [InlineData(2, "Transmit VHF 2")]
    [InlineData(3, "Transmit VHF 3")]
    [InlineData(0, "No transmit radio selected")]
    [InlineData(5, "Transmit channel 5")]
    [InlineData(-1, "Transmit radio not available")]
    public void TxChannelText_NamesTheSelectedTransmitter(double value, string expected)
        => Assert.Equal(expected, A220Panels.TxChannelText(value));

    [Fact]
    public void PressConfirmKeys_AreTheStatelessPresses()
        => Assert.Equal(
            new[] { "A22X_CVR_ERASE", "A22X_CVR_TEST", "A22X_L_ENG_FIRE", "A22X_MECH_CALL", "A22X_R_ENG_FIRE" },
            A220Panels.PressConfirmKeys.OrderBy(k => k, StringComparer.Ordinal));

    // ---- Audio control panel: VHF receive and volume ([A220-44]) ------------------------

    [Fact]
    public void AcpVolume_HasTheKnobsTwentyNineDetents_InPercent()
    {
        Assert.Equal(28, A220Panels.AcpVolumeSteps);
        Assert.Equal(29, A220Panels.AcpVolumeLabels.Count);
        Assert.Equal("0%", A220Panels.AcpVolumeLabels[0]);
        Assert.Equal("4%", A220Panels.AcpVolumeLabels[1]);
        Assert.Equal("50%", A220Panels.AcpVolumeLabels[14]);
        Assert.Equal("100%", A220Panels.AcpVolumeLabels[28]);
        Assert.Equal(29, A220Panels.AcpVolumeLabels.Distinct().Count());   // every detent says something different
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 14)]
    [InlineData(1.0, 28)]
    [InlineData(0.0357, 1)]     // 1/28 to four decimals still reads as detent 1
    [InlineData(0.99, 28)]
    [InlineData(-0.1, 0)]
    [InlineData(1.2, 28)]
    public void AcpVolumeStepFor_PicksTheNearestDetent(double raw, double step)
        => Assert.Equal(step, A220Panels.AcpVolumeStepFor(raw));

    [Fact]
    public void AcpVolumeStepFor_TreatsNaNAsZero()
        => Assert.Equal(0, A220Panels.AcpVolumeStepFor(double.NaN));

    [Theory]
    [InlineData(1, 0, "0 28 / (>L:A22X ACP VHF1 Volume)")]
    [InlineData(2, 14, "14 28 / (>L:A22X ACP VHF2 Volume)")]
    [InlineData(3, 28, "28 28 / (>L:A22X ACP VHF3 Volume)")]
    [InlineData(2, 40, "28 28 / (>L:A22X ACP VHF2 Volume)")]
    [InlineData(2, -3, "0 28 / (>L:A22X ACP VHF2 Volume)")]
    public void AcpVolumeCalc_IsTheKnobsOwnScale_NeverTheStepIndex(int vhf, int step, string expected)
        => Assert.Equal(expected, A220Panels.AcpVolumeCalc(vhf, step));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void AcpVolume_IsCultureInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("14 28 / (>L:A22X ACP VHF2 Volume)", A220Panels.AcpVolumeCalc(2, 14));
            Assert.Equal("VHF 2 volume did not change — reads 25%.", A220Panels.AcpVolumeReadBack(2, 14, 0.25));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(2, true, 1.0, null)]
    [InlineData(2, false, 0.0, null)]
    [InlineData(2, true, 0.0, "VHF 2 receiver did not change — still off.")]
    [InlineData(1, false, 1.0, "VHF 1 receiver did not change — still on.")]
    public void AcpReceiverReadBack_SpeaksOnlyWhenTheAircraftKeptAnotherState(int vhf, bool wantOn, double now, string? expected)
        => Assert.Equal(expected, A220Panels.AcpReceiverReadBack(vhf, wantOn, now));

    [Theory]
    [InlineData(2, 14, 0.5, null)]
    [InlineData(2, 28, 1.0, null)]
    [InlineData(2, 14, 0.25, "VHF 2 volume did not change — reads 25%.")]
    public void AcpVolumeReadBack_SpeaksOnlyWhenTheAircraftKeptAnotherLevel(int vhf, int step, double now, string? expected)
        => Assert.Equal(expected, A220Panels.AcpVolumeReadBack(vhf, step, now));

    [Fact]
    public void AcpReadBacks_WithNothingReadBack_AreNotConfirmed_NeverAClaimedState()
    {
        Assert.Equal("VHF 2 receiver not confirmed: the A220 did not report it.", A220Panels.AcpReceiverReadBack(2, true, null));
        Assert.Equal("VHF 3 volume not confirmed: the A220 did not report it.", A220Panels.AcpVolumeReadBack(3, 14, null));
        Assert.Equal("VHF 3 volume not confirmed: the A220 did not report it.", A220Panels.AcpVolumeReadBack(3, 14, double.NaN));
    }

    // ---- Standby altimeter (ISI, [A220-43]) ----------------------------------------------

    [Theory]
    [InlineData(1013, 1013.0)]
    [InlineData(1013.2, 1013.2)]
    [InlineData(940, 940.0)]
    [InlineData(1090, 1090.0)]
    [InlineData(29.92, 1013.21)]
    [InlineData(27.0, 914.33)]
    [InlineData(32.0, 1083.64)]
    public void IsiTargetHpa_AcceptsHectopascalsOrInches(double entry, double expectedHpa)
        => Assert.Equal(expectedHpa, A220Panels.IsiTargetHpa(entry)!.Value, 2);

    [Theory]
    [InlineData(2992)]        // inches typed without the decimal point
    [InlineData(33)]
    [InlineData(26.99)]
    [InlineData(939.9)]
    [InlineData(1090.1)]
    [InlineData(0)]           // a blank box reaches the handler as 0
    [InlineData(double.NaN)]  // an unparseable box (UnparseableTextAsNaN)
    public void IsiTargetHpa_RefusesEverythingElse(double entry)
        => Assert.Null(A220Panels.IsiTargetHpa(entry));

    [Theory]
    [InlineData(29.92, true)]
    [InlineData(27.0, true)]
    [InlineData(1013, false)]
    public void IsInHgEntry_TellsInchesFromHectopascals(double entry, bool inHg)
        => Assert.Equal(inHg, A220Panels.IsInHgEntry(entry));

    [Theory]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":true,\"qnh_hpa\":1013.25},\"diag\":{}}", 1013.25)]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":null,\"qnh_hpa\":1020},\"diag\":{}}", 1020.0)]
    public void ParseIsiQnhHpa_ReadsTheIsiStore(string json, double expected)
        => Assert.Equal(expected, A220Panels.ParseIsiQnhHpa(json)!.Value, 3);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"ok\":false}")]
    [InlineData("{\"ok\":true,\"isi\":null}")]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":false,\"qnh_hpa\":1013}}")]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":true,\"qnh_hpa\":null}}")]
    public void ParseIsiQnhHpa_IsNullWhenTheIsiCannotBeRead(string? json)
        => Assert.Null(A220Panels.ParseIsiQnhHpa(json));

    [Theory]
    [InlineData(1013.25, false, "Standby altimeter 1013 hectopascals")]
    [InlineData(1013.21, true, "Standby altimeter 29.92 inches")]
    public void FormatIsiBaro_SpeaksTheUnitThePilotTyped(double hpa, bool inHg, string expected)
        => Assert.Equal(expected, A220Panels.FormatIsiBaro(hpa, inHg));

    [Fact]
    public void FormatIsiBaro_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("Standby altimeter 30.08 inches", A220Panels.FormatIsiBaro(1018.6, inHg: true));
            Assert.Equal("Standby altimeter 1019 hectopascals", A220Panels.FormatIsiBaro(1018.6, inHg: false));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void IsiReadbackText_SaysWhenThereIsNoReading()
    {
        Assert.Equal("Standby altimeter 1013 hectopascals", A220Panels.IsiReadbackText(1013.25));
        Assert.Equal("Standby altimeter reading not available", A220Panels.IsiReadbackText(null));
    }
}

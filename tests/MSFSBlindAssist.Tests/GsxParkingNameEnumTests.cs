// Characterization tests for MSFSBlindAssist.Services.Gsx.GsxParkingNameEnum.
//
// No dedicated probe exists; cases derived by reading LetterToEnum/EnumToLetter (the
// A=12..Z=37 SetGate_Name enum encoding) and Matches (the GSX-confirmed-selection vs
// target ParkingSpot comparison), then confirmed by running the tests. This is
// characterization, not spec verification: if a literal ever disagrees with actual
// output, the test must be corrected to match real output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Tests;

public class GsxParkingNameEnumTests
{
    private static ParkingSpot Spot(string name, int number, string suffix = "")
        => new ParkingSpot { Name = name, Number = number, Suffix = suffix };

    // --- LetterToEnum / EnumToLetter round-trip ----------------------------------

    [Theory]
    [InlineData('A', 12)]
    [InlineData('B', 13)]
    [InlineData('H', 19)]
    [InlineData('Z', 37)]
    public void LetterToEnum_maps_documented_codes(char letter, int expected)
    {
        Assert.Equal(expected, GsxParkingNameEnum.LetterToEnum(letter));
    }

    [Fact]
    public void LetterToEnum_is_case_insensitive()
    {
        Assert.Equal(GsxParkingNameEnum.LetterToEnum('a'), GsxParkingNameEnum.LetterToEnum('A'));
    }

    [Fact]
    public void LetterToEnum_returns_null_for_a_non_letter()
    {
        Assert.Null(GsxParkingNameEnum.LetterToEnum('5'));
    }

    [Theory]
    [InlineData(12, 'A')]
    [InlineData(37, 'Z')]
    [InlineData(19, 'H')]
    public void EnumToLetter_maps_documented_codes(int code, char expected)
    {
        Assert.Equal(expected, GsxParkingNameEnum.EnumToLetter(code));
    }

    [Theory]
    [InlineData(0)]   // NONE
    [InlineData(1)]   // PARKING
    [InlineData(10)]  // GATE
    [InlineData(11)]  // DOCK
    [InlineData(38)]  // out of range
    [InlineData(-1)]  // unselected
    public void EnumToLetter_returns_null_outside_the_GateA_to_GateZ_range(int code)
    {
        Assert.Null(GsxParkingNameEnum.EnumToLetter(code));
    }

    [Fact]
    public void LetterToEnum_and_EnumToLetter_round_trip_the_whole_alphabet()
    {
        for (char c = 'A'; c <= 'Z'; c++)
        {
            int code = GsxParkingNameEnum.LetterToEnum(c)!.Value;
            Assert.Equal(c, GsxParkingNameEnum.EnumToLetter(code));
        }
    }

    // --- Matches ------------------------------------------------------------------

    [Fact]
    public void Matches_requires_the_number_to_agree()
    {
        bool result = GsxParkingNameEnum.Matches(
            setGateName: GsxParkingNameEnum.Gate, setGateNumber: 12, setGateSuffix: 0,
            spot: Spot("", 13));

        Assert.False(result);
    }

    [Fact]
    public void Matches_a_lettered_gate_selection_against_the_matching_spot_concourse()
    {
        int codeA = GsxParkingNameEnum.LetterToEnum('A')!.Value;
        bool result = GsxParkingNameEnum.Matches(
            setGateName: codeA, setGateNumber: 12, setGateSuffix: 0,
            spot: Spot("A", 12));

        Assert.True(result);
    }

    [Fact]
    public void A_lettered_gate_selection_rejects_a_spot_with_no_concourse_letter()
    {
        int codeA = GsxParkingNameEnum.LetterToEnum('A')!.Value;
        bool result = GsxParkingNameEnum.Matches(
            setGateName: codeA, setGateNumber: 12, setGateSuffix: 0,
            spot: Spot("", 12));

        Assert.False(result);
    }

    [Fact]
    public void A_lettered_gate_selection_rejects_a_spot_with_a_different_concourse_letter()
    {
        int codeA = GsxParkingNameEnum.LetterToEnum('A')!.Value;
        bool result = GsxParkingNameEnum.Matches(
            setGateName: codeA, setGateNumber: 12, setGateSuffix: 0,
            spot: Spot("B", 12));

        Assert.False(result);
    }

    [Fact]
    public void NONE_selection_matches_on_number_and_suffix_alone_pure_numeric_parking()
    {
        bool result = GsxParkingNameEnum.Matches(
            setGateName: GsxParkingNameEnum.None, setGateNumber: 51, setGateSuffix: 0,
            spot: Spot("", 51));

        Assert.True(result);
    }

    [Fact]
    public void PARKING_selection_over_confirms_when_the_spot_actually_has_a_concourse_letter()
    {
        // Documented behaviour: NONE/PARKING/GATE/DOCK carry no concourse letter to compare,
        // so number+suffix agreement alone is treated as sufficient -- even if the target
        // spot itself has a concourse letter GSX didn't report.
        bool result = GsxParkingNameEnum.Matches(
            setGateName: GsxParkingNameEnum.Parking, setGateNumber: 12, setGateSuffix: 0,
            spot: Spot("A", 12));

        Assert.True(result);
    }

    [Fact]
    public void Matches_requires_suffix_agreement_when_the_spot_has_a_suffix()
    {
        int suffixA = GsxParkingNameEnum.LetterToEnum('A')!.Value;
        bool result = GsxParkingNameEnum.Matches(
            setGateName: GsxParkingNameEnum.None, setGateNumber: 12, setGateSuffix: suffixA,
            spot: Spot("", 12, suffix: "A"));

        Assert.True(result);
    }

    [Fact]
    public void Matches_rejects_a_different_suffix_letter()
    {
        int suffixB = GsxParkingNameEnum.LetterToEnum('B')!.Value;
        bool result = GsxParkingNameEnum.Matches(
            setGateName: GsxParkingNameEnum.None, setGateNumber: 12, setGateSuffix: suffixB,
            spot: Spot("", 12, suffix: "A"));

        Assert.False(result);
    }

    [Fact]
    public void Matches_rejects_when_only_one_side_has_a_suffix()
    {
        bool result = GsxParkingNameEnum.Matches(
            setGateName: GsxParkingNameEnum.None, setGateNumber: 12, setGateSuffix: 0, // 0 -> no suffix
            spot: Spot("", 12, suffix: "A"));

        Assert.False(result);
    }

    [Fact]
    public void Unselected_suffix_code_minus_one_decodes_to_no_suffix()
    {
        bool result = GsxParkingNameEnum.Matches(
            setGateName: GsxParkingNameEnum.None, setGateNumber: 12, setGateSuffix: -1,
            spot: Spot("", 12)); // spot has no suffix either

        Assert.True(result);
    }
}

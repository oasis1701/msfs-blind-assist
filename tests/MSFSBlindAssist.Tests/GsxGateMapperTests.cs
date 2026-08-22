using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins <see cref="GsxGateMapper.MapGsxTypeToNavdataType"/> — the GSX-SDK-enum -> navdata-enum
/// translation both the <c>.ini</c> path and (via <c>GsxRemoteParkingReader.ResolveNavdataType</c>)
/// the Remote API path go through. The two enums are NOT the same numbering, and the two EXTRA
/// classes are SWAPPED between them, which is exactly the kind of thing that must be pinned.
/// </summary>
public class GsxGateMapperTests
{
    [Theory]
    // GSX SDK enum (input) -> navdata / ParkingSpot.Type (output)
    [InlineData(1, 2)]    // RAMP_GA           -> Ramp GA
    [InlineData(2, 3)]    // RAMP_GA_SMALL     -> Ramp GA Small
    [InlineData(3, 4)]    // RAMP_GA_MEDIUM    -> Ramp GA Medium
    [InlineData(4, 5)]    // RAMP_GA_LARGE     -> Ramp GA Large
    [InlineData(5, 6)]    // RAMP_CARGO        -> Ramp Cargo
    [InlineData(6, 7)]    // RAMP_MIL_CARGO    -> Ramp Military Cargo
    [InlineData(7, 8)]    // RAMP_MIL_COMBAT   -> Ramp Military Combat
    [InlineData(8, 9)]    // GATE_SMALL        -> Gate Small
    [InlineData(9, 10)]   // GATE_MEDIUM       -> Gate Medium
    [InlineData(10, 13)]  // GATE_HEAVY        -> Gate Heavy
    [InlineData(11, 12)]  // DOCK_GA           -> Dock GA
    public void Every_known_gsx_type_maps_to_its_navdata_type(int gsxType, int expectedNavdataType)
    {
        Assert.Equal(expectedNavdataType, GsxGateMapper.MapGsxTypeToNavdataType(gsxType));
    }

    [Fact]
    public void The_two_EXTRA_classes_map_and_are_swapped_between_the_enums()
    {
        // GSX: RAMP_GA_EXTRA = 14, GATE_EXTRA = 15 (published verbatim on every wire parking).
        // Navdata (ParkingSpot.GetParkingType): 14 = "Gate Extra", 15 = "Ramp GA Extra".
        // So the numbers CROSS. Before this pin neither had a case at all, so an A380-class
        // GATE_EXTRA stand resolved to type 0 and rendered as "Spot N - Unknown".
        Assert.Equal(14, GsxGateMapper.MapGsxTypeToNavdataType(15)); // GATE_EXTRA -> Gate Extra
        Assert.Equal(15, GsxGateMapper.MapGsxTypeToNavdataType(14)); // RAMP_GA_EXTRA -> Ramp GA Extra

        Assert.Equal("Gate Extra", new ParkingSpot { Type = GsxGateMapper.MapGsxTypeToNavdataType(15) }.GetParkingType());
        Assert.Equal("Ramp GA Extra", new ParkingSpot { Type = GsxGateMapper.MapGsxTypeToNavdataType(14) }.GetParkingType());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]   // FUEL    -- never a selectable stand; excluded before mapping on the API path
    [InlineData(13)]   // VEHICLE -- same
    [InlineData(99)]
    [InlineData(-1)]
    public void Anything_else_degrades_to_unknown_type_zero(int gsxType)
    {
        Assert.Equal(0, GsxGateMapper.MapGsxTypeToNavdataType(gsxType));
    }
}

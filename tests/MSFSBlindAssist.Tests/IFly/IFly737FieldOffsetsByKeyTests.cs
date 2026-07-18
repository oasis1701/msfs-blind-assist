// Characterization tests for IFly737MAXDefinition.FieldOffsetsByKey / ReadRawField
// (Aircraft/IFly737MAXDefinition.cs): the flattened-key -> (offset, Kind) map used to
// re-seed light state on SDK reconnect and to render a live Spoiler_Lever_Status
// display value. Both are deliberately `internal static` pure helpers (no instance
// state) so they're directly testable without constructing the definition.

namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect.IFly;

public class IFly737FieldOffsetsByKeyTests
{
    private static byte[] Buf() => new byte[IFlySdkOffsets.StructSize];
    private static IFlySdkSnapshot Snap(byte[] b) => new(b);

    [Fact]
    public void FlattenedKey_OfCountGreaterThanOneField_MapsToRightOffset()
    {
        // AP_Indicators_Light_Status: new("AP_Indicators_Light_Status", 296, 'B', 2, 1)
        // — index 0 stays the bare offset, index 1 is offset + 1*stride.
        Assert.Equal((296, 'B'), IFly737MAXDefinition.FieldOffsetsByKey["AP_Indicators_Light_Status_0"]);
        Assert.Equal((297, 'B'), IFly737MAXDefinition.FieldOffsetsByKey["AP_Indicators_Light_Status_1"]);
    }

    [Fact]
    public void FlattenedKey_OfCountOneField_UsesBareName()
    {
        // Spoiler_Lever_Status: new("Spoiler_Lever_Status", 772, 'I', 1, 4) — Count==1
        // so the flattened key is the bare field name, no "_0" suffix.
        Assert.Equal((772, 'I'), IFly737MAXDefinition.FieldOffsetsByKey["Spoiler_Lever_Status"]);
        Assert.False(IFly737MAXDefinition.FieldOffsetsByKey.ContainsKey("Spoiler_Lever_Status_0"));
    }

    [Fact]
    public void ReadRawField_UnknownKey_ReturnsNull()
    {
        Assert.Null(IFly737MAXDefinition.ReadRawField(Snap(Buf()), "Not_A_Real_Field"));
    }

    [Fact]
    public void ReadRawField_IntKind_ReadsSpoilerLeverStatus()
    {
        var b = Buf();
        b[IFlySdkOffsets.Spoiler_Lever_Status] = 149; // FLIGHT DETENT
        Assert.Equal(149.0, IFly737MAXDefinition.ReadRawField(Snap(b), "Spoiler_Lever_Status"));
    }

    [Fact]
    public void ReadRawField_ByteKind_ReadsFlattenedArrayElement()
    {
        var b = Buf();
        b[297] = 3; // AP_Indicators_Light_Status_1 (red, per the disengage table)
        Assert.Equal(3.0, IFly737MAXDefinition.ReadRawField(Snap(b), "AP_Indicators_Light_Status_1"));
        Assert.Equal(0.0, IFly737MAXDefinition.ReadRawField(Snap(b), "AP_Indicators_Light_Status_0"));
    }
}

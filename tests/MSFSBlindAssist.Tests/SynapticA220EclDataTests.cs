using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the generated Synaptic A220 ECL/CAS tables (<see cref="SynapticA220EclData"/>).
/// A220 checklist package JSON references these tables by RAW INDEX, so the order is an
/// ABI: a regeneration that shifts any index would silently remap every sensed checklist
/// item and CAS trigger. These spot checks make such a shift a loud test failure instead —
/// if one fails after regenerating, the upstream enum changed mid-list (not append-only),
/// which would break Synaptic's own liveries too and is worth raising upstream.
/// </summary>
public class SynapticA220EclDataTests
{
    [Fact]
    public void EclVariableTable_HasPinnedSizeAndOrder()
    {
        Assert.Equal(215, SynapticA220EclData.EclVariableNames.Length);
        Assert.Equal("DOME_ON", SynapticA220EclData.EclVariableName(0));
        Assert.Equal("PARK_BRAKE_ON", SynapticA220EclData.EclVariableName(148));
        Assert.Equal("SLAT_FLAP_LEVER_2", SynapticA220EclData.EclVariableName(170));
        Assert.Equal("ALL_DOORS_CLOSED", SynapticA220EclData.EclVariableName(198));
        Assert.Equal("R_ENG_BTL_2_SW_PRESSED", SynapticA220EclData.EclVariableName(214));
        Assert.Null(SynapticA220EclData.EclVariableName(215));
        Assert.Null(SynapticA220EclData.EclVariableName(-1));
    }

    [Fact]
    public void CasMessageTable_HasPinnedSizeOrderAndText()
    {
        Assert.Equal(574, SynapticA220EclData.CasMessages.Length);

        var first = SynapticA220EclData.CasMessage(0)!.Value;
        Assert.Equal("L_ENG_FIRE", first.Name);
        Assert.Equal(A220CasLevel.Warning, first.Level);
        Assert.Equal("L ENG FIRE", first.Text);

        var apuFire = SynapticA220EclData.CasMessage(11)!.Value;
        Assert.Equal("APU_FIRE", apuFire.Name);
        Assert.Equal(A220CasLevel.Warning, apuFire.Level);

        var gear = SynapticA220EclData.CasMessage(31)!.Value;
        Assert.Equal("GEAR", gear.Name);

        var last = SynapticA220EclData.CasMessage(573)!.Value;
        Assert.Equal("R_SIDE_WDW_HEAT_FAIL", last.Name);

        Assert.Null(SynapticA220EclData.CasMessage(574));
    }

    [Fact]
    public void EveryCasMessage_HasNonEmptyDisplayText()
    {
        Assert.All(SynapticA220EclData.CasMessages, m => Assert.False(string.IsNullOrWhiteSpace(m.Text)));
    }
}

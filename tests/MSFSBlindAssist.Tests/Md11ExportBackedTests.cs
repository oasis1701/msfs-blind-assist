using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The pure decision behind "a row on a read-only export is a status field, never a settable
/// control" (see <see cref="Md11ExportBacked"/>). The real map is checked in Md11DefinitionStateTests;
/// these pin the rule's edges on fixtures.
/// </summary>
public class Md11ExportBackedTests
{
    private static readonly IReadOnlySet<string> Exports = new HashSet<string>(
        new[] { "MD11_AP_HDG_TRK", "MD11_CAP_MINIMUMS" }, StringComparer.OrdinalIgnoreCase);

    private static Md11Control Control(string kind, string nodeId, string stateVar) => new()
    {
        NodeId = nodeId, Kind = kind, StateVar = stateVar,
    };

    [Theory]
    [InlineData(Md11Kinds.KnobPushPull)]
    [InlineData(Md11Kinds.Knob)]
    [InlineData(Md11Kinds.KnobPush)]
    [InlineData(Md11Kinds.Switch)]
    public void APositionControlReadingAnExport_IsReadOnly(string kind)
        => Assert.True(Md11ExportBacked.IsReadOnly(Control(kind, "MD11_CGS_HDG_KB", "MD11_AP_HDG_TRK"), Exports));

    [Fact]
    public void AControlReadingItsOwnVar_IsNotReadOnly()
        => Assert.False(Md11ExportBacked.IsReadOnly(Control(Md11Kinds.Knob, "MD11_CGS_HDG_BASE_KB", "MD11_CGS_HDG_BASE_KB"), Exports));

    [Fact]
    public void AButtonReadingAnExport_KeepsItsPress()
        => Assert.False(Md11ExportBacked.IsReadOnly(Control(Md11Kinds.Button, "MD11_SOME_BT", "MD11_AP_HDG_TRK"), Exports));

    [Fact]
    public void AControlWithNoStateVar_IsNotReadOnly()
        => Assert.False(Md11ExportBacked.IsReadOnly(Control(Md11Kinds.Knob, "MD11_X_KB", ""), Exports));

    [Fact]
    public void TheMatchIgnoresCase_LikeEveryOtherMd11VarLookup()
        => Assert.True(Md11ExportBacked.IsReadOnly(Control(Md11Kinds.KnobPush, "MD11_LECP_MINIMUMS_CAP", "md11_cap_minimums"), Exports));
}

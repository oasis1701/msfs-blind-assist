using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The SimConnect id enums are hand-numbered and a collision registers two definitions under
/// one id — the failure that once clobbered def 308 (see RequestSingleValue's comment). Pins
/// the camera slot and that no number repeats in either enum.
/// </summary>
public class SimConnectIdTests
{
    [Fact]
    public void TheCameraViewDefinitionAndRequest_ShareSlot341()
    {
        Assert.Equal(341, (int)SimConnectManager.DATA_DEFINITIONS.DEF_CAMERA_VIEW);
        Assert.Equal(341, (int)SimConnectManager.DATA_REQUESTS.REQUEST_CAMERA_VIEW);
    }

    [Fact]
    public void NoTwoDataDefinitionIds_ShareANumber()
    {
        var values = Enum.GetValues<SimConnectManager.DATA_DEFINITIONS>().Select(v => (int)v).ToArray();
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void NoTwoDataRequestIds_ShareANumber()
    {
        var values = Enum.GetValues<SimConnectManager.DATA_REQUESTS>().Select(v => (int)v).ToArray();
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}

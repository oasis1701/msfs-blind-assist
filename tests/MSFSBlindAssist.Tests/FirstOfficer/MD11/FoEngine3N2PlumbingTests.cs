using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// Engine 3's N2 rides the same one-shot FO request as engines 1 and 2, on the next free id pair
/// (385), and reaches only evaluators that opt in through IFoEngine3N2Sink.
/// </summary>
public class FoEngine3N2PlumbingTests
{
    [Fact]
    public void RequestAndDefinitionIds_Are385()
    {
        Assert.Equal(385, (int)SimConnectManager.DATA_REQUESTS.REQUEST_FO_ENG3_N2);
        Assert.Equal(385, (int)SimConnectManager.DATA_DEFINITIONS.DEF_FO_ENG3_N2);
    }

    [Fact]
    public void SinkInterface_HasOneSetter()
    {
        var m = typeof(IFoEngine3N2Sink).GetMethods();
        Assert.Single(m);
        Assert.Equal("SetEngine3N2", m[0].Name);
    }
}

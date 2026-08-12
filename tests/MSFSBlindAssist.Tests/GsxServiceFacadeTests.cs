using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Guards the facade contract: the public surface AccessGSXForm and MainForm rely
/// on must survive the transport swap. Reflection-based because GsxService needs a
/// window handle and cannot be constructed in a unit test.
/// </summary>
public class GsxServiceFacadeTests
{
    private static readonly Type T = typeof(MSFSBlindAssist.Services.GsxService);

    [Theory]
    [InlineData("IsConnected")]
    [InlineData("CouatlStarted")]
    [InlineData("StatusText")]
    [InlineData("MenuTitle")]
    [InlineData("LastTooltip")]
    [InlineData("IsMenuActive")]
    [InlineData("AnnounceWhenFormHidden")]
    [InlineData("SetGateName")]
    [InlineData("SetGateNumber")]
    [InlineData("SetGateSuffix")]
    [InlineData("Menu")]
    [InlineData("Services")]
    [InlineData("Settings")]
    [InlineData("Billing")]
    [InlineData("Receipt")]
    [InlineData("RemoteApiAvailable")]
    [InlineData("UnavailableReason")]
    public void Public_property_exists(string name) => Assert.NotNull(T.GetProperty(name));

    [Theory]
    [InlineData("Start")]
    [InlineData("Stop")]
    [InlineData("Dispose")]
    [InlineData("OpenMenu")]
    [InlineData("HideMenu")]
    [InlineData("Choose")]
    [InlineData("RefreshTooltip")]
    [InlineData("OpenSettings")]
    [InlineData("SetSettingNumber")]
    [InlineData("SetSettingText")]
    [InlineData("PulseSettingAction")]
    [InlineData("PickMenuEntry")]
    public void Public_method_exists(string name) => Assert.NotNull(T.GetMethod(name));

    [Theory]
    [InlineData("StateChanged")]
    [InlineData("MenuChanged")]
    [InlineData("MenuHidden")]
    [InlineData("MenuTimedOut")]
    [InlineData("TooltipChanged")]
    [InlineData("AnnouncementReady")]
    [InlineData("ActiveServicesChanged")]
    [InlineData("SettingsChanged")]
    public void Public_event_exists(string name) => Assert.NotNull(T.GetEvent(name));

    [Fact]
    public void ProcessWindowMessage_is_gone()
        => Assert.Null(T.GetMethod("ProcessWindowMessage"));
}

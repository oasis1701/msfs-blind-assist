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
    // GSX's own system commands (command.run). AccessGSXForm binds A/B/D/E to
    // these; they were dropped, not migrated, in the transport swap, and
    // "Restart GSX" is the standard recovery when Couatl wedges.
    [InlineData("RunCommand")]
    public void Public_method_exists(string name) => Assert.NotNull(T.GetMethod(name));

    [Fact]
    public void MenuTimedOut_is_gone()
        // The Remote API publishes no menu-timeout frame, so the event carried
        // over from the old transport could never be raised (a live CS0067) and
        // its whole UI path was dead with it. Do not re-add it without a real
        // frame to raise it from; WaitForNextMenuAsync's own await-timeout is
        // the only timeout signal there is.
        => Assert.Null(T.GetEvent("MenuTimedOut"));

    [Theory]
    [InlineData("StateChanged")]
    [InlineData("MenuChanged")]
    [InlineData("MenuHidden")]
    [InlineData("TooltipChanged")]
    [InlineData("AnnouncementReady")]
    [InlineData("ActiveServicesChanged")]
    [InlineData("SettingsChanged")]
    public void Public_event_exists(string name) => Assert.NotNull(T.GetEvent(name));

    [Fact]
    public void ProcessWindowMessage_is_gone()
        => Assert.Null(T.GetMethod("ProcessWindowMessage"));
}

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
    [InlineData("Capabilities")]
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
    // Spec 2 additions: GetHandlerDataAirport()/Capabilities feed
    // GateDataSource's Remote API gate-list path; SendCommandAsync is the
    // production wiring behind GsxCommandSender for GsxRemoteGateSelector
    // (both via MainForm.Dialogs.cs).
    [InlineData("GetHandlerDataAirport")]
    [InlineData("SendCommandAsync")]
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

    [Fact]
    public void PumpSimConnectMessage_is_gone()
        // Deleted with the menu-walking GsxGateSelector -- gate.select's own synchronous
        // result payload replaced the SetGate_* confirmation polling that was this
        // pump's (and the retained SimConnect client's) only reason to exist. GsxService
        // no longer touches SimConnect at all.
        => Assert.Null(T.GetMethod("PumpSimConnectMessage"));

    [Fact]
    public void The_no_remote_api_reason_names_GSX_4_0_8()
    {
        // This string IS the entire mitigation for a GSX build that predates the Remote
        // API — there is no fallback transport — so it has to tell the pilot what to
        // install. It used to say only "a recent GSX (Couatl) build", because the
        // minimum version genuinely wasn't known; Virtuali's own guide/release notes
        // settle it. 4.0.8, not 4.0.1: the Remote API shipped in 4.0.1 but gate.select
        // did not, so 4.0.1 would leave gate selection silently doing nothing.
        string reason = MSFSBlindAssist.Services.GsxService.ReasonNoRemoteApi;

        Assert.Contains("4.0.8", reason);
        Assert.DoesNotContain("4.0.1", reason);
        // A queued announcement of "" is silently dropped, so this can never be empty.
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void SetGate_confirmation_properties_are_gone()
        // Retired alongside the SimConnect client that populated them (see
        // PumpSimConnectMessage_is_gone) -- gate.select's payload.gate is the
        // confirmation now, read synchronously off the command result instead of
        // polled off a lagging L:var.
    {
        Assert.Null(T.GetProperty("SetGateName"));
        Assert.Null(T.GetProperty("SetGateNumber"));
        Assert.Null(T.GetProperty("SetGateSuffix"));
    }
}

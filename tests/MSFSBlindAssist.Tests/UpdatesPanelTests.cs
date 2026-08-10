using MSFSBlindAssist.Forms.Settings;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// LoadFrom/ApplyTo round-trip for the Updates tab, following the WeatherPanelTests
/// pattern: the panel is a plain UserControl whose controls are readable and writable
/// without a message pump.
///
/// Validate() is NOT exercised here — on a Release-to-Preview switch it shows a modal
/// confirmation, which cannot run unattended. The round-trip below is what these tests
/// pin; the confirmation is covered by the manual test plan.
/// </summary>
public class UpdatesPanelTests
{
    [Fact]
    public void RoundTrip_PreservesPreviewChannelAndAutoCheckOff()
    {
        var source = new UserSettings
        {
            UpdateChannel = UpdateChannel.Preview,
            CheckForUpdatesOnStartup = false
        };

        using var panel = new UpdatesPanel();
        panel.LoadFrom(source);
        var target = new UserSettings();
        panel.ApplyTo(target);

        Assert.Equal(UpdateChannel.Preview, target.UpdateChannel);
        Assert.False(target.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void RoundTrip_PreservesReleaseChannelAndAutoCheckOn()
    {
        var source = new UserSettings
        {
            UpdateChannel = UpdateChannel.Release,
            CheckForUpdatesOnStartup = true
        };

        using var panel = new UpdatesPanel();
        panel.LoadFrom(source);
        // Pre-set the target to the opposite of both values, so a panel that silently
        // wrote nothing would fail rather than pass on the defaults.
        var target = new UserSettings
        {
            UpdateChannel = UpdateChannel.Preview,
            CheckForUpdatesOnStartup = false
        };
        panel.ApplyTo(target);

        Assert.Equal(UpdateChannel.Release, target.UpdateChannel);
        Assert.True(target.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void Defaults_AreReleaseChannelAndAutoCheckOn()
    {
        var settings = new UserSettings();

        Assert.Equal(UpdateChannel.Release, settings.UpdateChannel);
        Assert.True(settings.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void StayingOnRelease_NeedsNoConfirmation()
    {
        // Validate must not prompt when the channel did not change to Preview, or every
        // OK press in the Settings dialog would raise a modal.
        var source = new UserSettings { UpdateChannel = UpdateChannel.Release };

        using var panel = new UpdatesPanel();
        panel.LoadFrom(source);

        Assert.True(panel.Validate(out var error, out _));
        Assert.Equal("", error);
    }

    [Fact]
    public void StayingOnPreview_NeedsNoConfirmation()
    {
        // Already on Preview and leaving it there is not a switch — no prompt.
        var source = new UserSettings { UpdateChannel = UpdateChannel.Preview };

        using var panel = new UpdatesPanel();
        panel.LoadFrom(source);

        Assert.True(panel.Validate(out var error, out _));
        Assert.Equal("", error);
    }
}

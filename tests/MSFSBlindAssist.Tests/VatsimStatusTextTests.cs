// The status field is the only way a blind user can confirm the vPilot chain works
// without going flying, so every state has to say something actionable — "not
// connected" must also say what to do about it.

using MSFSBlindAssist.Services.VPilot;

namespace MSFSBlindAssist.Tests;

public class VatsimStatusTextTests
{
    private static VatsimStatus Ready() => new(
        Enabled: true, PluginsFolder: @"C:\vPilot\Plugins",
        PluginInstalled: true, PluginCurrent: true,
        ClientConnected: true, Muted: false);

    [Fact]
    public void Everything_working_names_the_folder_and_confirms_the_connection()
    {
        string text = VatsimStatusText.Compose(Ready());
        Assert.Contains(@"C:\vPilot\Plugins", text);
        Assert.Contains("installed and up to date", text);
        Assert.Contains("vPilot is connected", text);
    }

    [Fact]
    public void Feature_off_says_so_first()
    {
        string text = VatsimStatusText.Compose(Ready() with { Enabled = false });
        Assert.StartsWith("VATSIM announcements are turned off.", text);
    }

    [Fact]
    public void No_vPilot_folder_tells_the_user_to_browse()
    {
        string text = VatsimStatusText.Compose(Ready() with { PluginsFolder = null, PluginInstalled = false, PluginCurrent = false, ClientConnected = false });
        Assert.Contains("vPilot was not found", text);
        Assert.Contains("Browse", text);
    }

    [Fact]
    public void Plugin_not_installed_yet_says_OK_will_install_it()
    {
        string text = VatsimStatusText.Compose(Ready() with { PluginInstalled = false, PluginCurrent = false, ClientConnected = false });
        Assert.Contains("not installed", text);
        Assert.Contains("OK", text);
    }

    [Fact]
    public void An_out_of_date_plugin_is_reported_as_such_not_as_missing()
    {
        string text = VatsimStatusText.Compose(Ready() with { PluginCurrent = false, ClientConnected = false });
        Assert.Contains("older plugin", text);
        Assert.DoesNotContain("not installed", text);
    }

    [Fact]
    public void Not_connected_says_what_to_do_about_it()
    {
        string text = VatsimStatusText.Compose(Ready() with { ClientConnected = false });
        Assert.Contains("not connected", text);
        Assert.Contains("restart vPilot", text);
    }

    [Fact]
    public void A_muted_session_is_reported_with_the_key_that_unmutes_it()
    {
        string text = VatsimStatusText.Compose(Ready() with { Muted = true });
        Assert.Contains("muted", text);
        Assert.Contains("Alt+V", text);
    }

    [Fact]
    public void An_unmuted_session_does_not_mention_muting()
        => Assert.DoesNotContain("muted", VatsimStatusText.Compose(Ready()));

    [Fact]
    public void Every_state_produces_at_least_one_line()
    {
        foreach (var status in new[]
        {
            Ready(),
            Ready() with { Enabled = false },
            Ready() with { PluginsFolder = null },
            Ready() with { PluginInstalled = false },
            Ready() with { ClientConnected = false },
        })
        {
            Assert.False(string.IsNullOrWhiteSpace(VatsimStatusText.Compose(status)));
        }
    }

    [Fact]
    public void A_disabled_feature_does_not_send_the_pilot_off_to_start_vPilot()
    {
        // The folder and plugin lines still matter here — the panel refreshes this text
        // as the master switch is ticked — but the connection advice would point at the
        // wrong cause.
        string text = VatsimStatusText.Compose(Ready() with { Enabled = false, ClientConnected = false });
        Assert.Contains("The plugin is installed and up to date.", text);
        Assert.DoesNotContain("not connected", text);
        Assert.DoesNotContain("restart vPilot", text);
    }

    [Fact]
    public void A_disabled_feature_with_no_plugin_is_told_to_flip_the_switch_before_OK_does_anything()
    {
        // ApplySettings returns before Install() ever runs while the master switch is
        // off, so "Press OK to install it" alone is false for this state — the default
        // state of every new user's very first read of this box.
        string text = VatsimStatusText.Compose(Ready() with
        {
            Enabled = false, PluginInstalled = false, PluginCurrent = false, ClientConnected = false
        });
        Assert.Contains("not installed", text);
        Assert.Contains("Turn on the switch above, then press OK to install it.", text);
    }

    [Fact]
    public void A_disabled_feature_with_an_older_plugin_is_told_to_flip_the_switch_before_OK_does_anything()
    {
        string text = VatsimStatusText.Compose(Ready() with
        {
            Enabled = false, PluginCurrent = false, ClientConnected = false
        });
        Assert.Contains("older plugin", text);
        Assert.Contains("Turn on the switch above, then press OK to update it.", text);
    }
}

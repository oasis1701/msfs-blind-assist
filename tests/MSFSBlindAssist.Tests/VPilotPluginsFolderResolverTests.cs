// Where the vPilot Plugins folder is. The standalone app read one registry key and gave
// up silently when it was missing; this resolver tries three sources in order and its
// precedence is the whole reason a user with a relocated vPilot can be helped without a
// support round-trip. The filesystem probe is injected so none of this touches disk.

using MSFSBlindAssist.Services.VPilot;

namespace MSFSBlindAssist.Tests;

public class VPilotPluginsFolderResolverTests
{
    private const string LocalAppData = @"C:\Users\pilot\AppData\Local";
    private const string DefaultInstall = @"C:\Users\pilot\AppData\Local\vPilot";
    private const string DefaultPlugins = @"C:\Users\pilot\AppData\Local\vPilot\Plugins";

    private static Func<string, bool> Exists(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return p => set.Contains(p);
    }

    [Fact]
    public void Override_wins_over_registry_and_default()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"D:\vPilot", @"C:\Program Files\vPilot", LocalAppData,
            Exists(@"D:\vPilot", @"D:\vPilot\Plugins",
                   @"C:\Program Files\vPilot", @"C:\Program Files\vPilot\Plugins",
                   DefaultInstall, DefaultPlugins));
        Assert.Equal(@"D:\vPilot\Plugins", result);
    }

    [Fact]
    public void Registry_wins_over_the_default_when_no_override_is_set()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            "", @"C:\Program Files\vPilot", LocalAppData,
            Exists(@"C:\Program Files\vPilot", @"C:\Program Files\vPilot\Plugins",
                   DefaultInstall, DefaultPlugins));
        Assert.Equal(@"C:\Program Files\vPilot\Plugins", result);
    }

    [Fact]
    public void Default_location_is_used_when_nothing_else_is_configured()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            null, null, LocalAppData, Exists(DefaultInstall, DefaultPlugins));
        Assert.Equal(DefaultPlugins, result);
    }

    [Fact]
    public void A_candidate_that_does_not_exist_is_skipped_rather_than_returned()
    {
        // A stale override left over from an uninstall must not dead-end discovery.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"D:\gone", null, LocalAppData, Exists(DefaultInstall, DefaultPlugins));
        Assert.Equal(DefaultPlugins, result);
    }

    [Fact]
    public void Browsing_straight_to_the_Plugins_folder_is_accepted_as_is()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"D:\vPilot\Plugins", null, LocalAppData, Exists(@"D:\vPilot\Plugins"));
        Assert.Equal(@"D:\vPilot\Plugins", result);
    }

    [Fact]
    public void A_trailing_separator_does_not_defeat_the_Plugins_folder_check()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"D:\vPilot\Plugins\", null, LocalAppData, Exists(@"D:\vPilot\Plugins"));
        Assert.Equal(@"D:\vPilot\Plugins", result);
    }

    [Fact]
    public void An_install_folder_with_no_Plugins_subfolder_yet_still_resolves()
    {
        // Fresh vPilot install that has never loaded a plugin. Install() creates it.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            null, @"C:\Program Files\vPilot", LocalAppData, Exists(@"C:\Program Files\vPilot"));
        Assert.Equal(@"C:\Program Files\vPilot\Plugins", result);
    }

    [Fact]
    public void Nothing_found_anywhere_returns_null()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            null, null, LocalAppData, Exists());
        Assert.Null(result);
    }

    [Fact]
    public void Whitespace_candidates_are_ignored()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            "   ", "  ", LocalAppData, Exists(DefaultInstall, DefaultPlugins));
        Assert.Equal(DefaultPlugins, result);
    }
}

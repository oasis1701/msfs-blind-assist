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
        // This is now the case that needs vPilot.exe itself as evidence — the
        // now-removed third branch used to accept ANY existing directory here, which is
        // exactly the bug the vPilot.exe requirement (see the two tests below) fixes;
        // this scenario's own docstring already describes a folder that genuinely is a
        // vPilot install, so giving it an executable to find is just making the fixture
        // match what it claims to be, not changing what the test proves.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            null, @"C:\Program Files\vPilot", LocalAppData,
            Exists(@"C:\Program Files\vPilot"),
            Exists(@"C:\Program Files\vPilot\vPilot.exe"));
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

    [Fact]
    public void A_plain_existing_directory_with_no_vPilot_evidence_is_rejected()
    {
        // The old third branch accepted ANY existing directory as a vPilot install —
        // pick "Documents" by mistake in Browse and Install() would create
        // Documents\Plugins, copy the DLL into it, and cheerfully report success. A
        // directory with no Plugins subfolder and no vPilot.exe is not evidence of
        // anything, so with no other candidate configured this must resolve to nothing.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"C:\Users\pilot\Documents", null, LocalAppData,
            Exists(@"C:\Users\pilot\Documents"),
            Exists());
        Assert.Null(result);
    }

    [Fact]
    public void A_rejected_candidate_falls_through_to_the_next_one_instead_of_dead_ending()
    {
        // A mistaken override must not dead-end discovery any more than a genuinely
        // absent candidate already doesn't (see the "does not exist" test above) —
        // rejecting Documents must still let the default install location resolve.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"C:\Users\pilot\Documents", null, LocalAppData,
            Exists(@"C:\Users\pilot\Documents", DefaultInstall, DefaultPlugins),
            Exists());
        Assert.Equal(DefaultPlugins, result);
    }

    [Fact]
    public void A_folder_with_no_Plugins_subfolder_but_a_vPilot_exe_inside_it_is_accepted()
    {
        // The positive case for the tightened third branch, isolated from the
        // registry/override precedence the earlier test above also exercises.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"D:\Portable\vPilot", null, LocalAppData,
            Exists(@"D:\Portable\vPilot"),
            Exists(@"D:\Portable\vPilot\vPilot.exe"));
        Assert.Equal(@"D:\Portable\vPilot\Plugins", result);
    }
}

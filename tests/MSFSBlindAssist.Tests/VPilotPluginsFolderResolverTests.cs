// Where the vPilot Plugins folder is. The standalone app read one registry key and gave
// up silently when it was missing; this resolver tries that key and then the default
// install location, and requires real evidence of vPilot before treating a candidate as
// one. There is deliberately no user-supplied override — see ResolvePluginsFolder's doc
// comment. The filesystem probe is injected so none of this touches disk.

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
    public void Registry_wins_over_the_default_install_location()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"C:\Program Files\vPilot", LocalAppData,
            Exists(@"C:\Program Files\vPilot", @"C:\Program Files\vPilot\Plugins",
                   DefaultInstall, DefaultPlugins));
        Assert.Equal(@"C:\Program Files\vPilot\Plugins", result);
    }

    [Fact]
    public void Default_location_is_used_when_the_registry_key_is_absent()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            null, LocalAppData, Exists(DefaultInstall, DefaultPlugins));
        Assert.Equal(DefaultPlugins, result);
    }

    [Fact]
    public void A_candidate_that_does_not_exist_is_skipped_rather_than_returned()
    {
        // A stale registry value left over from an uninstall must not dead-end discovery.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"D:\gone", LocalAppData, Exists(DefaultInstall, DefaultPlugins));
        Assert.Equal(DefaultPlugins, result);
    }

    [Fact]
    public void A_trailing_separator_does_not_defeat_the_Plugins_folder_check()
    {
        // vPilot writes Install_Dir without one, but a hand-edited key might not.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"D:\vPilot\", LocalAppData, Exists(@"D:\vPilot\Plugins"));
        Assert.Equal(@"D:\vPilot\Plugins", result);
    }

    [Fact]
    public void An_install_folder_with_no_Plugins_subfolder_yet_still_resolves()
    {
        // Fresh vPilot install that has never loaded a plugin. Install() creates it.
        // This is the case that needs vPilot.exe itself as evidence — the now-removed
        // form of this branch used to accept ANY existing directory here.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"C:\Program Files\vPilot", LocalAppData,
            Exists(@"C:\Program Files\vPilot"),
            Exists(@"C:\Program Files\vPilot\vPilot.exe"));
        Assert.Equal(@"C:\Program Files\vPilot\Plugins", result);
    }

    [Fact]
    public void Nothing_found_anywhere_returns_null()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(null, LocalAppData, Exists());
        Assert.Null(result);
    }

    [Fact]
    public void Whitespace_candidates_are_ignored()
    {
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            "  ", LocalAppData, Exists(DefaultInstall, DefaultPlugins));
        Assert.Equal(DefaultPlugins, result);
    }

    [Fact]
    public void A_plain_existing_directory_with_no_vPilot_evidence_is_rejected()
    {
        // A registry value pointing at a directory that is not a vPilot install — a
        // leftover from an uninstall, say. With no Plugins subfolder and no vPilot.exe
        // it is not evidence of anything, so with no other candidate this resolves to
        // nothing rather than letting Install() create Plugins inside it.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"C:\Users\pilot\Documents", LocalAppData,
            Exists(@"C:\Users\pilot\Documents"),
            Exists());
        Assert.Null(result);
    }

    [Fact]
    public void A_rejected_candidate_falls_through_to_the_next_one_instead_of_dead_ending()
    {
        // A stale registry value must not dead-end discovery any more than a genuinely
        // absent one already doesn't — rejecting it must still let the default install
        // location resolve.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"C:\Users\pilot\Documents", LocalAppData,
            Exists(@"C:\Users\pilot\Documents", DefaultInstall, DefaultPlugins),
            Exists());
        Assert.Equal(DefaultPlugins, result);
    }

    [Fact]
    public void A_folder_with_no_Plugins_subfolder_but_a_vPilot_exe_inside_it_is_accepted()
    {
        // The positive case for the vPilot.exe branch, isolated from the registry/default
        // precedence the earlier test above also exercises.
        var result = VPilotPluginInstaller.ResolvePluginsFolder(
            @"D:\Elsewhere\vPilot", LocalAppData,
            Exists(@"D:\Elsewhere\vPilot"),
            Exists(@"D:\Elsewhere\vPilot\vPilot.exe"));
        Assert.Equal(@"D:\Elsewhere\vPilot\Plugins", result);
    }
}

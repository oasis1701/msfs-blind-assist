using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// Characterization tests for the pure parts of Services/Volanta/VolantaLauncher: how Volanta's exe is read from
// the uninstall entry its installer registers (measured 1.19.2: a DisplayIcon of "<exe>,0", no InstallLocation).
public class VolantaLauncherTests
{
    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Volanta\Volanta.exe,0", @"C:\Users\x\AppData\Local\Programs\Volanta\Volanta.exe")]
    [InlineData(@"""C:\Program Files\Volanta\Volanta.exe"",-1", @"C:\Program Files\Volanta\Volanta.exe")]
    [InlineData(@"D:\Apps\Volanta\volanta.exe", @"D:\Apps\Volanta\volanta.exe")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Volanta\Uninstall Volanta.exe,0", null)]   // another program's icon
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Volanta\Volanta.ico", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_exe_is_read_from_an_uninstall_entry_s_display_icon(string? displayIcon, string? expected)
        => Assert.Equal(expected, VolantaLauncher.ExeFromDisplayIcon(displayIcon));

    [Fact]
    public void The_exe_is_inside_an_install_location_when_there_is_one()
    {
        Assert.Equal(@"C:\Program Files\Volanta\Volanta.exe", VolantaLauncher.ExeFromInstallLocation(@"C:\Program Files\Volanta\"));
        Assert.Equal(@"C:\Program Files\Volanta\Volanta.exe", VolantaLauncher.ExeFromInstallLocation(@"""C:\Program Files\Volanta"""));
        Assert.Null(VolantaLauncher.ExeFromInstallLocation(""));
        Assert.Null(VolantaLauncher.ExeFromInstallLocation(null));
    }

    [Fact]
    public void The_not_found_message_tells_the_pilot_the_way_that_always_works()
    {
        // A running Volanta's exe is read from its process, so starting it by hand and pressing again works.
        Assert.Contains("start Volanta yourself", VolantaLauncher.NotFoundMessage);
        Assert.Contains("Connection tab", VolantaLauncher.NotFoundMessage);
    }
}

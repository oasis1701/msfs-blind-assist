using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 tablet window's status when the tablet cannot be read. The shared module session words its reasons for the PMDG
/// 737 (<see cref="FmaReasons"/>); the 757 window says the 757's own (<see cref="Bbs757DisplayReasons"/>). Found 2026-11-07:
/// with no 757 layout file installed, the window said "PMDG file not recognised. Is the 757 loaded?" with the 757 loaded.
/// </summary>
public class Bbs757EfbTabletStatusTests
{
    [Theory]
    [InlineData(FmaReasons.FileNotRecognised, Bbs757DisplayReasons.FileNotRecognised)]
    [InlineData(FmaReasons.FilesNotFound, Bbs757DisplayReasons.FilesNotFound)]
    [InlineData(FmaReasons.NoAccess, Bbs757DisplayReasons.NoAccess)]
    [InlineData(FmaReasons.MemoryNotFound, Bbs757DisplayReasons.MemoryNotFound)]
    public void The_sessions_reason_becomes_the_757s_own(string session, string expected)
    {
        Assert.Equal(expected, Bbs757DisplayReasons.FromSession(session));
    }

    [Fact]
    public void No_757_reason_names_PMDG()
    {
        foreach (string session in new[] { FmaReasons.FileNotRecognised, FmaReasons.FilesNotFound, FmaReasons.NoAccess, FmaReasons.MemoryNotFound })
            Assert.DoesNotContain("PMDG", Bbs757DisplayReasons.FromSession(session));
    }

    [Fact]
    public void A_build_with_no_layout_says_so_and_not_is_the_757_loaded()
    {
        var read = Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.FileNotRecognised);

        Assert.Null(read.Model);
        Assert.Equal(
            "The BlueBird tablet could not be read: BlueBird file not recognised. "
            + "This copy of MSFS Blind Assist cannot read this version of the 757; check for an update.",
            read.Status);
    }

    [Fact]
    public void Memory_not_found_still_asks_whether_the_757_is_loaded()
    {
        var read = Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.MemoryNotFound);

        Assert.Null(read.Model);
        Assert.Equal(
            "The BlueBird tablet could not be read: aircraft memory not found. Is the 757 loaded? Press F5 to try again.",
            read.Status);
    }
}

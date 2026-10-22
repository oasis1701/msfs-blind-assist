// Pins how the Fenix v3 EFB window sorts the page it is on (plan 03, Owner decision 8). The EFB's MCDU
// and FAP pages cannot be read with a screen reader, so the window steps back to the EFB home and says
// where the working windows are; every other page is left alone. Classification is by the fragment's
// first segment, on the gateway's own origin only (http 127.0.0.1:8083), so a page that merely carries
// "#/mcdu" on another host is not caught.

using MSFSBlindAssist.Forms.Fenix;

namespace MSFSBlindAssist.Tests;

public class FenixEfbRoutesTests
{
    [Fact]
    public void Dead_ends_are_recognised()
    {
        Assert.Equal(FenixEfbRoute.Mcdu, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/mcdu"));
        Assert.Equal(FenixEfbRoute.Mcdu, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/mcdu?cdu=1&fullscreen=false"));
        Assert.Equal(FenixEfbRoute.Fap, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/fap/screen-saver"));
        Assert.Equal(FenixEfbRoute.Fap, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/fap/cabin-status"));
    }

    [Fact]
    public void Efb_routes_are_left_alone()
    {
        Assert.Equal(FenixEfbRoute.Efb, FenixEfbRoutes.Classify("http://127.0.0.1:8083/?target=external#/efb"));
        Assert.Equal(FenixEfbRoute.Efb, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/efb/fenix/ground-services"));
        Assert.Equal(FenixEfbRoute.Efb, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/efb/settings"));
        Assert.Equal(FenixEfbRoute.Efb, FenixEfbRoutes.Classify("http://127.0.0.1:8083/"));

        Assert.Equal(FenixEfbRoute.Other, FenixEfbRoutes.Classify("https://support.fenixsim.com/#/mcdu"));
        Assert.Equal(FenixEfbRoute.Other, FenixEfbRoutes.Classify(null));
        Assert.Equal(FenixEfbRoute.Other, FenixEfbRoutes.Classify(""));
    }

    [Theory]
    [InlineData("http://example.com/#/fap/cabin-status")]
    [InlineData("http://127.0.0.1:9999/#/mcdu")]
    [InlineData("https://127.0.0.1:8083/#/fap")]
    [InlineData("http://localhost:8083/#/mcdu")]
    [InlineData("http://127.0.0.1:8083/#/somewhere/else")]
    [InlineData("http://127.0.0.1:8083/#mcdu")]
    [InlineData("not a url")]
    public void Another_origin_another_page_or_an_unparseable_url_is_other(string url)
    {
        Assert.Equal(FenixEfbRoute.Other, FenixEfbRoutes.Classify(url));
    }

    [Fact]
    public void The_scheme_host_and_segment_are_compared_ignoring_case()
    {
        Assert.Equal(FenixEfbRoute.Mcdu, FenixEfbRoutes.Classify("HTTP://127.0.0.1:8083/#/MCDU"));
        Assert.Equal(FenixEfbRoute.Fap, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/Fap/cabin-status"));
    }

    [Fact]
    public void Each_dead_end_says_where_to_go()
    {
        Assert.Equal(
            "The EFB's MCDU can't be read with a screen reader. Use the MCDU window instead: input mode, then Shift+M.",
            FenixEfbRoutes.DeadEndMessage(FenixEfbRoute.Mcdu));
        Assert.Equal(
            "The EFB's FAP can't be read with a screen reader. Use the Fenix windows list instead: input mode, then Control+Shift+F.",
            FenixEfbRoutes.DeadEndMessage(FenixEfbRoute.Fap));
        Assert.Null(FenixEfbRoutes.DeadEndMessage(FenixEfbRoute.Efb));
        Assert.Null(FenixEfbRoutes.DeadEndMessage(FenixEfbRoute.Other));
    }
}

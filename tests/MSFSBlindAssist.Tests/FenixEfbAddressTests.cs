// Pins the Fenix v3 EFB window's address and status text. The window
// loads the EFB's external target on 127.0.0.1: the gateway listens on IPv4 only, and by name Windows
// tries ::1 first and spends about 2 s refusing it on every new connection ([A320-40]); the v3 page is
// NOT accessible on its own, so the window adds the layer. The not-reachable text names the address it
// tried, and the status text is a tabbable read-only TextBox, never a Label ([VAT-13]). The constants
// are pinned by value; the source scan pins what a value cannot (no localhost, no Label, no stale claim).

using System.Text.RegularExpressions;
using MSFSBlindAssist.Forms.Fenix;

namespace MSFSBlindAssist.Tests;

public class FenixEfbAddressTests
{
    [Fact]
    public void The_efb_page_is_the_external_target_on_ipv4()
    {
        Assert.Equal("http://127.0.0.1:8083/?target=external#/efb", FenixEFBForm.EfbUrl);

        var u = new Uri(FenixEFBForm.EfbUrl);
        Assert.Equal("127.0.0.1", u.Host);
        Assert.Equal("?target=external", u.Query);
        Assert.Equal("#/efb", u.Fragment);
    }

    [Fact]
    public void The_not_reachable_message_names_the_address_it_tried()
    {
        Assert.Equal(
            "Fenix EFB is not reachable at http://127.0.0.1:8083. Make sure the Fenix A320 is fully loaded, then press Retry.",
            FenixEFBForm.NotReachableMessage);
    }

    [Fact]
    public void The_window_never_addresses_the_gateway_by_name_and_shows_no_label()
    {
        string source = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "Fenix", "FenixEFBForm.cs"));

        Assert.DoesNotMatch(new Regex(@"(?i)(ws|http)s?://localhost"), source);
        Assert.DoesNotContain("new Label", source);
        // Case-insensitive and line-wrap tolerant: the stale claim was "ALREADY" and a "///" away from "screen-reader accessible".
        Assert.DoesNotMatch(new Regex(@"(?i)already\W+screen-reader\s+accessible"), source);
    }
}

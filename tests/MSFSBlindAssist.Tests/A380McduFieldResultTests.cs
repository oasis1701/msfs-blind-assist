// What the A380 MCDU window says when the MFD agent reports that a command changed nothing (the
// verdict of a field entry or a page jump, pushed back by CoherentDebuggerClient as a
// command-result state).
//
// The window speaks the typed text (or "Cleared") as it sends an entry, which sounds like success.
// A fixed-value pick list ignores typed text and clears since FBW #10929 and the MFD shows no
// message, so without this the pilot heard their value read back for an entry that set nothing.
// A page jump to a disabled page item changes no page title, so it was silent too.

using MSFSBlindAssist.Forms.FBWA380;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A380McduFieldResultTests
{
    [Theory]
    [InlineData("send_to_field", "nomatch", "Not in list")]
    [InlineData("send_to_field", "noclear", "Cannot be cleared")]
    [InlineData("send_to_field", "inactive", "Unavailable")]
    [InlineData("navigate_by_id", "disabled", "Not available")]       // WIND before #10929, REPORT, GNSS …
    [InlineData("navigate_by_id", "missing", "Not available from this page")]
    public void A_refused_command_is_spoken(string command, string verdict, string expected)
    {
        Assert.Equal(expected, FBWA380MCDUForm.CommandResultPhrase(command, verdict));
    }

    [Theory]
    [InlineData("send_to_field", "ok")]
    [InlineData("send_to_field", "")]          // no live socket, or the eval timed out
    [InlineData("send_to_field", "missing")]   // the page changed under the entry: its own push says so
    [InlineData("send_to_field", "noinput")]
    [InlineData("navigate_by_id", "ok")]       // the new page's title is announced
    [InlineData("navigate_by_id", "key")]      // the KCCU key fallback fired
    [InlineData("navigate_uri", "disabled")]   // only the commands the client reports are mapped
    public void Anything_else_is_silent(string command, string verdict)
    {
        Assert.Null(FBWA380MCDUForm.CommandResultPhrase(command, verdict));
    }

    [Fact]
    public void Entries_and_page_jumps_are_pushed_back()
    {
        Assert.True(CoherentDebuggerClient.ReportsResult("send_to_field"));
        Assert.True(CoherentDebuggerClient.ReportsResult("navigate_by_id"));
        Assert.False(CoherentDebuggerClient.ReportsResult("click_mcdu_element"));
        Assert.False(CoherentDebuggerClient.ReportsResult("get_mcdu_elements"));
    }
}

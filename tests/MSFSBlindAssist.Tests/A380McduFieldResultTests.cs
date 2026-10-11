// What the A380 MCDU window says when the MFD agent refuses a field entry (sendToField's verdict,
// pushed back by CoherentDebuggerClient as a field-result state).
//
// The window speaks the typed text as it sends it, which sounds like success. A fixed-value pick
// list ignores typed text since FBW #10929 and the MFD shows no message, so without this the
// pilot heard their value read back for an entry that set nothing.

using MSFSBlindAssist.Forms.FBWA380;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A380McduFieldResultTests
{
    [Theory]
    [InlineData("nomatch", "Not in list")]
    [InlineData("inactive", "Unavailable")]
    public void A_refused_entry_is_spoken(string verdict, string expected)
    {
        Assert.Equal(expected, FBWA380MCDUForm.FieldResultPhrase(verdict));
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("")]          // no live socket, or the eval timed out
    [InlineData("missing")]   // the page changed under the entry: its own push says so
    [InlineData("noinput")]
    public void Anything_else_is_silent(string verdict)
    {
        Assert.Null(FBWA380MCDUForm.FieldResultPhrase(verdict));
    }

    [Fact]
    public void Only_a_refusal_is_pushed_back()
    {
        Assert.True(CoherentDebuggerClient.IsFieldRefusal("nomatch"));
        Assert.True(CoherentDebuggerClient.IsFieldRefusal("inactive"));
        Assert.False(CoherentDebuggerClient.IsFieldRefusal("ok"));
        Assert.False(CoherentDebuggerClient.IsFieldRefusal(""));
    }
}

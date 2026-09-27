using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A typed MCDU entry holding a character the keyboard lacks is REFUSED whole and the character
/// is named — never sent with that character dropped. "N123*" went in as N123 and "KJFK,KLAX" as
/// KJFKKLAX with nothing spoken, on an aircraft whose screens a blind pilot cannot read; the
/// window's own rule (a press that cannot be delivered is spoken, never swallowed) stopped one
/// layer below the character mapping. These pin the pure half the window now validates with.
/// </summary>
public class Md11McduTypedTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("KJFK/KLAX")]
    [InlineData("N123")]
    [InlineData("FL350 +10 -5")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789./+- ")]
    public void UntypeableCharacters_IsEmptyWhenEveryCharacterHasAKey(string text)
    {
        Assert.Equal("", Md11McduKeys.UntypeableCharacters(text));
        Assert.Null(Md11McduKeys.RefusalFor(text));
    }

    [Theory]
    [InlineData("N123*", "*")]
    [InlineData("KJFK,KLAX", ",")]
    [InlineData("N123*,*", "*,")]      // each offender once, in order of first appearance
    [InlineData("#A%B#", "#%")]
    public void UntypeableCharacters_ListsEachOffenderOnceInOrder(string text, string expected)
    {
        Assert.Equal(expected, Md11McduKeys.UntypeableCharacters(text));
    }

    /// <summary>
    /// The character is NAMED, not echoed: a screen reader's symbol level decides whether a bare
    /// "*" is read as "star" or as nothing, and "," is routinely swallowed as punctuation.
    /// </summary>
    [Theory]
    [InlineData("KJFK,KLAX", "Not sent. The MCDU keyboard has no comma key.")]
    [InlineData("N123*", "Not sent. The MCDU keyboard has no asterisk key.")]
    [InlineData("N123*,", "Not sent. The MCDU keyboard has no asterisk or comma key.")]
    [InlineData("N123*,#", "Not sent. The MCDU keyboard has no asterisk, comma or hash key.")]
    public void RefusalFor_NamesEveryMissingKeyAndSendsNothing(string text, string expected)
    {
        Assert.Equal(expected, Md11McduKeys.RefusalFor(text));
    }

    /// <summary>A character with no spoken name is still reported — as itself, never dropped.</summary>
    [Fact]
    public void RefusalFor_SpeaksAnUnnamedCharacterAsItself()
    {
        Assert.Equal("Not sent. The MCDU keyboard has no É key.", Md11McduKeys.RefusalFor("ÉCOLE"));
    }

    /// <summary>
    /// The refusal and the key table must agree by construction: whatever ForChar rejects is what
    /// the refusal reports, so a key added to one can never be silently dropped by the other.
    /// </summary>
    [Fact]
    public void UntypeableCharacters_AgreesWithForChar()
    {
        for (var c = (char)32; c < 127; c++)
        {
            var reported = Md11McduKeys.UntypeableCharacters(c.ToString());
            Assert.Equal(Md11McduKeys.ForChar(c) == null, reported.Length == 1);
        }
    }

    // ------------------------------------------------------------------ deliverability (C3)
    //
    // Review round 2: the window pressed key by key, spoke "{key} key unavailable" for each key
    // that could not be delivered, sent the rest — and then cleared the box. An entry is now
    // checked whole before its first key, and refused in ONE sentence with the text kept.

    [Fact]
    public void FirstUndeliverableKey_IsNullWhenEveryKeyCanBePressed()
    {
        Assert.Null(Md11McduKeys.FirstUndeliverableKey("KJFK/KLAX", _ => true));
        Assert.Null(Md11McduKeys.FirstUndeliverableKey("", _ => false));   // nothing to press
    }

    [Fact]
    public void FirstUndeliverableKey_NamesTheFirstDeadKeyInTypingOrder()
    {
        var dead = new HashSet<string> { "SLASH", "L" };
        Assert.Equal("SLASH", Md11McduKeys.FirstUndeliverableKey("KJFK/KLAX", k => !dead.Contains(k)));
    }

    [Fact]
    public void FirstUndeliverableKey_AsksAboutTheKeySuffixesTheWindowPresses()
    {
        var asked = new List<string>();
        Md11McduKeys.FirstUndeliverableKey("A. -1", k => { asked.Add(k); return true; });
        Assert.Equal(new[] { "A", "DOT", "SP", "MINUS", "1" }, asked);
    }

    [Fact]
    public void FirstUndeliverableKey_LeavesAnUntypeableCharacterToRefusalFor()
    {
        // RefusalFor runs first and names it; this check is only about keys that exist. So '*' (no
        // MCDU key) is never offered to the predicate — the window answers it with CanPress on a
        // node id built from the key — and never reported as an undeliverable key.
        var asked = new List<string>();
        Assert.Null(Md11McduKeys.FirstUndeliverableKey("N12*", k => { asked.Add(k); return true; }));
        Assert.Equal(new[] { "N", "1", "2" }, asked);
    }

    [Theory]
    [InlineData("SLASH", "Not sent. The MCDU slash key is unavailable.")]
    [InlineData("DOT", "Not sent. The MCDU dot key is unavailable.")]
    [InlineData("SP", "Not sent. The MCDU space key is unavailable.")]
    [InlineData("PLUS", "Not sent. The MCDU plus key is unavailable.")]
    [InlineData("MINUS", "Not sent. The MCDU minus key is unavailable.")]
    [InlineData("K", "Not sent. The MCDU K key is unavailable.")]
    [InlineData("7", "Not sent. The MCDU 7 key is unavailable.")]
    public void UndeliverableRefusal_IsOneSentenceNamingTheKey(string key, string expected)
    {
        Assert.Equal(expected, Md11McduKeys.UndeliverableRefusal(key));
    }

    /// <summary>
    /// CanPress answers under PressControl's own conditions: with no bus attached (Attach has not
    /// run — the test suite never attaches) nothing can be pressed, a real MCDU key included.
    /// Both now also require the transport to be able to SEND
    /// (<c>SimConnectManager.CalcWriteCanLand</c>, false during a SimConnect outage and once the
    /// calc-path probe has concluded unverified, when the bus's write returns having done nothing
    /// while the pump consumes the id) — unreachable
    /// here for the same reason there is no bus, and the equality below is what keeps the two
    /// answers in step whichever condition fails.
    /// </summary>
    [Fact]
    public void An_unattached_definition_can_press_nothing()
    {
        var def = new TFDiMD11Definition();
        var realKey = Md11McduKeys.NodeId(Md11McduUnit.Left, "A");

        Assert.False(def.CanPress(realKey));
        Assert.False(def.CanPress("MD11_NO_SUCH_NODE_BT"));
        Assert.Equal(def.CanPress(realKey), def.PressControl(realKey));
    }
}

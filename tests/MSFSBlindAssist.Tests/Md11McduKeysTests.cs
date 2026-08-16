using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Every key the MCDU window can press must resolve to a real, pressable control in the embedded
/// map — for all three units.
///
/// This is the test that earns its keep on this aircraft. A wrong node id does not throw and does
/// not fail a build: PressControl simply finds nothing and returns false. The MD-11's screens are
/// WASM-rendered and unreadable, so a pilot pressing a dead key has no way to see that nothing
/// happened. These tests are the only thing standing between a typo and a key that is silently
/// dead in flight.
/// </summary>
public class Md11McduKeysTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();

    private static readonly Md11McduUnit[] Units =
        { Md11McduUnit.Left, Md11McduUnit.Center, Md11McduUnit.Right };

    /// <summary>A control exists AND can actually be pressed (a lamp would satisfy the first only).</summary>
    private static bool IsPressable(string nodeId)
    {
        var c = Map.Controls.FirstOrDefault(
            x => string.Equals(x.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        return c != null && c.Events.ContainsKey("LEFT_BUTTON_DOWN") && c.Events.ContainsKey("LEFT_BUTTON_UP");
    }

    [Fact]
    public void Prefixes_AreDistinctPerUnit()
    {
        var prefixes = Units.Select(Md11McduKeys.Prefix).ToList();

        Assert.Equal(prefixes.Count, prefixes.Distinct().Count());
        Assert.Equal("MD11_LMCDU_", Md11McduKeys.Prefix(Md11McduUnit.Left));
        Assert.Equal("MD11_CMCDU_", Md11McduKeys.Prefix(Md11McduUnit.Center));
        Assert.Equal("MD11_RMCDU_", Md11McduKeys.Prefix(Md11McduUnit.Right));
    }

    [Fact]
    public void NodeId_BuildsTheMapsOwnShape()
    {
        Assert.Equal("MD11_LMCDU_INIT_BT", Md11McduKeys.NodeId(Md11McduUnit.Left, "INIT"));
        Assert.Equal("MD11_RMCDU_LSK_3L_BT", Md11McduKeys.NodeId(Md11McduUnit.Right, Md11McduKeys.Lsk(3, right: false)));
    }

    // ---------------------------------------------------------------------------------
    // Page accelerators
    // ---------------------------------------------------------------------------------

    /// <summary>The Alt+letter each button label declares via its &amp; mnemonic.</summary>
    private static char? Accelerator(string label)
    {
        var i = label.IndexOf('&');
        return i >= 0 && i + 1 < label.Length ? char.ToUpperInvariant(label[i + 1]) : null;
    }

    /// <summary>
    /// Two buttons sharing an Alt+letter is not a compile error and not a crash — WinForms simply
    /// cycles focus between them instead of pressing either. On a CDU that reads as a page key that
    /// "sometimes doesn't work", which is close to impossible to diagnose from the cockpit.
    /// </summary>
    [Fact]
    public void PageAccelerators_AreUnique()
    {
        var accels = Md11McduKeys.PageButtons
            .Select(b => Accelerator(b.Label))
            .Where(a => a != null)
            .ToList();

        Assert.Equal(accels.Count, accels.Distinct().Count());
    }

    /// <summary>
    /// The window reserves Alt+S (focus scratchpad) and Alt+1..6 (right line-select keys) in its own
    /// KeyDown handler. A page button claiming one of those would be shadowed by the handler — the
    /// button would exist but its accelerator would never reach it.
    /// </summary>
    [Fact]
    public void PageAccelerators_DoNotCollideWithReservedChords()
    {
        foreach (var (label, key) in Md11McduKeys.PageButtons)
        {
            var a = Accelerator(label);
            if (a == null) continue;

            Assert.True(a != 'S', $"{key} claims Alt+S, which is reserved for the scratchpad");
            Assert.False(char.IsDigit(a.Value), $"{key} claims a digit, reserved for the line-select keys");
        }
    }

    /// <summary>
    /// Every page key the MD-11 has must be reachable from the keyboard, not just by clicking. SEC
    /// FPLN is the deliberate exception: Alt+F is already Fpln, so it takes Alt+Shift+F in the
    /// window's KeyDown — the same chord, for the same reason, as the Fenix and FBW forms.
    /// </summary>
    [Fact]
    public void EveryPageKey_HasAnAcceleratorExceptTheDocumentedExceptions()
    {
        // Slew/paging keys have dedicated chords (Page Up/Down, Alt+arrows) rather than letters.
        var byChord = new[] { "SEC_FPLN", "NEXTPAGE", "UP", "DOWN" };

        foreach (var (label, key) in Md11McduKeys.PageButtons)
        {
            if (byChord.Contains(key)) continue;
            Assert.True(Accelerator(label) != null, $"{key} has no Alt accelerator");
        }
    }

    // ---------------------------------------------------------------------------------
    // Every pressable key, on every unit
    // ---------------------------------------------------------------------------------

    [Fact]
    public void PageButtons_AllExistOnAllThreeUnits()
    {
        foreach (var unit in Units)
            foreach (var (label, key) in Md11McduKeys.PageButtons)
                Assert.True(IsPressable(Md11McduKeys.NodeId(unit, key)),
                    $"{label} ({key}) missing on {unit}: {Md11McduKeys.NodeId(unit, key)}");
    }

    [Fact]
    public void LineSelectKeys_AllTwelveExistOnAllThreeUnits()
    {
        foreach (var unit in Units)
            for (var row = 1; row <= 6; row++)
                foreach (var right in new[] { false, true })
                {
                    var node = Md11McduKeys.NodeId(unit, Md11McduKeys.Lsk(row, right));
                    Assert.True(IsPressable(node), $"LSK {row}{(right ? 'R' : 'L')} missing on {unit}: {node}");
                }
    }

    /// <summary>
    /// Every character the scratchpad accepts must map to a key that exists. This is what stops a
    /// typed entry from silently losing characters — e.g. an ident with a slash in it going in as
    /// the wrong text with no visible cue.
    /// </summary>
    [Fact]
    public void EveryTypeableCharacter_MapsToAKeyThatExists()
    {
        const string typeable = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789./+- ";

        foreach (var unit in Units)
            foreach (var c in typeable)
            {
                var key = Md11McduKeys.ForChar(c);
                Assert.True(key != null, $"'{c}' has no key mapping");
                Assert.True(IsPressable(Md11McduKeys.NodeId(unit, key!)),
                    $"'{c}' -> {key} missing on {unit}");
            }
    }

    [Theory]
    [InlineData('.', "DOT")]
    [InlineData('/', "SLASH")]
    [InlineData(' ', "SP")]
    [InlineData('A', "A")]
    [InlineData('7', "7")]
    public void ForChar_MapsPunctuationToItsKeyName(char c, string expected)
    {
        Assert.Equal(expected, Md11McduKeys.ForChar(c));
    }

    /// <summary>
    /// The MD-11 has SEPARATE plus and minus keys — unlike the Airbus's single combined +/- key,
    /// which has to be pressed twice to reach minus. Collapsing these onto one key would enter the
    /// wrong sign.
    /// </summary>
    [Fact]
    public void ForChar_PlusAndMinusAreSeparateKeys()
    {
        Assert.Equal("PLUS", Md11McduKeys.ForChar('+'));
        Assert.Equal("MINUS", Md11McduKeys.ForChar('-'));
        Assert.True(IsPressable(Md11McduKeys.NodeId(Md11McduUnit.Left, "PLUS")));
        Assert.True(IsPressable(Md11McduKeys.NodeId(Md11McduUnit.Left, "MINUS")));
    }

    [Theory]
    [InlineData('*')]
    [InlineData('#')]
    [InlineData('%')]
    public void ForChar_UnsupportedCharacterIsRejected(char c)
    {
        Assert.Null(Md11McduKeys.ForChar(c));
    }

    // ---------------------------------------------------------------------------------
    // Keys the MD-11 does NOT have
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The real MD-11 CDU has no EXEC, no DEL and no PREV PAGE: it slews with UP/DOWN and confirms
    /// via LSKs. Pinning their ABSENCE keeps a future contributor from "restoring" a Boeing key the
    /// aircraft has never had — it would resolve to nothing and read as a dead key.
    /// </summary>
    [Theory]
    [InlineData("EXEC")]
    [InlineData("DEL")]
    [InlineData("PREVPAGE")]
    public void KeysTheAircraftDoesNotHave_AreAbsentFromTheMap(string key)
    {
        foreach (var unit in Units)
            Assert.False(IsPressable(Md11McduKeys.NodeId(unit, key)));
    }

    /// <summary>
    /// L_BT and R_BT are the LETTERS L and R. The map generator derived their labels as "Left
    /// button" / "Right button", which reads exactly like a cursor key — following those labels
    /// instead of the node id would wire two alphabet keys to the wrong function.
    /// </summary>
    [Fact]
    public void LAndRKeys_AreLettersNotDirections()
    {
        Assert.Equal("L", Md11McduKeys.ForChar('L'));
        Assert.Equal("R", Md11McduKeys.ForChar('R'));
        Assert.True(IsPressable(Md11McduKeys.NodeId(Md11McduUnit.Left, "L")));
        Assert.True(IsPressable(Md11McduKeys.NodeId(Md11McduUnit.Left, "R")));
    }
}

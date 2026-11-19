// The read-back of the 737's MCP toggles in the heading, speed, altitude and V/S windows (owner,
// 2026-10-23, option A). ValueInputForm's own read-back is an interrupting announce 1.2 s after a press:
// on the 737 it repeated the MCP light and cut the FMA line off. The window's read-back is switched off
// for them; 3 s after the press the state is read again, and only a press that changed nothing is read
// back, queued. Driven with a captured scheduler: no timer and no window.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class Pmdg737McpToggleReadbackTests
{
    // ---- the decision ---------------------------------------------------------------------------

    [Fact]
    public void Unchanged_state_is_read_back_in_the_windows_words() =>
        Assert.Equal("Level Change Off", Pmdg737McpToggleReadback.Decide("&Level Change", "Off", "Off", refused: false));

    [Fact]
    public void Changed_state_says_nothing() =>
        Assert.Null(Pmdg737McpToggleReadback.Decide("&Level Change", "Off", "Engaged", refused: false));

    [Fact]
    public void Refused_press_says_nothing() =>
        Assert.Null(Pmdg737McpToggleReadback.Decide("&Level Change", "Off", "Off", refused: true));

    [Fact]
    public void A_toggle_with_no_state_is_not_the_checks_business() =>
        // Altitude Intervene shows no state: its press is confirmed at once ("… pressed"), never by the check.
        Assert.Null(Pmdg737McpToggleReadback.Decide("Altitude &Intervene", "", "", refused: false));

    [Fact]
    public void The_pressed_line_is_the_name_and_pressed() =>
        Assert.Equal("Altitude Intervene pressed", Pmdg737McpToggleReadback.PressedLine("Altitude &Intervene"));

    // ---- the wrapped toggle -----------------------------------------------------------------------

    private sealed class Rig
    {
        public string State = "Off";
        public bool Refused;
        public int Presses;
        public readonly List<(int DelayMs, Action Check)> Scheduled = new();
        public readonly SpeechCapture Speech = new();
        public readonly ToggleButtonDef Wrapped;

        public Rig(bool withRefusalFlag = false)
        {
            var toggle = new ToggleButtonDef("&Level Change", () => State, () => Presses++)
            {
                IsEnabled = () => true,
                SuppressStateAnnounce = withRefusalFlag ? () => Refused : null,
            };
            Wrapped = Pmdg737McpToggleReadback.Wrap(toggle, Speech, (ms, check) => Scheduled.Add((ms, check)));
        }
    }

    [Fact]
    public void The_windows_own_read_back_is_always_switched_off()
    {
        var rig = new Rig(withRefusalFlag: true);
        Assert.True(rig.Wrapped.SuppressStateAnnounce!());
        rig.Refused = false;
        Assert.True(rig.Wrapped.SuppressStateAnnounce!());
    }

    [Fact]
    public void Label_state_and_enable_gate_are_the_toggles_own()
    {
        var rig = new Rig();
        Assert.Equal("&Level Change", rig.Wrapped.Label);
        rig.State = "Engaged";
        Assert.Equal("Engaged", rig.Wrapped.GetCurrentState());
        Assert.NotNull(rig.Wrapped.IsEnabled);
        Assert.True(rig.Wrapped.IsEnabled!());
    }

    [Fact]
    public void A_press_presses_once_and_checks_3_s_later()
    {
        var rig = new Rig();
        rig.Wrapped.OnPressed();
        Assert.Equal(1, rig.Presses);
        Assert.Equal([3000], rig.Scheduled.Select(s => s.DelayMs));
        Assert.Empty(rig.Speech.All); // nothing until the check
    }

    [Fact]
    public void A_press_that_changed_nothing_is_read_back_once_without_interrupting()
    {
        var rig = new Rig();
        rig.Wrapped.OnPressed();
        rig.Scheduled.Single().Check();
        Assert.Equal(["Level Change Off"], rig.Speech.All);
        Assert.Empty(rig.Speech.Interrupts); // never cuts the FMA or the MCP light off
    }

    [Fact]
    public void A_press_that_worked_says_nothing_more()
    {
        // The screen reader reads the button's new label, and the MCP light and FMA lines speak.
        var rig = new Rig();
        rig.Wrapped.OnPressed();
        rig.State = "Engaged";
        rig.Scheduled.Single().Check();
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_state_is_taken_at_the_press_not_at_wrap_time()
    {
        var rig = new Rig();
        rig.State = "Engaged"; // engaged elsewhere after the window opened
        rig.Wrapped.OnPressed(); // the pilot presses: it should disengage
        rig.Scheduled.Single().Check(); // but nothing happened
        Assert.Equal(["Level Change Engaged"], rig.Speech.All);
    }

    [Fact]
    public void A_refused_press_is_not_read_back()
    {
        var rig = new Rig(withRefusalFlag: true);
        rig.Wrapped.OnPressed();
        rig.Refused = true; // the action spoke its refusal
        rig.Scheduled.Single().Check();
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_toggle_with_no_state_says_pressed_at_once_without_a_check()
    {
        // Owner, 2026-10-23: "Altitude Intervene pressed", not the bare name 3 s later. A toggle that shows
        // no state has nothing to wait for, so the press is confirmed at once, queued (it never cuts the
        // FMA or the MCP light off), in the app's words for a momentary button.
        var rig = new Rig();
        rig.State = "";
        rig.Wrapped.OnPressed();
        Assert.Equal(1, rig.Presses);
        Assert.Equal(["Level Change pressed"], rig.Speech.All);
        Assert.Empty(rig.Speech.Interrupts);
        Assert.Empty(rig.Scheduled);
    }

    [Fact]
    public void A_refused_press_of_a_toggle_with_no_state_says_nothing()
    {
        var rig = new Rig(withRefusalFlag: true);
        rig.State = "";
        rig.Refused = true; // the action spoke its refusal during the press
        rig.Wrapped.OnPressed();
        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Scheduled);
    }

    [Fact]
    public void Each_press_gets_its_own_check()
    {
        var rig = new Rig();
        rig.Wrapped.OnPressed();
        rig.State = "Engaged";
        rig.Wrapped.OnPressed();
        rig.Scheduled[0].Check(); // the first press worked
        rig.Scheduled[1].Check(); // the second changed nothing
        Assert.Equal(["Level Change Engaged"], rig.Speech.All);
    }

    [Fact]
    public void WrapAll_wraps_every_toggle_in_order()
    {
        var speech = new SpeechCapture();
        var toggles = new List<ToggleButtonDef>
        {
            new("&Heading Select", () => "Off", () => { }),
            new("&LNAV", () => "Off", () => { }),
        };
        var wrapped = Pmdg737McpToggleReadback.WrapAll(toggles, speech);
        Assert.Equal(["&Heading Select", "&LNAV"], wrapped.Select(t => t.Label));
        Assert.All(wrapped, t => Assert.True(t.SuppressStateAnnounce!()));
    }

    // ---- the wiring: the four MCP windows use it, the altimeter window does not --------------------

    [Theory]
    [InlineData("private void ShowPMDGHeadingDialog(", true)]
    [InlineData("private void ShowPMDGSpeedDialog(", true)]
    [InlineData("private void ShowPMDGAltitudeDialog(", true)]
    [InlineData("private void ShowPMDGVSDialog(", true)]
    [InlineData("private void ShowPMDGBaroDialog(", false)]
    public void The_four_mcp_windows_wrap_their_toggles(string signature, bool wrapped)
    {
        string body = MethodBody(File.ReadAllText(DefinitionSourcePath()), signature);
        Assert.Contains("new ValueInputForm(", body);
        if (wrapped)
            Assert.Contains("Pmdg737McpToggleReadback.WrapAll(toggles, announcer)", body);
        else
            Assert.DoesNotContain(nameof(Pmdg737McpToggleReadback), body); // its refusal read-back stays the window's
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new InvalidOperationException($"{signature} has no closing brace");
    }

    private static string DefinitionSourcePath([System.Runtime.CompilerServices.CallerFilePath] string thisTestFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisTestFilePath)!,
            "..", "..", "MSFSBlindAssist", "Aircraft", "PMDG737Definition.cs"));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Spoken_in_a_comma_decimal_culture(string culture) => TestCulture.Run(culture, () =>
    {
        var rig = new Rig();
        rig.State = "Engaged";
        rig.Wrapped.OnPressed();
        rig.Scheduled.Single().Check();
        Assert.Equal(["Level Change Engaged"], rig.Speech.All);
        return 0;
    });
}

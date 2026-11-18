// Characterization tests for the Fenix display channel's key dispatch (FenixA320Definition.DisplayChannel.cs): which
// feature owns each hotkey action, that TryChannelDisplayRead sends the keys of each owner to that owner's seam and to
// no other, and the words of SpeakNotAvailable, which every seam says for a key whose display it cannot read. No sim
// and no socket: the owner of an action is a pure function. Each feature implements its seam in a partial of its own and
// tests it there; a seam that leaves `handled` false lets the key carry on: a display key to its AI read, any other to
// nothing.
//
// The contract: the five display keys (Alt+P, N, E, S, I), the six speed reads (Shift+1 to Shift+6), Ctrl+W, D,
// Shift+D and the app-wide ShowDCDU each have exactly one owner, and every other action has none, so an action outside
// that table never reaches a seam. TryChannelDisplayRead calls the seam of the action's owner with the action, the
// context and its own `handled` by reference (so the seam's answer reaches HandleHotkeyAction), and no other seam. It
// is a private partial method, so which seam each owner reaches is pinned by reading the method's source. The seam runs
// inside RunChannelKeySeam: one that throws is reported and leaves `handled` false, so the key carries on as it does with
// no feature (a display key to its AI read, ShowDCDU to MainForm's sentence, any other key to nothing) and costs only
// itself. No seam can be made to throw from a test, so
// that helper is tested with seam calls of the tests' own, and that the dispatch goes through it is pinned by reading
// its source too.

using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDispatchTests
{
    // ---- Who owns a key ---------------------------------------------------------------------------

    // The owner travels as text: a public test method cannot take an internal type (CS0051).
    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, "Pfd")]            // Alt+P
    [InlineData(HotkeyAction.ReadSpeedGD, "Pfd")]               // Shift+1
    [InlineData(HotkeyAction.ReadSpeedS, "Pfd")]                // Shift+2
    [InlineData(HotkeyAction.ReadSpeedF, "Pfd")]                // Shift+3
    [InlineData(HotkeyAction.ReadSpeedVLS, "Pfd")]              // Shift+4
    [InlineData(HotkeyAction.ReadSpeedVS, "Pfd")]               // Shift+5
    [InlineData(HotkeyAction.ReadSpeedVFE, "Pfd")]              // Shift+6
    [InlineData(HotkeyAction.ReadDisplayND, "Nd")]              // Alt+N
    [InlineData(HotkeyAction.ReadNDWaypoint, "Nd")]             // Ctrl+W
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, "Ewd")]      // Alt+E
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, "Sd")]       // Alt+S
    [InlineData(HotkeyAction.ReadDisplayISIS, "Isis")]          // Alt+I
    [InlineData(HotkeyAction.ReadDistanceToDest, "Distances")]  // D
    [InlineData(HotkeyAction.ReadDistanceToTOD, "Distances")]   // Shift+D
    [InlineData(HotkeyAction.ShowDCDU, "Dcdu")]                 // input Ctrl+Shift+D
    public void Each_channel_key_has_its_owner(HotkeyAction action, string owner)
    {
        Assert.Equal(
            Enum.Parse<FenixA320Definition.ChannelKeyOwner>(owner), FenixA320Definition.ChannelKeyOwnerOf(action));
    }

    [Theory]
    [InlineData(HotkeyAction.ShowAircraftWindows)]      // the window list, which HandleHotkeyAction answers first
    [InlineData(HotkeyAction.ShowFenixMCDU)]
    [InlineData(HotkeyAction.ShowRMP)]
    [InlineData(HotkeyAction.ShowOANS)]
    [InlineData(HotkeyAction.ReadWaypointInfo)]         // not Ctrl+W, which is ReadNDWaypoint
    [InlineData(HotkeyAction.ReadApproachCapability)]
    [InlineData(HotkeyAction.ReadAltimeter)]
    [InlineData(HotkeyAction.ReadFuelInfo)]
    [InlineData(HotkeyAction.ReadNavRadioInfo)]
    [InlineData(HotkeyAction.FCUHeadingPush)]
    [InlineData(HotkeyAction.FCUSetAltitude)]
    [InlineData(HotkeyAction.ToggleECAMMonitoring)]
    [InlineData(HotkeyAction.DescribeScene)]
    [InlineData(HotkeyAction.ReadHeading)]              // the first member of the enum
    public void Any_other_key_has_no_owner(HotkeyAction action)
    {
        Assert.Equal(FenixA320Definition.ChannelKeyOwner.None, FenixA320Definition.ChannelKeyOwnerOf(action));
    }

    [Fact]
    public void No_action_outside_the_table_has_an_owner()
    {
        // The table is closed: the fifteen rows of Each_channel_key_has_its_owner are every action with an owner. A key
        // added to ChannelKeyOwnerOf without a row there fails here, so a key that starts reaching a seam is a decision
        // with its own row and its own check in the sim, never a side effect.
        int owned = Enum.GetValues<HotkeyAction>()
            .Count(action => FenixA320Definition.ChannelKeyOwnerOf(action) != FenixA320Definition.ChannelKeyOwner.None);

        Assert.Equal(15, owned);
    }

    // ---- Which seam an owner reaches --------------------------------------------------------------

    [Fact]
    public void Each_owner_has_one_seam_call()
    {
        List<(string Owner, string Body)> cases = CasesOf(DispatchSource());

        foreach (FenixA320Definition.ChannelKeyOwner owner in Enum.GetValues<FenixA320Definition.ChannelKeyOwner>())
        {
            if (owner == FenixA320Definition.ChannelKeyOwner.None) continue;

            string name = owner.ToString();
            List<(string Owner, string Body)> own = cases.Where(c => c.Owner == name).ToList();
            Assert.True(own.Count == 1,
                $"{name} is in {own.Count} cases of the dispatch switch: it needs exactly one");

            MatchCollection calls = SeamCall.Matches(own[0].Body);
            Assert.True(calls.Count == 1,
                $"The {name} case makes {calls.Count} seam calls: it needs exactly one, Channel{name}Key");
            Assert.Equal($"Channel{name}Key", calls[0].Groups["seam"].Value);
            // The action, the context, and the dispatch's own `handled` by reference: a local in its place would
            // drop the seam's answer.
            Assert.Equal("action,ctx,refhandled", Whitespace.Replace(calls[0].Groups["args"].Value, ""));
        }

        // None is no feature's key: if the switch names it at all, that case calls no seam.
        Assert.DoesNotContain(cases.Where(c => c.Owner == "None"), c => SeamCall.IsMatch(c.Body));
    }

    // ---- What a key says when its display cannot be read ------------------------------------------

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_key_that_cannot_be_read_says_its_name_then_not_available(string culture)
    {
        // The words of the other aircraft's key readouts ("Top of descent not available" in BaseAircraftDefinition):
        // the name, one space, "not available", no period, and spoken at once, as a key's answer is. No number is
        // formatted, so the line is the same where the PC's culture writes a comma.
        SpeechCapture speech = TestCulture.Run(culture, () =>
        {
            var capture = new SpeechCapture();
            var context = new FenixA320Definition.ChannelKeyContext(null!, capture, null!, null!, null);
            var definition = new FenixA320Definition();

            definition.SpeakNotAvailable(context, "Green dot");
            definition.SpeakNotAvailable(context, "VFE next");
            definition.SpeakNotAvailable(context, "Top of descent");
            return capture;
        });

        Assert.Equal(new[] { "Green dot not available", "VFE next not available", "Top of descent not available" },
            speech.Interrupts);
        // Everything went out through AnnounceImmediate: nothing was queued beside it.
        Assert.Equal(speech.Interrupts, speech.All);
    }

    // ---- A seam that throws -----------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_seam_that_returns_keeps_its_answer(bool answers)
    {
        bool handled = false;

        FenixA320Definition.RunChannelKeySeam(FenixA320Definition.ChannelKeyOwner.Pfd, ContextWith(null), ref handled,
            (ref bool seamHandled) => seamHandled = answers);

        Assert.Equal(answers, handled);
    }

    [Theory]
    [InlineData(false)]     // it threw before it answered
    [InlineData(true)]      // it set handled and then threw: a key that failed is not an answered key
    public void A_seam_that_throws_leaves_the_key_unhandled(bool answeredFirst)
    {
        var speech = new SpeechCapture();
        bool handled = false;

        Exception? escaped = Record.Exception(() => FenixA320Definition.RunChannelKeySeam(
            FenixA320Definition.ChannelKeyOwner.Pfd, ContextWith(null, speech), ref handled,
            (ref bool seamHandled) =>
            {
                seamHandled = answeredFirst;
                throw new InvalidOperationException("no speed limits");
            }));

        Assert.Null(escaped);
        Assert.False(handled);
        // Nothing is said: the key carries on as it does with no feature, a display key to its AI read.
        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_seam_that_throws_is_reported_once_through_the_monitor(string culture)
    {
        // The monitor writes each distinct line once, so a seam that throws at every press is one line in the log, and
        // another owner's is a second: "<owner>: key failed: <exception type>: <message>". The report formats no
        // number, so it reads the same where the PC's culture writes a comma.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        FenixA320Definition.ChannelKeyContext context = ContextWith(monitor);

        bool[] handledAfter = TestCulture.Run(culture, () =>
        {
            var after = new List<bool>();
            foreach (FenixA320Definition.ChannelKeyOwner owner in new[]
            {
                FenixA320Definition.ChannelKeyOwner.Pfd, FenixA320Definition.ChannelKeyOwner.Pfd,
                FenixA320Definition.ChannelKeyOwner.Pfd, FenixA320Definition.ChannelKeyOwner.Nd,
            })
            {
                bool handled = false;
                FenixA320Definition.RunChannelKeySeam(owner, context, ref handled, AnswersThenThrows);
                after.Add(handled);
            }
            return after.ToArray();
        });

        Assert.Equal(new[] { false, false, false, false }, handledAfter);
        Assert.Equal(
            new[]
            {
                "Pfd: key failed: InvalidOperationException: no speed limits",
                "Nd: key failed: InvalidOperationException: no speed limits",
            },
            lines.All);
    }

    [Fact]
    public void A_seam_that_throws_with_no_monitor_is_isolated_too()
    {
        // Before Start and after Stop there is no monitor: the failure goes to the app's own log instead, and the key
        // still carries on.
        bool handled = false;

        Exception? escaped = Record.Exception(() => FenixA320Definition.RunChannelKeySeam(
            FenixA320Definition.ChannelKeyOwner.Isis, ContextWith(null), ref handled, AnswersThenThrows));

        Assert.Null(escaped);
        Assert.False(handled);
    }

    [Fact]
    public void The_dispatch_runs_its_seam_inside_the_isolation()
    {
        // TryChannelDisplayRead is the one way into a seam. Its body hands the switch to RunChannelKeySeam, with the
        // dispatch's own `handled`, and calls no seam of its own: the isolation is tested above, and this pins that the
        // dispatch is in it.
        string code = Comment.Replace(DispatchSource(), "");
        int start = code.IndexOf("partial void TryChannelDisplayRead(", StringComparison.Ordinal);
        Assert.True(start >= 0, "FenixA320Definition.DisplayChannel.cs has no TryChannelDisplayRead");
        string body = BlockAfter(code, start);

        Assert.Matches(
            @"\bRunChannelKeySeam\s*\(\s*owner\s*,\s*ctx\s*,\s*ref\s+handled\s*,[^;]*\bCallChannelKeySeam\s*\(", body);
        Assert.DoesNotMatch(SeamCall, body);
    }

    // A seam call that sets handled and then throws, the worst case for the key.
    private static void AnswersThenThrows(ref bool handled)
    {
        handled = true;
        throw new InvalidOperationException("no speed limits");
    }

    // A context with only the pieces these tests touch: the announcer and the monitor.
    private static FenixA320Definition.ChannelKeyContext ContextWith(
        FenixChannelMonitor? monitor, SpeechCapture? speech = null)
        => new(null!, speech ?? new SpeechCapture(), null!, null!, monitor);

    // ---- Reading the dispatch's source ------------------------------------------------------------

    // The definition's channel partial, read from the repository.
    private static string DispatchSource() => File.ReadAllText(Path.Combine(
        ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.DisplayChannel.cs"));

    // The block that opens at the first "{" after start, braces and all, up to its closing brace.
    private static string BlockAfter(string code, int start)
    {
        int open = code.IndexOf('{', start);
        Assert.True(open >= 0, "no block after the declaration");
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
        }

        throw new InvalidOperationException("the block is not closed");
    }

    // A line comment or a block comment: a seam named in a comment is no call.
    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // A case label of the dispatch switch, which names the owner it is for.
    private static readonly Regex CaseLabel = new(@"\bcase\s+ChannelKeyOwner\.(?<owner>\w+)\s*:",
        RegexOptions.CultureInvariant);

    // The end of a case: the first break or return after its label.
    private static readonly Regex CaseEnd = new(@"\b(?:break|return)\s*;", RegexOptions.CultureInvariant);

    // A call to a channel key seam, with the arguments it passes.
    private static readonly Regex SeamCall = new(@"\b(?<seam>Channel\w+Key)\s*\((?<args>[^;]*)\)\s*;",
        RegexOptions.CultureInvariant);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    // The cases of the switch, read narrowly: only "case ChannelKeyOwner.X:" labels count, each with the statements
    // up to its first break or return. Anything else in the file (the context, the window code a later task adds) is
    // never looked at, so it cannot break this test.
    private static List<(string Owner, string Body)> CasesOf(string source)
    {
        string code = Comment.Replace(source, "");
        var cases = new List<(string Owner, string Body)>();
        foreach (Match label in CaseLabel.Matches(code))
        {
            int start = label.Index + label.Length;
            Match end = CaseEnd.Match(code, start);
            string body = end.Success ? code.Substring(start, end.Index + end.Length - start) : code.Substring(start);
            cases.Add((label.Groups["owner"].Value, body));
        }
        return cases;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The contract between the DA40's G1000 window and the agent it injects.
///
/// These two files talk to each other by NAME across a WebSocket, so nothing in the
/// compiler notices when one side is renamed. Every fault guarded here has already
/// happened once:
///
///   - the window parsed the agent's answer by field COUNT, and the agent grew a field;
///   - the agent defined A.press TWICE, so the first one was silently unreachable;
///   - the window called a function the agent no longer exposed, and the key went dead
///     with no error anywhere.
///
/// So the shape of the answer, the names the window calls, and the absence of duplicate
/// definitions are all asserted here rather than found in the cockpit.
/// </summary>
public class CowsDA40G1000AgentContractTests
{
    private static string Agent()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources",
            "coherent-da40-g1000-agent.js");
        Assert.True(File.Exists(path), "Agent not copied to the output: " + path);
        return File.ReadAllText(path);
    }

    private static string Form()
    {
        // Walk up out of bin/<config>/<tfm> to the repository, then to the form. The form
        // is the OTHER half of the contract and there is no other way to compare them.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, "MSFSBlindAssist", "Forms", "DA40",
            "CowsDA40DisplayForm.cs");
        Assert.True(File.Exists(path), "Display form not found: " + path);
        return File.ReadAllText(path);
    }

    [Fact]
    public void EveryAgentFunctionTheWindowCallsExists()
    {
        string agent = Agent();
        string form = Form();

        // Every __MSFSBA_DA40G1000.<name>( the form invokes must be defined in the agent.
        var called = Regex.Matches(form, @"__MSFSBA_DA40G1000\.(\w+)\(")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(called);

        foreach (string name in called)
        {
            Assert.True(agent.Contains("A." + name + " = function", StringComparison.Ordinal),
                "The window calls " + name + "() but the agent does not define it.");
        }
    }

    [Fact]
    public void TheWindowsScrapeEntryPointExists()
    {
        Assert.Contains("window.__MSFSBA_DISP", Agent(), StringComparison.Ordinal);
        Assert.Contains("MSFSBA_DISP_INSTALLED", Agent(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoFunctionIsDefinedTwiceOnTheAgent()
    {
        // A.press was defined twice - once for softkeys, once for the bezel - and the
        // second silently replaced the first, so a whole branch of the agent had never run.
        var names = Regex.Matches(Agent(), @"^\s*A\.(\w+) = function", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();

        var duplicates = names.GroupBy(n => n)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Defined more than once on the agent, so only the last one exists: " +
            string.Join(", ", duplicates));
    }

    [Fact]
    public void TheStateAnswerCarriesFiveFields()
    {
        // "ok|cursor|view|focus|summary". The window splits on '|' and reads index 2 as the
        // view, 3 as the focused field and 4 onwards as the summary, so the agent growing
        // or losing a field silently shifts every one of them. It has grown once already.
        Assert.Contains("\"ok|\" + cursor + \"|\" + key + \"|\" + focus + \"|\" + summary",
            Agent(), StringComparison.Ordinal);
        Assert.Contains("if (parts.Length < 5)", Form(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCursorIsNotAnnouncedOnEveryField()
    {
        // "Cursor on." used to prefix every single field, which is four syllables of
        // nothing fourteen times down a setup page. The window says it on the TRANSITION.
        Assert.DoesNotContain("\"Cursor on. \" + modelSaid", Agent(), StringComparison.Ordinal);

        // ⚠️ AND COMPARED PER VIEW. Every view owns its own scroll controller, and the page
        // SELECTOR is a view opened by the very knob the pilot is turning - so an unscoped
        // comparison read the cursor as switching itself on and off while they changed
        // pages.
        //
        // ⚠️ THE FIRST FIX FOR THAT REQUIRED THE VIEW TO BE UNCHANGED, AND THAT SWALLOWED
        // THE ANNOUNCEMENT ENTIRELY on the first press after arriving somewhere - which is
        // the normal habit, go to the page and then arm - reported from the cockpit as the
        // arm/disarm message no longer being announced at all. Remembering the state PER
        // VIEW keeps the selector measured against the selector and the page against the
        // page, without discarding a real change.
        Assert.Contains("_cursorByView", Form(), StringComparison.Ordinal);
        Assert.DoesNotContain("if (view == _lastView && cursorOn != _lastCursorOn)", Form(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheSoftkeyRowPrefixMatchesWhatTheWindowLooksFor()
    {
        // The window finds pressable rows with a regex on "Softkey N:", which is the other
        // by-name contract between these two files.
        Assert.Contains("\"Softkey \" + key.index", Agent(), StringComparison.Ordinal);
        Assert.Contains("^Softkey (\\d{1,2}):", Form(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheAgentAsksTheInstrumentForTheCursorRatherThanTheStylesheet()
    {
        string agent = Agent();

        // The cursor had been read off CSS classes and was wrong on every page whose class
        // this reader had not met. It is the instrument's own scroll controller now.
        Assert.Contains("getIsScrollEnabled", agent, StringComparison.Ordinal);
        Assert.Contains("A.M.cursor = function", agent, StringComparison.Ordinal);

        // And the DOM reader survives ONLY as the fallback for a display whose instrument
        // element is not up yet - which is what the null answer means.
        Assert.Contains("if (modelCursor === null)", agent, StringComparison.Ordinal);
    }

    [Fact]
    public void BothControlFrameworksAreRead()
    {
        string agent = Agent();

        // The flight plan page and the checklist keep their controls in G1000UiControl, not
        // in the scroll controller, and reading only the latter reported both as empty.
        Assert.Contains("_UICONTROL_", agent, StringComparison.Ordinal);
        Assert.Contains("getFocusedIndex", agent, StringComparison.Ordinal);
        Assert.Contains("A.M.f2Say = function", agent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void AircraftOptionsPanelHoldsOnlyWhatTheG1000MenuDoesNot(DA40Variant variant)
    {
        // The MFD Engine page menu carries Failures Mode, State Saving, Engine Damage,
        // Realistic Parking Brake, Panel Shake, Steering Mode, Trim Speed and Prop Speed,
        // and the display window now reads and drives that menu. Those eight are therefore
        // duplicates and were removed; these two are not on the menu and have nowhere else
        // to live.
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Aircraft Options"];

        Assert.Equal(new[] { "DA40_OPT_TIMER_EXPIRED_SET", "DA40_OPT_KILL_FMA" }, controls.ToArray());
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheOptionsRemovedFromThePanelAreStillWatched(DA40Variant variant)
    {
        // Removing the CONTROLS must not remove the ability to HEAR a setting change on the
        // MFD. Every one of the eight is still a defined, announced variable.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (string key in new[]
        {
            "DA40_OPT_STATE_SAVING", "DA40_OPT_DAMAGE", "DA40_OPT_FAILURES_MODE",
            "DA40_OPT_REALISTIC_PARK_BRAKE", "DA40_OPT_WHEEL_ASSIST",
            "DA40_OPT_TRIM_SPEED", "DA40_OPT_SLOW_PROPS", "DA40_OPT_PANEL_SHAKE"
        })
        {
            Assert.True(vars.ContainsKey(key), key + " is no longer defined at all.");
            Assert.True(vars[key].IsAnnounced, key + " would change in silence.");
        }
    }
    /// <summary>
    /// ONE WORD FOR ONE IDEA: an unselectable thing is ", dimmed".
    ///
    /// The pilot's own ruling, given after this display had shipped ", not available",
    /// " (not available)" and a "Shown, not selectable:" heading for the same fact:
    /// "That's how the A380 MFD does things that are not selectable. It says dimmed.
    /// So, standardize that, please." A pilot who flies both aeroplanes must never meet
    /// a second spelling of it.
    ///
    /// The one survivor of the old phrase is A.M.goPage's RETURN CODE, which means a page
    /// key the display does not know — a different thing entirely, and read by the window
    /// rather than spoken.
    /// </summary>
    /// <summary>
    /// ⚠️ A ROW IS NEVER NAMED AFTER SOMETHING THAT IS NOT ON SCREEN, OR AFTER A NUMBER.
    ///
    /// The PFD's Hold At dialog draws EITHER a leg distance or a leg time and keeps both in
    /// the DOM. The hidden "4.0 NM" pair registers as the row's FIRST control, so the row
    /// was named after the branch the page is not drawing and the readout said "4.0: 1" -
    /// announcing a four-mile leg to a pilot holding for one minute. The same row then
    /// offered its minute digit as a name for the seconds digits, which names nothing.
    ///
    /// Both guards live in A.M.nameByRow and are checked here because the failure is
    /// invisible except on one dialog of one display.
    /// </summary>
    [Fact]
    public void ARowIsNamedOnlyByAVisibleWord()
    {
        string agent = string.Join(" ", Agent()
            .Split('\u000A')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // Not selectable: the knob cannot reach it, and here it is the hidden branch.
        Assert.Contains("if (!f || f.able === false) return false;", agent,
            StringComparison.Ordinal);
        // A value with no letter in it describes the thing it sits in, it does not name it.
        Assert.Contains("return /[A-Za-z]/.test(String(f.value || \"\"));", agent,
            StringComparison.Ordinal);
        // And the naming value goes through that test rather than round it.
        Assert.Contains("canName(fields[m]) ? fields[m].value : \"\"", agent,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠️ EACH BAR CHART HAS ITS OWN DELTA-FROM-PEAK AND THE FIRST IN THE DOM IS HIDDEN.
    ///
    /// On the XLS Engine page the EGT chart's block is display:none outside lean assist
    /// while the CHT chart's is on screen, so one querySelector read the dead copy and the
    /// visible number was never spoken - the same trap that had the CAS block reading off a
    /// hidden duplicate. Found by tools/coherent-coverage.js, not by a pilot.
    /// </summary>
    [Fact]
    public void BothTemperatureChartsReportTheirOwnDeltaFromPeak()
    {
        string agent = string.Join(" ", Agent()
            .Split('\u000A')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.Contains("[\".egt-bar-chart\", egtLabel], [\".cht-bar-chart\", chtLabel]", agent,
            StringComparison.Ordinal);
        Assert.DoesNotContain("temps.querySelector(\".bar-chart-delta-peak\")", agent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnselectableIsAlwaysSpokenAsDimmed()
    {
        // ⚠️ SCAN THE CODE, NOT THE COMMENTS. The comments deliberately QUOTE the retired
        // spellings so the next reader knows what was replaced and why, and a raw substring
        // scan therefore fails on the very documentation that records the rule.
        string agent = string.Join(" ", Agent()
            .Split('\u000A')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        foreach (var banned in new[] { "\", not available\"", "\" (not available)\"",
                                       "Shown, not selectable" })
        {
            Assert.False(agent.Contains(banned, StringComparison.Ordinal),
                "The display speaks " + banned + " where the app-wide word is \", dimmed\".");
        }

        // The ONE survivor is A.M.goPage's RETURN CODE — a page key the display does not
        // know, which is a different fact and is read by the window rather than spoken.
        Assert.Contains("if (!A.M.has(key)) return \"not available\"", agent,
            StringComparison.Ordinal);

        Assert.Contains("\", dimmed\"", agent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A GREYED-OUT SOFTKEY MUST SAY SO.
    ///
    /// The G1000 disables a softkey it cannot honour on the current page and marks it
    /// `text-disabled`. Measured live across the 17 real MFD pages: 22 labelled keys are
    /// dimmed at rest, and the Flight Plan Catalog — the page the documented SimBrief
    /// import workflow runs on — has NINE of its twelve dimmed until a flight is focused.
    /// Without this the pilot hears "Softkey 5: Activate", presses it, and gets silence
    /// with nothing to say why.
    ///
    /// A blank slot is deliberately NOT dimmed: no label means the key does nothing here
    /// at all, which is a different fact and already has its own word.
    /// </summary>
    [Fact]
    public void ADisabledSoftkeyIsReportedDimmed()
    {
        string agent = Agent();

        Assert.Contains("disabled: cls.indexOf(\"text-disabled\") >= 0", agent,
            StringComparison.Ordinal);
        Assert.Contains("key.disabled && key.label ? \", dimmed\" : \"\"", agent,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// WHETHER A GROUP BOX IS SELECTABLE IS A STRUCTURAL QUESTION, ASKED OF THE INSTRUMENT.
    ///
    /// ⚠️ NEVER "did anyone say the words". An earlier version compared a box's TEXT against
    /// the rows already built and marked TEN boxes on Aux System Setup dimmed — Date / Time
    /// and Display Units among them — every one of which the pilot can select perfectly
    /// well, because the field walk renders "Date: 10 - SEP - 26" while the box concatenates
    /// differently and the match simply failed. It scored BETTER on the coverage sweep than
    /// the version it replaced, which is exactly what made it look right. Telling a pilot
    /// they cannot reach something they can is worse than the gap it was closing.
    ///
    /// A.M.fields() reports each field's own groupbox title, straight from the view's scroll
    /// controller — so the answer comes from the instrument, not from string similarity.
    /// And a view that cannot be asked returns null, which marks NOTHING dimmed: a missing
    /// answer must never invent unselectable boxes.
    /// </summary>
    [Fact]
    public void GroupBoxSelectabilityComesFromTheViewNotFromTextMatching()
    {
        string agent = Agent();

        Assert.Contains("A.ownedGroupTitles = function ()", agent, StringComparison.Ordinal);
        Assert.Contains("A.M.fields()", agent, StringComparison.Ordinal);

        // Null means "could not ask", and must be the safe direction.
        int start = agent.IndexOf("A.ownedGroupTitles = function ()", StringComparison.Ordinal);
        int end = agent.IndexOf("A.groupboxLines = function", start, StringComparison.Ordinal);
        Assert.True(end > start, "ownedGroupTitles must sit above groupboxLines.");
        string body = agent.Substring(start, end - start);
        Assert.Contains("return null", body, StringComparison.Ordinal);

        // Dialogs must NOT pass an ownership set - see groupboxLines' own comment.
        Assert.Contains("A.groupboxLines(p, A.ownedGroupTitles())", agent,
            StringComparison.Ordinal);
        Assert.Contains("A.groupboxLines(d)", agent, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE PAGE IS READ ONCE, NOT THREE TIMES.
    ///
    /// A.rows() once ended with a pushUnreadBoxes() backstop, and Aux System Setup then came
    /// to 111 rows for about forty distinct facts: the field walk, "Page content" (which
    /// already covered every box), and the backstop re-emitting five of them a third time.
    /// Nearest Airports was worse — 114 rows with 36 duplicates, down to 73 with none.
    /// Removing it cost NOTHING in coverage (the sweep stayed at its 22 known overdraw
    /// false positives), because the dimmed marking moved into A.groupboxLines where the
    /// pilot reads the value.
    /// </summary>
    [Fact]
    public void TheBackstopThatReadEveryPageAThirdTimeIsGone()
    {
        string agent = Agent();

        Assert.DoesNotContain("A.pushUnreadBoxes = function", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("A.pushUnreadBoxes(rows)", agent, StringComparison.Ordinal);
    }
    /// <summary>
    /// NOTHING MAY TOUCH A.M BEFORE A.M EXISTS.
    ///
    /// ⚠️ THIS FAILURE IS SILENT AND LOOKS LIKE "MY CHANGE DID NOTHING". The agent is one
    /// script evaluated top to bottom, so an `A.M.foo = ...` written above `A.M = {}` throws
    /// "undefined is not an object (evaluating 'A.M')" at LOAD — and a load that throws
    /// leaves the PREVIOUS agent resident in the page. Every function still answers, every
    /// readout still works, and the edit simply never arrives. Paid for while moving the page
    /// title onto the instrument: three live sweeps in a row reported the identical old
    /// numbers before the injection result was read.
    ///
    /// A helper that NEEDS A.M belongs beside the rest of A.M, however far that is from its
    /// caller — calls resolve at run time, definitions do not.
    /// </summary>
    [Fact]
    public void NothingAssignsIntoTheModelBeforeItExists()
    {
        string[] lines = Agent().Split('\u000A');

        int created = -1;
        for (int i = 0; i < lines.Length && created < 0; i++)
        {
            if (lines[i].Contains("A.M = {}", StringComparison.Ordinal)) created = i;
        }

        Assert.True(created >= 0, "The agent no longer creates A.M; this guard needs updating.");

        for (int i = 0; i < created; i++)
        {
            string line = lines[i];
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            Assert.False(Regex.IsMatch(line, @"^\s*A\.M\.\w+\s*="),
                $"Line {i + 1} assigns into A.M before A.M is created on line {created + 1}: " +
                line.Trim());
        }
    }
    /// <summary>
    /// THE SOFTKEY-ROW SUMMARY MUST NOT BE COMMA-JOINED, BECAUSE A LABEL NOW CONTAINS A COMMA.
    ///
    /// A dimmed key reads "Activate, dimmed". Comma-joining the twelve labels turned the
    /// Flight Plan Catalog - nine of whose keys are dimmed until a flight is focused - into
    /// "New, dimmed, Activate, dimmed, Invert, dimmed, ...", in which "dimmed" reads as a key
    /// of its own and there is no telling where one label ends and the next begins.
    /// </summary>
    [Fact]
    public void TheSoftkeyRowSummaryIsNotCommaJoined()
    {
        string form = Form();

        Assert.DoesNotContain("string.Join(\", \", SoftkeyLabels", form, StringComparison.Ordinal);
        Assert.Contains("string.Join(\"; \", SoftkeyLabels", form, StringComparison.Ordinal);
    }



}

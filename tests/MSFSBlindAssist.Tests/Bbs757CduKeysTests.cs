using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757 CDU's keys. A name the cockpit does not have would be refused by the tablet
/// agent's whitelist ("bad-key") and the pilot's key would silently do nothing, so every name the
/// CDU window can send is checked against the agent's own list, read from the shipped script.
/// </summary>
public class Bbs757CduKeysTests
{
    private static IReadOnlyList<string> AgentKeyNames()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-tablet-agent.js"));
        var list = Regex.Match(agent, @"var KEY_NAMES = \[(.*?)\];", RegexOptions.Singleline);
        Assert.True(list.Success, "the agent's KEY_NAMES list was not found");
        return Regex.Matches(list.Groups[1].Value, "'([A-Z0-9]+)'").Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void The_cockpit_has_69_keys_per_cdu_and_the_agent_whitelists_exactly_them()
    {
        var agent = AgentKeyNames();

        Assert.Equal(69, Bbs757CduKeys.All.Count);
        Assert.Equal(69, Bbs757CduKeys.All.Distinct().Count());
        Assert.Equal(Bbs757CduKeys.All.OrderBy(k => k, StringComparer.Ordinal), agent.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData('a', "KEYA")]
    [InlineData('K', "KEYK")]
    [InlineData('z', "KEYZ")]
    [InlineData('0', "NUM0")]
    [InlineData('9', "NUM9")]
    [InlineData('.', "NUMPER")]
    [InlineData(' ', "KEYSP")]
    [InlineData('/', "KEYSLASH")]
    [InlineData('-', "NUMPLUS")]
    [InlineData('+', "NUMPLUS NUMPLUS")]   // the Boeing +/- key: "-" first, a second press makes it "+"
    public void A_typed_character_takes_its_cdu_keys(char c, string keys)
    {
        Assert.Equal(keys.Split(' '), Bbs757CduKeys.ForChar(c));
    }

    [Theory]
    [InlineData('*')]
    [InlineData('_')]
    [InlineData(',')]
    [InlineData('é')]   // é
    [InlineData('\n')]
    public void A_character_with_no_cdu_key_takes_none(char c)
    {
        Assert.Empty(Bbs757CduKeys.ForChar(c));
    }

    [Theory]
    [InlineData(Keys.I, "INIT")]
    [InlineData(Keys.R, "RTE")]
    [InlineData(Keys.D, "DEPARR")]
    [InlineData(Keys.T, "ATC")]
    [InlineData(Keys.V, "VNAV")]
    [InlineData(Keys.F, "FIX")]
    [InlineData(Keys.G, "LEGS")]
    [InlineData(Keys.H, "HOLD")]
    [InlineData(Keys.O, "FMCCOMM")]
    [InlineData(Keys.P, "PROG")]
    [InlineData(Keys.M, "MENU")]
    [InlineData(Keys.N, "NAVRAD")]
    [InlineData(Keys.E, "EXEC")]
    [InlineData(Keys.S, null)]      // Alt+S focuses the scratchpad box
    [InlineData(Keys.A, null)]      // the HS787's ALTN: the 757 has no such key
    [InlineData(Keys.C, null)]      // Alt+C is no page key: the window clears the scratchpad (ClearScratchpad)
    [InlineData(Keys.Home, null)]
    public void Alt_letters_press_the_hs787_windows_page_keys(Keys key, string? expected)
    {
        Assert.Equal(expected, Bbs757CduKeys.ForAltLetter(key));
    }

    [Fact]
    public void Line_select_keys_are_L1_to_L6_and_R1_to_R6()
    {
        Assert.Equal("L1", Bbs757CduKeys.Lsk(right: false, 1));
        Assert.Equal("R6", Bbs757CduKeys.Lsk(right: true, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757CduKeys.Lsk(right: false, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757CduKeys.Lsk(right: true, 7));
    }

    [Fact]
    public void Every_key_the_cdu_window_can_send_is_in_the_agents_whitelist()
    {
        var agent = AgentKeyNames().ToHashSet(StringComparer.Ordinal);
        var sendable = new List<string>();

        // The named keys (buttons, PREV/NEXT, EXEC, CLR, DEL) are the class's constants.
        sendable.AddRange(typeof(Bbs757CduKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!));
        for (int n = 1; n <= 6; n++)
        {
            sendable.Add(Bbs757CduKeys.Lsk(right: false, n));
            sendable.Add(Bbs757CduKeys.Lsk(right: true, n));
        }
        for (int c = 0; c <= 0x2FF; c++) sendable.AddRange(Bbs757CduKeys.ForChar((char)c));
        foreach (Keys k in Enum.GetValues<Keys>())
            if (Bbs757CduKeys.ForAltLetter(k) is { } page) sendable.Add(page);
        foreach (var scratchpad in new[] { null, "", "KATL" })
            foreach (var keysQueued in new[] { false, true })
                sendable.AddRange(Bbs757CduKeys.ClearScratchpad(scratchpad, keysQueued).Select(p => p.Key));

        Assert.Empty(sendable.Where(k => !agent.Contains(k)).Distinct());
        Assert.All(sendable, k => Assert.True(Bbs757CduKeys.IsKey(k), k));
    }

    // ---- Alt+C: clear the scratchpad (plan 09) ----------------------------------------------------

    private static readonly (string Key, int HoldMs)[] TwoLongClrs =
        { (Bbs757CduKeys.Clr, Bbs757CduKeys.ClearAllHoldMs), (Bbs757CduKeys.Clr, Bbs757CduKeys.ClearAllHoldMs) };

    [Theory]
    [InlineData("KATL", false)]
    [InlineData("INVALID ENTRY", false)]
    [InlineData("A", false)]
    [InlineData(" KATL ", false)]
    [InlineData("KATL", true)]
    public void A_scratchpad_with_text_takes_two_long_CLRs(string scratchpad, bool keysQueued)
    {
        // One CLR held 1.3 s clears the whole scratchpad 45 times in 60 and removes one character
        // otherwise (plan 08, 2026-10-26); a second long CLR always clears the rest.
        Assert.Equal(TwoLongClrs, Bbs757CduKeys.ClearScratchpad(scratchpad, keysQueued));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_scratchpad_not_known_yet_takes_two_long_CLRs(bool keysQueued)
    {
        // No screen read yet: it may hold anything, and a CLR on an empty scratchpad does nothing
        // (2026-10-28), so the second long CLR is harmless.
        Assert.Equal(TwoLongClrs, Bbs757CduKeys.ClearScratchpad(null, keysQueued));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t ")]
    public void An_empty_scratchpad_with_no_keys_queued_takes_one_DEL_at_the_ordinary_hold(string scratchpad)
    {
        // As the iFly's Alt+C: nothing to clear, so DEL (the CDU's delete mode).
        Assert.Equal(
            new[] { (Bbs757CduKeys.Del, CoherentBbs757TabletClient.KeyHoldMs) },
            Bbs757CduKeys.ClearScratchpad(scratchpad, keysQueued: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t ")]
    public void An_empty_looking_scratchpad_with_keys_still_queued_is_not_known_empty_and_takes_two_long_CLRs(string scratchpad)
    {
        // The screen is the state before the queued keys: the first long CLR of an earlier Alt+C empties it
        // about a second in, and an entry typed with Alt+S then Enter and cleared before its keys went out
        // still reads empty. DEL behind either would leave "DELETE" on the scratchpad, so only a scratchpad
        // that is empty with nothing queued gets DEL (review of plan 09 task 3).
        Assert.Equal(TwoLongClrs, Bbs757CduKeys.ClearScratchpad(scratchpad, keysQueued: true));
    }

    [Fact]
    public void The_long_CLR_hold_is_the_measured_1300_ms_and_within_what_the_client_allows()
    {
        Assert.Equal(1300, Bbs757CduKeys.ClearAllHoldMs);
        Assert.InRange(Bbs757CduKeys.ClearAllHoldMs, CoherentBbs757TabletClient.MinHoldMs, CoherentBbs757TabletClient.MaxHoldMs);
    }

    // ---- Ctrl+Shift+L / Ctrl+Shift+R: select a side (plan 09) --------------------------------------

    [Theory]
    [InlineData(Keys.L | Keys.Control | Keys.Shift, 1)]
    [InlineData(Keys.R | Keys.Control | Keys.Shift, 2)]
    public void Ctrl_Shift_L_and_R_select_the_captains_and_first_officers_cdu(Keys keyData, int side)
    {
        Assert.Equal(side, Bbs757CduKeys.SideFor(keyData));
    }

    [Theory]
    [InlineData(Keys.L)]
    [InlineData(Keys.L | Keys.Control)]
    [InlineData(Keys.L | Keys.Shift)]
    [InlineData(Keys.L | Keys.Alt)]
    [InlineData(Keys.L | Keys.Control | Keys.Shift | Keys.Alt)]
    [InlineData(Keys.R | Keys.Control)]
    [InlineData(Keys.R | Keys.Alt | Keys.Shift)]
    [InlineData(Keys.R | Keys.Control | Keys.Alt)]
    [InlineData(Keys.Tab | Keys.Control)]
    [InlineData(Keys.T | Keys.Control | Keys.Shift)]
    [InlineData(Keys.Control | Keys.Shift)]
    public void Any_other_combination_selects_no_side(Keys keyData)
    {
        Assert.Null(Bbs757CduKeys.SideFor(keyData));
    }

    [Fact]
    public void The_window_title_names_the_side()
    {
        Assert.Equal("BlueBird 757 CDU (Captain)", BBS757CduForm.TitleFor(1));
        Assert.Equal("BlueBird 757 CDU (First officer)", BBS757CduForm.TitleFor(2));
    }
}

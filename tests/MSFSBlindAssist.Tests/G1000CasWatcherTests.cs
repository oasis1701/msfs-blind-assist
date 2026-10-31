using System;
using System.Collections.Generic;
using System.IO;
using MSFSBlindAssist.Aircraft.G1000;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ A DARK SCREEN IS NOT A CLEARED MESSAGE (DA40G-31). Unpowered, the G1000 still scrapes as
/// "CAS messages: none", which the watcher took for every standing message clearing at each
/// shutdown. The agent now leads with "Display power: off", and the watcher forgets silently and
/// stays baselined, so the messages the G1000 raises at power-up are spoken. The rows are the
/// C172's PFD as measured on 2026-10-18.
/// </summary>
public class G1000CasWatcherTests
{
    private static readonly List<string> Dark = new()
    {
        "Display power: off", "CAS messages: none", "Autopilot: off", "Navigation source: not shown",
    };

    private static List<string> Powered(params string[] cas)
    {
        var rows = new List<string> { cas.Length == 0 ? "CAS messages: none" : $"CAS messages: {cas.Length}" };
        foreach (string c in cas) rows.Add("  " + c);
        rows.Add("Autopilot: off");
        return rows;
    }

    private static (G1000CasWatcher Watcher, GatedSpeechCapture Said) Armed()
    {
        var said = new GatedSpeechCapture();
        var watcher = new G1000CasWatcher("test", () => false);
        watcher.Arm(said);
        return (watcher, said);
    }

    [Fact]
    public void The_dark_row_is_recognised_and_nothing_else_is()
    {
        Assert.True(G1000CasWatcher.IsDisplayDark(Dark));
        Assert.False(G1000CasWatcher.IsDisplayDark(Powered("WARNING: LOW VOLTS")));
        Assert.False(G1000CasWatcher.IsDisplayDark(new[] { "  Display power: off" }));
    }

    [Fact]
    public void Powering_up_from_dark_speaks_what_the_g1000_raises()
    {
        var (watcher, said) = Armed();

        watcher.ProcessRows(Dark);
        watcher.ProcessRows(Powered("WARNING: LOW VOLTS", "WARNING: OIL PRESSURE", "Caution: LOW VACUUM"));

        Assert.Equal(new[] { "WARNING: LOW VOLTS. WARNING: OIL PRESSURE. Caution: LOW VACUUM." }, said.All);
    }

    [Fact]
    public void Going_dark_says_nothing_cleared()
    {
        var (watcher, said) = Armed();

        watcher.ProcessRows(Powered("WARNING: LOW VOLTS", "WARNING: OIL PRESSURE"));   // the baseline
        watcher.ProcessRows(Dark);

        Assert.Empty(said.All);
    }

    [Fact]
    public void A_message_that_clears_on_a_lit_screen_is_still_said()
    {
        var (watcher, said) = Armed();
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        watcher.Now = () => now;

        watcher.ProcessRows(Powered("WARNING: LOW VOLTS", "WARNING: OIL PRESSURE"));
        watcher.ProcessRows(Powered("WARNING: LOW VOLTS"));
        // Gone, but not yet for the clear hold (DA40G-32): a flicker would be back by now.
        Assert.Empty(said.All);

        now = now.AddMilliseconds(G1000CasWatcher.CasClearDebounce.ClearSettleMs);
        watcher.ProcessRows(Powered("WARNING: LOW VOLTS"));

        Assert.Equal(new[] { "OIL PRESSURE cleared" }, said.All);
    }

    // ⚠️ A REFUSED MODE PRESS IS SILENT ON THE FMA, so the aircraft asks the watcher whether the
    // FMA moved (MTL/VCRI, 2026-10-19: APR, NAV and AP presses refused by the GFC 700 said
    // nothing at all). The count moves only on a real change after the baseline, and the rows'
    // arrival time says whether the answer is fresh.
    [Fact]
    public void The_fma_change_count_moves_only_when_the_modes_do()
    {
        var (watcher, _) = Armed();
        var now = new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
        watcher.Now = () => now;

        static List<string> Fma(string modes) => new() { "CAS messages: none", "Autopilot: " + modes };

        watcher.ProcessRows(Fma("AP, HDG, ALT, target 1800 FT"));             // the baseline
        Assert.Equal(0, watcher.FmaChanges);
        Assert.Equal(now, watcher.LastRowsAt);

        now = now.AddSeconds(1.5);
        watcher.ProcessRows(Fma("AP, HDG, ALT, target 1800 FT"));
        Assert.Equal(0, watcher.FmaChanges);
        Assert.Equal(now, watcher.LastRowsAt);

        watcher.ProcessRows(Fma("AP, HDG, LOC armed, ALT, target 1800 FT"));
        Assert.Equal(1, watcher.FmaChanges);
    }

    [Fact]
    public void An_fma_change_in_the_quiet_period_still_counts()
    {
        // Unspoken while a flight load settles, but a mode pressed then must not read as refused.
        bool quiet = false;
        var said = new GatedSpeechCapture();
        var watcher = new G1000CasWatcher("test", () => quiet);
        watcher.Arm(said);

        watcher.ProcessRows(new() { "CAS messages: none", "Autopilot: AP, ROL, PIT" });   // the baseline
        quiet = true;
        watcher.ProcessRows(new() { "CAS messages: none", "Autopilot: AP, HDG, PIT" });

        Assert.Equal(1, watcher.FmaChanges);
        Assert.Empty(said.All);
    }

    [Fact]
    public void A_shutdown_and_restart_speaks_the_power_up_again()
    {
        var (watcher, said) = Armed();

        watcher.ProcessRows(Powered("WARNING: LOW VOLTS"));
        watcher.ProcessRows(Dark);
        watcher.ProcessRows(Powered("Caution: STBY BATT", "WARNING: LOW VOLTS"));

        Assert.Equal(new[] { "Caution: STBY BATT. WARNING: LOW VOLTS." }, said.All);
    }

    [Fact]
    public void The_first_lit_scrape_after_connecting_is_still_a_silent_baseline()
    {
        var (watcher, said) = Armed();

        watcher.ProcessRows(Powered("WARNING: LOW VOLTS"));

        Assert.Empty(said.All);
    }

    [Fact]
    public void The_quiet_period_after_a_load_records_without_speaking()
    {
        bool quiet = true;
        var said = new GatedSpeechCapture();
        var watcher = new G1000CasWatcher("test", () => quiet);
        watcher.Arm(said);

        watcher.ProcessRows(Dark);
        watcher.ProcessRows(Powered("WARNING: LOW VOLTS"));
        quiet = false;
        watcher.ProcessRows(Powered("WARNING: LOW VOLTS", "Caution: LOW VACUUM"));

        Assert.Equal(new[] { "Caution: LOW VACUUM." }, said.All);
    }

    /// <summary>
    /// Alt+E: what stands on the screen now, in the display's order, the baseline and the quiet
    /// period included (nothing is announced then, but asking still answers). A dark screen and a
    /// screen not yet read say so, never "No CAS messages".
    /// </summary>
    [Fact]
    public void Alt_E_lists_the_messages_on_the_screen_now()
    {
        var (watcher, _) = Armed();
        Assert.Equal("CAS messages not read yet.", watcher.DescribeCurrentMessages());

        watcher.ProcessRows(Powered("WARNING: LOW VOLTS", "WARNING: OIL PRESSURE", "Caution: LOW VACUUM"));   // the baseline
        Assert.Equal("3 CAS messages. WARNING: LOW VOLTS. WARNING: OIL PRESSURE. Caution: LOW VACUUM.",
            watcher.DescribeCurrentMessages());

        watcher.ProcessRows(Powered("Caution: LOW VACUUM"));
        Assert.Equal("1 CAS message. Caution: LOW VACUUM.", watcher.DescribeCurrentMessages());

        watcher.ProcessRows(Powered());
        Assert.Equal("No CAS messages.", watcher.DescribeCurrentMessages());

        watcher.ProcessRows(Dark);
        Assert.Equal("The PFD is off.", watcher.DescribeCurrentMessages());
    }

    [Fact]
    public void Alt_E_answers_during_the_quiet_period_after_a_load()
    {
        var said = new GatedSpeechCapture();
        var watcher = new G1000CasWatcher("test", () => true);
        watcher.Arm(said);

        watcher.ProcessRows(Powered("WARNING: LOW VOLTS"));

        Assert.Empty(said.All);
        Assert.Equal("1 CAS message. WARNING: LOW VOLTS.", watcher.DescribeCurrentMessages());
    }

    private static List<string> Nav(string source, string modes) => new()
    {
        "CAS messages: none", "Autopilot: " + modes, "Navigation source: " + source,
    };

    // ⚠️ THE G1000 SWITCHES THE CDI BY ITSELF (ESGG ILS Y 21, 2026-10-26): GPS to LOC1 before
    // the final fix dropped GPS mode to ROL, and only "Autopilot ROL" was said.
    [Fact]
    public void The_g1000_switching_the_source_is_said_before_the_mode_it_dropped()
    {
        var (watcher, said) = Armed();

        watcher.ProcessRows(Nav("GPS, TERM", "AP, GPS, FLC, ALTS armed"));   // the baseline
        watcher.ProcessRows(Nav("LOC1", "AP, ROL, FLC, ALTS armed"));

        Assert.Equal(new[] { "Navigation source LOC1. Autopilot ROL." }, said.All);
    }

    // The DA40's GFC 700 panel labels its mode buttons from the FMA, and a press's own row arrives
    // before the FMA is next read, so the label was computed from the FMA before the press and
    // stayed that way (review, 2026-10-27). Each read now says so, with its FMA already current.
    [Fact]
    public void Each_read_is_announced_with_its_fma_already_current()
    {
        var (watcher, _) = Armed();
        var fmaAtEachRead = new List<string>();
        watcher.RowsRead += () => fmaAtEachRead.Add(watcher.CurrentFma);

        watcher.ProcessRows(Nav("GPS, TERM", "AP, GPS, ALT"));   // the baseline
        watcher.ProcessRows(Nav("GPS, TERM", "AP, HDG, ALT"));

        Assert.Equal(new[] { "AP, GPS, ALT", "AP, HDG, ALT" }, fmaAtEachRead);
    }

    [Fact]
    public void Sensitivity_and_suspend_are_not_a_source_change()
    {
        var (watcher, said) = Armed();

        watcher.ProcessRows(Nav("GPS, TERM", "off"));
        watcher.ProcessRows(Nav("GPS, APR", "off"));
        watcher.ProcessRows(Nav("GPS, APR, SUSPENDED", "off"));

        Assert.Empty(said.All);
    }

    [Fact]
    public void A_source_that_drops_out_and_comes_back_is_not_a_change()
    {
        var (watcher, said) = Armed();

        watcher.ProcessRows(Nav("GPS, ENR", "off"));
        watcher.ProcessRows(Nav("not shown", "off"));
        watcher.ProcessRows(Nav("GPS, ENR", "off"));

        Assert.Empty(said.All);
    }

    [Fact]
    public void Powering_up_does_not_announce_the_source()
    {
        var (watcher, said) = Armed();

        watcher.ProcessRows(Dark);
        watcher.ProcessRows(Nav("GPS, ENR", "off"));

        Assert.Empty(said.All);
    }

    [Fact]
    public void The_pilots_own_cdi_press_is_left_to_the_windows_read_back()
    {
        var (watcher, said) = Armed();
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        watcher.Now = () => now;

        watcher.ProcessRows(Nav("GPS, ENR", "off"));
        watcher.NoteSoftkeyPress();
        now = now.AddMilliseconds(1500);
        watcher.ProcessRows(Nav("LOC1", "off"));
        Assert.Empty(said.All);

        now = now.AddMilliseconds(G1000CasWatcher.SoftkeyQuietMs);
        watcher.ProcessRows(Nav("GPS, TERM", "off"));
        Assert.Equal(new[] { "Navigation source GPS." }, said.All);
    }

    [Fact]
    public void The_agent_writes_the_navigation_source_row_the_watcher_reads()
    {
        string agent = File.ReadAllText(Path.Combine(RepoRoot(),
            "MSFSBlindAssist", "Resources", "coherent-da40-g1000-agent.js"));

        Assert.Contains("rows.push(\"Navigation source: \" + (n.source || \"not shown\")", agent);
    }

    [Fact]
    public void The_agent_writes_the_row_the_watcher_reads()
    {
        string agent = File.ReadAllText(Path.Combine(RepoRoot(),
            "MSFSBlindAssist", "Resources", "coherent-da40-g1000-agent.js"));

        Assert.Contains($"rows.unshift(\"{G1000CasWatcher.DisplayDarkRow}\")", agent);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException("MSFSBlindAssist.sln not found");
    }
}

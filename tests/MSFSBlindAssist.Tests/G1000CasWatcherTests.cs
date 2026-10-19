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

        watcher.ProcessRows(Powered("WARNING: LOW VOLTS", "WARNING: OIL PRESSURE"));
        watcher.ProcessRows(Powered("WARNING: LOW VOLTS"));

        Assert.Equal(new[] { "OIL PRESSURE cleared" }, said.All);
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

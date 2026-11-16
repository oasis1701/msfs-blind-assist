using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect.BBS757;
using R =MSFSBlindAssist.Aircraft.BBS757.Bbs757Readouts;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The seams the 757 gap plans build on (docs/b757.md, "Seams"): the optional
/// partial-method hooks on <see cref="BBS757Definition"/>, the Ctrl+M rows they extend and the shared
/// variable keys. An unimplemented hook compiles away, so with none implemented the definition must
/// behave as it did before the seams.
/// </summary>
public class Bbs757SeamsTests
{
    private static readonly BBS757Definition Def = new();

    /// <summary>Every hook later plans implement, by name (the contract in plan 01, Task 1).</summary>
    public static readonly string[] HookNames =
    {
        // Plan 03, then 12
        "AddOverheadVariables", "AddOverheadPanels", "HandleOverheadTypedText", "HandleOverheadSet", "FormatOverheadDisplay",
        // Plan 04
        "AddRadioVariables", "AddRadioPanels", "HandleRadioSet", "HandleRadioTypedText", "ProcessRadioUpdate",
        "HandleRadioHotkey", "FormatRadioDisplay", "ResetRadios", "CloseRadioWindows",
        // Plan 05
        "ProcessAutoflightUpdate", "ResetAutoflight",
        // Plan 07, then 11
        "AddEfbVariables", "AddEfbPanels", "ProcessEfbUpdate", "FormatEfbDisplay",
        // Plan 06, then 14
        "HandleReadoutHotkey", "OnOwnSquawkSet", "OnOwnAltimeterSet",
        // Plan 15
        "AddThrustVariables", "AddThrustPanels", "HandleThrustSet", "HandleThrustTypedText", "FormatThrustDisplay", "ResetThrust",
        // Plan 10
        "AddFirstOfficerVariables",
        // Plan 13
        "OnOwnWrite", "OnConnectionBatch", "ResetAnnouncements",
        // Ctrl+M rows (plans 09 and 13)
        "AddCduMonitorRows", "AddCockpitMonitorRows",
    };

    [Fact]
    public void Hooks_file_declares_every_seam()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Aircraft", "BBS757Definition.Hooks.cs"));

        var missing = HookNames.Where(name => !Regex.IsMatch(source, $@"partial void\s+{name}\s*\(")).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Monitor_rows_begin_with_todays()
    {
        // The hooks' rows (plan 09's CDU messages, plan 13's lights and switches) only ever follow.
        var today = MonitorRowBuilder.Build(Def.GetVariables());
        Assert.Equal(today, Def.MonitorRows().Take(today.Count));
    }

    [Fact]
    public void Shared_keys_are_distinct()
    {
        string[] keys = { Bbs757SharedKeys.ExtPwrOn, Bbs757SharedKeys.ExtPwrAvail, Bbs757SharedKeys.GpuConnected };

        Assert.Equal(new[] { "BBS757_EXT_PWR_ON", "BBS757_EXT_PWR_AVAIL", "BBS757_GPU_CONNECTED" }, keys);
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, k => Assert.StartsWith("BBS757_", k, StringComparison.Ordinal));
    }

    [Fact]
    public void Display_override_unchanged_without_hooks()
    {
        // A characterization guard: the display seam runs first, so with no hook implemented the
        // squawk (BCO16) and the flap handle still read as they did before it.
        Assert.True(Def.TryGetDisplayOverride(Bbs757Squawk.CodeKey, 0x4743, out string squawk));
        Assert.Equal("4743", squawk);
        Assert.True(Def.TryGetDisplayOverride(R.FlapsHandleKey, 2, out string flaps));
        Assert.Equal("5", flaps);
    }

    // ---- The definition owns the tablet client (Task 3) -------------------------------------------

    /// <summary>A client that never starts: no socket, no loop.</summary>
    private static CoherentBbs757TabletClient IdleClient() => new(() => "");

    [Fact]
    public void Tablet_client_is_created_once()
    {
        int made = 0;
        var def = new BBS757Definition { TabletClientFactory = () => { made++; return IdleClient(); } };

        var first = def.EnsureTabletClient();
        var second = def.EnsureTabletClient();

        Assert.Same(first, second);
        Assert.Equal(1, made);
    }

    [Fact]
    public void Switching_away_disposes_it_after_the_windows()
    {
        // The switch calls OnSwitchedAway before it disposes MainForm's 757 windows, so the definition
        // closes the windows on its client itself, first: none is left running on a disposed client.
        var client = IdleClient();
        var def = new BBS757Definition { TabletClientFactory = () => client };
        var order = new List<string>();
        def.EnsureTabletClient();
        def.CloseTabletWindows = () => order.Add(client.IsDisposed ? "windows closed after the client" : "windows closed");

        def.OnSwitchedAway();

        Assert.Equal(new[] { "windows closed" }, order);
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public void Switching_away_makes_no_client()
    {
        int made = 0;
        var def = new BBS757Definition { TabletClientFactory = () => { made++; return IdleClient(); } };

        def.OnSwitchedAway();

        Assert.Equal(0, made);
    }

    [Fact]
    public async Task A_late_call_after_switching_away_makes_no_client()
    {
        // Nothing used the client before the pilot left the 757. A feature that calls in afterwards
        // gets a disposed client that never started, never a new socket claiming the tablet view for
        // a profile that is gone ([DBG-9]); its page service answers null (false) to everything.
        int made = 0;
        var def = new BBS757Definition { TabletClientFactory = () => { made++; return IdleClient(); } };
        const int fo = Bbs757CduPages.FirstOfficer;
        var ct = CancellationToken.None;

        def.OnSwitchedAway();
        var client = def.EnsureTabletClient();
        var pages = def.CduPages;

        Assert.Equal(0, made);
        Assert.True(client.IsDisposed);
        Assert.Same(client, def.EnsureTabletClient());
        Assert.Null(await pages.ReadAsync(fo, ct));
        Assert.Null(await pages.ShowPageAsync(fo, Bbs757CduKeys.NavRad, _ => true, ct));
        Assert.False(await pages.WipeScratchpadAsync(fo, ct));
        Assert.Null(await pages.EnterAsync(fo, "113.90", "L1", ct));
        Assert.Null(await pages.PressKeysAsync(fo, new[] { "KEYDEL", "L1" }, ct));
        Assert.Equal(0, made);
    }

    // ---- The map's guard fields (Task 6)----------------------------------------------------------

    [Fact]
    public void Loader_reads_guard_fields()
    {
        // The emergency lights switch sits behind a guard: its guard var was measured live at 0 when closed,
        // and closing it writes 1 (ARMED) to the switch. A control without a guard carries none.
        const string json = """
            {
              "controls": [
                {
                  "node": "B757_OVHD_EMER_LIGHTS_SWITCH", "section": "Overhead", "panel": "Emergency Lights",
                  "label": "Emergency Lights", "kind": "switch", "state_var": "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK",
                  "guard": "B757_OVHD_SWITCH_GAURD", "guard_closed": 0, "close_sets": 1
                },
                {
                  "node": "B757_OVHD_BATTERY_BUTTON", "section": "Overhead", "panel": "Electrical",
                  "label": "Battery", "kind": "toggle", "state_var": "B757_OVHD_BATTERY_BUTTON"
                }
              ]
            }
            """;

        var map = Bbs757ControlMap.Parse(json);

        var guarded = map.Controls[0];
        Assert.Equal("B757_OVHD_SWITCH_GAURD", guarded.Guard);
        Assert.Equal(0.0, guarded.GuardClosed);
        Assert.Equal(1.0, guarded.CloseSets);

        var plain = map.Controls[1];
        Assert.Null(plain.Guard);
        Assert.Null(plain.GuardClosed);
        Assert.Null(plain.CloseSets);
    }

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}

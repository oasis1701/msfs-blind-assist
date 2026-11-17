using System.Globalization;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The parts of the 757 EFB code that need no simulator and have no test class of their own: how a click sequence goes to the
/// agent, the SimBrief ID rules, BlueBird's work folder and the values read from it (the SimBrief ID, the unit system, the
/// parking brake mode, the installed AIRAC cycle), and the EFB client and its agent with nothing to reach. (The Navigraph
/// sign-in helpers moved with the flow to <c>Bbs757NavigraphSignInTests</c>.)
/// </summary>
public class Bbs757EfbTests
{
    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("12a")]
    [InlineData(" 189")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\u0661\u0662")]   // Arabic-Indic digits: char.IsDigit, but not ASCII digits
    [InlineData("12345678901")]    // longer than BlueBird's int can hold
    public void A_simbrief_id_that_is_not_plain_digits_is_refused(string text)
    {
        Assert.False(Bbs757EfbLayout.IsSimBriefId(text));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("189")]
    [InlineData("2147483647")]
    public void Plain_digit_simbrief_ids_are_accepted(string text)
    {
        Assert.True(Bbs757EfbLayout.IsSimBriefId(text));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("2147483648")]
    public void A_simbrief_id_blueBird_cannot_store_is_refused(string text)
    {
        Assert.False(Bbs757EfbLayout.IsSimBriefId(text));
    }

    // ---- The parking brake mode ------------------------------------------------------------

    [Theory]
    [InlineData("{\"parking_brake_realistic\":true}", true)]
    [InlineData("{\"parking_brake_realistic\":false}", false)]
    [InlineData("{\"hoppie_logon_code\":\"NOT-A-REAL-CODE\",\"parking_brake_realistic\":true}", true)]
    public void ReadParkingBrakeRealistic_reads_only_the_brake_key(string json, bool expected)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Equal(expected, Bbs757WorkFolder.ReadParkingBrakeRealistic(tmp.Path));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"parking_brake_realistic\":1}")]
    [InlineData("not json")]
    public void ReadParkingBrakeRealistic_is_null_for_anything_but_a_bool(string json)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Null(Bbs757WorkFolder.ReadParkingBrakeRealistic(tmp.Path));
    }

    // ---- The positions and the sequence the agent takes ------------------------------------

    [Fact]
    public void Home_sits_at_its_measured_position_and_the_chocks_have_their_variable()
    {
        Assert.Equal(new System.Drawing.Point(19, 62), Bbs757EfbScreen.HomeButton);   // the window's own Home button, on every app page
        Assert.Equal("BBS757_vis_chocks", Bbs757EfbLayout.ChocksVar);
    }

    [Fact]
    public void A_sequence_goes_to_the_agent_as_invariant_integer_triples()
    {
        InCulture("de-DE", () =>
        {
            Assert.Equal("[[19,62,1500],[1073,190,1500]]",
                Bbs757EfbLayout.ToJson(new (EfbPoint, int)[] { (new EfbPoint(19, 62), 1500), (new EfbPoint(1073, 190), 1500) }));
        });
        InCulture("sv-SE", () =>
        {
            Assert.Equal("[[1291,337,0]]", Bbs757EfbLayout.ToJson(new (EfbPoint, int)[] { (new EfbPoint(1291, 337), 0) }));
        });
    }

    [Fact]
    public void The_planned_time_of_a_sequence_is_the_sum_of_its_delays()
    {
        Assert.Equal(1750, Bbs757EfbLayout.TotalMs(new (EfbPoint, int)[] { (new EfbPoint(19, 62), 1500), (new EfbPoint(1291, 337), 250) }));
        Assert.Equal(0, Bbs757EfbLayout.TotalMs(Array.Empty<(EfbPoint, int)>()));
    }

    // ---- BlueBird's work folder -------------------------------------------

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msfsba-bbs757-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string Sub(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    private static void WriteSettingsAt(string work, DateTime writtenUtc)
    {
        Directory.CreateDirectory(work);
        string path = Path.Combine(work, "efb_settings.json");
        File.WriteAllText(path, "{\"simbrief_id\":-1}");
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    [Fact]
    public void Find_picks_the_root_whose_settings_were_written_last()
    {
        using var tmp = new TempDir();
        string older = tmp.Sub("older"), newer = tmp.Sub("newer"), bare = tmp.Sub("bare"), missing = tmp.Sub("missing");
        WriteSettingsAt(older, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        WriteSettingsAt(newer, new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc));
        Directory.CreateDirectory(bare);   // a folder without BlueBird's settings file

        Assert.Equal(newer, Bbs757WorkFolder.Find(new[] { older, newer, bare }));
        Assert.Equal(newer, Bbs757WorkFolder.Find(new[] { newer, older }));
        Assert.Equal(newer, Bbs757WorkFolder.Find(new[] { missing, bare, older, newer }));
        Assert.Equal(older, Bbs757WorkFolder.Find(new[] { bare, older }));
    }

    [Fact]
    public void With_no_settings_file_anywhere_Find_takes_the_first_root_that_exists()
    {
        using var tmp = new TempDir();
        string missing = tmp.Sub("missing"), second = tmp.Sub("second"), third = tmp.Sub("third");
        Directory.CreateDirectory(second);
        Directory.CreateDirectory(third);

        Assert.Equal(second, Bbs757WorkFolder.Find(new[] { missing, second, third }));
        Assert.Equal(third, Bbs757WorkFolder.Find(new[] { third, second }));
        Assert.Null(Bbs757WorkFolder.Find(new[] { missing }));
        Assert.Null(Bbs757WorkFolder.Find(Array.Empty<string>()));
    }

    [Fact]
    public void The_default_roots_are_the_six_install_locations_in_order()
    {
        var roots = Bbs757WorkFolder.DefaultRoots();

        Assert.Equal(6, roots.Count);
        Assert.EndsWith(@"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2020\bluebirdsim-aircraft-757-200\work", roots[0]);
        Assert.EndsWith(@"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2024\bluebirdsim-aircraft-757-200\work", roots[1]);
        Assert.EndsWith(@"Microsoft Flight Simulator 2024\WASM\MSFS2020\bluebirdsim-aircraft-757-200\work", roots[2]);
        Assert.EndsWith(@"Microsoft Flight Simulator 2024\WASM\MSFS2024\bluebirdsim-aircraft-757-200\work", roots[3]);
        Assert.EndsWith(@"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\packages\bluebirdsim-aircraft-757-200\work", roots[4]);
        Assert.EndsWith(@"Microsoft Flight Simulator\Packages\bluebirdsim-aircraft-757-200\work", roots[5]);
    }

    private static void WriteCycle(string work, string json)
    {
        string nav = Path.Combine(work, "NavigationData");
        Directory.CreateDirectory(nav);
        File.WriteAllText(Path.Combine(nav, "cycle.json"), json);
    }

    [Fact]
    public void ReadInstalledCycle_reads_the_cycle_from_blueBirds_cycle_file()
    {
        using var tmp = new TempDir();
        WriteCycle(tmp.Path, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-cycle.json")));

        Assert.True(Bbs757WorkFolder.HasCycleFile(tmp.Path));
        Assert.Equal(2610, Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"cycle\":\"abcd\"}")]
    [InlineData("{\"cycle\":\"26100\"}")]
    [InlineData("{\"cycle\":\"2615\"}")]    // no 15th cycle in a year
    [InlineData("{\"cycle\":\"2600\"}")]
    [InlineData("{\"cycle\":\"0999\"}")]    // four digits, but under 1000: no year's cycle
    [InlineData("{\"cycle\":\"0901\"}")]    // under 1000 with a cycle number that exists: only the lower bound refuses it
    [InlineData("{\"cycle\":10001}")]       // five digits
    [InlineData("{\"cycle\":true}")]
    [InlineData("{\"cycle\":{\"id\":2610}}")]
    [InlineData("{\"cycle\":26.10}")]
    public void ReadInstalledCycle_returns_null_for_garbage(string json)
    {
        using var tmp = new TempDir();
        WriteCycle(tmp.Path, json);

        Assert.True(Bbs757WorkFolder.HasCycleFile(tmp.Path));
        Assert.Null(Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
    }

    [Fact]
    public void ReadInstalledCycle_takes_a_numeric_cycle_too()
    {
        using var tmp = new TempDir();
        WriteCycle(tmp.Path, "{\"cycle\":2610}");

        Assert.Equal(2610, Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
    }

    [Theory]
    [InlineData("{\"cycle\":\"2614\"}", 2614)]   // a year's 14th cycle (2020 had one, its last 2014)
    [InlineData("{\"cycle\":2614}", 2614)]
    [InlineData("{\"cycle\":\"2601\"}", 2601)]
    public void ReadInstalledCycle_takes_every_cycle_of_a_year(string json, int cycle)
    {
        using var tmp = new TempDir();
        WriteCycle(tmp.Path, json);

        Assert.Equal(cycle, Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
    }

    [Fact]
    public void With_no_cycle_file_nothing_is_installed()
    {
        using var tmp = new TempDir();

        Assert.False(Bbs757WorkFolder.HasCycleFile(tmp.Path));
        Assert.Null(Bbs757WorkFolder.ReadInstalledCycle(tmp.Path));
        Assert.Null(Bbs757WorkFolder.ReadInstalledCycle(tmp.Sub("no-such-folder")));
    }

    private static void WriteSettings(string work, string json) => File.WriteAllText(Path.Combine(work, "efb_settings.json"), json);

    [Theory]
    [InlineData("{\"simbrief_id\":-1}")]
    [InlineData("{\"simbrief_id\":0}")]
    [InlineData("{}")]
    [InlineData("{\"simbrief_id\":null}")]
    [InlineData("{\"simbrief_id\":\"abc\"}")]
    [InlineData("{\"simbrief_id\":12.5}")]
    [InlineData("not json")]
    [InlineData("[123456]")]
    public void ReadSimBriefId_maps_minus_one_and_garbage_to_null(string json)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Null(Bbs757WorkFolder.ReadSimBriefId(tmp.Path));
    }

    [Fact]
    public void ReadSimBriefId_reads_only_the_simbrief_id()
    {
        using var tmp = new TempDir();
        // A made-up ID beside made-up credentials: only the ID comes back.
        WriteSettings(tmp.Path,
            "{\"hoppie_logon_code\":\"NOT-A-REAL-CODE\",\"sayintentions_acars_key\":\"NOT-A-REAL-KEY\",\"simbrief_id\":123456,\"tiller_realistic\":true}");

        Assert.Equal(123456L, Bbs757WorkFolder.ReadSimBriefId(tmp.Path));
    }

    [Fact]
    public void ReadSimBriefId_with_no_settings_file_is_null()
    {
        using var tmp = new TempDir();

        Assert.Null(Bbs757WorkFolder.ReadSimBriefId(tmp.Path));
    }

    // ---- client with nothing to reach ---------------------------------------

    [Fact]
    public async Task With_no_agent_the_efb_client_is_not_ready_and_runs_nothing()
    {
        using var client = new CoherentBbs757EfbClient(() => "");
        client.Start();

        Assert.False(client.IsReady);
        Assert.Equal(Bbs757EfbRunResult.Unreachable,
            await client.RunAsync(new[] { (new EfbPoint(19, 62), 250) }, CancellationToken.None));
        Assert.Equal(Bbs757EfbRunResult.Unreachable, await client.RunAsync(Array.Empty<(EfbPoint, int)>(), CancellationToken.None));
    }

    [Theory]
    [InlineData("no-canvas: size 1200x900", Bbs757EfbRunResult.LayoutUnknown)]
    [InlineData("no-canvas: canvas", Bbs757EfbRunResult.Unreachable)]
    [InlineData("no-canvas: handlers", Bbs757EfbRunResult.Unreachable)]
    [InlineData("busy", Bbs757EfbRunResult.NotFinished)]
    [InlineData("bad-seq", Bbs757EfbRunResult.NotFinished)]
    public void An_agent_refusal_maps_to_what_the_window_says(string error, Bbs757EfbRunResult expected)
    {
        Assert.Equal(expected, CoherentBbs757EfbClient.ResultForRefusal(error));
    }

    [Theory]
    [InlineData("MSFSBA_BBS757_EFB_NOT_READY:size 1200x900", true)]
    [InlineData("MSFSBA_BBS757_EFB_NOT_READY:canvas", false)]
    [InlineData("MSFSBA_BBS757_EFB_NOT_READY:Coherent", false)]
    [InlineData("no-canvas: size 1397x900", true)]
    public void Only_a_canvas_of_another_size_means_an_unknown_layout(string answer, bool expected)
    {
        Assert.Equal(expected, CoherentBbs757EfbClient.IsSizeProblem(answer));
    }

    [Fact]
    public void The_efb_agent_is_es5_and_answers_with_the_clients_markers()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));

        Assert.Contains("'" + CoherentBbs757EfbClient.InstallMarker + "'", agent);
        Assert.Contains("window.__MSFSBA_BBS757EFB = A;", agent);
        Assert.Contains("MSFSBA_BBS757_EFB_NOT_READY:", agent);
        // ES5 only (Coherent GT is an old Chromium): checked on the code with its comments removed.
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");
        Assert.DoesNotContain("=>", code);
        Assert.DoesNotContain("`", code);
        Assert.DoesNotMatch(@"\b(let|const|class)\s", code);
        Assert.DoesNotContain("Array.from", code);
        Assert.DoesNotContain(".includes(", code);
        Assert.StartsWith("(function () {", code.TrimStart());
        Assert.Contains("} catch (e) {", code);
        Assert.DoesNotContain("cancelled", agent);   // US spelling
    }

    [Fact]
    public void The_efb_agent_runs_a_click_anywhere_on_the_tablet()
    {
        // The owner's ruling of 2026-10-29: nothing refuses a press, the AIRAC page's UPDATE/REMOVE spot included. Agent versions 3 to
        // 6 refused any click near that spot unless the run carried the nav data update's page guards; the refusal, the spot's constants
        // and the run guards are gone (version 7), and the client asks for that version.
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");

        Assert.DoesNotContain("unguarded-update", agent);   // not even a comment about it
        Assert.DoesNotContain("UPDATE_", code);             // the spot: UPDATE_X, UPDATE_Y, UPDATE_HALF_W, UPDATE_HALF_H
        Assert.DoesNotContain("touchesUpdateSpot", code);
        Assert.DoesNotContain("updateGuarded", code);
        foreach (string guard in new[] { "notBeforeLoadMs", "minAgeMs", "samePageAsMs", "onceKey", "guardProblem" })
            Assert.DoesNotContain(guard, code);

        // The only reasons left to refuse a run are the ones about the run itself, never about where it clicks: a drag's point off the
        // canvas ("bad-point") cannot be dragged at all, and "error: " is a throw before its run began.
        var refusals = System.Text.RegularExpressions.Regex.Matches(code, @"refuse\('([^']*)'")
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "bad-point", "bad-seq", "busy", "error: ", "no-canvas: ", "unknown-run" }, refusals);

        Assert.Equal(11, CoherentBbs757EfbClient.AgentVersion);
        Assert.Contains("var VERSION = " + CoherentBbs757EfbClient.AgentVersion.ToString(CultureInfo.InvariantCulture) + ";", code);
    }

    [Fact]
    public void The_efb_client_logs_no_count_of_a_runs_clicks()
    {
        // The final review's M1 (D2, B757-29): a typing run's clicks are keys, so how many of a run's clicks went out before it stopped
        // gives away the length of what was typed (a SimBrief ID is DEL and its digits). The client's run lines say why a run stopped,
        // never how far it got. Read from the source: the log has no seam the tests may set (VP-1).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string[] lines = File.ReadAllLines(Path.Combine(dir!.FullName, "MSFSBlindAssist", "SimConnect", "BBS757", "CoherentBbs757EfbClient.cs"));

        var logs = lines.Where(l => l.Contains("Log.", StringComparison.Ordinal)).ToList();
        Assert.Contains(logs, l => l.Contains("EFB sequence stopped", StringComparison.Ordinal));
        Assert.Contains(logs, l => l.Contains("EFB sequence still running", StringComparison.Ordinal));
        Assert.All(logs, l => Assert.DoesNotMatch(@"\.(done|n)\b", l));
    }

    [Fact]
    public void The_efb_agent_scrolls_by_dragging_not_by_the_wheel()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");
        Assert.Contains("A.drag = function (x, y0, y1)", code);
        Assert.Contains("return refuse('busy')", code);
        Assert.DoesNotContain("A.wheel", code);
        Assert.DoesNotContain("onMouseWheel", code);
        Assert.Contains("8: drag(x, y0, y1) replaces wheel", agent);
    }

    [Fact]
    public void The_efb_agent_answers_a_drag_with_its_run_so_the_app_waits_for_it()
    {
        // The final review's I7: the app waited a fixed 1,500 ms for a drag that runs on the page's timers, so the tap after it could be
        // refused as busy. The drag now answers as clickSeq does, and the client waits for that run by its status.
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");
        Assert.DoesNotContain("'started'", code);
        Assert.Contains("return { ok: true, id: run.id, n: run.n, done: run.done, totalMs: total, error: run.error };", code);
        Assert.Contains("9: drag answers with its run", agent);

        Assert.Equal((7, 1400), CoherentBbs757EfbClient.DragRun("{\"ok\":true,\"id\":7,\"n\":22,\"done\":2,\"totalMs\":1400,\"error\":null}"));
        Assert.Equal((8, CoherentBbs757EfbClient.DragPlannedMs), CoherentBbs757EfbClient.DragRun("{\"ok\":true,\"id\":8,\"n\":22,\"done\":2,\"error\":null}"));
        Assert.Null(CoherentBbs757EfbClient.DragRun("{\"ok\":false,\"error\":\"busy\"}"));
        Assert.Null(CoherentBbs757EfbClient.DragRun("{\"ok\":true,\"id\":9,\"n\":22,\"done\":0,\"running\":false,\"error\":\"error: boom\"}"));
        Assert.Null(CoherentBbs757EfbClient.DragRun("\"started\""));   // an older agent's answer
    }

    [Fact]
    public void The_efb_agent_eases_a_drag_out_so_the_page_stops_where_it_let_go()
    {
        // Measured live, 2026-11-07: BlueBird's scrollbox flings on when the button comes up by about the square of the last step, the
        // 400 ms hold before it notwithstanding: 20 even steps of 5 px scrolled Settings, Aircraft 125.7 px, a move to the same place just
        // before the release changed nothing (version 10), and the eased drag below scrolled 100 px then 0.98 px on (766 px: 7.5 px on).
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");
        Assert.Matches(@"var t = 1 - k / DRAG_MOVES;\s*lastY = y0 \+ \(y1 - y0\) \* \(1 - t \* t\);", code);
        Assert.DoesNotContain("(y1 - y0) * k / DRAG_MOVES", code);   // the even steps that flung the page
        Assert.Contains("11: a drag eases out to a stop", agent);
    }

    [Fact]
    public void A_throw_at_the_start_of_a_drag_ends_its_run_and_lets_go_only_after_the_press()
    {
        // The final review's M7: a throw in the first mouse move or OnMouseDown left the run "running", so the agent answered busy for
        // about 6 s. The start is now its own try: the run is failed (not running), and the button is let go only once OnMouseDown was
        // called (a throw in the first move, before any press, lets nothing go).
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");
        Assert.Matches(@"try \{\s*c\.onMouseMove\(ev\(y0\)\);\s*down = true;\s*c\.OnMouseDown\(ev\(y0\)\);\s*\} catch \(e\) \{\s*fail\(e\);\s*return runInfo\(run\);", code);
        Assert.Matches(@"run\.letGo = function \(\) \{\s*run\.letGo = null;\s*if \(!down\) return;", code);
        Assert.Matches(@"var fail = function \(e\) \{[^}]*run\.running = false;", code);
    }

    [Fact]
    public void The_efb_agent_lets_go_of_the_mouse_when_a_drag_is_stopped_or_fails()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-efb-agent.js"));
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");
        // The drag marks itself as holding the button, and the one place a run stops (cancel, a new run) and the failure path
        // both call it; the release itself is the mouse up at the last position.
        Assert.Contains("run.letGo = function ()", code);
        Assert.Contains("c.OnMouseUp(ev(lastY))", code);
        Assert.Matches(@"function stopRun\(run, reason\) \{[^}]*if \(run\.letGo\) run\.letGo\(\);", code);
        Assert.Matches(@"var fail = function \(e\) \{[^}]*if \(run\.letGo\) run\.letGo\(\);", code);
        Assert.Matches(@"var release = function \(\) \{[^}]*if \(run\.letGo\) run\.letGo\(\);", code);
    }

    // ---- weight units -------------------------------------------------------

    [Fact]
    public void ReadUnits_reads_the_units_field_from_a_settings_file_of_blueBirds_shape()
    {
        using var tmp = new TempDir();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-efb-settings.json"), Path.Combine(tmp.Path, "efb_settings.json"));

        Assert.Equal(Bbs757WeightUnits.Pounds, Bbs757WorkFolder.ReadUnits(tmp.Path));
        Assert.Equal(123456L, Bbs757WorkFolder.ReadSimBriefId(tmp.Path));
    }

    [Theory]
    [InlineData("{\"units\":0}", Bbs757WeightUnits.Kilograms)]   // METRIC
    [InlineData("{\"units\":1}", Bbs757WeightUnits.Pounds)]      // IMPERIAL
    [InlineData("{\"hoppie_logon_code\":\"NOT-A-REAL-CODE\",\"units\":0,\"sayintentions_acars_key\":\"NOT-A-REAL-KEY\"}", Bbs757WeightUnits.Kilograms)]
    public void ReadUnits_maps_metric_to_kilograms_and_imperial_to_pounds(string json, Bbs757WeightUnits expected)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Equal(expected, Bbs757WorkFolder.ReadUnits(tmp.Path));
    }

    [Theory]
    [InlineData("{\"units\":2}")]
    [InlineData("{\"units\":-1}")]
    [InlineData("{\"units\":\"1\"}")]
    [InlineData("{\"units\":true}")]
    [InlineData("{\"units\":1.5}")]
    [InlineData("{\"units\":null}")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void ReadUnits_returns_null_for_anything_else(string json)
    {
        using var tmp = new TempDir();
        WriteSettings(tmp.Path, json);

        Assert.Null(Bbs757WorkFolder.ReadUnits(tmp.Path));
    }

    [Fact]
    public void ReadUnits_with_no_settings_file_is_null()
    {
        using var tmp = new TempDir();

        Assert.Null(Bbs757WorkFolder.ReadUnits(tmp.Path));
    }
}

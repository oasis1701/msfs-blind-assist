using System.Text.Json;
using MSFSBlindAssist.Models;

namespace MSFSBlindAssist.Services;

/// <summary>
/// "Load latest SimBrief plan" on the sim tablet (MSFS 2024's own EFB): the pilot's latest OFP,
/// fetched by MSFSBA, typed into the tablet's Flight Planner exactly as a pilot would, through its
/// "Enter route string" box, then the plan's runways picked on its runway dropdowns. Sending the
/// route to the avionics stays the pilot's own press (the Route page's Send menu), so the route can
/// be reviewed first.
///
/// The tablet has no SimBrief link of its own; Navigraph's SimBrief tablet app is an add-on this
/// does not need. Every step is one call into the reader (<c>coherent-sim-tablet-agent.js</c>,
/// <c>A.route.*</c>), synchronous in the page; the WAITS are here, because the tablet applies a typed
/// value on its next tick and loads airports asynchronously, and because the client re-installs the
/// reader on an eval timeout, which would drop a timer chain kept inside the page.
/// </summary>
public sealed class SimTabletSimBriefImport
{
    /// <summary>The reader's window global.</summary>
    public const string AgentGlobal = "__MSFSBA_SIM_TABLET";

    /// <summary>The idx of the reader's synthetic "Load latest SimBrief plan" button (A.SIMBRIEF_IDX).</summary>
    public const int ActionIdx = 990001;

    internal const int PollMs = 300;
    internal const int OpenTries = 15;     // 4.5 s for the Flight Planner and its Route panel to show
    internal const int SaveTries = 10;     // 3 s for SAVE to enable once the box holds the text
    internal const int LoadTries = 30;     // 9 s for both airports to load
    internal const int RunwayTries = 15;   // 4.5 s for a runway pick to show

    internal const string NotConnected = "The sim tablet is not connected.";

    private readonly Func<string, Task<string>> _eval;
    private readonly Func<int, CancellationToken, Task> _delay;

    /// <param name="eval">Runs one expression in the tablet's view and returns its value ("" when it cannot).</param>
    /// <param name="delay">The wait between polls; tests pass one that returns at once.</param>
    public SimTabletSimBriefImport(Func<string, Task<string>> eval, Func<int, CancellationToken, Task>? delay = null)
    {
        _eval = eval;
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
    }

    public sealed record Result(bool Success, string Message);

    public async Task<Result> RunAsync(SimBriefOFP ofp, CancellationToken ct = default)
    {
        string? text = SimTabletRoute.Build(ofp);
        if (text == null) return new Result(false, "Your SimBrief plan names no departure or arrival airport.");
        string origin = ofp.OriginIcao.Trim().ToUpperInvariant();
        string dest = ofp.DestIcao.Trim().ToUpperInvariant();

        // 1. The Flight Planner, on its Route panel.
        bool open = false;
        for (int i = 0; i < OpenTries && !open; i++)
        {
            var r = await Call("route.open()");
            if (r == null) return new Result(false, NotConnected);
            if (!r.Ok) return new Result(false, r.Error == "no-flight-planner"
                ? "The sim tablet has no Flight Planner on its dock."
                : "The sim tablet's Flight Planner could not be opened.");
            open = r.Done;
            if (!open) await _delay(PollMs, ct);
        }
        if (!open) return new Result(false, "The sim tablet's Flight Planner did not open.");

        // 2. Type the route into "Enter route string".
        var entered = await Call("route.enter(" + JsonSerializer.Serialize(text) + ")");
        if (entered == null) return new Result(false, NotConnected);
        if (!entered.Ok) return new Result(false, entered.Error == "locked"
            ? "The sim tablet's flight plan is locked, so a route cannot be entered now."
            : "The sim tablet's route box could not be opened.");

        // 3. SAVE, which the tablet enables once its box has taken the text. When the box already
        //    held this route (the plan is loaded) SAVE never enables, so the reader closed the box
        //    and said unchanged: there is nothing to save.
        var notes = new List<string>();
        if (entered.Unchanged)
        {
            notes.Add("The Flight Planner already had this route.");
        }
        else
        {
            bool saved = false;
            for (int i = 0; i < SaveTries && !saved; i++)
            {
                await _delay(PollMs, ct);
                var r = await Call("route.save()");
                if (r == null) return new Result(false, NotConnected);
                if (r.Ok) saved = true;
                else if (r.Error != "save-disabled") break;
            }
            if (!saved)
            {
                await Call("route.closeBox()");   // leave no popup open over the Route page
                return new Result(false, "The sim tablet did not take the route.");
            }
        }

        // 4. Both airports loaded.
        SimTabletRoute.RouteState? st = null;
        for (int i = 0; i < LoadTries; i++)
        {
            await _delay(PollMs, ct);
            st = await State();
            if (st != null && SimTabletRoute.HasAirports(st, origin, dest)) break;
            st = null;
        }
        if (st == null) return new Result(false, $"The sim tablet did not load the route from {origin} to {dest}.");

        // 5. The plan's runways. The tablet picks its own on loading, and may pick a procedure for
        //    the runway set here, which the summary then says.
        st = await SetRunway(0, ofp.OriginRunway, origin, st, notes, ct) ?? st;
        st = await SetRunway(1, ofp.DestRunway, dest, st, notes, ct) ?? st;

        var final = await State() ?? st;
        return new Result(true, SimTabletRoute.Summary(final, notes));
    }

    private async Task<SimTabletRoute.RouteState?> SetRunway(int which, string planned, string icao,
        SimTabletRoute.RouteState st, List<string> notes, CancellationToken ct)
    {
        string want = SimTabletRoute.RunwayKey(planned);
        if (want.Length == 0 || which >= st.airports.Count) return null;
        if (st.airports[which].runwayKey == want) return null;

        // The runway list can lag the airport by a tick.
        for (int i = 0; i < RunwayTries && st.airports[which].runways.Count == 0; i++)
        {
            await _delay(PollMs, ct);
            st = await State() ?? st;
            if (which >= st.airports.Count) return null;
        }
        if (!st.airports[which].runways.Exists(r => SimTabletRoute.RunwayKey(r) == want))
        {
            notes.Add($"SimBrief planned runway {planned.Trim()} at {icao}, which the tablet does not list.");
            return st;
        }

        var r = await Call("route.setRunway(" + which + "," + JsonSerializer.Serialize(planned) + ")");
        if (r == null || !r.Ok)
        {
            notes.Add($"The tablet did not take runway {planned.Trim()} at {icao}.");
            return st;
        }
        for (int i = 0; i < RunwayTries; i++)
        {
            await _delay(PollMs, ct);
            var now = await State();
            if (now != null && which < now.airports.Count && now.airports[which].runwayKey == want) return now;
        }
        notes.Add($"The tablet did not take runway {planned.Trim()} at {icao}.");
        return st;
    }

    private async Task<SimTabletRoute.RouteState?> State() =>
        SimTabletRoute.ParseState(await _eval($"window.{AgentGlobal} ? {AgentGlobal}.route.state() : ''"));

    private sealed record Step(bool Ok, bool Done, string Error, bool Unchanged);

    /// <summary>One reader call; null when the tablet did not answer at all.</summary>
    private async Task<Step?> Call(string member)
    {
        string raw = await _eval($"window.{AgentGlobal} ? {AgentGlobal}.{member} : ''");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            bool ok = root.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
            bool done = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
            string err = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
            bool unchanged = root.TryGetProperty("unchanged", out var u) && u.ValueKind == JsonValueKind.True;
            return new Step(ok, done, err, unchanged);
        }
        catch (JsonException) { return null; }
    }
}

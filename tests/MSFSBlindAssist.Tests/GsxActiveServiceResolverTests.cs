using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// GsxService.ApplyServices/RecomputeTooltip feed GsxActiveServiceResolver directly.
/// It derives the "active" (State == "performing") service set AccessGSXForm's Active
/// Services combo lists, and resolves which one governs LastTooltip when the pilot has
/// (or hasn't) picked one — docs/gsx.md's documented "selecting a service chooses which
/// active row drives the tooltip" behaviour. Internal type, reached via
/// InternalsVisibleTo (Properties/InternalsVisibleTo.cs) — GsxService itself needs a
/// window handle and cannot be constructed in a unit test.
/// </summary>
public class GsxActiveServiceResolverTests
{
    private static GsxServiceState Svc(
        string id,
        string state,
        string display = "",
        string stateText = "",
        string progressText = "") =>
        new()
        {
            Id = id,
            State = state,
            DisplayName = display,
            StateText = stateText,
            ProgressText = progressText,
        };

    // ── NameOf ───────────────────────────────────────────────────────────

    [Fact]
    public void NameOf_prefers_DisplayName_over_Id()
    {
        var s = Svc("Deboarding", "performing", display: "Deboard");
        Assert.Equal("Deboard", GsxActiveServiceResolver.NameOf(s));
    }

    [Fact]
    public void NameOf_falls_back_to_Id_when_DisplayName_is_blank()
    {
        var s = Svc("Refueling", "performing", display: "");
        Assert.Equal("Refueling", GsxActiveServiceResolver.NameOf(s));
    }

    // ── ActiveNames ──────────────────────────────────────────────────────

    [Fact]
    public void ActiveNames_includes_only_performing_services()
    {
        var services = new[]
        {
            Svc("Boarding", "performing", display: "Board"),
            Svc("Catering", "available"),
            Svc("Refueling", "performing", display: "Refuel"),
            Svc("Jetway", "completed"),
        };

        var active = GsxActiveServiceResolver.ActiveNames(services);

        Assert.Equal(new[] { "Board", "Refuel" }, active);
    }

    [Fact]
    public void ActiveNames_is_empty_when_nothing_is_performing()
    {
        var services = new[] { Svc("Catering", "available"), Svc("Jetway", "completed") };
        Assert.Empty(GsxActiveServiceResolver.ActiveNames(services));
    }

    [Fact]
    public void ActiveNames_preserves_the_services_arrays_own_order()
    {
        var services = new[]
        {
            Svc("C", "performing", display: "Charlie"),
            Svc("A", "performing", display: "Alpha"),
            Svc("B", "performing", display: "Bravo"),
        };

        Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, GsxActiveServiceResolver.ActiveNames(services));
    }

    // ── ResolveGoverning ─────────────────────────────────────────────────

    [Fact]
    public void ResolveGoverning_returns_the_selected_service_when_it_is_active()
    {
        var boarding = Svc("Boarding", "performing", display: "Board");
        var services = new[] { boarding, Svc("Refueling", "performing", display: "Refuel") };

        var governing = GsxActiveServiceResolver.ResolveGoverning(services, "Refuel");

        Assert.Same(services[1], governing);
    }

    [Fact]
    public void ResolveGoverning_falls_back_to_first_active_when_selection_is_no_longer_active()
    {
        var services = new[]
        {
            Svc("Boarding", "completed", display: "Board"),
            Svc("Refueling", "performing", display: "Refuel"),
        };

        // "Board" finished (state is no longer "performing") -- a stale selection
        // must not strand the readout on a service that's done.
        var governing = GsxActiveServiceResolver.ResolveGoverning(services, "Board");

        Assert.Same(services[1], governing);
    }

    [Fact]
    public void ResolveGoverning_returns_the_first_active_service_when_nothing_is_selected()
    {
        var services = new[]
        {
            Svc("Boarding", "performing", display: "Board"),
            Svc("Refueling", "performing", display: "Refuel"),
        };

        Assert.Same(services[0], GsxActiveServiceResolver.ResolveGoverning(services, null));
    }

    [Fact]
    public void ResolveGoverning_returns_null_when_nothing_is_active()
    {
        var services = new[] { Svc("Catering", "available") };
        Assert.Null(GsxActiveServiceResolver.ResolveGoverning(services, "Catering"));
        Assert.Null(GsxActiveServiceResolver.ResolveGoverning(services, null));
    }

    [Fact]
    public void ResolveGoverning_matches_the_selection_case_insensitively()
    {
        var refuel = Svc("Refueling", "performing", display: "Refuel");
        var services = new[] { refuel };

        Assert.Same(refuel, GsxActiveServiceResolver.ResolveGoverning(services, "REFUEL"));
    }

    // ── ComposeTooltip ───────────────────────────────────────────────────

    [Fact]
    public void ComposeTooltip_uses_StateText_when_GSX_published_one()
    {
        var s = Svc("Deboarding", "performing", display: "Deboard", stateText: "Deboarding service is being performed");
        Assert.Equal("Deboarding service is being performed", GsxActiveServiceResolver.ComposeTooltip(s));
    }

    [Fact]
    public void ComposeTooltip_falls_back_to_the_service_name_when_StateText_is_blank()
    {
        var s = Svc("Refueling", "performing", display: "Refuel", stateText: "");
        Assert.Equal("Refuel", GsxActiveServiceResolver.ComposeTooltip(s));
    }

    [Fact]
    public void ComposeTooltip_appends_ProgressText_in_parentheses_when_present()
    {
        var s = Svc("Deboarding", "performing", stateText: "Deboarding in progress", progressText: "181/181");
        Assert.Equal("Deboarding in progress (181/181)", GsxActiveServiceResolver.ComposeTooltip(s));
    }

    [Fact]
    public void ComposeTooltip_has_no_trailing_parentheses_when_ProgressText_is_blank()
    {
        var s = Svc("Deboarding", "performing", stateText: "Deboarding in progress", progressText: "");
        Assert.Equal("Deboarding in progress", GsxActiveServiceResolver.ComposeTooltip(s));
    }

    [Fact]
    public void ComposeTooltip_prefers_GSXs_own_statusText_over_progressText()
    {
        var s = new GsxServiceState
        {
            Id = "Deboarding", DisplayName = "Deboard", State = "performing",
            StateText = "Deboarding service is being performed",
            StatusText = "bus in position\npax 181/186\nbags 100%",
            ProgressText = "181/181",
        };

        Assert.Equal("Deboarding service is being performed (bus in position, pax 181/186, bags 100%)",
                     GsxActiveServiceResolver.ComposeTooltip(s));
    }

    [Fact]
    public void ComposeTooltip_never_renders_the_misleading_bare_n_over_n()
    {
        // The real captured row: progress.current/total is 181/181 while
        // detail.pax is 181 of 186. Rendered bare, "(181/181)" tells a blind
        // pilot deboarding finished with five passengers still on board.
        var services = GsxServiceState.ParseList(Fixture("gsx-services.json"));
        var deboarding = Assert.Single(services, s => s.Id == "Deboarding");

        string tooltip = GsxActiveServiceResolver.ComposeTooltip(deboarding);

        Assert.DoesNotContain("181/181", tooltip, StringComparison.Ordinal);
        Assert.Contains("181/186", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeTooltip_composes_from_typed_detail_when_GSX_sent_no_statusText()
    {
        var s = new GsxServiceState
        {
            Id = "Boarding", DisplayName = "Board", State = "performing",
            StateText = "Boarding service is being performed",
            PaxDone = 40, PaxTotal = 186, BagsPercent = 25,
            ProgressText = "40/40",
        };

        Assert.Equal("Boarding service is being performed (pax 40/186, bags 25%)",
                     GsxActiveServiceResolver.ComposeTooltip(s));
    }

    // ── MessageText (GSX's idle tooltip slot) ────────────────────────────

    [Fact]
    public void MessageText_reads_the_object_shape_GSX_actually_publishes()
    {
        // The live shape. Reading this slot as a bare string returned "" for
        // every idle moment — parked, pre-departure, cruise, and after every
        // service completes — which is precisely when it is the only tooltip
        // source there is.
        Assert.Equal("Boarding will start shortly",
            GsxActiveServiceResolver.MessageText(
                Json("""{"text":"Boarding will start shortly","visible":true}""")));
    }

    [Fact]
    public void MessageText_honours_visible_exactly_as_GSXs_own_client_does()
    {
        Assert.Equal("", GsxActiveServiceResolver.MessageText(
            Json("""{"text":"stale banner","visible":false}""")));
        Assert.Equal("", GsxActiveServiceResolver.MessageText(Json("""{"text":"no flag at all"}""")));
    }

    [Fact]
    public void MessageText_matches_the_captured_snapshot_slot()
    {
        var snapshot = Fixture("gsx-snapshot.json");
        Assert.True(snapshot.TryGetProperty("message", out var message));
        Assert.Equal(System.Text.Json.JsonValueKind.Object, message.ValueKind);   // not a string
        Assert.Equal("", GsxActiveServiceResolver.MessageText(message));          // visible:false
    }

    [Fact]
    public void MessageText_degrades_on_anything_else_rather_than_throwing()
    {
        Assert.Equal("", GsxActiveServiceResolver.MessageText(Json("null")));
        Assert.Equal("", GsxActiveServiceResolver.MessageText(Json("[]")));
        Assert.Equal("", GsxActiveServiceResolver.MessageText(Json("""{"visible":true}""")));
        Assert.Equal("", GsxActiveServiceResolver.MessageText(Json("""{"text":42,"visible":true}""")));
        Assert.Equal("", GsxActiveServiceResolver.MessageText(default));
        // A bare string is still accepted should GSX ever simplify the slot.
        Assert.Equal("plain", GsxActiveServiceResolver.MessageText(Json("\"plain\"")));
    }

    private static System.Text.Json.JsonElement Json(string json)
        => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    private static System.Text.Json.JsonElement Fixture(string name)
        => Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));
}

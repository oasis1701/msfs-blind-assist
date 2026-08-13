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
}

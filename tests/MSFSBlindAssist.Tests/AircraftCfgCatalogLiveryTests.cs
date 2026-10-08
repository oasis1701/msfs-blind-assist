using System;
using System.Collections.Generic;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins how a title that lives in a separate LIVERY package (an aircraft.cfg carrying
/// <c>[VARIATION] base_container</c>) is attributed to the AIRFRAME package it layers on — the
/// folder MSFS keys the per-package "work" storage by, and so the only folder a PMDG variant's
/// <c>&lt;family&gt;_Options.ini</c> can ever be found under. Before this, a third-party livery
/// resolved to its own package folder and the SDK broadcast check reported the configuration
/// file missing on every flight of that livery.
/// </summary>
public class AircraftCfgCatalogLiveryTests
{
    [Fact]
    public void An_airframe_cfg_has_no_base_container()
    {
        var lines = new[]
        {
            "[VERSION]",
            "major = 1",
            "[GENERAL]",
            "icao_type_designator = \"B738\"",
            "[FLTSIM.0]",
            "title = \"PMDG 737-800 PMDG House\"",
        };

        Assert.Null(AircraftCfgCatalog.ParseBaseContainer(lines));
    }

    [Fact]
    public void A_livery_cfg_base_container_is_read_with_its_quotes_stripped()
    {
        var lines = new[]
        {
            "[VARIATION]",
            "base_container = \"..\\PMDG 737-800\"",
            "[FLTSIM.0]",
            "title = \"PMDG 737-800 United Airlines\"",
        };

        Assert.Equal("..\\PMDG 737-800", AircraftCfgCatalog.ParseBaseContainer(lines));
    }

    [Fact]
    public void A_livery_title_resolves_to_the_airframe_package_not_its_own()
    {
        var airframes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PMDG 737-800"] = "pmdg-aircraft-738",
        };

        string resolved = AircraftCfgCatalog.ResolveAirframePackageFolder(
            "flightsimto-united-738", "..\\PMDG 737-800", airframes);

        Assert.Equal("pmdg-aircraft-738", resolved);
    }

    [Fact]
    public void An_airframe_title_resolves_to_its_own_package()
    {
        var airframes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PMDG 737-800"] = "pmdg-aircraft-738",
        };

        Assert.Equal("pmdg-aircraft-738",
            AircraftCfgCatalog.ResolveAirframePackageFolder("pmdg-aircraft-738", null, airframes));
    }

    [Fact]
    public void A_livery_whose_airframe_is_not_installed_falls_back_to_its_own_package()
    {
        var airframes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("orphan-livery-738",
            AircraftCfgCatalog.ResolveAirframePackageFolder("orphan-livery-738", "..\\PMDG 737-800", airframes));
    }

    [Fact]
    public void Base_container_leaf_matching_ignores_separator_style_and_case()
    {
        var airframes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PMDG 777-300ER"] = "pmdg-aircraft-77w",
        };

        Assert.Equal("pmdg-aircraft-77w",
            AircraftCfgCatalog.ResolveAirframePackageFolder("some-livery", "../pmdg 777-300er/", airframes));
    }
}

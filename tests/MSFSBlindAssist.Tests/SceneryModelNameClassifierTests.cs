// Every row is a real model name measured in an installed package on 2026-09-06 or 2026-09-20.
using System.Reflection;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryModelNameClassifierTests
{
    [Theory]
    [InlineData("KTIW_Cessna_Service_Hanger", "KTIW", FeatureKind.Hangar, "Cessna Service Hangar")]
    [InlineData("KTIW_ATP_Hanger", "KTIW", FeatureKind.Hangar, "ATP Hangar")]
    [InlineData("KTIW_Pavco_Hanger", "KTIW", FeatureKind.Hangar, "Pavco Hangar")]
    [InlineData("KTIW_Narrows_Aviation_Hangar_Large_1", "KTIW", FeatureKind.Hangar, "Narrows Aviation Hangar Large")]
    [InlineData("KTIW_Hangar_09_B", "KTIW", FeatureKind.Hangar, "Hangar 9")]
    [InlineData("KTIW_Hangar_09B_1", "KTIW", FeatureKind.Hangar, "Hangar 9B")]
    [InlineData("KTIW_Hangar_09", "KTIW", FeatureKind.Hangar, "Hangar 9")]
    [InlineData("KJAC_Hangar_1", "KJAC", FeatureKind.Hangar, "Hangar 1")]
    [InlineData("KJAC_Hangar_2", "KJAC", FeatureKind.Hangar, "Hangar 2")]
    [InlineData("KTIW_Outskirt_Hangars_A", "KTIW", FeatureKind.Hangar, "Outskirt Hangars A")]
    [InlineData("KTIW_hangar_blue_octogon", "KTIW", FeatureKind.Hangar, "Hangar Blue Octogon")]
    [InlineData("Hangar_04", "KTIW", FeatureKind.Hangar, "Hangar 4")]
    [InlineData("Control_Tower_1", "KTIW", FeatureKind.Tower, "Control Tower")]
    [InlineData("KJAC_Tower", "KJAC", FeatureKind.Tower, "Tower")]
    [InlineData("tower_01", "KATL", FeatureKind.Tower, "Tower")]
    [InlineData("Fueltank", "KTIW", FeatureKind.Fuel, "Fuel")]
    [InlineData("Airport_Office_1", "KTIW", FeatureKind.Office, "Airport Office")]
    [InlineData("HubCafe_1", "KTIW", FeatureKind.Office, "Hub Cafe")]
    [InlineData("concourse_a_02", "KATL", FeatureKind.Concourse, "Concourse A")]
    [InlineData("concourse_a_interface_12m_b36", "KATL", FeatureKind.Concourse, "Concourse A")]
    [InlineData("concourse_t_canopy_01", "KATL", FeatureKind.Concourse, "Concourse T")]
    [InlineData("northwestern_cargo_01", "KATL", FeatureKind.Cargo, "Northwestern Cargo")]
    [InlineData("southern_hangar_01", "KATL", FeatureKind.Hangar, "Southern Hangar")]
    public void Classifies_measured_model_names(string model, string icao, FeatureKind kind, string name)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        Assert.NotNull(c);
        Assert.Equal(kind, c!.Kind);
        Assert.Equal(name, c.Name);
    }

    [Theory]
    [InlineData("KTIW_Fence2")]
    [InlineData("concourse_a_aircon4")]
    [InlineData("terminal_light1")]
    [InlineData("ground_terminal_light_01")]
    [InlineData("concourse_e_rooflight01")]
    [InlineData("concourse_t_carparks_02")]
    [InlineData("concourse_t_pedestrian_crossing01")]
    [InlineData("jetway_base")]
    [InlineData("safegate01")]
    [InlineData("KATL2020_truck_fuel")]
    [InlineData("KATL2020_cargovan")]
    [InlineData("KATL2020_cargo_loader_01")]
    [InlineData("KJAC_Vehicles_Fuel_Truck_JHA")]
    [InlineData("KTIW_Bridge1")]
    [InlineData("KTIW_Silo")]
    [InlineData("KTIW_Pylon")]
    [InlineData("ViewingPlatform1")]
    [InlineData("gates")]
    [InlineData("fence_black")]
    [InlineData("")]
    public void Drops_noise_and_unclassifiable_names(string model)
        => Assert.Null(SceneryModelNameClassifier.Classify(model, "KTIW"));

    // Measured 2026-09-20 in installed packages: every one of these was classified as an airport
    // feature, spoken ("Passing Iby Ramp Fire Connector, on the left") and offered as a routable Place.
    [Theory]
    [InlineData("iby_cargo_dolly_lg", "KPDX")] [InlineData("iby_cargo_train_01", "KPDX")]
    [InlineData("kpdx_ramp_cargo_unmarked", "KPDX")] [InlineData("kpdx_ramp_cargo_wrapped", "KPDX")] [InlineData("kpdx_ramp_cargo_strapped", "KPDX")]
    [InlineData("iby_deice_sm", "KPDX")] [InlineData("iby_ramp_fire_connector", "KPDX")]
    [InlineData("katl471_cargo_dolly3", "KATL")] [InlineData("katl76_portable_fire_extinguisher", "KATL")]
    [InlineData("katl535_water_tank", "KATL")] [InlineData("katl55_tower_parking_guard", "KATL")]
    [InlineData("Lg-ANORTH_CARGO_RAMP-Gl_Text", "KATL")]
    [InlineData("ini_GSE_FuelPumper_Menzies", "EGSS")] [InlineData("iniscene-lib-cargo-containers-cargo-jet", "EGSS")]
    [InlineData("iniscene-egss_Control_Tower_INT", "EGSS")] [InlineData("EGSS_Ryanair_Hangar_Interior", "EGSS")]
    [InlineData("EGSS_Main_Terminal_Interior_clutter", "EGSS")] [InlineData("FBKDEN_GroundTowerInterior", "KDEN")]
    [InlineData("EDDF_Equipment_FireExt_001", "EDDF")] [InlineData("MK_BIKF_DK_Sewer_Tank", "BIKF")]
    [InlineData("Barriers_Cargo_12", "EBBR")] [InlineData("kpdx_nxt_concourse_b_walkway", "KPDX")]
    public void Clutter_and_interiors_are_not_features(string model, string icao)
        => Assert.Null(SceneryModelNameClassifier.Classify(model, icao));

    [Theory]
    [InlineData("iniscene-egss_Control_Tower", "EGSS", FeatureKind.Tower, "Control Tower")]        // vendor prefix gone
    [InlineData("iniscene-egss-harrods-aviation", "EGSS", FeatureKind.Fbo, "Harrods Aviation")]
    [InlineData("iniscene_egss_Inflite_Jet_Centre_01", "EGSS", FeatureKind.Fbo, "Inflite Jet Centre")]
    [InlineData("iniscene-egss-Fedex_Building", "EGSS", FeatureKind.Cargo, "Fedex Building")]
    [InlineData("ini_egss_fire_station", "EGSS", FeatureKind.FireStation, "Fire Station")]
    [InlineData("iniscene_egss_Fuel_Farm", "EGSS", FeatureKind.Fuel, "Fuel Farm")]
    [InlineData("FBKMSP_Tower", "KMSP", FeatureKind.Tower, "Tower")]                               // a token that ENDS with the ICAO
    [InlineData("FBKSFO_FBO", "KSFO", FeatureKind.Fbo, "FBO")]
    [InlineData("kpdx_nxt_tower", "KPDX", FeatureKind.Tower, "Tower")]
    [InlineData("kpdx_ameriflight_hangars_msfs", "KPDX", FeatureKind.Hangar, "Ameriflight Hangars")]
    [InlineData("kpdx_nxt_ameriflight_hangar_new", "KPDX", FeatureKind.Hangar, "Ameriflight Hangar")]
    [InlineData("kpdx_new_FBO", "KPDX", FeatureKind.Fbo, "FBO")]
    [InlineData("kpdx_concourse_B", "KPDX", FeatureKind.Concourse, "Concourse B")]
    [InlineData("kpdx_fire_station", "KPDX", FeatureKind.FireStation, "Fire Station")]
    [InlineData("kpdx_cargo_ramp", "KPDX", FeatureKind.Cargo, "Cargo Ramp")]
    [InlineData("ConcourseB_East", "KDEN", FeatureKind.Concourse, "Concourse B")]
    [InlineData("TerminalA", "KDEN", FeatureKind.Terminal, "Terminal A")]
    [InlineData("FBKMSP_Terminal2", "KMSP", FeatureKind.Terminal, "Terminal 2")]                  // "Terminal2": \b never matched a digit-glued word
    [InlineData("cyyz_terminal1", "CYYZ", FeatureKind.Terminal, "Terminal 1")]
    [InlineData("cyyz_hangars3", "CYYZ", FeatureKind.Hangar, "Hangars 3")]
    [InlineData("EGSS_Terminal_Main_Part1", "EGSS", FeatureKind.Terminal, "Terminal")]            // parts of one building share one name
    [InlineData("EGSS_Main_Terminal_Part2", "EGSS", FeatureKind.Terminal, "Terminal")]
    [InlineData("MK_BIKF_DS_Hangar_03", "BIKF", FeatureKind.Hangar, "DS Hangar")]
    [InlineData("mk_bikf_da_terminal", "BIKF", FeatureKind.Terminal, "Terminal")]
    [InlineData("Titan_Airways_Hangar", "EGSS", FeatureKind.Hangar, "Titan Airways Hangar")]
    public void Real_buildings_lose_their_vendor_prefix_and_keep_their_name(string model, string icao, FeatureKind kind, string name)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        Assert.NotNull(c);
        Assert.Equal(kind, c!.Kind);
        Assert.Equal(name, c.Name);
    }

    // NOT measured model names: rows that pin the kind ORDER against the other two tiers. The
    // catalog never merges across kinds, so one building classified Terminal from scenery and
    // Cargo from OSM is listed twice under two names. OsmFeatureClassifier.TerminalKind and
    // GsxTerminalFeatureSource.KindOf both decide Fbo and Cargo before Terminal; "flugsteig" is
    // OSM's own word for a concourse at EDDF and belongs to the shared FeatureLexicon.
    [Theory]
    [InlineData("KXYZ_Cargo_Terminal_01", "KXYZ", FeatureKind.Cargo, "Cargo Terminal")]
    [InlineData("KXYZ_Executive_Terminal", "KXYZ", FeatureKind.Fbo, "Executive Terminal")]
    [InlineData("EDDF_Flugsteig_A_01", "EDDF", FeatureKind.Concourse, "Flugsteig A")]
    public void A_kind_word_decides_the_same_way_here_as_in_the_osm_and_gsx_tiers(string model, string icao, FeatureKind kind, string name)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        Assert.NotNull(c);
        Assert.Equal(kind, c!.Kind);
        Assert.Equal(name, c.Name);
    }

    // The kind table matches FeatureLexicon.Concourse, but the spoken name is built from the
    // KEYWORD TOKEN a SEPARATE regex finds, so a lexicon word that regex does not know is
    // classified as a Concourse and then dropped for having no name to build — silently, and only
    // for the one vocabulary the two share. Read off the LIVE pattern, so a word added to the
    // lexicon tomorrow is covered without anyone remembering this test exists.
    [Fact]
    public void Every_concourse_word_the_shared_lexicon_knows_can_still_be_named()
    {
        string[] words = System.Text.RegularExpressions.Regex
            .Match(FeatureLexicon.Concourse.ToString(), @"\(([^)]*)\)").Groups[1].Value.Split('|');

        Assert.Contains("flugsteig", words);                    // the pattern really was read
        Assert.All(words, w => Assert.True(w.Length > 0 && w.All(char.IsLetter),
            $"'{w}' is not a plain word — the lexicon grew a nested group, so extend the extraction above"));

        foreach (string w in words)
        {
            var c = SceneryModelNameClassifier.Classify($"KXYZ_{w}_A_01", "KXYZ");
            Assert.True(c != null, w);
            Assert.Equal(FeatureKind.Concourse, c!.Kind);
            Assert.False(string.IsNullOrWhiteSpace(c.Name), w);
        }
    }

    [Fact]
    public void A_bare_kind_word_is_a_generic_name_and_a_proper_name_is_not()
    {
        Assert.True(SceneryModelNameClassifier.Classify("KJAC_Tower", "KJAC")!.NameIsGeneric);
        Assert.True(SceneryModelNameClassifier.Classify("mk_bikf_da_terminal", "BIKF")!.NameIsGeneric);
        Assert.False(SceneryModelNameClassifier.Classify("Titan_Airways_Hangar", "EGSS")!.NameIsGeneric);
        Assert.False(SceneryModelNameClassifier.Classify("KJAC_Hangar_1", "KJAC")!.NameIsGeneric);
    }

    [Theory]
    [InlineData("Building_A_Terminal.001", "KXYZ")]     // the keyword token used to be missed and token 0 ("Building") was spoken
    [InlineData("KJAC_Hangar_2147483648", "KJAC")]      // int.Parse overflow used to abort the whole package
    public void Odd_tokens_never_throw_and_never_leak(string model, string icao)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        if (c != null) { Assert.DoesNotContain(".001", c.Name); Assert.NotEqual("Building", c.Name); }
    }

    [Fact]
    public void The_prefilter_keeps_anything_with_a_kind_word_and_drops_the_rest()
    {
        Assert.True(SceneryModelNameClassifier.MightBeFeature("iniscene-egss-harrods-aviation"));
        Assert.True(SceneryModelNameClassifier.MightBeFeature("FBKMSP_Terminal2"));
        Assert.True(SceneryModelNameClassifier.MightBeFeature("iby_cargo_dolly_lg"));      // the stop list is Classify's job, not the prefilter's
        Assert.False(SceneryModelNameClassifier.MightBeFeature("iby_ils_glideslope_antenna"));
        Assert.False(SceneryModelNameClassifier.MightBeFeature("MLA0117"));
    }

    // The indexer never calls Classify for a name the prefilter rejects, so a false negative loses a
    // place for good while a false positive costs one Classify call. Run every measured row in this
    // file through both: whatever Classify accepts, the prefilter must have kept.
    [Fact]
    public void The_prefilter_never_drops_a_name_the_classifier_accepts()
    {
        int accepted = 0;
        foreach (var (model, icao) in MeasuredRows())
        {
            if (SceneryModelNameClassifier.Classify(model, icao) == null) continue;
            accepted++;
            Assert.True(SceneryModelNameClassifier.MightBeFeature(model), model);
        }
        Assert.True(accepted >= 40, $"only {accepted} measured rows classified — the row walk found nothing");
    }

    /// <summary>(model, icao) of every [InlineData] row in this class, so the prefilter property can
    /// never drift from the tables above. The single-argument rows are the drop test's, at KTIW.</summary>
    private static IEnumerable<(string Model, string Icao)> MeasuredRows()
    {
        foreach (var method in typeof(SceneryModelNameClassifierTests).GetMethods())
            foreach (var attr in method.GetCustomAttributesData())
            {
                if (attr.AttributeType != typeof(InlineDataAttribute) || attr.ConstructorArguments.Count == 0) continue;
                var args = (IReadOnlyList<CustomAttributeTypedArgument>?)attr.ConstructorArguments[0].Value;
                if (args == null || args.Count == 0 || args[0].Value is not string model) continue;
                yield return (model, args.Count > 1 && args[1].Value is string icao ? icao : "KTIW");
            }
    }
}

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
    [InlineData("Fuel-truck_KPHX", "KPHX")]          // measured 2026-09-22: with only its ICAO removed it is still a fuel truck
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

    // Measured 2026-09-22 over all 26,098 model names in the 35 installed airport packages: FIVE names
    // put the airport's ICAO at the END, after a kind word, so stripping everything up to the ICAO
    // threw them away whole. The rule affects those five and NAMES four: the three pinned here (each a
    // single placement of a real building; DHL ~93 m from cargo stand P 91 at YSSY) and
    // cargo_rwy25_yssy ("Cargo Rwy", a recorded residual — runway designators inside a model name are
    // a separate lexical question — deliberately not pinned); the fifth, Fuel-truck_KPHX, stays
    // clutter in the drop table above.
    [Theory]
    [InlineData("DHL_YSSY", "YSSY", FeatureKind.Cargo, "DHL")]
    [InlineData("Security_DHL_yssy", "YSSY", FeatureKind.Cargo, "Security DHL")]
    [InlineData("TankOil_KPHX", "KPHX", FeatureKind.Fuel, "Tank Oil")]
    public void An_icao_that_ends_the_name_costs_only_itself(string model, string icao, FeatureKind kind, string name)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        Assert.NotNull(c);
        Assert.Equal(kind, c!.Kind);
        Assert.Equal(name, c.Name);
    }

    // NOT measured model names: the review's shapes for the same rule, and the case it must not
    // change. What the rule owes them is that they are no longer DROPPED; their names follow the
    // classifier's existing rules, and the spec's guessed "Main Terminal", "Hangar 02" and "Boeing
    // Hangar 11" were never requirements. A terminal is its keyword plus a designator only — how
    // EGSS_Main_Terminal_Part2 and EGSS_Terminal_Main_Part1 share ONE name — so "Main_Terminal_KSEA"
    // is "Terminal". "02" reads as 2, as KTIW_Hangar_09 reads "Hangar 9". Where the ICAO does end a
    // prefix, everything through it still goes ("mesh"), and the trailing "11" is a part number.
    [Theory]
    [InlineData("Main_Terminal_KSEA", "KSEA", FeatureKind.Terminal, "Terminal")]
    [InlineData("Hangar_KTIW_02", "KTIW", FeatureKind.Hangar, "Hangar 2")]
    [InlineData("mesh_EGKK_Boeing_Hangar_11", "EGKK", FeatureKind.Hangar, "Boeing Hangar")]
    public void The_icao_rule_on_the_reviews_shapes(string model, string icao, FeatureKind kind, string name)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        Assert.NotNull(c);
        Assert.Equal(kind, c!.Kind);
        Assert.Equal(name, c.Name);
    }

    [Fact]
    public void A_kind_phrase_is_never_completed_across_the_removed_icao()
    {
        // "Jet Centre" is an FBO phrase only when the two words are the name's OWN neighbours.
        // MightBeFeature knows no ICAO and sees "Jet KXYZ Centre": had Classify joined across the gap
        // it would accept a name the prefilter had already thrown away.
        Assert.Null(SceneryModelNameClassifier.Classify("Jet_KXYZ_Centre", "KXYZ"));
        Assert.False(SceneryModelNameClassifier.MightBeFeature("Jet_KXYZ_Centre"));
    }

    // NOT measured model names: rows that pin the kind ORDER against the other two tiers. The
    // catalog never merges across kinds, so one building classified Terminal from scenery and
    // Cargo from OSM is listed twice under two names. Every tier reads a name through
    // FeatureLexicon.NamedKind — Cargo, then Fbo, then Concourse, all before Terminal (the
    // cross-tier table is FeatureKindAcrossTiersTests); "flugsteig" is OSM's own word for a
    // concourse at EDDF and belongs to the shared FeatureLexicon.
    [Theory]
    [InlineData("KXYZ_Cargo_Terminal_01", "KXYZ", FeatureKind.Cargo, "Cargo Terminal")]
    [InlineData("KXYZ_Executive_Terminal", "KXYZ", FeatureKind.Fbo, "Executive Terminal")]
    [InlineData("EDDF_Flugsteig_A_01", "EDDF", FeatureKind.Concourse, "Flugsteig A")]
    [InlineData("KXYZ_DHL_Aviation", "KXYZ", FeatureKind.Cargo, "DHL Aviation")]
    [InlineData("KXYZ_Menzies_Aviation_Cargo", "KXYZ", FeatureKind.Cargo, "Menzies Aviation Cargo")]
    [InlineData("KXYZ_Cargo_Satellite", "KXYZ", FeatureKind.Cargo, "Cargo Satellite")]
    [InlineData("KXYZ_Deicing_Pad", "KXYZ", FeatureKind.DeicePad, "Deicing Pad")]
    [InlineData("KXYZ_Avfuel_Tank", "KXYZ", FeatureKind.Fuel, "Avfuel Tank")]            // a fuel BRAND, never an FBO word: Fuel, as OSM's aeroway=fuel "Avfuel" is
    public void A_kind_word_decides_the_same_way_here_as_in_the_osm_and_gsx_tiers(string model, string icao, FeatureKind kind, string name)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        Assert.NotNull(c);
        Assert.Equal(kind, c!.Kind);
        Assert.Equal(name, c.Name);
    }

    // The kind test and the NAMER must know the same concourse words: the kind table matches
    // FeatureLexicon.Concourse and the spoken name is built from the keyword TOKEN, so a word one
    // knew and the other did not was classified and then dropped for having no name. Both are made
    // from FeatureLexicon.ConcourseWords now, and this walks that array — no pattern text is parsed.
    [Fact]
    public void Every_concourse_word_in_the_shared_list_is_classified_and_named()
    {
        Assert.NotEmpty(FeatureLexicon.ConcourseWords);
        foreach (string w in FeatureLexicon.ConcourseWords)
        {
            var c = SceneryModelNameClassifier.Classify($"KXYZ_{w}_A_01", "KXYZ");
            Assert.True(c != null, w);
            Assert.Equal(FeatureKind.Concourse, c!.Kind);
            Assert.Equal(char.ToUpperInvariant(w[0]) + w[1..] + " A", c.Name);
        }
    }

    [Fact]
    public void A_vetoed_fbo_word_is_no_feature_but_the_prefilter_still_keeps_the_name()
    {
        // "Civil Aviation Authority" is an office, not an FBO (FeatureLexicon.IsFboName)…
        Assert.Null(SceneryModelNameClassifier.Classify("KXYZ_Civil_Aviation_Authority", "KXYZ"));
        // …but the prefilter asks POSITIVE words only. A veto can turn a match OFF when one word more
        // is seen, and the prefilter sees MORE words than Classify (it keeps the ICAO prefix), so a
        // vetoing prefilter could drop a name Classify accepts at another airport.
        Assert.True(SceneryModelNameClassifier.MightBeFeature("KXYZ_Civil_Aviation_Authority"));
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

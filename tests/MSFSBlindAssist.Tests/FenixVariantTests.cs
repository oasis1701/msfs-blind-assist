// Fenix v3 variants: which Fenix is loaded (A318/A319/A320/A321, CFM/IAE, extra tanks,
// equipment), read from eight config L:vars. The pure record is FenixVariant; the definition
// registers the eight vars and shares one answer through FenixA320Definition.Variant.
//
// Measured on the Fenix A320 CEO CFM and A321 IAE (2026-10-21): aircraft type 3 = A320 and 4 = A321,
// engine 0 = CFM and 1 = IAE, ACT count 2, STDBY 0 = ISIS, DCDU 1 = fitted, IS_NEO 0 = CEO, CARGOMEAT 0
// on the A321 IAE whose settings say cargo heat is off. Types 0-2 and engines 2-3 follow the Fenix
// settings order and ADIRS 0 = GNADIRS follows the model's visibility template: all three unmeasured.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixVariantTests
{
    private static FenixVariant Complete() => new(
        FenixAircraftType.A321, FenixEngineFamily.IaeV2500, 2, HasIsis: true, HasDcdu: true, IsNeo: false,
        HasGnadirs: false, HasCargoHeat: false);

    // The field a config var drives, so one theory can pin every boolean var.
    private static bool? Flag(FenixVariant variant, string configVar) => configVar switch
    {
        FenixConfigVars.Standby => variant.HasIsis,
        FenixConfigVars.Dcdu => variant.HasDcdu,
        FenixConfigVars.IsNeo => variant.IsNeo,
        FenixConfigVars.Adirs => variant.HasGnadirs,
        FenixConfigVars.CargoHeat => variant.HasCargoHeat,
        _ => throw new ArgumentException($"{configVar} is not a boolean config var"),
    };

    [Fact]
    public void Unknown_until_type_and_engine_known()
    {
        Assert.Equal(FenixAircraftType.Unknown, FenixVariant.Unknown.Type);
        Assert.Equal(FenixEngineFamily.Unknown, FenixVariant.Unknown.Engine);
        Assert.Null(FenixVariant.Unknown.ActCount);
        Assert.Null(FenixVariant.Unknown.HasIsis);
        Assert.Null(FenixVariant.Unknown.HasDcdu);
        Assert.Null(FenixVariant.Unknown.IsNeo);
        Assert.Null(FenixVariant.Unknown.HasGnadirs);
        Assert.Null(FenixVariant.Unknown.HasCargoHeat);

        Assert.False(FenixVariant.Unknown.IsKnown);
        var typeOnly = FenixVariant.Unknown.With(FenixConfigVars.AircraftType, 3);
        Assert.False(typeOnly.IsKnown);
        var engineOnly = FenixVariant.Unknown.With(FenixConfigVars.EngineType, 0);
        Assert.False(engineOnly.IsKnown);
        Assert.True(typeOnly.With(FenixConfigVars.EngineType, 0).IsKnown);
        Assert.True(engineOnly.With(FenixConfigVars.AircraftType, 3).IsKnown);
    }

    [Fact]
    public void Constructed_with_seven_arguments_leaves_cargo_heat_unknown()
    {
        var variant = new FenixVariant(FenixAircraftType.A320, FenixEngineFamily.Cfm56, 0, true, true, false, true);
        Assert.Null(variant.HasCargoHeat);
    }

    [Fact]
    public void With_maps_the_measured_values()
    {
        var unknown = FenixVariant.Unknown;
        Assert.Equal(FenixAircraftType.A320, unknown.With("N_CONFIG_AIRCRAFT_TYPE", 3).Type);
        Assert.Equal(FenixAircraftType.A321, unknown.With("N_CONFIG_AIRCRAFT_TYPE", 4).Type);
        Assert.Equal(FenixEngineFamily.Cfm56, unknown.With("N_CONFIG_ENGINETYPE", 0).Engine);
        Assert.Equal(FenixEngineFamily.IaeV2500, unknown.With("N_CONFIG_ENGINETYPE", 1).Engine);
        Assert.Equal(2, unknown.With("N_CONFIG_ACT_COUNT", 2).ActCount);
        Assert.True(unknown.With("B_CONFIG_STDBY", 0).HasIsis);
        Assert.True(unknown.With("B_CONFIG_DCDU", 1).HasDcdu);
        Assert.False(unknown.With("B_CONFIG_IS_NEO", 0).IsNeo);
        Assert.True(unknown.With("B_CONFIG_ADIRS", 0).HasGnadirs);
        Assert.False(unknown.With("B_CONFIG_ADIRS", 1).HasGnadirs);
        Assert.False(unknown.With("B_CONFIG_CARGOMEAT", 0).HasCargoHeat);
        Assert.True(unknown.With("B_CONFIG_CARGOMEAT", 1).HasCargoHeat);
    }

    [Fact]
    public void The_config_var_names_are_the_Fenix_spellings()
    {
        Assert.Equal(
            new[]
            {
                "N_CONFIG_AIRCRAFT_TYPE", "N_CONFIG_ENGINETYPE", "N_CONFIG_ACT_COUNT", "B_CONFIG_STDBY",
                "B_CONFIG_DCDU", "B_CONFIG_IS_NEO", "B_CONFIG_ADIRS", "B_CONFIG_CARGOMEAT",
            },
            FenixConfigVars.All);
    }

    [Theory]
    [InlineData(0, FenixAircraftType.Unknown)]
    [InlineData(1, FenixAircraftType.A318)]
    [InlineData(2, FenixAircraftType.A319)]
    [InlineData(3, FenixAircraftType.A320)]
    [InlineData(4, FenixAircraftType.A321)]
    public void Aircraft_type_follows_the_Fenix_settings_order(double value, FenixAircraftType expected)
        => Assert.Equal(expected, FenixVariant.Unknown.With(FenixConfigVars.AircraftType, value).Type);

    [Theory]
    [InlineData(0, FenixEngineFamily.Cfm56)]
    [InlineData(1, FenixEngineFamily.IaeV2500)]
    [InlineData(2, FenixEngineFamily.CfmLeap)]
    [InlineData(3, FenixEngineFamily.PwGtf)]
    public void Engine_type_follows_the_Fenix_settings_order(double value, FenixEngineFamily expected)
        => Assert.Equal(expected, FenixVariant.Unknown.With(FenixConfigVars.EngineType, value).Engine);

    [Theory]
    [InlineData(FenixConfigVars.Standby, 0, true)]     // 0 = ISIS fitted
    [InlineData(FenixConfigVars.Standby, 1, false)]
    [InlineData(FenixConfigVars.Dcdu, 0, false)]
    [InlineData(FenixConfigVars.Dcdu, 1, true)]        // 1 = DCDU fitted
    [InlineData(FenixConfigVars.IsNeo, 0, false)]      // 0 = CEO
    [InlineData(FenixConfigVars.IsNeo, 1, true)]
    [InlineData(FenixConfigVars.Adirs, 0, true)]       // 0 = GNADIRS keypad
    [InlineData(FenixConfigVars.Adirs, 1, false)]      // 1 = plain ADIRS
    [InlineData(FenixConfigVars.CargoHeat, 0, false)]
    [InlineData(FenixConfigVars.CargoHeat, 1, true)]
    public void Boolean_config_vars_map_both_of_their_values(string configVar, double value, bool expected)
        => Assert.Equal(expected, Flag(FenixVariant.Unknown.With(configVar, value), configVar));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    [InlineData(-1)]
    [InlineData(0.5)]
    public void Any_non_zero_cargo_heat_value_means_it_is_fitted(double value)
        => Assert.True(FenixVariant.Unknown.With(FenixConfigVars.CargoHeat, value).HasCargoHeat);

    [Theory]
    [InlineData(7)]
    [InlineData(5)]
    [InlineData(-1)]
    [InlineData(3.5)]
    [InlineData(1e12)]
    public void An_out_of_range_aircraft_type_is_unknown(double value)
        => Assert.Equal(FenixAircraftType.Unknown, FenixVariant.Unknown.With(FenixConfigVars.AircraftType, value).Type);

    [Theory]
    [InlineData(9)]
    [InlineData(4)]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(0.5)]
    [InlineData(1e12)]
    public void An_out_of_range_engine_type_is_unknown(double value)
        => Assert.Equal(FenixEngineFamily.Unknown, FenixVariant.Unknown.With(FenixConfigVars.EngineType, value).Engine);

    [Theory]
    [InlineData(-1)]
    [InlineData(1.5)]
    [InlineData(1e12)]
    public void An_act_count_that_is_not_a_whole_non_negative_number_is_unknown(double value)
        => Assert.Null(FenixVariant.Unknown.With(FenixConfigVars.ActCount, value).ActCount);

    [Theory]
    [InlineData(FenixConfigVars.Standby, 2)]
    [InlineData(FenixConfigVars.Standby, -1)]
    [InlineData(FenixConfigVars.Dcdu, 2)]
    [InlineData(FenixConfigVars.Dcdu, 0.5)]
    [InlineData(FenixConfigVars.IsNeo, 3)]
    [InlineData(FenixConfigVars.IsNeo, -1)]
    [InlineData(FenixConfigVars.Adirs, 2)]
    [InlineData(FenixConfigVars.Adirs, 0.5)]
    public void A_value_a_boolean_var_never_takes_is_unknown(string configVar, double value)
        => Assert.Null(Flag(FenixVariant.Unknown.With(configVar, value), configVar));

    [Fact]
    public void A_bad_value_replaces_a_known_one_with_unknown()
    {
        var known = Complete();

        Assert.Equal(FenixAircraftType.Unknown, known.With(FenixConfigVars.AircraftType, 7).Type);
        Assert.Equal(FenixEngineFamily.Unknown, known.With(FenixConfigVars.EngineType, 9).Engine);
        Assert.Null(known.With(FenixConfigVars.ActCount, -1).ActCount);
        Assert.Null(known.With(FenixConfigVars.Standby, 2).HasIsis);
        Assert.Null(known.With(FenixConfigVars.Dcdu, 2).HasDcdu);
        Assert.Null(known.With(FenixConfigVars.IsNeo, 2).IsNeo);
        Assert.Null(known.With(FenixConfigVars.Adirs, 2).HasGnadirs);
        // The other fields are untouched.
        Assert.Equal(known with { Type = FenixAircraftType.Unknown }, known.With(FenixConfigVars.AircraftType, 7));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_that_is_not_a_finite_number_leaves_the_record_unchanged(double value)
    {
        var known = Complete();
        foreach (string configVar in FenixConfigVars.All)
            Assert.Same(known, known.With(configVar, value));
        Assert.Same(FenixVariant.Unknown, FenixVariant.Unknown.With(FenixConfigVars.AircraftType, value));
    }

    [Fact]
    public void An_unknown_name_returns_the_same_record()
    {
        var known = Complete();
        Assert.Same(known, known.With("N_FCU_SPEED", 1));
        Assert.Same(known, known.With("", 1));
    }

    [Fact]
    public void Each_var_changes_only_its_own_field()
    {
        var known = Complete();
        Assert.Equal(known with { ActCount = 0 }, known.With(FenixConfigVars.ActCount, 0));
        Assert.Equal(known with { HasIsis = false }, known.With(FenixConfigVars.Standby, 1));
        Assert.Equal(known with { HasDcdu = false }, known.With(FenixConfigVars.Dcdu, 0));
        Assert.Equal(known with { IsNeo = true }, known.With(FenixConfigVars.IsNeo, 1));
        Assert.Equal(known with { HasGnadirs = true }, known.With(FenixConfigVars.Adirs, 0));
        Assert.Equal(known with { HasCargoHeat = true }, known.With(FenixConfigVars.CargoHeat, 1));
        // Writing the value a field already holds gives an equal record: no change to announce.
        Assert.Equal(known, known.With(FenixConfigVars.AircraftType, 4));
    }

    [Fact]
    public void Describe_reads_naturally()
    {
        Assert.Equal("Fenix A321, IAE engines, 2 extra tanks", Complete().Describe());

        var a320 = new FenixVariant(FenixAircraftType.A320, FenixEngineFamily.Cfm56, 0, true, true, false, true);
        Assert.Equal("Fenix A320, CFM engines", a320.Describe());
    }

    [Theory]
    [InlineData(FenixAircraftType.A318, "Fenix A318")]
    [InlineData(FenixAircraftType.A319, "Fenix A319")]
    [InlineData(FenixAircraftType.A320, "Fenix A320")]
    [InlineData(FenixAircraftType.A321, "Fenix A321")]
    [InlineData(FenixAircraftType.Unknown, "Fenix")]
    public void Describe_names_the_aircraft_type_when_it_is_known(FenixAircraftType type, string expected)
        => Assert.Equal(expected, (FenixVariant.Unknown with { Type = type }).Describe());

    [Theory]
    [InlineData(FenixEngineFamily.Cfm56, "Fenix A320, CFM engines")]
    [InlineData(FenixEngineFamily.IaeV2500, "Fenix A320, IAE engines")]
    [InlineData(FenixEngineFamily.CfmLeap, "Fenix A320, CFM LEAP engines")]
    [InlineData(FenixEngineFamily.PwGtf, "Fenix A320, Pratt and Whitney GTF engines")]
    [InlineData(FenixEngineFamily.Unknown, "Fenix A320")]
    public void Describe_names_the_engine_family_when_it_is_known(FenixEngineFamily engine, string expected)
        => Assert.Equal(expected, (FenixVariant.Unknown with { Type = FenixAircraftType.A320, Engine = engine }).Describe());

    [Fact]
    public void Describe_leaves_out_what_is_unknown()
    {
        Assert.Equal("Fenix", FenixVariant.Unknown.Describe());
        Assert.Equal("Fenix, CFM engines",
            (FenixVariant.Unknown with { Engine = FenixEngineFamily.Cfm56 }).Describe());
        Assert.Equal("Fenix, 2 extra tanks", (FenixVariant.Unknown with { ActCount = 2 }).Describe());
    }

    [Theory]
    [InlineData(null, "Fenix A321, IAE engines")]
    [InlineData(0, "Fenix A321, IAE engines")]
    [InlineData(1, "Fenix A321, IAE engines, 1 extra tank")]
    [InlineData(2, "Fenix A321, IAE engines, 2 extra tanks")]
    [InlineData(12, "Fenix A321, IAE engines, 12 extra tanks")]
    public void Describe_counts_the_extra_tanks(int? count, string expected)
        => Assert.Equal(expected, (Complete() with { ActCount = count }).Describe());

    [Fact]
    public void Describe_changes_with_the_tank_count_and_not_with_the_equipment()
    {
        // The app re-announces the loaded aircraft when Describe() changes, so it must change with
        // the tank count and stay put when the ISIS, DCDU, NEO, GNADIRS or cargo heat readings change.
        var known = Complete();
        Assert.NotEqual(known.Describe(), (known with { ActCount = 3 }).Describe());

        foreach (bool? flag in new bool?[] { true, false, null })
        {
            Assert.Equal(known.Describe(), (known with { HasIsis = flag }).Describe());
            Assert.Equal(known.Describe(), (known with { HasDcdu = flag }).Describe());
            Assert.Equal(known.Describe(), (known with { IsNeo = flag }).Describe());
            Assert.Equal(known.Describe(), (known with { HasGnadirs = flag }).Describe());
            Assert.Equal(known.Describe(), (known with { HasCargoHeat = flag }).Describe());
        }
    }

    [Fact]
    public void Describe_reads_the_same_under_a_comma_decimal_culture()
        => Assert.Equal("Fenix A321, IAE engines, 2 extra tanks",
            TestCulture.Run("de-DE", () => Complete().Describe()));

    [Fact]
    public void Definition_registers_the_config_vars()
    {
        var variables = new FenixA320Definition().GetVariables();

        foreach (string name in FenixConfigVars.All)
        {
            Assert.True(variables.TryGetValue(name, out var variable), $"{name} is not registered");
            Assert.Equal(name, variable!.Name);
            Assert.Equal(SimVarType.LVar, variable.Type);
            Assert.Equal(UpdateFrequency.Continuous, variable.UpdateFrequency);
            Assert.Equal("number", variable.Units);
            Assert.True(variable.ExcludeFromMonitorManager, $"{name} would earn a Ctrl+M row that mutes nothing");
            // Nothing is spoken (ProcessSimVarUpdate consumes each, see the silent-consumption tests), but
            // only an ANNOUNCED Continuous var is subscribed: without it nothing requests the var and
            // Variant would stay Unknown in the app.
            Assert.True(variable.IsAnnounced, $"{name} must be announced-flagged or nothing ever reads it");
        }
    }

    [Fact]
    public void The_config_vars_ride_the_continuous_batch_the_app_registers()
    {
        // The batch is what delivers them to ProcessSimVarUpdate; ProcessSimVarUpdate calls in the tests
        // below bypass it, so this pins the delivery itself with the batch's own membership test.
        var variables = new FenixA320Definition().GetVariables();
        var batchNumbers = ContinuousBatchLayout.BatchNumbers(variables);

        foreach (string name in FenixConfigVars.All)
        {
            Assert.True(ContinuousBatchLayout.RidesBatch(variables[name]), $"{name} does not ride the continuous batch");
            // SimConnectManager registers five batches of ContinuousBatchLayout.BatchSize; a var past them is never requested.
            int batch = batchNumbers.GetValueOrDefault(name);
            Assert.True(batch is >= 1 and <= 5, $"{name} lands in batch {batch}, outside the five that are registered");
        }
    }

    [Fact]
    public void The_config_vars_have_distinct_display_names()
    {
        var variables = new FenixA320Definition().GetVariables();
        var names = FenixConfigVars.All.Select(name => variables[name].DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, name => Assert.StartsWith("Fenix config: ", name));
    }

    [Fact]
    public void The_config_vars_share_no_name_with_another_batched_var()
    {
        // [VAR-7]: two continuous batched vars on one Name shift every later struct slot.
        var variables = new FenixA320Definition().GetVariables();
        foreach (string name in FenixConfigVars.All)
        {
            var sharers = variables
                .Where(kv => kv.Value.Name == name && ContinuousBatchLayout.RidesBatch(kv.Value))
                .Select(kv => kv.Key)
                .ToList();
            Assert.Equal(new[] { name }, sharers);
        }
    }

    [Fact]
    public void The_definition_starts_with_an_unknown_variant()
        => Assert.Same(FenixVariant.Unknown, new FenixA320Definition().Variant);

    [Fact]
    public void A_config_value_is_consumed_silently_and_updates_the_variant()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.True(definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 4, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixConfigVars.EngineType, 1, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixConfigVars.ActCount, 2, speech));

        Assert.Empty(speech.All);
        Assert.True(definition.Variant.IsKnown);
        Assert.Equal("Fenix A321, IAE engines, 2 extra tanks", definition.Variant.Describe());
    }

    [Fact]
    public void Every_config_var_is_consumed_even_when_its_value_changes_nothing()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        foreach (string name in FenixConfigVars.All)
        {
            Assert.True(definition.ProcessSimVarUpdate(name, 1, speech), $"{name} was not consumed");
            Assert.True(definition.ProcessSimVarUpdate(name, 1, speech), $"{name} was not consumed again");
            Assert.True(definition.ProcessSimVarUpdate(name, double.NaN, speech), $"{name} NaN was not consumed");
        }
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Variant_changed_is_raised_with_the_new_record_only_when_it_changes()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        var raised = new List<(object? Sender, FenixVariant Variant, FenixVariant Current)>();
        definition.VariantChanged += (sender, variant) => raised.Add((sender, variant, definition.Variant));

        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 3, speech);
        Assert.Single(raised);
        Assert.Same(definition, raised[0].Sender);
        Assert.Equal(FenixAircraftType.A320, raised[0].Variant.Type);
        Assert.Same(raised[0].Variant, raised[0].Current);   // Variant is updated before the event is raised

        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 3, speech);   // the same reading again
        definition.ProcessSimVarUpdate(FenixConfigVars.EngineType, double.NaN, speech);   // nothing to apply
        definition.ProcessSimVarUpdate("NOT_A_FENIX_CONFIG_VAR", 250, speech);   // not a config var
        Assert.Single(raised);

        definition.ProcessSimVarUpdate(FenixConfigVars.EngineType, 0, speech);
        Assert.Equal(2, raised.Count);
        Assert.Equal(FenixEngineFamily.Cfm56, raised[1].Variant.Engine);
        Assert.Equal(FenixAircraftType.A320, raised[1].Variant.Type);

        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 4, speech);   // the type changes: raised again
        Assert.Equal(3, raised.Count);
        Assert.Equal(FenixAircraftType.A321, raised[2].Variant.Type);
    }

    [Fact]
    public void A_config_value_that_changes_only_equipment_still_raises_the_event()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        int raised = 0;
        definition.VariantChanged += (_, _) => raised++;
        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 3, speech);   // the config is published
        Assert.Equal(1, raised);

        definition.ProcessSimVarUpdate(FenixConfigVars.Standby, 0, speech);
        definition.ProcessSimVarUpdate(FenixConfigVars.Standby, 1, speech);
        Assert.Equal(3, raised);
        Assert.False(definition.Variant.HasIsis);
    }

    // SimConnect reads an L:var the Fenix has not written yet as 0, and 0 is a real value for
    // seven of the eight (CFM, ISIS, GNADIRS, no DCDU, CEO, no cargo heat, no extra tanks). An aircraft type
    // of 1-4 is the sign the Fenix has published its config; until then the variant stays Unknown.

    [Fact]
    public void Readings_before_the_aircraft_type_leave_the_variant_unknown_and_raise_nothing()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        int raised = 0;
        definition.VariantChanged += (_, _) => raised++;

        // The sim menu, or the Fenix still loading: all eight read 0.
        foreach (string name in FenixConfigVars.All)
            Assert.True(definition.ProcessSimVarUpdate(name, 0, speech), $"{name} was not consumed");
        // Other readings before the type are consumed too, and claim nothing either.
        foreach (string name in FenixConfigVars.All.Where(n => n != FenixConfigVars.AircraftType))
            Assert.True(definition.ProcessSimVarUpdate(name, 1, speech), $"{name} was not consumed");

        Assert.Same(FenixVariant.Unknown, definition.Variant);
        Assert.Equal(0, raised);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_aircraft_type_brings_every_earlier_reading_in_one_change_and_later_readings_apply_directly()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        var raised = new List<(FenixVariant Variant, FenixVariant Current)>();
        definition.VariantChanged += (_, variant) => raised.Add((variant, definition.Variant));
        foreach (string name in FenixConfigVars.All)
            definition.ProcessSimVarUpdate(name, 0, speech);

        // The A321 IAE publishes its config, the type last here; each latest reading counts.
        definition.ProcessSimVarUpdate(FenixConfigVars.EngineType, 1, speech);
        definition.ProcessSimVarUpdate(FenixConfigVars.EngineType, double.NaN, speech);   // nothing to apply
        definition.ProcessSimVarUpdate(FenixConfigVars.ActCount, 2, speech);
        definition.ProcessSimVarUpdate(FenixConfigVars.Dcdu, 1, speech);
        definition.ProcessSimVarUpdate(FenixConfigVars.Adirs, 1, speech);
        Assert.Empty(raised);

        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 4, speech);

        var a321 = new FenixVariant(FenixAircraftType.A321, FenixEngineFamily.IaeV2500, 2,
            HasIsis: true, HasDcdu: true, IsNeo: false, HasGnadirs: false, HasCargoHeat: false);
        var change = Assert.Single(raised);
        Assert.Equal(a321, change.Variant);
        Assert.Same(change.Variant, change.Current);
        Assert.Equal(a321, definition.Variant);

        // From now on each reading applies on its own.
        definition.ProcessSimVarUpdate(FenixConfigVars.ActCount, 3, speech);
        Assert.Equal(2, raised.Count);
        Assert.Equal(a321 with { ActCount = 3 }, definition.Variant);
        definition.ProcessSimVarUpdate(FenixConfigVars.ActCount, 3, speech);
        Assert.Equal(2, raised.Count);
        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(3.5)]
    public void An_aircraft_type_that_stops_reading_1_to_4_makes_the_variant_unknown_again(double type)
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        var raised = new List<FenixVariant>();
        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 3, speech);
        definition.ProcessSimVarUpdate(FenixConfigVars.EngineType, 0, speech);
        definition.ProcessSimVarUpdate(FenixConfigVars.ActCount, 2, speech);
        definition.VariantChanged += (_, variant) => raised.Add(variant);

        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, type, speech);

        Assert.Same(FenixVariant.Unknown, definition.Variant);
        Assert.Same(FenixVariant.Unknown, Assert.Single(raised));

        // While it is unknown, readings are kept, not applied, and the same type again changes nothing.
        definition.ProcessSimVarUpdate(FenixConfigVars.EngineType, 1, speech);
        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, type, speech);
        Assert.Same(FenixVariant.Unknown, definition.Variant);
        Assert.Single(raised);

        // Published again: one change carrying every reading so far.
        definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 4, speech);
        Assert.Equal(2, raised.Count);
        Assert.Equal(FenixVariant.Unknown with
        {
            Type = FenixAircraftType.A321, Engine = FenixEngineFamily.IaeV2500, ActCount = 2,
        }, raised[1]);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_throwing_variant_subscriber_stops_neither_the_next_subscriber_nor_the_update()
    {
        // Other code subscribes; one that throws must not keep the others from the change, and the
        // reading must still be consumed (each config value is delivered once).
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        var seen = new List<FenixVariant>();
        definition.VariantChanged += (_, _) => throw new InvalidOperationException("a later plan's handler failed");
        definition.VariantChanged += (_, variant) => seen.Add(variant);

        bool consumed = definition.ProcessSimVarUpdate(FenixConfigVars.AircraftType, 3, speech);

        Assert.True(consumed);
        Assert.Equal(FenixAircraftType.A320, Assert.Single(seen).Type);
        Assert.Equal(FenixAircraftType.A320, definition.Variant.Type);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_var_that_is_not_a_config_var_is_not_consumed_by_variant_detection()
    {
        // Variant detection alone, not what the rest of ProcessSimVarUpdate does with the name.
        var definition = new FenixA320Definition();

        Assert.False(definition.TryApplyConfigValue("NOT_A_FENIX_CONFIG_VAR", 1));
        Assert.Same(FenixVariant.Unknown, definition.Variant);

        // Nor once the config is published.
        Assert.True(definition.TryApplyConfigValue(FenixConfigVars.AircraftType, 3));
        var published = definition.Variant;
        int raised = 0;
        definition.VariantChanged += (_, _) => raised++;

        Assert.False(definition.TryApplyConfigValue("NOT_A_FENIX_CONFIG_VAR", 1));
        Assert.Same(published, definition.Variant);
        Assert.Equal(0, raised);
    }
}

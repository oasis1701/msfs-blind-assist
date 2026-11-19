using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's fuel and payload load from SimBrief, which BlueBird's own EFB loads (task 11e): the
/// plan's fields and units, the whole-pound targets per tank, cabin and hold as BlueBird spreads them
/// (measured 2026-10-25; docs/b757.md, "What the app adds around a press"), the units check made
/// before any tap, the read-back, and the sentences the load watch says.
/// </summary>
public class Bbs757LoadsheetTests
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

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-simbrief-ofp.xml"));

    /// <summary>An OFP with only the fields the load reads; a null value leaves the element out.</summary>
    private static string Ofp(string? units = "lbs", string? ramp = "25550", string? paxActual = "224",
        string? pax = "224", string? paxWeight = "175", string? payload = "51459", string? cargo = "12259",
        string? airline = "QQX", string? flight = "1234", string? origin = "KSEA", string? destination = "KDEN",
        string? aircraft = "B752")
    {
        static string El(string name, string? value) => value == null ? "" : $"<{name}>{value}</{name}>";
        return "<OFP>" +
               $"<params>{El("units", units)}</params>" +
               $"<general>{El("icao_airline", airline)}{El("flight_number", flight)}</general>" +
               $"<origin>{El("icao_code", origin)}</origin><destination>{El("icao_code", destination)}</destination>" +
               $"<aircraft>{El("icaocode", aircraft)}</aircraft>" +
               $"<fuel>{El("plan_ramp", ramp)}</fuel>" +
               $"<weights>{El("pax_count", pax)}{El("pax_count_actual", paxActual)}{El("pax_weight", paxWeight)}" +
               $"{El("cargo", cargo)}{El("payload", payload)}</weights>" +
               "</OFP>";
    }

    private static Bbs757SimBriefPlan Plan(double block = 25550, int pax = 224, double paxWeight = 175, double payload = 51459,
        string aircraft = "B752")
        => new("QQX 1234", "KSEA", "KDEN", aircraft, block, pax, paxWeight, payload);

    private static Bbs757Loads Loads(Bbs757SimBriefPlan plan)
    {
        var (loads, refusal) = Bbs757Loadsheet.Compute(plan);
        Assert.Null(refusal);
        Assert.NotNull(loads);
        return loads!;
    }

    /// <summary>A read-back that holds exactly the targets, at the sim's 6.7 pounds per gallon.</summary>
    private static Bbs757LoadReadBack ReadBackOf(Bbs757Loads loads, double firstShare = 2800.0 / 30800.0) => new(
        LeftGallons: loads.LeftLbs / 6.7, RightGallons: loads.RightLbs / 6.7, CenterGallons: loads.CenterLbs / 6.7,
        PoundsPerGallon: 6.7,
        FirstClassLbs: loads.PassengerLbs * firstShare, EconomyLbs: loads.PassengerLbs * (1 - firstShare),
        ForwardHoldLbs: loads.ForwardLbs, AftHoldLbs: loads.AftLbs);

    // ---- the plan ---------------------------------------------------------------------------

    [Fact]
    public void The_fixture_reads_as_the_measured_plan()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Fixture());

        Assert.NotNull(plan);
        Assert.Equal("QQX 1234", plan!.Flight);
        Assert.Equal("KSEA", plan.Origin);
        Assert.Equal("KDEN", plan.Destination);
        Assert.Equal("B752", plan.AircraftIcao);
        Assert.Equal(25550, plan.BlockFuelLbs);
        Assert.Equal(224, plan.Passengers);
        Assert.Equal(175, plan.PassengerWeightLbs);
        Assert.Equal(51459, plan.PayloadLbs);
    }

    [Fact]
    public void A_kilogram_plan_is_read_in_pounds()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Ofp(units: "kgs", ramp: "11589", paxWeight: "84", payload: "23341"));

        Assert.NotNull(plan);
        Assert.Equal(11589 * 2.20462, plan!.BlockFuelLbs, 6);
        Assert.Equal(84 * 2.20462, plan.PassengerWeightLbs, 6);
        Assert.Equal(23341 * 2.20462, plan.PayloadLbs, 6);
        Assert.Equal(224, plan.Passengers);   // a count, not a weight
    }

    [Fact]
    public void The_units_are_read_whatever_their_case()
    {
        Assert.Equal(25550, Bbs757Loadsheet.ParsePlan(Ofp(units: " LBS "))!.BlockFuelLbs);
        Assert.Equal(1000 * 2.20462, Bbs757Loadsheet.ParsePlan(Ofp(units: "KGS", ramp: "1000"))!.BlockFuelLbs, 6);
    }

    [Fact]
    public void The_actual_passenger_count_is_preferred_and_the_planned_one_stands_in()
    {
        Assert.Equal(200, Bbs757Loadsheet.ParsePlan(Ofp(paxActual: "200", pax: "224"))!.Passengers);
        Assert.Equal(224, Bbs757Loadsheet.ParsePlan(Ofp(paxActual: null, pax: "224"))!.Passengers);
    }

    [Fact]
    public void A_plan_with_no_payload_takes_the_passengers_and_cargo()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Ofp(payload: null, cargo: "12259"));

        Assert.Equal(224 * 175 + 12259, plan!.PayloadLbs);
    }

    [Theory]
    [InlineData("units")]
    [InlineData("ramp")]
    [InlineData("pax")]
    [InlineData("paxWeight")]
    [InlineData("payloadAndCargo")]
    public void A_plan_missing_a_field_it_needs_reads_as_null(string missing)
    {
        string xml = missing switch
        {
            "units" => Ofp(units: null),
            "ramp" => Ofp(ramp: null),
            "pax" => Ofp(paxActual: null, pax: null),
            "paxWeight" => Ofp(paxWeight: null),
            _ => Ofp(payload: null, cargo: null),
        };

        Assert.Null(Bbs757Loadsheet.ParsePlan(xml));
    }

    [Theory]
    [InlineData("tons", "25550", "224")]    // units neither pounds nor kilograms: never guessed
    [InlineData("lbs", "-1", "224")]        // a negative block
    [InlineData("lbs", "abc", "224")]       // not a number
    [InlineData("lbs", "NaN", "224")]
    [InlineData("lbs", "25550", "22.5")]    // a passenger count that is not whole
    [InlineData("lbs", "25550", "-3")]
    public void A_plan_with_a_value_it_cannot_use_reads_as_null(string units, string ramp, string pax)
    {
        Assert.Null(Bbs757Loadsheet.ParsePlan(Ofp(units: units, ramp: ramp, paxActual: pax)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<html><body>Error</body></html>")]
    [InlineData("<OFP><fetch><status>Error: Unknown UserID</status></fetch></OFP>")]
    public void What_is_not_a_plan_reads_as_null(string xml)
    {
        Assert.Null(Bbs757Loadsheet.ParsePlan(xml));
    }

    [Fact]
    public void Null_reads_as_null()
    {
        Assert.Null(Bbs757Loadsheet.ParsePlan(null));
    }

    [Fact]
    public void The_flight_is_the_airline_and_number_and_either_may_be_missing()
    {
        Assert.Equal("1234", Bbs757Loadsheet.ParsePlan(Ofp(airline: null))!.Flight);
        Assert.Equal("QQX", Bbs757Loadsheet.ParsePlan(Ofp(flight: null))!.Flight);
        Assert.Equal("", Bbs757Loadsheet.ParsePlan(Ofp(airline: null, flight: null))!.Flight);
        Assert.Equal("", Bbs757Loadsheet.ParsePlan(Ofp(origin: null))!.Origin);
    }

    [Fact]
    public void A_plan_with_no_aircraft_type_reads_with_an_empty_one()
    {
        Assert.Equal("", Bbs757Loadsheet.ParsePlan(Ofp(aircraft: null))!.AircraftIcao);
    }

    [Fact]
    public void Numbers_read_the_same_under_a_comma_decimal_culture()
    {
        InCulture("de-DE", () =>
        {
            var plan = Bbs757Loadsheet.ParsePlan(Ofp(units: "kgs", ramp: "11589.5"));
            Assert.Equal(11589.5 * 2.20462, plan!.BlockFuelLbs, 6);
        });
    }

    // ---- the aircraft -----------------------------------------------------------------------

    [Fact]
    public void A_plan_for_another_aircraft_is_refused()
    {
        var (loads, refusal) = Bbs757Loadsheet.Compute(Plan(aircraft: "A320"));

        Assert.Null(loads);
        Assert.Equal("Your latest SimBrief plan is for A320, not a 757-200. Create a B752 plan, then try again.", refusal);
    }

    [Fact]
    public void A_757_300_plan_is_refused_too()
    {
        Assert.Equal("Your latest SimBrief plan is for B753, not a 757-200. Create a B752 plan, then try again.",
            Bbs757Loadsheet.Compute(Plan(aircraft: "B753")).Refusal);
    }

    [Fact]
    public void A_plan_with_no_aircraft_type_is_refused()
    {
        Assert.Equal("Your latest SimBrief plan names no aircraft type. Create a B752 plan, then try again.",
            Bbs757Loadsheet.Compute(Plan(aircraft: "")).Refusal);
    }

    [Fact]
    public void The_aircraft_type_is_matched_whatever_its_case()
    {
        Assert.Null(Bbs757Loadsheet.Compute(Plan(aircraft: "b752")).Refusal);
    }

    [Fact]
    public void A_wrong_aircraft_is_refused_before_the_fuel_is_judged()
    {
        Assert.StartsWith("Your latest SimBrief plan is for A388", Bbs757Loadsheet.Compute(Plan(block: 500000, aircraft: "A388")).Refusal);
    }

    // ---- the loads --------------------------------------------------------------------------

    [Fact]
    public void The_measured_plan_spreads_as_BlueBird_spread_it()
    {
        // BlueBird's EFB on 2026-10-25: both wings evenly with the center empty, 176 seats at 175 lb,
        // cargo = payload - passenger weight, the forward hold first up to its 10,300 lb, then aft.
        var loads = Loads(Plan());

        Assert.Equal(12775, loads.LeftLbs);
        Assert.Equal(12775, loads.RightLbs);
        Assert.Equal(0, loads.CenterLbs);
        Assert.Equal(30800, loads.PassengerLbs);
        Assert.Equal(10300, loads.ForwardLbs);
        Assert.Equal(10359, loads.AftLbs);
        Assert.Equal(224, loads.PlannedPassengers);
        Assert.Equal(176, loads.SeatedPassengers);
        Assert.True(loads.PassengersCapped);
    }

    [Fact]
    public void A_block_past_the_wings_fills_them_and_puts_the_rest_in_the_center()
    {
        var loads = Loads(Plan(block: 40000));

        Assert.Equal(14579, loads.LeftLbs);
        Assert.Equal(14579, loads.RightLbs);
        Assert.Equal(10842, loads.CenterLbs);
    }

    [Fact]
    public void A_block_that_fills_every_tank_exactly_loads()
    {
        var loads = Loads(Plan(block: 2 * 14579 + 46390));

        Assert.Equal(46390, loads.CenterLbs);
    }

    [Fact]
    public void Fuel_past_the_tanks_is_refused()
    {
        var (loads, refusal) = Bbs757Loadsheet.Compute(Plan(block: 2 * 14579 + 46390 + 1));

        Assert.Null(loads);
        Assert.Equal("The plan's fuel is more than the tanks hold.", refusal);
    }

    [Fact]
    public void Cargo_past_the_holds_is_refused()
    {
        // 176 x 175 = 30800 in the cabin; 25001 left for holds that take 10300 + 14700.
        var (loads, refusal) = Bbs757Loadsheet.Compute(Plan(payload: 30800 + 25001));

        Assert.Null(loads);
        Assert.Equal("The plan's cargo is more than the holds take.", refusal);
    }

    [Fact]
    public void Cargo_that_fills_both_holds_exactly_loads()
    {
        var loads = Loads(Plan(payload: 30800 + 25000));

        Assert.Equal(10300, loads.ForwardLbs);
        Assert.Equal(14700, loads.AftLbs);
    }

    [Fact]
    public void A_payload_no_heavier_than_the_passengers_leaves_the_holds_empty()
    {
        var loads = Loads(Plan(pax: 150, payload: 20000));   // 150 x 175 = 26250, more than the payload

        Assert.Equal(26250, loads.PassengerLbs);
        Assert.Equal(0, loads.ForwardLbs);
        Assert.Equal(0, loads.AftLbs);
    }

    [Fact]
    public void Fewer_passengers_than_seats_all_sit_and_the_cargo_fills_the_forward_hold_first()
    {
        var loads = Loads(Plan(pax: 150, payload: 40000));

        Assert.Equal(150, loads.SeatedPassengers);
        Assert.False(loads.PassengersCapped);
        Assert.Equal(26250, loads.PassengerLbs);
        Assert.Equal(10300, loads.ForwardLbs);
        Assert.Equal(3450, loads.AftLbs);
    }

    [Fact]
    public void Cargo_the_forward_hold_takes_goes_there_alone()
    {
        // The evening's load (task 11e step 0): 114 passengers at 185 lb, 3,990 lb of cargo, all forward.
        var loads = Loads(Plan(block: 16400, pax: 114, paxWeight: 185, payload: 21090 + 3990));

        Assert.Equal(21090, loads.PassengerLbs);
        Assert.Equal(3990, loads.ForwardLbs);
        Assert.Equal(0, loads.AftLbs);
    }

    [Fact]
    public void An_empty_plan_loads_nothing()
    {
        var loads = Loads(Plan(block: 0, pax: 0, payload: 0));

        Assert.Equal(0, loads.LeftLbs + loads.RightLbs + loads.CenterLbs + loads.PassengerLbs + loads.ForwardLbs + loads.AftLbs);
    }

    private static bool IsWhole(double v) => v == Math.Floor(v);

    [Fact]
    public void An_odd_block_puts_the_odd_pound_in_the_left_wing_never_alone_in_the_center()
    {
        // A 1 lb center tank is unmeasured on BlueBird and could trip a fuel-configuration message.
        var loads = Loads(Plan(block: 25551, payload: 51460));

        Assert.Equal(12776, loads.LeftLbs);
        Assert.Equal(12775, loads.RightLbs);
        Assert.Equal(0, loads.CenterLbs);
        Assert.Equal(10300, loads.ForwardLbs);
        Assert.Equal(10360, loads.AftLbs);
    }

    [Fact]
    public void A_block_the_wings_take_exactly_leaves_the_center_empty()
    {
        var loads = Loads(Plan(block: 2 * 14579));

        Assert.Equal((14579.0, 14579.0, 0.0), (loads.LeftLbs, loads.RightLbs, loads.CenterLbs));
    }

    [Fact]
    public void One_pound_past_the_wings_goes_in_the_center()
    {
        var loads = Loads(Plan(block: 2 * 14579 + 1));

        Assert.Equal((14579.0, 14579.0, 1.0), (loads.LeftLbs, loads.RightLbs, loads.CenterLbs));
    }

    [Fact]
    public void A_kilogram_plan_loads_whole_pounds_that_add_up_to_its_rounded_totals()
    {
        var plan = Bbs757Loadsheet.ParsePlan(Ofp(units: "kgs", ramp: "11589", paxActual: "150", paxWeight: "84", payload: "18341"))!;
        var loads = Loads(plan);

        Assert.All(new[] { loads.LeftLbs, loads.RightLbs, loads.CenterLbs, loads.PassengerLbs, loads.ForwardLbs, loads.AftLbs },
            v => Assert.True(IsWhole(v), v.ToString(CultureInfo.InvariantCulture)));
        Assert.Equal(Math.Round(plan.BlockFuelLbs, MidpointRounding.AwayFromZero), loads.LeftLbs + loads.RightLbs + loads.CenterLbs);
        Assert.Equal(Math.Round(150 * plan.PassengerWeightLbs, MidpointRounding.AwayFromZero), loads.PassengerLbs);
        Assert.Equal(Math.Round(plan.PayloadLbs - loads.PassengerLbs, MidpointRounding.AwayFromZero), loads.ForwardLbs + loads.AftLbs);
        Assert.InRange(loads.LeftLbs - loads.RightLbs, 0, 1);   // an odd pound to the left wing
        Assert.Equal(0, loads.CenterLbs);
    }

    [Fact]
    public void A_fractional_plan_under_the_wings_capacity_never_puts_fuel_in_the_center()
    {
        foreach (double block in new[] { 0.4, 0.6, 1.4, 1.6, 2.5, 3.5, 25550.5, 29157.6, 29158.4 })
        {
            var loads = Loads(Plan(block: block));
            Assert.True(loads.CenterLbs == 0, block.ToString(CultureInfo.InvariantCulture));
            Assert.InRange(loads.LeftLbs - loads.RightLbs, 0, 1);
            Assert.Equal(Math.Round(block, MidpointRounding.AwayFromZero), loads.LeftLbs + loads.RightLbs + loads.CenterLbs);
        }
    }

    // ---- the read-back ----------------------------------------------------------------------

    [Fact]
    public void The_read_back_variables_are_the_tanks_the_density_and_the_four_stations()
    {
        var byKey = Bbs757Loadsheet.ReadBackVariables.ToDictionary(v => v.Key);

        Assert.Equal(8, byKey.Count);
        Assert.Equal(("FUEL TANK LEFT MAIN QUANTITY", "gallons"), (byKey[Bbs757Loadsheet.LeftFuelKey].SimVar, byKey[Bbs757Loadsheet.LeftFuelKey].Units));
        Assert.Equal(("FUEL TANK RIGHT MAIN QUANTITY", "gallons"), (byKey[Bbs757Loadsheet.RightFuelKey].SimVar, byKey[Bbs757Loadsheet.RightFuelKey].Units));
        Assert.Equal(("FUEL TANK CENTER QUANTITY", "gallons"), (byKey[Bbs757Loadsheet.CenterFuelKey].SimVar, byKey[Bbs757Loadsheet.CenterFuelKey].Units));
        Assert.Equal(("FUEL WEIGHT PER GALLON", "pounds"), (byKey[Bbs757Loadsheet.FuelDensityKey].SimVar, byKey[Bbs757Loadsheet.FuelDensityKey].Units));
        Assert.Equal(("PAYLOAD STATION WEIGHT:3", "pounds"), (byKey[Bbs757Loadsheet.FirstClassKey].SimVar, byKey[Bbs757Loadsheet.FirstClassKey].Units));
        Assert.Equal(("PAYLOAD STATION WEIGHT:4", "pounds"), (byKey[Bbs757Loadsheet.EconomyKey].SimVar, byKey[Bbs757Loadsheet.EconomyKey].Units));
        Assert.Equal(("PAYLOAD STATION WEIGHT:5", "pounds"), (byKey[Bbs757Loadsheet.ForwardHoldKey].SimVar, byKey[Bbs757Loadsheet.ForwardHoldKey].Units));
        Assert.Equal(("PAYLOAD STATION WEIGHT:6", "pounds"), (byKey[Bbs757Loadsheet.AftHoldKey].SimVar, byKey[Bbs757Loadsheet.AftHoldKey].Units));
    }

    [Fact]
    public void The_read_back_variables_are_registered_quiet_on_request_and_on_no_panel()
    {
        // ReadFreshAsync delivers only a registered key; read for the spoken result alone, so no
        // Ctrl+M row ([VAR-9]) and no panel.
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var onPanels = def.GetPanelControls().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var v in Bbs757Loadsheet.ReadBackVariables)
        {
            Assert.True(vars.ContainsKey(v.Key), v.Key);
            var d = vars[v.Key];
            Assert.Equal(v.SimVar, d.Name);
            Assert.Equal(v.Units, d.Units);
            Assert.Equal(SimVarType.SimVar, d.Type);
            Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
            Assert.False(d.IsAnnounced);
            Assert.True(d.ExcludeFromMonitorManager);
            Assert.DoesNotContain(v.Key, onPanels);
        }
    }

    [Fact]
    public void The_read_back_is_taken_by_key_and_a_missing_read_stays_missing()
    {
        var values = new Dictionary<string, double?>
        {
            [Bbs757Loadsheet.LeftFuelKey] = 1906.7,
            [Bbs757Loadsheet.RightFuelKey] = 1906.8,
            [Bbs757Loadsheet.CenterFuelKey] = 0,
            [Bbs757Loadsheet.FuelDensityKey] = 6.7,
            [Bbs757Loadsheet.FirstClassKey] = 2800,
            [Bbs757Loadsheet.EconomyKey] = null,
            [Bbs757Loadsheet.ForwardHoldKey] = 10300,
        };

        var read = Bbs757Loadsheet.ReadBack(values);

        Assert.Equal(1906.7, read.LeftGallons);
        Assert.Equal(1906.8, read.RightGallons);
        Assert.Equal(0, read.CenterGallons);
        Assert.Equal(6.7, read.PoundsPerGallon);
        Assert.Equal(2800, read.FirstClassLbs);
        Assert.Null(read.EconomyLbs);
        Assert.Equal(10300, read.ForwardHoldLbs);
        Assert.Null(read.AftHoldLbs);   // not read at all
    }

    [Fact]
    public void The_totals_are_pounds_and_unread_when_any_part_is()
    {
        // The evening's load once done: 1,850 + 19,240 lb in the cabin, 3,990 lb forward.
        var read = new Bbs757LoadReadBack(1000, 1000, 500, 6.7, 1850, 19240, 3990, 0);

        Assert.Equal(2500 * 6.7, read.FuelLbs!.Value, 6);
        Assert.Equal(21090, read.PassengerLbs);
        Assert.Equal(3990, read.CargoLbs);
        Assert.Null((read with { PoundsPerGallon = null }).FuelLbs);
        Assert.Null((read with { CenterGallons = null }).FuelLbs);
        Assert.Null((read with { FirstClassLbs = null }).PassengerLbs);
        Assert.Null((read with { AftHoldLbs = null }).CargoLbs);
    }

    // ---- what is said -----------------------------------------------------------------------

    [Fact]
    public void The_fuel_is_said_in_pounds_rounded_to_ten_its_total_the_sum_of_its_parts()
    {
        // BlueBird converts at about 6.72 lb/gal and the sim reads 6.70: 12775 loaded reads back lower.
        var read = ReadBackOf(Loads(Plan())) with { LeftGallons = 12775 / 6.72, RightGallons = 12775 / 6.72 };

        Assert.Equal("Fuel 25480 pounds: left 12740, right 12740, center 0.", Bbs757Loadsheet.FuelSentence(read, Bbs757WeightUnits.Pounds));
    }

    [Fact]
    public void The_passengers_and_the_cargo_are_said_in_pounds()
    {
        var read = ReadBackOf(Loads(Plan()));

        Assert.Equal("Passengers 30800 pounds.", Bbs757Loadsheet.PassengersSentence(read, Bbs757WeightUnits.Pounds));
        Assert.Equal("Cargo 20660 pounds: forward 10300, aft 10360.", Bbs757Loadsheet.CargoSentence(read, Bbs757WeightUnits.Pounds));
    }

    [Fact]
    public void A_kilogram_aircraft_hears_kilograms()
    {
        var read = ReadBackOf(Loads(Plan()));

        Assert.Equal("Fuel 11580 kilograms: left 5790, right 5790, center 0.", Bbs757Loadsheet.FuelSentence(read, Bbs757WeightUnits.Kilograms));
        Assert.Equal("Passengers 13970 kilograms.", Bbs757Loadsheet.PassengersSentence(read, Bbs757WeightUnits.Kilograms));
        Assert.Equal("Cargo 9370 kilograms: forward 4670, aft 4700.", Bbs757Loadsheet.CargoSentence(read, Bbs757WeightUnits.Kilograms));
    }

    [Fact]
    public void Unread_aircraft_units_are_said_in_pounds()
    {
        var read = ReadBackOf(Loads(Plan()));

        Assert.Equal("Passengers 30800 pounds.", Bbs757Loadsheet.PassengersSentence(read, null));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_sentences_are_the_same_under_a_comma_decimal_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var read = ReadBackOf(Loads(Plan())) with { LeftGallons = 12775 / 6.72, RightGallons = 12775 / 6.72 };
            Assert.Equal("Fuel 25480 pounds: left 12740, right 12740, center 0.", Bbs757Loadsheet.FuelSentence(read, Bbs757WeightUnits.Pounds));
            Assert.Equal("Cargo 9370 kilograms: forward 4670, aft 4700.", Bbs757Loadsheet.CargoSentence(read, Bbs757WeightUnits.Kilograms));
        });
    }

    [Fact]
    public void A_tank_read_a_hair_below_zero_says_zero()
    {
        var read = ReadBackOf(Loads(Plan())) with { CenterGallons = -0.0001 };

        string said = Bbs757Loadsheet.FuelSentence(read, Bbs757WeightUnits.Pounds)!;

        Assert.EndsWith("center 0.", said);
        Assert.DoesNotContain("-0", said);
    }

    [Fact]
    public void A_group_with_a_part_unread_has_no_sentence()
    {
        var read = ReadBackOf(Loads(Plan()));

        Assert.Null(Bbs757Loadsheet.FuelSentence(read with { PoundsPerGallon = null }, Bbs757WeightUnits.Pounds));
        Assert.Null(Bbs757Loadsheet.FuelSentence(read with { RightGallons = null }, Bbs757WeightUnits.Pounds));
        Assert.Null(Bbs757Loadsheet.PassengersSentence(read with { EconomyLbs = null }, Bbs757WeightUnits.Pounds));
        Assert.Null(Bbs757Loadsheet.CargoSentence(read with { ForwardHoldLbs = null }, Bbs757WeightUnits.Pounds));
    }

    // ---- the log ----------------------------------------------------------------------------

    [Fact]
    public void The_log_line_is_numbers_only()
    {
        var loads = Loads(Plan());
        var read = ReadBackOf(loads) with { LeftGallons = 12775 / 6.72, RightGallons = 12775 / 6.72, AftHoldLbs = null };

        Assert.Equal("SimBrief load fueling complete: targets 12775/12775/0, 30800, 10300/10359; " +
                     "now 12737/12737/0, 30800, 10300/-", Bbs757Loadsheet.LogLine("fueling complete", loads, read));
    }

    [Fact]
    public void The_start_is_logged_with_the_targets_alone()
    {
        Assert.Equal("SimBrief load started: targets 12775/12775/0, 30800, 10300/10359",
            Bbs757Loadsheet.LogLine("started", Loads(Plan()), null));
    }
}

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's load watch (task 11e): after the EFB window taps IMPORT FROM SIMBRIEF and START BOARDING,
/// BlueBird fuels in about two minutes and boards at its realistic pace (under 40 minutes, measured
/// 2026-10-25), and the watch, reading every 15 s, says once each when the fuel and the boarding are
/// done; after an hour it says what has not finished. The judgment is pure
/// (<see cref="Bbs757LoadWatch"/>); the definition owns the timing loop.
/// </summary>
public class Bbs757LoadWatchTests
{
    // The plan BlueBird loaded on the evening of 2026-10-25: a 16,400 lb block, 114 passengers at
    // 185 lb, 3,990 lb of cargo, all forward.
    private static readonly Bbs757Loads Plan = new(8200, 8200, 0, 21090, 3990, 0, 114, 114);

    private const string Fueled = "Fueling complete. Fuel 16400 pounds: left 8200, right 8200, center 0.";

    // Nothing about the ACARS weight and balance: the aircraft's systems request it by themselves once boarding completes (the
    // owner's ruling of 2026-10-30), so the WGTBAL sentence that ended this was dropped.
    private const string Boarded = "Boarding complete. Passengers 21090 pounds. Cargo 3990 pounds: forward 3990, aft 0.";

    /// <summary>A read in pounds (one pound per "gallon"), each wing holding <paramref name="wing"/>.</summary>
    private static Bbs757LoadReadBack Read(double wing, double center = 0, double first = 0, double economy = 0,
        double forward = 0, double aft = 0) => new(wing, wing, center, 1, first, economy, forward, aft);

    /// <summary>The plan's fuel, passengers and cargo, as BlueBird left them once done.</summary>
    private static Bbs757LoadReadBack Loaded => Read(8200, first: 1850, economy: 19240, forward: 3990);

    /// <summary>The plan's fuel aboard and the cabin and holds as <paramref name="first"/> to <paramref name="aft"/>.</summary>
    private static Bbs757LoadReadBack Boarding(double first, double economy, double forward = 0, double aft = 0)
        => Read(8200, first: first, economy: economy, forward: forward, aft: aft);

    /// <summary>The aircraft's units as the watch reads them each time it speaks: pounds, as most tests have them.</summary>
    private static readonly Func<Bbs757WeightUnits?> Pounds = () => Bbs757WeightUnits.Pounds;

    /// <summary>A watch whose fuel is said already, so a test hears the boarding alone.</summary>
    private static Bbs757LoadWatch FueledWatch(Bbs757Loads? plan = null)
    {
        var watch = new Bbs757LoadWatch(plan ?? Plan, Pounds);
        var full = Read(((plan ?? Plan).LeftLbs + (plan ?? Plan).RightLbs) / 2);
        watch.Observe(full, 0);
        Assert.StartsWith("Fueling complete.", watch.Observe(full, 15_000));
        return watch;
    }

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

    // ---- the fuel ---------------------------------------------------------------------------

    [Fact]
    public void Fueling_complete_is_said_once_the_fuel_reaches_the_plan_and_holds_still()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);

        Assert.Null(watch.Observe(Read(6554.5), 0));       // 13,109 lb at the start: seeds the watch
        Assert.Null(watch.Observe(Read(7500), 15_000));     // fueling
        Assert.Null(watch.Observe(Read(8173), 30_000));     // 16,346 lb, within 1% of the plan, but 1,346 lb on
        Assert.Equal("Fueling complete. Fuel 16340 pounds: left 8170, right 8170, center 0.",
            watch.Observe(Read(8172), 45_000));              // 2 lb less: the APU burns, so never "unchanged"
        Assert.True(watch.FuelDone);
        Assert.False(watch.Finished);
        Assert.Null(watch.Observe(Read(8171), 60_000));     // said once
    }

    [Fact]
    public void The_first_read_never_completes_even_at_the_plan()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);

        Assert.Null(watch.Observe(Read(8200), 0));
        Assert.Equal(Fueled, watch.Observe(Read(8200), 15_000));
    }

    [Fact]
    public void Fuel_still_moving_twenty_pounds_in_a_tank_is_not_complete()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        watch.Observe(Read(8170), 0);

        Assert.Null(watch.Observe(Read(8190), 15_000));     // 20 lb more in each wing: still fueling
        Assert.StartsWith("Fueling complete.", watch.Observe(Read(8190), 30_000));
    }

    [Fact]
    public void Fuel_moving_between_tanks_at_the_plans_total_is_not_complete_until_every_tank_settles()
    {
        // The final review's minor 2: a turnaround whose fuel is already near the plan. BlueBird moves fuel
        // between tanks (session 5: it filled the wings while draining the center), so the total holds
        // still while the tanks do not.
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        watch.Observe(Read(7000, center: 2400), 0);

        Assert.Null(watch.Observe(Read(7600, center: 1200), 15_000));
        Assert.Null(watch.Observe(Read(8200, center: 0), 30_000));
        Assert.Equal(Fueled, watch.Observe(Read(8200, center: 0), 45_000));
    }

    [Fact]
    public void A_slow_transfer_between_tanks_counts_as_started()
    {
        // The fuel's total holds still while the tanks trade fuel: that is loading, never "not started".
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        for (int i = 0; i <= 16; i++)
        {
            string? said = watch.Observe(
                Read(7000 + 50 * i, center: 2400 - 100 * i, first: 1850, economy: 19240, forward: 3990), i * 15_000L);
            Assert.True(said == null || !said.Contains("not started"), said);
        }
        Assert.False(watch.FuelDone);   // the tanks never settled
    }

    [Fact]
    public void Fuel_that_stops_short_of_the_plan_is_not_complete()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        watch.Observe(Read(8000), 0);

        // 16,000 lb is 400 short: more than the larger of 1% (164) and 100 lb.
        Assert.Null(watch.Observe(Read(8000), 15_000));
        Assert.False(watch.FuelDone);
    }

    [Theory]
    [InlineData(16236, true)]    // the plan less 1% (164 lb)
    [InlineData(16234, false)]
    [InlineData(16564, true)]    // over as well as under
    [InlineData(16566, false)]
    public void The_fuel_counts_within_one_percent_of_the_plan_or_100_pounds(double total, bool complete)
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        watch.Observe(Read(total / 2), 0);

        Assert.Equal(complete, watch.Observe(Read(total / 2), 15_000) != null);
    }

    [Fact]
    public void A_small_block_counts_within_100_pounds()
    {
        var small = new Bbs757Loads(2000, 2000, 0, 0, 0, 0, 0, 0);   // 1% of 4,000 is 40: the 100 lb floor
        var watch = new Bbs757LoadWatch(small, Pounds);
        watch.Observe(Read(1950), 0);

        Assert.StartsWith("Fueling complete.", watch.Observe(Read(1950), 15_000));
    }

    // ---- the boarding -----------------------------------------------------------------------

    [Fact]
    public void Boarding_complete_is_said_once_the_cabin_and_the_cargo_reach_the_plan_and_hold_still()
    {
        var watch = FueledWatch();

        Assert.Null(watch.Observe(Boarding(1850, 9000), 30_000));   // boarding
        Assert.Null(watch.Observe(Loaded, 45_000));                 // all aboard, just now
        Assert.Equal(Boarded, watch.Observe(Loaded, 60_000));
        Assert.True(watch.BoardingDone);
        Assert.True(watch.Finished);
        Assert.Null(watch.Observe(Loaded, 75_000));
    }

    [Fact]
    public void The_cargo_is_judged_as_a_total_whichever_hold_BlueBird_fills()
    {
        var watch = FueledWatch();
        watch.Observe(Boarding(1850, 19240, forward: 2000, aft: 1990), 30_000);

        Assert.Equal("Boarding complete. Passengers 21090 pounds. Cargo 3990 pounds: forward 2000, aft 1990.",
            watch.Observe(Boarding(1850, 19240, forward: 2000, aft: 1990), 45_000));
    }

    [Fact]
    public void A_cabin_one_passenger_short_is_not_complete()
    {
        // One passenger (185 lb) is more than the cabin's tolerance (0.5% of 21,090: 105 lb).
        var watch = FueledWatch();
        watch.Observe(Boarding(1850, 19055, forward: 3990), 30_000);

        Assert.Null(watch.Observe(Boarding(1850, 19055, forward: 3990), 45_000));
        Assert.False(watch.BoardingDone);
    }

    [Fact]
    public void Cargo_still_loading_is_not_complete()
    {
        var watch = FueledWatch();
        watch.Observe(Boarding(1850, 19240, forward: 3975), 30_000);   // within 20 lb, still moving

        Assert.Null(watch.Observe(Boarding(1850, 19240, forward: 3980), 45_000));
        Assert.Equal(Boarded.Replace("Cargo 3990 pounds: forward 3990", "Cargo 3980 pounds: forward 3980"),
            watch.Observe(Boarding(1850, 19240, forward: 3980), 60_000));
    }

    [Fact]
    public void Both_done_at_one_read_are_said_together_the_fuel_first()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        watch.Observe(Loaded, 0);

        Assert.Equal(Fueled + " " + Boarded, watch.Observe(Loaded, 15_000));
        Assert.True(watch.Finished);
    }

    [Fact]
    public void A_plan_with_more_passengers_than_seats_says_the_cap_when_boarded()
    {
        var capped = new Bbs757Loads(12775, 12775, 0, 30800, 10300, 10359, 224, 176);
        var watch = FueledWatch(capped);
        var aboard = Read(12775, first: 2800, economy: 28000, forward: 10300, aft: 10359);
        watch.Observe(aboard, 30_000);

        // The cap is the phrase's last sentence.
        Assert.Equal("Boarding complete. Passengers 30800 pounds. Cargo 20660 pounds: forward 10300, aft 10360. " +
                     "The plan's 224 passengers are capped at the 757's 176 seats; the rest of the payload goes in the holds.",
            watch.Observe(aboard, 45_000));
    }

    // ---- a load that never starts -----------------------------------------------------------

    [Fact]
    public void A_load_that_never_starts_says_so_after_three_minutes_and_ends()
    {
        // The final review's important 1: with BBS Nest closed BlueBird ignores every tap. Otherwise
        // fueling and boarding start at once (measured), so nothing moving for three minutes means the load
        // never started.
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        for (long t = 0; t < 180_000; t += 15_000)
            Assert.Null(watch.Observe(Read(5000), t));

        Assert.Equal("BlueBird has not started loading. Is BBS Nest running? Check the fuel with F and the weight with W.",
            watch.Observe(Read(5000), 180_000));
        Assert.True(watch.Finished);
        Assert.Null(watch.Observe(Read(5000), 195_000));
    }

    [Fact]
    public void Fueling_that_never_starts_is_said_and_the_boarding_is_still_watched()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        watch.Observe(Read(5000), 0);
        watch.Observe(Read(5000, first: 1850, economy: 2000), 90_000);   // boarding under way

        Assert.Equal("BlueBird has not started fueling. Check the fuel with F.",
            watch.Observe(Read(5000, first: 1850, economy: 4000), 180_000));
        Assert.False(watch.Finished);

        watch.Observe(Read(5000, first: 1850, economy: 19240, forward: 3990), 1_200_000);
        Assert.Equal(Boarded, watch.Observe(Read(5000, first: 1850, economy: 19240, forward: 3990), 1_215_000));
        Assert.True(watch.Finished);
    }

    [Fact]
    public void Boarding_that_never_starts_is_said_once_the_fuel_is_done()
    {
        var watch = FueledWatch();   // the cabin and the holds empty all along

        Assert.Null(watch.Observe(Read(8200), 165_000));
        Assert.Equal("BlueBird has not started boarding. Check the weight with W.", watch.Observe(Read(8200), 180_000));
        Assert.True(watch.Finished);
    }

    // ---- what cannot be read, and the hour ---------------------------------------------------

    [Fact]
    public void An_unread_value_neither_completes_nor_counts_as_the_previous_read()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        watch.Observe(Read(8200), 0);

        Assert.Null(watch.Observe(Read(8200) with { PoundsPerGallon = null }, 15_000));
        Assert.Equal(Fueled, watch.Observe(Read(8200), 30_000));   // judged against the read at 0
    }

    [Fact]
    public void Boarding_unfinished_after_an_hour_says_so_once_and_the_watch_ends()
    {
        var watch = FueledWatch();
        var halfway = Boarding(1850, 5000);

        Assert.Null(watch.Observe(halfway, Bbs757LoadWatch.TimeoutMs - 1));
        Assert.Equal("Boarding has not finished after an hour. Check the weight with W.",
            watch.Observe(halfway, Bbs757LoadWatch.TimeoutMs));
        Assert.True(watch.Finished);
        Assert.Null(watch.Observe(Loaded, Bbs757LoadWatch.TimeoutMs + 15_000));
        Assert.Null(watch.Observe(Loaded, Bbs757LoadWatch.TimeoutMs + 30_000));
    }

    [Fact]
    public void Fuel_and_boarding_unfinished_after_an_hour_say_both()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        watch.Observe(Read(5000), 0);
        watch.Observe(Read(5100, first: 500), 165_000);   // both under way, slowly

        Assert.Equal("Fueling and boarding have not finished after an hour. Check the fuel with F and the weight with W.",
            watch.Observe(Read(5200, first: 1000), Bbs757LoadWatch.TimeoutMs));
        Assert.True(watch.Finished);
    }

    [Fact]
    public void Fuel_unfinished_after_an_hour_with_the_boarding_done_says_the_fuel()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds);
        var shortOfFuel = Loaded with { LeftGallons = 5000, RightGallons = 5000 };
        watch.Observe(shortOfFuel, 0);
        Assert.Equal(Boarded, watch.Observe(shortOfFuel, 15_000));
        watch.Observe(shortOfFuel with { LeftGallons = 5200, RightGallons = 5200 }, 165_000);   // fueling, slowly

        Assert.Equal("Fueling has not finished after an hour. Check the fuel with F.",
            watch.Observe(shortOfFuel with { LeftGallons = 5400, RightGallons = 5400 }, Bbs757LoadWatch.TimeoutMs));
    }

    [Fact]
    public void The_hour_is_one_hour_the_start_is_judged_at_three_minutes_and_the_reads_fifteen_seconds_apart()
    {
        Assert.Equal(60 * 60_000, Bbs757LoadWatch.TimeoutMs);
        Assert.Equal(3 * 60_000, Bbs757LoadWatch.NotStartedMs);
        Assert.Equal(15_000, Bbs757LoadWatch.IntervalMs);
    }

    // ---- units and culture ------------------------------------------------------------------

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private const string FueledInKilograms = "Fueling complete. Fuel 7440 kilograms: left 3720, right 3720, center 0.";

    [Fact]
    public void A_kilogram_aircraft_hears_kilograms()
    {
        var watch = new Bbs757LoadWatch(Plan, () => Bbs757WeightUnits.Kilograms);
        watch.Observe(Read(8200), 0);

        Assert.Equal(FueledInKilograms, watch.Observe(Read(8200), 15_000));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_units_are_read_when_a_sentence_is_said_not_when_the_watch_began(string culture)
    {
        // In-sim check 11 (2026-10-30): FETCH SIMBRIEF OFP pressed with the aircraft in kilograms; BlueBird's units question came a
        // moment later and its YES switched the aircraft to pounds. Read once when the watch began, both sentences came in kilograms.
        InCulture(culture, () =>
        {
            Bbs757WeightUnits? units = Bbs757WeightUnits.Kilograms;
            int reads = 0;
            var watch = new Bbs757LoadWatch(Plan, () =>
            {
                reads++;
                return units;
            });

            Assert.Null(watch.Observe(Read(6000), 0));                                                   // the hook, in kilograms
            units = Bbs757WeightUnits.Pounds;                                                            // the question's YES
            Assert.Null(watch.Observe(Read(7000, first: 1000, economy: 9000, forward: 2000), 15_000));
            Assert.Null(watch.Observe(Loaded, 30_000));
            Assert.Equal(0, reads);                                                                      // nothing said, nothing read

            Assert.Equal(Fueled + " " + Boarded, watch.Observe(Loaded, 45_000));
            Assert.Equal(2, reads);                                                                      // one read for each sentence
            Assert.True(watch.Finished);
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Each_sentence_is_said_in_the_units_of_its_moment(string culture)
    {
        // The fueling done while the aircraft was in kilograms, then the units switched: the boarding is said in pounds.
        InCulture(culture, () =>
        {
            Bbs757WeightUnits? units = Bbs757WeightUnits.Kilograms;
            var watch = new Bbs757LoadWatch(Plan, () => units);
            watch.Observe(Read(8200), 0);
            Assert.Equal(FueledInKilograms, watch.Observe(Read(8200), 15_000));

            units = Bbs757WeightUnits.Pounds;
            Assert.Null(watch.Observe(Loaded, 30_000));
            Assert.Equal(Boarded, watch.Observe(Loaded, 45_000));
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Units_that_cannot_be_read_are_said_in_pounds(string culture)
    {
        InCulture(culture, () =>
        {
            var watch = new Bbs757LoadWatch(Plan, () => null);
            watch.Observe(Loaded, 0);
            Assert.Equal(Fueled + " " + Boarded, watch.Observe(Loaded, 15_000));
        });
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_announcements_are_the_same_under_a_comma_decimal_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var watch = new Bbs757LoadWatch(Plan, Pounds);
            watch.Observe(Loaded, 0);
            Assert.Equal(Fueled + " " + Boarded, watch.Observe(Loaded, 15_000));
        });
    }

    // ---- the parts of the load a watch watches (plan 18, task 6) --------------------------------

    /// <summary>
    /// Fuel that is always loading and never settles: it creeps up for three minutes, then wanders by 30 lb a tank at every
    /// read. Neither "not started" nor "complete" ever fits it, so the watch stays alive for as long as a test needs.
    /// </summary>
    private static double Wandering(long atMs)
    {
        long read = atMs / 15_000;
        return 5000 + 40 * Math.Min(read, 12) + (read % 2 == 0 ? 0 : 30);
    }

    [Fact]
    public void A_watch_of_both_says_as_today()
    {
        // No parts named means both, and naming both changes nothing: fuel then boarding, then each never starting.
        var asDefault = new Bbs757LoadWatch(Plan, Pounds);
        var named = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Both);
        Assert.Equal(Bbs757LoadParts.Both, asDefault.Parts);
        Assert.Equal(Bbs757LoadParts.Both, named.Parts);

        foreach (var watch in new[] { asDefault, named })
        {
            Assert.Null(watch.Observe(Read(6000), 0));
            Assert.Null(watch.Observe(Read(7000, first: 500, economy: 3000, forward: 1000), 15_000));
            Assert.Null(watch.Observe(Read(8200, first: 1000, economy: 8000, forward: 2000), 30_000));   // at the plan, but 1,200 lb on
            Assert.Equal(Fueled, watch.Observe(Read(8200, first: 1500, economy: 14000, forward: 3000), 45_000));
            Assert.Null(watch.Observe(Loaded, 60_000));
            Assert.Equal(Boarded, watch.Observe(Loaded, 75_000));
            Assert.True(watch.FuelDone);
            Assert.True(watch.BoardingDone);
            Assert.True(watch.Finished);
        }

        foreach (var watch in new[] { new Bbs757LoadWatch(Plan, Pounds), new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Both) })
        {
            for (long t = 0; t < 180_000; t += 15_000) Assert.Null(watch.Observe(Read(5000), t));
            Assert.Equal("BlueBird has not started loading. Is BBS Nest running? Check the fuel with F and the weight with W.",
                watch.Observe(Read(5000), 180_000));
            Assert.True(watch.FuelGaveUp);
            Assert.True(watch.BoardingGaveUp);
            Assert.True(watch.Finished);
        }
    }

    [Fact]
    public void Fuel_only_never_says_boarding_has_not_started()
    {
        // IMPORT FROM SIMBRIEF alone: the cabin and the holds stay empty for ten minutes, and that is not the watch's to say.
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel);
        var said = new List<string>();
        for (long t = 0; t <= 600_000; t += 15_000)
            if (watch.Observe(Read(Wandering(t)), t) is { } text) said.Add(text);

        Assert.Empty(said);
        Assert.Equal(Bbs757LoadParts.Fuel, watch.Parts);
        Assert.False(watch.BoardingGaveUp);
        Assert.False(watch.Finished);   // the fuel is still loading

        // The fuel reaches the plan: that is all this watch had to say, and it ends.
        Assert.Null(watch.Observe(Read(8200), 615_000));
        Assert.Equal(Fueled, watch.Observe(Read(8200), 630_000));
        Assert.True(watch.Finished);
        Assert.False(watch.BoardingDone);
        Assert.False(watch.BoardingGaveUp);
    }

    [Fact]
    public void A_fuel_only_watch_whose_fuel_never_starts_says_only_fueling_and_ends()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel);
        for (long t = 0; t < 180_000; t += 15_000) Assert.Null(watch.Observe(Read(5000), t));

        // Not the "loading" sentence, which names BBS Nest for fuel and boarding both standing still.
        Assert.Equal("BlueBird has not started fueling. Check the fuel with F.", watch.Observe(Read(5000), 180_000));
        Assert.True(watch.FuelGaveUp);
        Assert.False(watch.BoardingGaveUp);
        Assert.True(watch.Finished);
    }

    [Fact]
    public void A_fuel_only_watch_still_loading_after_an_hour_says_only_the_fuel()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel);
        for (long t = 0; t < Bbs757LoadWatch.TimeoutMs; t += 15_000) Assert.Null(watch.Observe(Read(Wandering(t)), t));

        Assert.Equal("Fueling has not finished after an hour. Check the fuel with F.",
            watch.Observe(Read(Wandering(Bbs757LoadWatch.TimeoutMs)), Bbs757LoadWatch.TimeoutMs));
        Assert.True(watch.Finished);
    }

    [Fact]
    public void Boarding_only_never_says_fueling_complete_though_the_tanks_sit_at_the_plan()
    {
        // START BOARDING alone: the tanks are at the plan and settled from the first read. A fuel watch would say so at the
        // second read; this one is about the boarding and says nothing of the fuel.
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Boarding);

        Assert.Null(watch.Observe(Read(8200), 0));
        Assert.Null(watch.Observe(Read(8200, first: 1000, economy: 8000, forward: 2000), 15_000));
        Assert.Null(watch.Observe(Loaded, 30_000));
        Assert.Equal(Boarded, watch.Observe(Loaded, 45_000));
        Assert.False(watch.FuelDone);
        Assert.True(watch.Finished);
    }

    [Fact]
    public void A_boarding_only_watch_whose_boarding_never_starts_says_only_boarding_and_ends()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Boarding);
        for (long t = 0; t < 180_000; t += 15_000) Assert.Null(watch.Observe(Read(8200), t));

        Assert.Equal("BlueBird has not started boarding. Check the weight with W.", watch.Observe(Read(8200), 180_000));
        Assert.True(watch.BoardingGaveUp);
        Assert.False(watch.FuelGaveUp);
        Assert.False(watch.FuelDone);
        Assert.True(watch.Finished);
    }

    [Fact]
    public void A_boarding_only_watch_still_boarding_after_an_hour_says_only_the_boarding()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Boarding);
        for (long t = 0; t < Bbs757LoadWatch.TimeoutMs; t += 15_000)
            Assert.Null(watch.Observe(Read(8200, first: 100 + t / 15_000, economy: 0), t));   // a pound or so more at every read

        Assert.Equal("Boarding has not finished after an hour. Check the weight with W.",
            watch.Observe(Read(8200, first: 100 + Bbs757LoadWatch.TimeoutMs / 15_000, economy: 0), Bbs757LoadWatch.TimeoutMs));
        Assert.True(watch.Finished);
    }

    [Fact]
    public void Boarding_added_later_is_judged_from_its_own_start()
    {
        // IMPORT FROM SIMBRIEF, then START BOARDING a minute later. The fuel keeps loading, so the watch lives; the cabin and the
        // holds never move. The fuel's start is judged three minutes after the import (and fuel is loading), the boarding's three
        // minutes after ITS press: not at 180 s, when the boarding is two minutes old.
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel);
        var said = new List<(long At, string Text)>();
        for (long t = 0; t <= 300_000; t += 15_000)
        {
            if (t == 60_000) watch.Include(Bbs757LoadParts.Boarding, t);
            if (watch.Observe(Read(Wandering(t)), t) is { } text) said.Add((t, text));
        }

        Assert.Equal(new[] { (240_000L, "BlueBird has not started boarding. Check the weight with W.") }, said);
        Assert.Equal(Bbs757LoadParts.Both, watch.Parts);
        Assert.True(watch.BoardingGaveUp);
        Assert.False(watch.FuelGaveUp);
        Assert.False(watch.Finished);   // the fuel is still loading
    }

    [Fact]
    public void A_part_added_later_is_seeded_by_its_own_first_read()
    {
        // The payload is aboard already when START BOARDING is pressed. As at the start of a watch, the first read of the part
        // only seeds, so "Boarding complete." comes with the second.
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel);
        Bbs757LoadReadBack WithPayload(long t) => Read(Wandering(t), first: 1850, economy: 19240, forward: 3990);
        for (long t = 0; t < 60_000; t += 15_000) Assert.Null(watch.Observe(WithPayload(t), t));

        watch.Include(Bbs757LoadParts.Boarding, 60_000);

        Assert.Null(watch.Observe(WithPayload(60_000), 60_000));
        Assert.Equal(Boarded, watch.Observe(WithPayload(75_000), 75_000));
        Assert.True(watch.BoardingDone);
        Assert.False(watch.Finished);   // the fuel is still loading
    }

    [Fact]
    public void Fuel_added_to_a_boarding_watch_is_judged_from_its_own_start_too()
    {
        // START BOARDING first, IMPORT FROM SIMBRIEF two minutes later: the tanks stand still, and that is said three minutes
        // after the import, not at 180 s.
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Boarding);
        var said = new List<(long At, string Text)>();
        for (long t = 0; t <= 330_000; t += 15_000)
        {
            if (t == 120_000) watch.Include(Bbs757LoadParts.Fuel, t);
            // The cabin keeps filling (a pound at a time, never settled), so the boarding is under way.
            if (watch.Observe(Read(5000, first: 100 + t / 15_000, economy: 0), t) is { } text) said.Add((t, text));
        }

        Assert.Equal(new[] { (300_000L, "BlueBird has not started fueling. Check the fuel with F.") }, said);
        Assert.True(watch.FuelGaveUp);
        Assert.False(watch.BoardingGaveUp);
    }

    [Fact]
    public void Including_a_part_already_watched_keeps_its_start()
    {
        // A second IMPORT press while the first is watched: the fuel is still judged from the first.
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel);
        watch.Observe(Read(5000), 0);
        watch.Include(Bbs757LoadParts.Fuel, 120_000);
        for (long t = 15_000; t < 180_000; t += 15_000) Assert.Null(watch.Observe(Read(5000), t));

        Assert.Equal("BlueBird has not started fueling. Check the fuel with F.", watch.Observe(Read(5000), 180_000));
        Assert.Equal(Bbs757LoadParts.Fuel, watch.Parts);
    }

    [Fact]
    public void Parts_only_ever_grow_by_the_parts_included()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel);

        watch.Include(Bbs757LoadParts.None, 15_000);
        Assert.Equal(Bbs757LoadParts.Fuel, watch.Parts);
        watch.Include((Bbs757LoadParts)4, 15_000);   // no such part
        Assert.Equal(Bbs757LoadParts.Fuel, watch.Parts);
        watch.Include(Bbs757LoadParts.Boarding, 30_000);
        Assert.Equal(Bbs757LoadParts.Both, watch.Parts);
        watch.Include(Bbs757LoadParts.Both, 45_000);
        Assert.Equal(Bbs757LoadParts.Both, watch.Parts);
    }

    [Fact]
    public void A_watch_of_no_parts_is_finished_and_silent()
    {
        var watch = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.None);

        Assert.True(watch.Finished);
        Assert.Null(watch.Observe(Loaded, 0));
        Assert.Null(watch.Observe(Loaded, 15_000));
        Assert.False(watch.FuelDone);
        Assert.False(watch.BoardingDone);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_part_watched_alone_says_the_same_under_a_comma_decimal_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var fuel = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel);
            fuel.Observe(Loaded, 0);
            Assert.Equal(Fueled, fuel.Observe(Loaded, 15_000));

            var boarding = new Bbs757LoadWatch(Plan, Pounds, Bbs757LoadParts.Boarding);
            boarding.Observe(Loaded, 0);
            Assert.Equal(Boarded, boarding.Observe(Loaded, 15_000));
        });
    }

    // ---- the definition's watch -------------------------------------------------------------

    /// <summary>The eight load variables behind the definition's reads, and a clock its waits move on.</summary>
    private sealed class LoadSim
    {
        public long Now;
        public readonly Dictionary<string, double?> Values = new(StringComparer.Ordinal);
        public readonly List<int> Waits = new();
        public bool WaitForever;

        /// <summary>Each wait stays pending until <see cref="Release"/> lets it pass, so a test can act between two reads.</summary>
        public bool Manual;

        private TaskCompletionSource? _gate;
        private int _gateMs;

        /// <summary>Lets the pending wait pass: the clock moves on and the watch reads again before this returns.</summary>
        public void Release()
        {
            var gate = _gate ?? throw new InvalidOperationException("No wait is pending.");
            _gate = null;
            Now += _gateMs;
            gate.SetResult();
        }

        public void Set(Bbs757LoadReadBack r)
        {
            Values[Bbs757Loadsheet.LeftFuelKey] = r.LeftGallons;
            Values[Bbs757Loadsheet.RightFuelKey] = r.RightGallons;
            Values[Bbs757Loadsheet.CenterFuelKey] = r.CenterGallons;
            Values[Bbs757Loadsheet.FuelDensityKey] = r.PoundsPerGallon;
            Values[Bbs757Loadsheet.FirstClassKey] = r.FirstClassLbs;
            Values[Bbs757Loadsheet.EconomyKey] = r.EconomyLbs;
            Values[Bbs757Loadsheet.ForwardHoldKey] = r.ForwardHoldLbs;
            Values[Bbs757Loadsheet.AftHoldKey] = r.AftHoldLbs;
        }

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => true,
            ReadFresh = (key, ct) => Task.FromResult(Values.TryGetValue(key, out double? v) ? v : null),
            Cached = _ => null,
            Write = _ => { },
            RequestRead = _ => { },
            Delay = (ms, ct) =>
            {
                Waits.Add(ms);
                if (WaitForever) return Task.Delay(Timeout.Infinite, ct);
                ct.ThrowIfCancellationRequested();
                if (Manual)
                {
                    _gateMs = ms;
                    var gate = _gate = new TaskCompletionSource();
                    ct.Register(() => gate.TrySetCanceled(ct));
                    return gate.Task;
                }
                Now += ms;
                return Task.CompletedTask;
            },
        };
    }

    private static (BBS757Definition Def, SpeechCapture Speech) Watching(LoadSim sim)
        => (new BBS757Definition { NestRunning = () => true, NowMs = () => sim.Now, WriteIo = _ => sim.Io }, new SpeechCapture());

    [Fact]
    public void The_definition_says_each_completion_once_as_a_background_announcement()
    {
        var sim = new LoadSim();
        sim.Set(Loaded);
        var (def, speech) = Watching(sim);

        Assert.True(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Both, new SimConnectManager(IntPtr.Zero), speech));

        Assert.Equal(new[] { Fueled + " " + Boarded }, speech.All);
        Assert.Empty(speech.Interrupts);            // never cuts the pilot off
        Assert.False(def.LoadWatchRunning);         // both said: the watch is over
        Assert.Equal(new[] { Bbs757LoadWatch.IntervalMs }, sim.Waits);
    }

    [Fact]
    public void The_definitions_watch_speaks_in_the_units_read_as_it_speaks()
    {
        // MainForm hands the watch a reader of BlueBird's units: kilograms when the hook came, pounds by the time the load is aboard
        // (the units question answered meanwhile), so the sentences are in pounds.
        var sim = new LoadSim { Manual = true };
        sim.Set(Read(6000));
        var (def, speech) = Watching(sim);
        Bbs757WeightUnits? units = Bbs757WeightUnits.Kilograms;

        Assert.True(def.StartLoadWatch(Plan, () => units, Bbs757LoadParts.Both, new SimConnectManager(IntPtr.Zero), speech));
        units = Bbs757WeightUnits.Pounds;
        sim.Set(Loaded);
        sim.Release();   // 15 s: the load aboard, against the read at the hook
        sim.Release();   // 30 s: settled

        Assert.Equal(new[] { Fueled + " " + Boarded }, speech.All);
        Assert.False(def.LoadWatchRunning);
    }

    [Fact]
    public void A_load_that_never_starts_is_given_up_after_three_minutes_of_reads_fifteen_seconds_apart()
    {
        var sim = new LoadSim();
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);

        def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Both, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { "BlueBird has not started loading. Is BBS Nest running? Check the fuel with F and the weight with W." },
            speech.All);
        Assert.Equal(12, sim.Waits.Count);   // reads at 0, 15, …, 180 s
        Assert.All(sim.Waits, w => Assert.Equal(15_000, w));
        Assert.False(def.LoadWatchRunning);
    }

    [Fact]
    public void A_second_load_while_the_watch_runs_joins_it_and_starts_no_second_watch()
    {
        var sim = new LoadSim { WaitForever = true };
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);

        Assert.True(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel, new SimConnectManager(IntPtr.Zero), speech));
        Assert.True(def.LoadWatchRunning);
        Assert.True(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Boarding, new SimConnectManager(IntPtr.Zero), speech));

        Assert.Single(sim.Waits);   // one loop: one read made, one wait pending
        def.OnSwitchedAway();
    }

    [Fact]
    public void A_load_of_no_parts_starts_no_watch()
    {
        var sim = new LoadSim { WaitForever = true };
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);

        Assert.False(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.None, new SimConnectManager(IntPtr.Zero), speech));

        Assert.False(def.LoadWatchRunning);
        Assert.Empty(sim.Waits);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_part_added_to_the_running_watch_is_judged_from_the_moment_it_was_added()
    {
        // IMPORT FROM SIMBRIEF, then START BOARDING a minute later. Neither moves anything (BBS Nest closed, say): the fuel is
        // judged three minutes after the import, the boarding three minutes after ITS press.
        var sim = new LoadSim { Manual = true };
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);
        Assert.True(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel, new SimConnectManager(IntPtr.Zero), speech));
        for (int i = 0; i < 4; i++) sim.Release();
        Assert.Equal(60_000, sim.Now);

        Assert.True(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Boarding, new SimConnectManager(IntPtr.Zero), speech));
        Assert.True(def.LoadWatchRunning);
        Assert.Empty(speech.All);
        while (def.LoadWatchRunning) sim.Release();

        Assert.Equal(new[]
        {
            "BlueBird has not started fueling. Check the fuel with F.",
            "BlueBird has not started boarding. Check the weight with W.",
        }, speech.All);
        Assert.Equal(240_000, sim.Now);   // the last read: 180 s after the boarding was added
        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void A_part_added_after_the_watch_ended_gets_a_watch_of_its_own()
    {
        var sim = new LoadSim();
        sim.Set(Loaded);
        var (def, speech) = Watching(sim);

        Assert.True(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Fuel, new SimConnectManager(IntPtr.Zero), speech));
        Assert.Equal(new[] { Fueled }, speech.All);
        Assert.False(def.LoadWatchRunning);   // the fuel was all there was to watch

        Assert.True(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Boarding, new SimConnectManager(IntPtr.Zero), speech));
        Assert.Equal(new[] { Fueled, Boarded }, speech.All);
        Assert.False(def.LoadWatchRunning);
    }

    [Fact]
    public void A_flight_load_or_a_lost_connection_ends_the_watch_silently()
    {
        var sim = new LoadSim { WaitForever = true };
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);
        def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Both, new SimConnectManager(IntPtr.Zero), speech);

        def.OnSimContextReset();

        Assert.False(def.LoadWatchRunning);
        Assert.Empty(speech.All);
        Assert.True(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Both, new SimConnectManager(IntPtr.Zero), speech));   // a new load may start
        def.OnSwitchedAway();
    }

    [Fact]
    public void A_profile_switch_ends_the_watch_and_none_starts_after_it()
    {
        var sim = new LoadSim { WaitForever = true };
        sim.Set(Read(5000));
        var (def, speech) = Watching(sim);
        def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Both, new SimConnectManager(IntPtr.Zero), speech);

        def.OnSwitchedAway();

        Assert.False(def.LoadWatchRunning);
        Assert.False(def.StartLoadWatch(Plan, Pounds, Bbs757LoadParts.Both, new SimConnectManager(IntPtr.Zero), speech));
        Assert.Empty(speech.All);
    }
}

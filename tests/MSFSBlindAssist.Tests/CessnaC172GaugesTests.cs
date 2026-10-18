// The Cessna 172's engine, fuel and electrical readings are spoken with the zone each sits in on the
// G1000's own engine page, from its panel.xml's colour zones (C172-18). Every boundary is pinned on
// its literal number, and every spoken text under a comma-decimal culture.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Aircraft.CessnaC172Gauges;

namespace MSFSBlindAssist.Tests;

public class CessnaC172GaugesTests
{
    [Theory]
    [InlineData(0, "red, below minimum")]
    [InlineData(20, "red, below minimum")]      // the CAS's OIL PRESSURE is at or below 20
    [InlineData(30, "below the green arc")]     // uncoloured, with the red arc beyond it
    [InlineData(50, "green")]
    [InlineData(90, "green")]
    [InlineData(100, "above the green arc")]
    [InlineData(115, "red, above maximum")]
    public void Oil_pressure_zones(double psi, string band) => Assert.Equal(band, Band(psi, OilPressure));

    [Theory]
    [InlineData(78, "")]                        // cold oil: no arc below the green, so just the number
    [InlineData(100, "green")]
    [InlineData(245, "green")]                  // the red blink starts above 245
    [InlineData(246, "red, above maximum")]
    public void Oil_temperature_zones(double f, string band) => Assert.Equal(band, Band(f, OilTemperature));

    [Theory]
    [InlineData(0, "red, below minimum")]
    [InlineData(1.5, "yellow, low")]            // the gauge draws yellow from 1.5 up
    [InlineData(3, "yellow, low")]
    [InlineData(5, "green")]
    [InlineData(24, "green")]
    [InlineData(26, "")]                        // a full tank is above the green with nothing beyond
    public void Fuel_quantity_zones(double gallons, string band) => Assert.Equal(band, Band(gallons, FuelQuantity));

    [Theory]
    [InlineData(4.3, "")]                       // measured at idle: below the green, nothing beyond
    [InlineData(4.5, "green")]
    [InlineData(5.5, "green")]
    [InlineData(6, "")]
    public void Vacuum_zones(double inHg, string band) => Assert.Equal(band, Band(inHg, Vacuum));

    [Theory]
    [InlineData(0.35, "green")]
    [InlineData(12, "green")]
    [InlineData(14, "")]
    public void Fuel_flow_zones(double gph, string band) => Assert.Equal(band, Band(gph, FuelFlow));

    // The arc as its green top: 2500 LowAlt, 2600 MedAlt, 2700 HighAlt (RpmArc is internal).
    private static RpmArc Arc(int greenTop) => greenTop switch { 2500 => RpmArc.LowAlt, 2600 => RpmArc.MedAlt, _ => RpmArc.HighAlt };

    [Theory]
    [InlineData(2500, 622, "")]                      // idle: no arc below the green
    [InlineData(2500, 2100, "green")]
    [InlineData(2500, 2500, "green")]
    [InlineData(2500, 2600, "above the green arc")]
    [InlineData(2500, 2700, "red, above maximum")]   // the red arc begins at 2700; the redline warning is above it
    [InlineData(2600, 2600, "green")]
    [InlineData(2600, 2650, "above the green arc")]
    [InlineData(2700, 2700, "green")]
    [InlineData(2700, 2750, "red, above maximum")]
    public void Rpm_zones_follow_the_arc(int greenTop, double rpm, string band)
    {
        Assert.Equal(greenTop, RpmGreenTop(Arc(greenTop)));
        Assert.Equal(band, Band(rpm, Rpm(Arc(greenTop))));
    }

    [Theory]
    [InlineData(2500, 5300, 2500)]
    [InlineData(2500, 5301, 2600)]
    [InlineData(2600, 4700, 2600)]          // the hysteresis: back down only below 4700
    [InlineData(2600, 4699, 2500)]
    [InlineData(2600, 10301, 2700)]
    [InlineData(2700, 9700, 2700)]
    [InlineData(2700, 9699, 2600)]
    [InlineData(2500, 12000, 2700)]         // a load at altitude steps through to the end
    [InlineData(2700, 300, 2500)]
    public void The_rpm_arc_follows_the_altitude(int fromTop, double feet, int toTop) =>
        Assert.Equal(toTop, RpmGreenTop(NextRpmArc(Arc(fromTop), feet)));

    [Fact]
    public void Spoken_readings_keep_a_dot_in_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("622 rpm", DescribeRpm(622.13, Arc(2500)));
            Assert.Equal("66 psi, green", DescribeOilPressure(66.05));
            Assert.Equal("78 degrees fahrenheit", DescribeOilTemperature(77.77));
            Assert.Equal("528 degrees fahrenheit", DescribeTemperature(528.4));
            Assert.Equal("0.3 gallons per hour, green", DescribeFuelFlow(0.349));
            Assert.Equal("4.3 inches", DescribeVacuum(4.3457));
            Assert.Equal("11.2 gallons, green", DescribeFuelQuantity(11.2255));
            Assert.Equal("28.0 volts", DescribeVolts(28));
            Assert.Equal("0.6 hours", DescribeHours(0.6037));
            Assert.Equal("Left 11.2, Right 12.9, Total 24.1 gallons", DescribeFuelQuantities(11.2255, 12.9));
            Assert.Equal("Left 11.2, Right 12.9, Total 24.1 gallons, Flow 8.0 gallons per hour, Endurance 3.0 hours",
                DescribeFuelInfo(11.2255, 12.9, 8));
            Assert.Equal("100 percent", CessnaC172BaseDefinition.Percent(99.6));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(0, "Flaps Up")]
    [InlineData(1, "Flaps 10 degrees")]
    [InlineData(2, "Flaps 20 degrees")]
    [InlineData(3, "Flaps Full")]               // measured 30 degrees
    public void The_flap_notches_are_the_measured_ones(double index, string said) =>
        Assert.Equal(said, DescribeFlaps(index));

    [Fact]
    public void Altimeter_and_weight_read_as_the_da40_says_them_in_any_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

            // 29.92 is never "standard": a typed 29.92 and the G1000's STD are one number to the sim.
            Assert.Equal("Altimeter 29.92 inches, 1013 hectopascals", DescribeAltimeter(1013.25));
            Assert.Equal("Altimeter 30.12 inches, 1020 hectopascals", DescribeAltimeter(1020));
            Assert.Equal("Gross weight 943 kilograms, 2078 pounds. Maximum 1160 kilograms, 2558 pounds",
                DescribeGrossWeight(2078.17, 2558));
            Assert.Equal("Gross weight 943 kilograms, 2078 pounds", DescribeGrossWeight(2078.17, null));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // C172-19: the cache holds an on-request value's LAST fetch, however old, or nothing; a key that
    // read flaps, the altimeter or the autopilot's vertical speed from it said "Requesting" and nothing
    // more, or a value from before it last changed. Every literal cache lookup in the C172's code must
    // name a variable the batch keeps current.
    [Fact]
    public void Every_cache_lookup_names_a_variable_the_batch_keeps_current()
    {
        string dir = Path.Combine(RepoRoot(), "MSFSBlindAssist", "Aircraft", "C172");
        var vars = new CessnaC172G1000Definition().GetVariables();
        var lookup = new System.Text.RegularExpressions.Regex(@"GetCachedVariableValue\(""([A-Z0-9_]+)""\)");
        int checkedCount = 0;
        foreach (string file in Directory.GetFiles(dir, "*.cs"))
        {
            foreach (System.Text.RegularExpressions.Match m in lookup.Matches(File.ReadAllText(file)))
            {
                string key = m.Groups[1].Value;
                Assert.True(vars.TryGetValue(key, out var v), $"{Path.GetFileName(file)}: {key} is not a variable");
                Assert.True(v!.UpdateFrequency == SimConnect.UpdateFrequency.Continuous && v.IsAnnounced,
                    $"{Path.GetFileName(file)} reads {key} from the cache, but it is not Continuous and IsAnnounced: read it fresh");
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 0);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException("MSFSBlindAssist.sln not found");
    }

    [Fact]
    public void A_stopped_engine_gives_no_endurance()
    {
        Assert.Equal("Left 10.0, Right 10.0, Total 20.0 gallons, Flow 0.0 gallons per hour",
            DescribeFuelInfo(10, 10, 0));
        Assert.Equal("Left 10.0, Right 10.0, Total 20.0 gallons", DescribeFuelInfo(10, 10, null));
    }

    [Fact]
    public void Readings_compose_into_one_sentence_leaving_out_what_is_missing()
    {
        Assert.Equal("622 rpm. Fuel flow 0.3 gallons per hour, green.",
            CessnaC172BaseDefinition.ComposeReadings(new[]
            {
                ("", (string?)"622 rpm"), ("Fuel flow", "0.3 gallons per hour, green"), ("Throttle", null)
            }));
        Assert.Null(CessnaC172BaseDefinition.ComposeReadings(new[] { ("Oil", (string?)null) }));
    }

    [Fact]
    public void The_panels_show_each_reading_with_its_zone()
    {
        var def = new CessnaC172G1000Definition();

        Assert.True(def.TryGetDisplayOverride("C172_OIL_PRESSURE", 66, out string oil));
        Assert.Equal("66 psi, green", oil);
        Assert.True(def.TryGetDisplayOverride("C172_MAIN_BUS_VOLTS", 28, out string bus));
        Assert.Equal("28.0 volts", bus);
        Assert.True(def.TryGetDisplayOverride("C172_ENGINE_RPM", 2600, out string rpm));
        Assert.Equal("2600 rpm, above the green arc", rpm);    // low altitude until one arrives
    }

    [Theory]
    [InlineData("C172_MAIN_BUS_VOLTS")]
    [InlineData("C172_ESS_BUS_VOLTS")]
    [InlineData("C172_CHT")]
    [InlineData("C172_ENGINE_HOURS")]
    [InlineData("C172_OIL_TEMP")]
    [InlineData("C172_VACUUM")]
    public void A_silent_readout_is_cached_swallowed_and_has_no_row(string key)
    {
        var def = new CessnaC172G1000Definition();
        var v = def.GetVariables()[key];

        // In the cache the hotkeys and the Alt+S window read (DA40S-1).
        Assert.Equal(SimConnect.UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced);
        // Never spoken by itself, so no Ctrl+M row (VAR-9).
        var said = new GatedSpeechCapture();
        Assert.True(def.ProcessSimVarUpdate(key, 1, said));
        Assert.Empty(said.All);
        Assert.DoesNotContain(key, MonitorRowBuilder.Build(def.GetVariables()).Select(r => r.Key));
    }
}

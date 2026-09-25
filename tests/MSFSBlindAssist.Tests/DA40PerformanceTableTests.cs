using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The COWS POH's performance tables, pinned cell by cell where it matters.
///
/// These are TRANSCRIBED FROM AN IMAGE - the POH has no text layer - so the risk here is
/// not logic, it is a digit read wrong off a page. What is asserted is therefore the
/// corners and the shape of each table rather than its arithmetic: the first and last row,
/// the widest and narrowest columns, and every place the manual leaves a cell blank.
/// </summary>
public class DA40PerformanceTableTests
{
    // ------------------------------------------------------------------ NG minimum load

    [Theory]
    // Sea level: 94 through the three cold columns, 96 through the middle three, then down.
    [InlineData(0, -35, 94)]
    [InlineData(0, -10, 94)]
    [InlineData(0, 0, 96)]
    [InlineData(0, 20, 96)]
    [InlineData(0, 30, 95)]
    [InlineData(0, 40, 92)]
    [InlineData(0, 50, 90)]
    // 4000 and 6000 are the same row: 96 all the way to 20 degrees.
    [InlineData(4000, -35, 96)]
    [InlineData(6000, 20, 96)]
    // 10000 is where it falls away fastest.
    [InlineData(10000, -10, 96)]
    [InlineData(10000, 0, 94)]
    [InlineData(10000, 30, 88)]
    public void TheNgMinimumLoadTableIsTranscribed(double alt, double oat, double expected)
        => Assert.Equal(expected, DA40PerformanceTables.NgMinimumLoadPercent(alt, oat));

    [Theory]
    [InlineData(2000, 50)]    // the hot corner the table stops at above sea level
    [InlineData(10000, 40)]
    [InlineData(10000, 50)]
    public void ABlankCellIsReportedAsNoFigureRatherThanGuessed(double alt, double oat)
    {
        Assert.Null(DA40PerformanceTables.NgMinimumLoadPercent(alt, oat));
        Assert.Contains("no minimum load",
            DA40PerformanceTables.DescribeNgMinimumLoad(alt, oat));
    }

    /// <summary>
    /// Both axes snap to the nearest tabulated value. The table is a whole-percent pass/fail
    /// check, so interpolating between its cells would present arithmetic as the manual's.
    /// </summary>
    [Fact]
    public void BothAxesTakeTheNearestRowAndColumnRatherThanInterpolating()
    {
        // 4900 ft is nearer 4000 than 6000; 26 degrees is nearer 30 than 20.
        Assert.Equal(95, DA40PerformanceTables.NgMinimumLoadPercent(4900, 26));
        Assert.Contains("4000 feet, 30 degrees",
            DA40PerformanceTables.DescribeNgMinimumLoad(4900, 26));
    }

    // ------------------------------------------------------------------- XLS cruise

    [Fact]
    public void EveryXlsColumnTheManualPrintsIsPresent()
    {
        var pairs = DA40PerformanceTables.XlsCruise
            .Select(s => (s.PowerPercent, s.Rpm))
            .ToArray();

        // Four columns in the 45-55 table's 45 % half, three in its 55 % half; three at
        // 65 % and two at 75 %. Twelve in all, and the manual prints no others.
        Assert.Equal(12, pairs.Length);
        foreach (var want in new[]
                 {
                     (45, 1800), (45, 2000), (45, 2200), (45, 2400),
                     (55, 2000), (55, 2200), (55, 2400),
                     (65, 2000), (65, 2200), (65, 2400),
                     (75, 2200), (75, 2400)
                 })
        {
            Assert.Contains(want, pairs);
        }
    }

    [Theory]
    // The four corners of the 65-75 table.
    [InlineData(65, 2000, 0, 26.8)]
    [InlineData(65, 2000, 4000, 25.4)]
    [InlineData(65, 2400, 9000, 20.7)]
    [InlineData(75, 2200, 0, 27.3)]
    [InlineData(75, 2400, 5000, 24.1)]
    // And of the 45-55 table, which runs far higher.
    [InlineData(45, 1800, 0, 22.7)]
    [InlineData(45, 2400, 17000, 14.5)]
    [InlineData(55, 2000, 9000, 21.1)]
    [InlineData(55, 2400, 13000, 17.6)]
    public void TheXlsManifoldPressuresAreTranscribed(int pct, int rpm, double alt, double expected)
    {
        var setting = DA40PerformanceTables.XlsSetting(pct, rpm);
        Assert.NotNull(setting);
        Assert.Equal(expected, setting!.ManifoldAt(alt));
    }

    [Theory]
    [InlineData(45, 1800, 5.8, null)]     // no best-power figure at the lowest two settings
    [InlineData(45, 2000, 6.0, null)]
    [InlineData(45, 2200, 6.3, 7.3)]
    [InlineData(55, 2400, 7.5, 8.7)]
    [InlineData(65, 2000, 7.9, null)]
    [InlineData(65, 2400, 8.5, 9.8)]
    [InlineData(75, 2400, 9.5, 11.0)]
    public void TheXlsFuelFlowsAreTranscribed(int pct, int rpm, double economy, double? power)
    {
        var setting = DA40PerformanceTables.XlsSetting(pct, rpm);
        Assert.NotNull(setting);
        Assert.Equal(economy, setting!.BestEconomyGph);
        Assert.Equal(power, setting.BestPowerGph);
    }

    /// <summary>
    /// A column simply stops where the engine can no longer make that power - 65 % at
    /// 2000 RPM ends at 4000 ft. Above it the answer is "not available", never the last
    /// row's figure carried upward.
    /// </summary>
    [Fact]
    public void AColumnThatRunsOutIsNotExtrapolated()
    {
        var setting = DA40PerformanceTables.XlsSetting(65, 2000);
        Assert.NotNull(setting);
        Assert.Equal(25.4, setting!.ManifoldAt(4000));
        Assert.Null(setting.ManifoldAt(8000));
    }

    /// <summary>
    /// Read backwards, which is what a pilot in the cruise has: RPM and manifold pressure
    /// in front of them, wanting to know what they are making.
    /// </summary>
    [Fact]
    public void TheXlsTableAnswersFromTheLiveRpmAndManifoldPressure()
    {
        // 2400 RPM and 22.3 inches at 4000 ft is the 65 % column exactly.
        string text = DA40PerformanceTables.DescribeXlsCruise(4000, 2400, 22.3);
        Assert.Contains("65 percent at 2400 RPM", text);
        Assert.Contains("22.3 inches", text);
        Assert.Contains("8.5 gallons per hour best economy", text);
        Assert.Contains("9.8 best power", text);
    }

    [Fact]
    public void ASettingWithNoBestPowerFigureDoesNotInventOne()
    {
        // 1800 RPM at sea level, 22.7 inches - the 45 % column, economy only.
        string text = DA40PerformanceTables.DescribeXlsCruise(0, 1800, 22.7);
        Assert.Contains("45 percent at 1800 RPM", text);
        Assert.DoesNotContain("best power", text);
    }

    [Fact]
    public void TheCruiseTableSaysSoWhenTheEngineIsNotRunning()
        => Assert.Contains("engine running", DA40PerformanceTables.DescribeXlsCruise(0, 0, 0));

    // ------------------------------------------------------------------ ISA correction

    [Theory]
    [InlineData(0, 15)]        // standard at sea level
    [InlineData(6000, 3)]      // standard at 6000 ft: 15 - 12
    [InlineData(2000, 14)]     // 3 degrees warm, under the threshold
    public void AStandardDayAddsNothing(double alt, double oat)
        => Assert.Equal("", DA40PerformanceTables.IsaCorrection(alt, oat));

    // ------------------------------------------------------------------ where they show

    /// <summary>
    /// One row per airframe, on the panel that airframe's power lever lives on, and never
    /// the other's - the NG has no manifold pressure to look a cruise setting up with, and
    /// the XLS has no load percentage to check against a minimum.
    /// </summary>
    [Fact]
    public void EachAirframeCarriesItsOwnTableAndNotTheOthers()
    {
        var ng = new CowsDA40Definition(DA40Variant.NG);
        var xls = new CowsDA40Definition(DA40Variant.XLS);

        var ngRows = ng.GetPanelDisplayVariables()["Power and Levers"];
        var xlsRows = xls.GetPanelDisplayVariables()["Power and Levers"];

        Assert.Contains("DA40_NG_MIN_LOAD", ngRows);
        Assert.DoesNotContain("DA40_XLS_CRUISE_TABLE", ngRows);

        Assert.Contains("DA40_XLS_CRUISE_TABLE", xlsRows);
        Assert.DoesNotContain("DA40_NG_MIN_LOAD", xlsRows);

        // Both need the temperature, so both carry it.
        Assert.Contains("DA40_PERF_OAT", ngRows);
        Assert.Contains("DA40_PERF_OAT", xlsRows);
    }

    /// <summary>
    /// ⚠️ THE ONLY BATCHED KEY ON AMBIENT TEMPERATURE. Two keys on one SimVar corrupt the
    /// continuous batch, which sorts by name - so this one rides it and the cabin and
    /// ice/pitot readouts stay OnRequest. It is silent: a temperature is a number.
    /// </summary>
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void OnlyOneKeyOnTheOutsideAirTemperatureRidesTheBatch(DA40Variant variant)
    {
        var vars = new CowsDA40Definition(variant).GetVariables();

        var batched = vars
            .Where(kv => kv.Value.Name == "AMBIENT TEMPERATURE"
                      && kv.Value.UpdateFrequency == MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous
                      && kv.Value.IsAnnounced)
            .Select(kv => kv.Key)
            .ToArray();

        Assert.Equal(new[] { "DA40_PERF_OAT" }, batched);
    }

    /// <summary>Before the conditions have been read, the row says so rather than guessing.</summary>
    [Theory]
    [InlineData(DA40Variant.NG, "DA40_NG_MIN_LOAD")]
    [InlineData(DA40Variant.XLS, "DA40_XLS_CRUISE_TABLE")]
    public void TheRowSaysNotAvailableBeforeItHasTheConditions(DA40Variant variant, string key)
    {
        var def = new CowsDA40Definition(variant);
        Assert.True(def.TryGetDisplayOverride(key, 0, out string text));
        Assert.Equal("Not available yet", text);
    }

    [Fact]
    public void AHotDayCostsPowerAndAColdDayGivesIt()
    {
        string hot = DA40PerformanceTables.IsaCorrection(0, 30);
        Assert.Contains("ISA plus 15", hot);
        Assert.Contains("3 percent less power", hot);

        string cold = DA40PerformanceTables.IsaCorrection(0, 0);
        Assert.Contains("ISA minus 15", cold);
        Assert.Contains("3 percent more power", cold);
    }
}

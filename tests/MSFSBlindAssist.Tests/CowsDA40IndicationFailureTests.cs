using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ A QUANTITY WITH AN INDICATION MUST BE READ FROM THE INDICATION, so that when the
/// indication fails the blind pilot loses it exactly as the sighted pilot does.
///
/// COWS model the engine-indication failures as FAILURES_DISP_*, which drive the DISP_
/// variable off while the physics and sensor behind it carry on unchanged. Measured live:
///     FAILURES_DISP_OT      = 1  ->  DISP_OT    87.59 -> 0,       WC_TEMP_OIL_SENS  86.70
///     FAILURES_DISP_VOLT    = 1  ->  DISP_VOLTS 28.14 -> 0
///     FAILURES_DISP_FUEL_T:1= 1  ->  DISP_FT:1  36.73 -> -63.26,  FUEL_TEMP_C:1     36.81
///
/// ⚠️ THE FAILURE SIGNATURE IS NOT ALWAYS ZERO - fuel temperature goes off-scale NEGATIVE
/// where oil temperature and volts go to 0. Never detect a failed indication by comparing
/// against 0.
///
/// This rule was applied to oil, coolant and gearbox and MISSED on fuel temperature in the
/// same sitting, because diesel waxing made FUEL_TEMP_C look like safety information rather
/// than a duplicate. That is exactly why it is a test and not a note.
/// </summary>
public class CowsDA40IndicationFailureTests
{
    /// <summary>
    /// The physics/sensor variables that sit BEHIND an indication this definition already
    /// reads and whose failure it already binds. Binding any of these would read around a
    /// dead gauge.
    /// </summary>
    private static readonly string[] BehindAFailingIndication =
    {
        "WC_TEMP_OIL:1", "WC_TEMP_OIL_SENS:1",       // behind DISP_OT
        "WC_TEMP_WATER:1", "WC_TEMP_WATER_SENS:1",   // behind DISP_WT
        "GC_GEARBOX_TEMPERATURE:1", "WC_TEMP_GC_SENS:1", // behind DISP_GT
        "FUEL_TEMP_C:1", "FUEL_TEMP_C:2",            // behind DISP_FT:1/:2
    };

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoReadoutReadsAroundAFailedIndication(DA40Variant variant)
    {
        var vars = new CowsDA40Definition(variant).GetVariables();

        var offenders = vars
            .Where(kv => BehindAFailingIndication.Contains(kv.Value.Name))
            .Select(kv => $"{kv.Key} -> {kv.Value.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These read the value BEHIND an indication that can fail, so they would show a " +
            "healthy reading off a dead gauge: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheFuelQuantityExceptionIsDeliberateAndStaysDocumented(DA40Variant variant)
    {
        // ⚠️ THE ONE KNOWN EXCEPTION, AND IT IS OLDER THAN THE RULE. DA40_FUEL_*_ACTUAL read
        // the real quantity while DISP_FUEL:1/:2 is the indication and FAILURES_DISP_FUEL:1/:2
        // is bound - so a failed fuel indication IS read around. That was a considered
        // decision, not an oversight: the NG gauge SATURATES at 14 US gal (AFM 2.14.4,
        // measured 18.78 actual against 14.0 indicated), so the indication cannot answer
        // "how much fuel is aboard" even when it is working perfectly.
        //
        // It is pinned here so the exception stays VISIBLE rather than becoming precedent -
        // if it is ever revisited, it should be because someone weighed the saturation
        // against the failure, not because they never noticed the tension.
        var vars = new CowsDA40Definition(variant).GetVariables();
        if (variant != DA40Variant.NG) return;

        Assert.True(vars.ContainsKey("DA40_FUEL_MAIN_ACTUAL"));
        // It reads the STOCK quantity SimVar, not an L:var - which is what makes it the
        // true quantity rather than a second copy of the gauge.
        Assert.Equal("FUEL TANK LEFT MAIN QUANTITY", vars["DA40_FUEL_MAIN_ACTUAL"].Name);
    }
}

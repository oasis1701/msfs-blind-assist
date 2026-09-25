using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// COWS's own auto-start script, narrated. The steps are read from the XLS Logic
/// (the Autostart block): 0 master, 1 strobes, 2 the lower tank, 3 throttle, 4 pump, 5 prime,
/// 6 mixture out, 7 throttle, 8 crank (seven seconds, then it gives up), 9 mixture in,
/// 10 pump off, 11 alternator. A flooded engine jumps 3 → 7; a running one 0 → 9.
/// </summary>
public class DA40AutoStartTests
{
    [Fact]
    public void InactiveIsNotRunning_WhateverTheStepVariableHolds()
        // AUTOSTART_STEP read 1 on the ground with INPUT_START 0: the step is residue.
        => Assert.Equal("Not running", DA40AutoStart.Describe(active: false, step: 1, starterTimer: 1.6));

    [Theory]
    [InlineData(0.0, "Master on, waiting for the PFD")]
    [InlineData(1.1, "Strobes on")]
    [InlineData(2.0, "Selecting the lower tank")]
    [InlineData(3.0, "Throttle to the priming mark")]
    [InlineData(4.0, "Electric pump on")]
    [InlineData(5.0, "Priming")]
    [InlineData(6.0, "Mixture to cut-off")]
    [InlineData(7.0, "Throttle set for the crank")]
    [InlineData(9.0, "Mixture in")]
    [InlineData(10.0, "Electric pump off")]
    [InlineData(11.0, "Alternator on")]
    public void EachStepIsNamed(double step, string expected)
        => Assert.Equal(expected, DA40AutoStart.Describe(true, step, 0));

    [Fact]
    public void CrankingCarriesTheTimerAgainstTheSevenSecondLimit()
        => Assert.Equal("Cranking, 3 of 7 seconds", DA40AutoStart.Describe(true, 8.3, 3.2));

    [Theory]
    [InlineData(11.0, 11.0)]
    [InlineData(8.4, 0.0)]   // measured: the counter read 0 at the end of a start that worked
    public void AnEngineRunningAtTheStopIsComplete_WhateverTheCounterSays(double highest, double atStop)
        => Assert.Equal("Auto-start complete, engine running",
            DA40AutoStart.Outcome(highest, atStop, engineRunning: true));

    [Fact]
    public void StoppingFromTheCrankWithNoFireIsTheTimeout()
        // The script zeroes the step and INPUT_START in the same tick, so at the stop the
        // step already reads 0 - the highest step seen is what tells the timeout apart.
        => Assert.Equal("Auto-start gave up, no fire in 7 seconds of cranking",
            DA40AutoStart.Outcome(highestStepWhileActive: 8.4, stepAtStop: 0, engineRunning: false));

    [Fact]
    public void AnyOtherStopWithoutAnEngineIsJustStopped()
        => Assert.Equal("Auto-start stopped, engine not running",
            DA40AutoStart.Outcome(highestStepWhileActive: 5, stepAtStop: 0, engineRunning: false));
}

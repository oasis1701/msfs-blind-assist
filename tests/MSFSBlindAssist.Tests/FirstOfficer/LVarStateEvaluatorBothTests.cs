using System;
using MSFSBlindAssist.FirstOfficer.Generic;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// <see cref="LVarStateEvaluator.Both"/> is the "unknown is not false" contract every
/// two-input synthetic First Officer field keeps: NaN when either input is unknown.
/// </summary>
public class LVarStateEvaluatorBothTests
{
    private sealed class Probe : LVarStateEvaluator
    {
        public static double Run(double a, double b, Func<double, double, bool> test) => Both(a, b, test);
    }

    [Fact]
    public void An_unknown_input_makes_the_result_unknown()
    {
        Assert.True(double.IsNaN(Probe.Run(double.NaN, 1, (a, b) => a > b)));
        Assert.True(double.IsNaN(Probe.Run(1, double.NaN, (a, b) => a > b)));
    }

    [Fact]
    public void Known_inputs_give_one_when_the_test_holds_else_zero()
    {
        Assert.Equal(1.0, Probe.Run(1, 0, (a, b) => a > b));
        Assert.Equal(0.0, Probe.Run(0, 1, (a, b) => a > b));
    }
}

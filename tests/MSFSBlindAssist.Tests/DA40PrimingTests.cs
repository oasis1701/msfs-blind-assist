using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The XLS priming arithmetic, pinned to the live aircraft at EGNX with the engine hot:
/// evaporation 3.88963, atomisation 1.0 on every cylinder. Every expected figure below was
/// read off the aeroplane or reproduces a figure that was.
/// </summary>
public class DA40PrimingTests
{
    private const double Evap = 3.88963;
    private static readonly double[] Atom = { 1, 1, 1, 1 };

    [Fact]
    public void RequiredAndFloodedAreTheModelsOwnFormulas()
    {
        // 1.5 / 1 / 3.88963 and 1.6 / 3.88963 / 1 — a six percent window.
        Assert.Equal(0.3856, DA40Priming.RequiredPerCylinder(1, Evap), 4);
        Assert.Equal(0.4114, DA40Priming.FloodedPerCylinder(1, Evap), 4);
        Assert.Equal(1.5426, DA40Priming.RequiredTotal(1, Evap), 4);
    }

    [Fact]
    public void TheVendorsPercentageIsReproduced()
    {
        // Read live: system 4.685 g, cylinders 4.756 g, required 1.507 g → the gauge said 78.597.
        Assert.Equal(78.6, DA40Priming.GaugePercent(4.685, 4.756, 1.507), 1);
        // And it caps at 150, which is what it read after a two-second hot prime.
        Assert.Equal(150, DA40Priming.GaugePercent(9.7, 32.0, 1.7));
    }

    [Fact]
    public void AMixtureCutShutdownLeavesTheEngineFlooded()
    {
        // The cylinders as read after the pilot's own shutdown: 1.2 g each, three times the
        // charge — while the vendor's gauge was saying 78 %.
        var cyl = new[] { 1.30636, 1.18515, 1.16882, 1.14722 };
        Assert.Equal(PrimeState.Flooded, DA40Priming.Classify(cyl, Atom, Evap));
    }

    [Fact]
    public void ACrankAtCutOffClearsIt()
    {
        // After the AFM 4.5 (c) crank: 0.00002 g per cylinder.
        var cyl = new[] { 0.00002, 0.00002, 0.00001, 0.00001 };
        Assert.Equal(PrimeState.NotPrimed, DA40Priming.Classify(cyl, Atom, Evap));
    }

    [Fact]
    public void TheCorrectChargeIsPrimedAndNotFlooded()
    {
        // Just over the requirement on every cylinder, and every one under the flooded line.
        var cyl = new[] { 0.39, 0.39, 0.39, 0.39 };
        Assert.Equal(PrimeState.Primed, DA40Priming.Classify(cyl, Atom, Evap));
    }

    [Fact]
    public void OneFloodedCylinderIsAFloodedEngine()
    {
        // The autostart's test is an OR across the four.
        var cyl = new[] { 0.1, 0.1, 0.1, 0.5 };
        Assert.Equal(PrimeState.Flooded, DA40Priming.Classify(cyl, Atom, Evap));
    }

    [Theory]
    [InlineData(PrimeState.Flooded, 4.756, 1.507, "Flooded, 4.8 grams in the cylinders, 1.5 needed")]
    [InlineData(PrimeState.Primed, 1.56, 1.543, "Primed, 1.6 grams in the cylinders, 1.5 needed")]
    [InlineData(PrimeState.NotPrimed, 0.0, 1.543, "Not primed, 0.0 grams in the cylinders, 1.5 needed")]
    public void BothNumbersAreAlwaysSaid(PrimeState state, double cyl, double req, string expected)
        => Assert.Equal(expected, DA40Priming.Describe(state, cyl, req));

    [Fact]
    public void FloodedNamesTheCylindersOverTheirLine()
    {
        // VCBI, 2026-10-15, the AFM's cold-start priming: "Flooded, 2.5 grams in the cylinders,
        // 3.2 needed" - flooded with LESS than the charge, because one cylinder was over its
        // line (the model's OR). Saying which cylinder makes the sentence add up.
        var cyl = new[] { 0.1, 0.1, 0.1, 0.5 };
        Assert.Equal(new[] { 4 }, DA40Priming.FloodedCylinders(cyl, Atom, Evap));
        Assert.Equal("Flooded, cylinder 4 over its line, 0.8 grams in the cylinders, 1.5 needed",
            DA40Priming.Describe(PrimeState.Flooded, cyl.Sum(), 1.5426, DA40Priming.FloodedCylinders(cyl, Atom, Evap)));
    }

    [Fact]
    public void SeveralOrAllFloodedCylindersAreSaidOnce()
    {
        Assert.Equal("Flooded, cylinders 2 and 3 over their line, 1.0 grams in the cylinders, 1.5 needed",
            DA40Priming.Describe(PrimeState.Flooded, 1.0, 1.5426, new[] { 2, 3 }));
        Assert.Equal("Flooded, cylinders 1, 2 and 4 over their line, 1.5 grams in the cylinders, 1.5 needed",
            DA40Priming.Describe(PrimeState.Flooded, 1.5, 1.5426, new[] { 1, 2, 4 }));
        // Every cylinder over is the ordinary flooded engine: no list to add.
        Assert.Equal("Flooded, 4.8 grams in the cylinders, 1.5 needed",
            DA40Priming.Describe(PrimeState.Flooded, 4.756, 1.507, new[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void ACommaDecimalCultureStillSaysAPoint()
    {
        var was = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal("Primed, 1.6 grams in the cylinders, 1.5 needed",
                DA40Priming.Describe(PrimeState.Primed, 1.56, 1.543));
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = was; }
    }
}

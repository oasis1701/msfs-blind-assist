// Characterization tests for IFly737FoComposition — the pure math helpers a later
// task's iFly 737 MAX8 FO state evaluator composes altitude LED windows, reads the
// center fuel tank quantity, and tests switch+light "lit" encoding through. See
// .superpowers/sdd/task-2-brief.md.

namespace MSFSBlindAssist.Tests.FirstOfficer;

using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.SimConnect.IFly;

public class IFly737FoCompositionTests
{
    private static byte[] Buf() => new byte[IFlySdkOffsets.StructSize];
    private static IFlySdkSnapshot Snap(byte[] b) => new(b);

    [Fact]
    public void AltWindow_ComposesDigits()
    {
        var b = Buf();
        b[IFlySdkOffsets.Flight_Altitude_Indicator_10000_Status] = 0;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_1000_Status] = 8;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_100_Status] = 0;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_10_Status] = 0;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_1_Status] = 0;

        double result = IFly737FoComposition.ComposeAltWindow(
            Snap(b),
            IFlySdkOffsets.Flight_Altitude_Indicator_10000_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_1000_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_100_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_10_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_1_Status);

        Assert.Equal(8000.0, result);
    }

    [Fact]
    public void AltWindow_BlankCell_IsNaN()
    {
        var b = Buf();
        b[IFlySdkOffsets.Flight_Altitude_Indicator_10000_Status] = 0;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_1000_Status] = 8;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_100_Status] = 0;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_10_Status] = 0;
        b[IFlySdkOffsets.Flight_Altitude_Indicator_1_Status] = 11; // blank (per generated header: 10:'-', 11:blank)

        double result = IFly737FoComposition.ComposeAltWindow(
            Snap(b),
            IFlySdkOffsets.Flight_Altitude_Indicator_10000_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_1000_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_100_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_10_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_1_Status);

        Assert.True(double.IsNaN(result));
    }

    [Fact]
    public void CenterQty_UsUnits_ParsesPounds()
    {
        var b = Buf();
        int t2 = IFlySdkOffsets.Fuel_Quantity_Indicator_Status + 2 * IFlySdkOffsets.Fuel_Quantity_Indicator_Status_Stride0;
        b[t2 + 0] = 2;
        b[t2 + 1] = 3;
        b[t2 + 2] = 0;
        b[t2 + 3] = 0;
        b[t2 + 4] = 10; // trailing blank cell — FuelQuantityText trims this
        b[IFlySdkOffsets.UNITstyle] = 1; // U.S. System

        double result = IFly737FoComposition.CenterQuantityLbs(Snap(b));

        Assert.Equal(2300.0, result);
    }

    [Fact]
    public void CenterQty_MetricUnits_ConvertsToPounds()
    {
        var b = Buf();
        int t2 = IFlySdkOffsets.Fuel_Quantity_Indicator_Status + 2 * IFlySdkOffsets.Fuel_Quantity_Indicator_Status_Stride0;
        b[t2 + 0] = 1;
        b[t2 + 1] = 0;
        b[t2 + 2] = 0;
        b[t2 + 3] = 0;
        b[t2 + 4] = 10;
        b[IFlySdkOffsets.UNITstyle] = 0; // Metric

        double result = IFly737FoComposition.CenterQuantityLbs(Snap(b));

        Assert.Equal(1000.0 * IFly737FoComposition.KgToLb, result, 3);
    }

    [Fact]
    public void CenterQty_Blank_IsNaN()
    {
        var b = Buf();
        int t2 = IFlySdkOffsets.Fuel_Quantity_Indicator_Status + 2 * IFlySdkOffsets.Fuel_Quantity_Indicator_Status_Stride0;
        for (int i = 0; i < 5; i++) b[t2 + i] = 10; // all blank
        b[IFlySdkOffsets.UNITstyle] = 1;

        double result = IFly737FoComposition.CenterQuantityLbs(Snap(b));

        Assert.True(double.IsNaN(result));
    }

    [Fact]
    public void Lit_ModThreeEncoding()
    {
        Assert.False(IFly737FoComposition.Lit(0));
        Assert.True(IFly737FoComposition.Lit(4));
        Assert.False(IFly737FoComposition.Lit(3));
        Assert.False(IFly737FoComposition.Lit(double.NaN));
    }
}

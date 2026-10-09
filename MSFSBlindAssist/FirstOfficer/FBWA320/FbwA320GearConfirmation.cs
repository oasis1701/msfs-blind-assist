using System;
using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer.FBWA320;

/// <summary>
/// The FBW A32NX (and Headwind A330, which inherits its gear vars) landing gear confirmed
/// from the legs, never the lever alone (FO-13): the A32NX publishes no gear-indicator
/// lights the First Officer can read, but it does publish each leg's extension in percent
/// (the System Display's WHEEL page reads the same vars: above 95 down, below 5 up).
/// UP = handle up AND every leg below 5 %; DOWN = handle down AND every leg above 95 %.
/// NaN whenever the handle or any leg is not yet cached, so an unread field can never
/// read "confirmed". Published as the synthetic fields <see cref="UpField"/> and
/// <see cref="DownField"/> by both FBW-family evaluators.
/// </summary>
public static class FbwA320GearConfirmation
{
    public const string UpField = "FO_GEAR_UP";
    public const string DownField = "FO_GEAR_DOWN";
    /// <summary>Stock gear handle: 0 Up, 1 Down.</summary>
    public const string HandleField = "GEAR_HANDLE_POSITION";

    public static IReadOnlyList<string> LegFields { get; } = new[]
        { "A32NX_GEAR_CENTER_POSITION", "A32NX_GEAR_LEFT_POSITION", "A32NX_GEAR_RIGHT_POSITION" };

    private const double UpBelowPercent = 5.0;
    private const double DownAbovePercent = 95.0;

    public static double UpValue(Func<string, double> read)
    {
        double handle = read(HandleField);
        var legs = LegFields.Select(read).ToList();
        if (double.IsNaN(handle) || legs.Any(double.IsNaN)) return double.NaN;
        return handle < 0.5 && legs.All(l => l < UpBelowPercent) ? 1.0 : 0.0;
    }

    public static double DownValue(Func<string, double> read)
    {
        double handle = read(HandleField);
        var legs = LegFields.Select(read).ToList();
        if (double.IsNaN(handle) || legs.Any(double.IsNaN)) return double.NaN;
        return handle > 0.5 && legs.All(l => l > DownAbovePercent) ? 1.0 : 0.0;
    }
}

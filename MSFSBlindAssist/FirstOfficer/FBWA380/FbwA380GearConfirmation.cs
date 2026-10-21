namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// The FlyByWire A380X landing gear confirmed DOWN from the gear legs, never the lever alone (FO-13):
/// the gear lever (<see cref="HandleField"/>, 0 Up / 1 Down) down AND every gear position L:var over
/// 98 %, the A380 System Display WHEEL page's own green threshold (fbw-a380x SD/Pages/Wheel/elements/
/// Gear.tsx at 255546cb, L66-75; the page draws each side's wing and body gear from the same LEFT or
/// RIGHT var, and the nose from CENTER). NaN whenever the lever or any leg is not yet cached, so an
/// unread field can never read "confirmed". Published as the synthetic <see cref="DownField"/> by
/// <see cref="FbwA380StateEvaluator"/>, read by the Landing Checklist's "Landing gear: DOWN" and by the
/// Landing flow's last step, the read-only gear-down check. The A380 has no After Takeoff checklist, so
/// there is no gear-UP field.
/// </summary>
public static class FbwA380GearConfirmation
{
    public const string DownField = "FO_GEAR_DOWN";

    /// <summary>The A380 gear lever: 0 Up, 1 Down.</summary>
    public const string HandleField = "A32NX_GEAR_HANDLE_POSITION";

    public static IReadOnlyList<string> LegFields { get; } = new[]
        { "A32NX_GEAR_CENTER_POSITION", "A32NX_GEAR_LEFT_POSITION", "A32NX_GEAR_RIGHT_POSITION" };

    private const double DownAbovePercent = 98.0;

    public static double DownValue(Func<string, double> read)
    {
        double handle = read(HandleField);
        var legs = LegFields.Select(read).ToList();
        if (double.IsNaN(handle) || legs.Any(double.IsNaN)) return double.NaN;
        return handle > 0.5 && legs.All(l => l > DownAbovePercent) ? 1.0 : 0.0;
    }
}

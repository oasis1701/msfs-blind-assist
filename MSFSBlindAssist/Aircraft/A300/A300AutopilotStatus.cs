namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The Ctrl+P window's status list: the four FCU windows (worded as the typed values confirm them),
/// the flight mode annunciator's columns and the autopilot, all from the cache. The FMA words are
/// <see cref="A300Fma"/>'s and read as the Displays section's PFD box reads them. The speed, heading
/// and vertical speed windows are on-request readouts (the autopilot also writes them, so they are
/// never announced), so the window asks for them again on each refresh; the altitude window rides
/// the batch. Pure.
/// </summary>
public static class A300AutopilotStatus
{
    public static readonly IReadOnlyList<string> RequestedKeys = new[]
    {
        A300Readouts.SpeedKey, A300Readouts.HeadingKey, A300Readouts.VerticalSpeedKey,
    };

    public static IReadOnlyList<string> Lines(Func<string, double?> value)
    {
        bool isMach = value(A300FcuState.SpeedMachLightKey) is double m && m >= 0.5;
        var lines = new List<string>
        {
            Window(value(A300Readouts.SpeedKey), v => A300FcuWindows.Speed(v, isMach), "Speed"),
            Window(value(A300Readouts.HeadingKey), A300FcuWindows.Heading, "Heading"),
            Window(value(A300Readouts.AltitudeKey), A300FcuWindows.Altitude, "Altitude"),
            Window(value(A300Readouts.VerticalSpeedKey), A300FcuWindows.VerticalSpeed, "Vertical speed"),
        };
        var fma = A300Fma.Read(A300FmaSources.Compose(value));
        if (!fma.IsShown)
            lines.Add("Flight mode annunciator not shown");
        else
        {
            lines.Add($"Thrust mode: {fma.Thrust ?? "blank"}");
            lines.Add($"Pitch mode: {fma.Common ?? fma.Pitch ?? "blank"}");
            lines.Add($"Roll mode: {fma.Common ?? fma.Roll ?? "blank"}");
            lines.Add($"Armed modes: {(fma.Armed.Count == 0 ? "none" : string.Join(", ", fma.Armed))}");
        }
        lines.Add($"Autopilot: {fma.Autopilot ?? "off"}");
        return lines;
    }

    private static string Window(double? value, Func<double, string> words, string name) =>
        value is double v ? words(v) : $"{name} window not read yet";
}

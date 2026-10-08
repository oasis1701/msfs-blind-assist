namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>The altitude alerter takes whole hundreds from 0 to 99 900 feet (Interior.xml 5022-5032).</summary>
public static class Lj35Preselect
{
    public const int Min = 0, Max = 99900, Step = 100;

    public static int Clamp(double feet) => (int)Math.Clamp(Math.Round(feet / Step, MidpointRounding.AwayFromZero) * Step, Min, Max);

    public static (bool ok, string message) Validate(string text)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f)) return (false, "Enter feet.");
        return f is >= Min and <= Max ? (true, "") : (false, "Feet must be 0 to 99900.");
    }
}

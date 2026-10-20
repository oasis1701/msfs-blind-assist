namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>The vendor's protection ladder (Documentation/Pressurization.png) and knob scales (Pressurization.xml).</summary>
public static class Lj35Pressurization
{
    public static string Describe(double cabinAltFt) => cabinAltFt switch
    {
        >= 14000 => "Above 14000: passenger masks would deploy (not simulated)",
        >= 11000 => "Above 11000: cabin altitude limiter active",
        >= 10100 => "Above 10100: warning horn, emergency descent",
        >= 9500 => "Above 9500: emergency pressurization valves (not simulated)",
        >= 8750 => "Above 8750: automatic to manual, CAB ALT lamp",
        _ => "Normal"
    };

    /// <summary>Knob position × 100 + 100 = selected cabin altitude; −10..101 positions.</summary>
    public static int AltitudeKnobPosition(double feet) => (int)Math.Clamp(Math.Round((feet - 100) / 100.0), -10, 101);

    /// <summary>Knob position × 25 + 25 = selected cabin rate; 8..101 positions.</summary>
    public static int RateKnobPosition(double fpm) => (int)Math.Clamp(Math.Round((fpm - 25) / 25.0), 8, 101);

    public static string Category(double c) => c switch { 1 => "Door open", 2 => "Unpressurised", 3 => "Pressurised", _ => "Unknown" };

    public static string Mode(double m) => m switch
    {
        0 => "Ground or unpressurised", 0.5 => "Differential limit", 1 => "Maximum differential",
        2 => "Cabin climbing", 3 => "Cabin descending", _ => "Unknown"
    };
}

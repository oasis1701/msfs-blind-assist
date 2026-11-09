namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The aircraft variables the A300's flight mode annunciator is drawn from, one field per variable
/// (<see cref="A300FmaSources"/> names them). Modes are the A300's own numbers.
/// </summary>
public readonly record struct A300FmaInputs
{
    public int PitchMode { get; init; }          // L:INI_PITCH_MODE
    public int RollMode { get; init; }           // L:INI_ROLL_MODE
    public int CommonMode { get; init; }         // L:INI_PITCH_ROLL_MODE (land, flare, rollout, go-around)
    public int AtMode { get; init; }             // L:INI_at_mode
    public int PitchArmed { get; init; }         // L:INI_PITCH_MODE_ARM
    public int PitchArmed2 { get; init; }        // L:INI_PITCH_MODE_ARM2
    public int RollArmed { get; init; }          // L:INI_ROLL_MODE_ARM
    public int CommonArmed { get; init; }        // L:INI_PITCH_ROLL_MODE_ARM
    public int LastPitchMode { get; init; }      // L:INI_LAST_PITCH_MODE
    public bool AtOn { get; init; }              // L:INI_at_on
    public bool AtMasterSwitch1 { get; init; }   // L:INI_autothrottle_master_switch1
    public bool AtMasterSwitch2 { get; init; }   // L:INI_autothrottle_master_switch2
    public bool IsProfile { get; init; }         // L:INI_IS_PROFILE
    public bool IsMach { get; init; }            // L:INI_Airspeed_is_mach
    public bool TogaLock { get; init; }          // L:INI_TOGA_LOCK_ACTIVE
    public bool ForceThrustLatch { get; init; }  // L:INI_FORCE_THRUST_LATCH
    public bool Ap1 { get; init; }               // L:INI_ap1_on
    public bool Ap2 { get; init; }               // L:INI_ap2_on
    public bool Irs1Aligned { get; init; }       // L:INI_IRS1_ATTITUDE_ALIGNED
    public bool Irs2Aligned { get; init; }       // L:INI_IRS2_ATTITUDE_ALIGNED
    public bool Irs3Aligned { get; init; }       // L:INI_IRS3_ATTITUDE_ALIGNED
    public bool PitchTrim1 { get; init; }        // L:INI_pitch_trim1
    public bool PitchTrim2 { get; init; }        // L:INI_pitch_trim2
    public bool CaptainFdSourceSwitch { get; init; }  // L:INI_capt_switch_fd_fo1
    public bool EssentialBus2Off { get; init; }  // L:INI_ac_essential_bus2_off
}

/// <summary>What the FMA shows, in readable words. Null is a blank column.</summary>
public sealed record A300FmaReading(
    bool IsShown,
    string? Thrust,
    string? Pitch,
    string? Roll,
    string? Common,
    IReadOnlyList<string> Armed,
    string? Autopilot);

/// <summary>
/// Decodes the A300 flight mode annunciator from its mode numbers, the way the A300's own PFD
/// drawing routine (PFD::drawFMA, v1.0.11) does: the thrust column, pitch and roll (or one combined
/// mode across both: LAND, FLARE, ROLLOUT, GO AROUND), the armed modes beneath them, and CMD 1, CMD 2
/// or DUAL. The words are the annunciator's own, read out: "P.CLB" is "Profile climb", "HDG/S" is
/// "Heading select", "ALT*" is "Altitude capture".
///
/// The PFD draws the guidance columns only while a flight guidance channel is available (an IRS
/// aligned, essential bus 2 powered, and pitch trim 2, or pitch trim 1 with the captain's FD source
/// switch); <see cref="A300FmaReading.IsShown"/> is that test. This decoder blanks the thrust column
/// with them, although the PFD keeps it: the mode numbers hold old values while the aircraft is cold
/// and dark, and a thrust mode read out over a blank PFD would be wrong. The autopilot column is
/// always read. Pure.
/// </summary>
public static class A300Fma
{
    /// <summary>Pitch modes during which the speed word reads "Profile speed".</summary>
    private static readonly HashSet<int> ProfileSpeedPitchModes = new() { 3, 5, 12, 13, 15, 20, 22, 24, 25, 26 };

    /// <summary>Pitch modes that hide an armed ALT (the profile climbs and descents).</summary>
    private static readonly HashSet<int> HidesAltitudeArmed = new() { 3, 5, 16, 18, 24, 25 };

    public static A300FmaReading Read(in A300FmaInputs inputs)
    {
        string? autopilot = (inputs.Ap1, inputs.Ap2) switch
        {
            (true, true) => "Dual",
            (true, false) => "CMD 1",
            (false, true) => "CMD 2",
            _ => null,
        };
        if (!IsShown(inputs))
            return new A300FmaReading(false, null, null, null, null, Array.Empty<string>(), autopilot);

        string? common = inputs.CommonMode switch
        {
            1 => "Land",
            2 => "Flare",
            3 => "Rollout",
            4 => "Go around",
            _ => null,
        };
        var armed = new List<string>();
        if (inputs.CommonMode == 0)
        {
            Add(armed, PitchArmedWords(inputs.PitchArmed, inputs.PitchMode));
            Add(armed, inputs.PitchArmed2 switch { 7 => "Glide slope", 13 => "Profile descent", _ => null });
            Add(armed, inputs.RollArmed switch { 1 => "NAV", 2 => "Heading select", 5 => "Localizer", 7 => "VOR", _ => null });
        }
        if (inputs.CommonArmed == 6)
            Add(armed, "Altitude");

        return new A300FmaReading(
            true,
            ThrustWords(inputs),
            common == null ? PitchWords(inputs) : null,
            common == null ? RollWords(inputs.RollMode) : null,
            common,
            armed,
            autopilot);
    }

    /// <summary>Whether the PFD draws the guidance columns at all.</summary>
    public static bool IsShown(in A300FmaInputs inputs) =>
        !inputs.EssentialBus2Off
        && (inputs.Irs1Aligned || inputs.Irs2Aligned || inputs.Irs3Aligned)
        && (inputs.PitchTrim2 || (inputs.PitchTrim1 && inputs.CaptainFdSourceSwitch));

    private static string? ThrustWords(in A300FmaInputs inputs)
    {
        if (inputs.TogaLock || inputs.ForceThrustLatch)
            return "Thrust lock";
        bool masterSwitch = inputs.AtMasterSwitch1 || inputs.AtMasterSwitch2;
        if (masterSwitch && (!inputs.AtOn || inputs.AtMode == 0))
            return "Manual thrust";
        return inputs.AtMode switch
        {
            1 => IsProfilePitch(inputs) ? "Profile speed" : inputs.IsMach ? "Mach" : "Speed",
            2 => "Autothrottle",
            3 or 12 => inputs.IsProfile ? "Profile thrust" : "Thrust",
            5 => "Thrust",
            9 => "Retard",
            13 => "Thrust lock",
            _ => null,
        };
    }

    private static bool IsProfilePitch(in A300FmaInputs inputs) =>
        ProfileSpeedPitchModes.Contains(inputs.PitchMode)
        || (inputs.PitchMode == 9 && inputs.LastPitchMode is 3 or 5 or 24 or 25);

    private static string? PitchWords(in A300FmaInputs inputs) => inputs.PitchMode switch
    {
        1 => "SRS",
        2 or 4 => inputs.IsMach ? "Mach" : "Speed",
        3 or 24 or 27 => "Profile climb",
        5 or 13 or 16 or 25 => "Profile descent",
        6 or 28 or 29 => "Altitude",
        7 => "Glide slope",
        8 => "Vertical speed",
        9 => inputs.LastPitchMode switch
        {
            3 or 24 => "Profile climb",
            5 or 25 => "Profile descent",
            _ => null,
        },
        12 or 14 or 15 or 20 or 26 => "Profile altitude",
        22 => "Altitude capture",
        _ => null,
    };

    private static string? RollWords(int mode) => mode switch
    {
        1 => "NAV",
        2 => "Heading select",
        3 => "Heading hold",
        4 => "Runway",
        5 => "Localizer",
        7 => "VOR",
        8 => "VOR capture",
        _ => null,
    };

    private static string? PitchArmedWords(int armed, int pitchMode) => armed switch
    {
        2 => "Speed",
        3 => "Profile climb",
        5 or 13 or 21 => "Profile descent",
        6 => HidesAltitudeArmed.Contains(pitchMode) ? null : "Altitude",
        7 => "Glide slope",
        22 => "Profile altitude",
        _ => null,
    };

    private static void Add(List<string> armed, string? words)
    {
        if (words != null && !armed.Contains(words))
            armed.Add(words);
    }
}

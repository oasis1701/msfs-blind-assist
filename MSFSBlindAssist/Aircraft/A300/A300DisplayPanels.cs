namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The Displays section: one status box per A300 screen, each read from the aircraft's own variables
/// (no screen capture, no AI, no memory reading). It follows the FlyByWire A320's PFD, ND and ISIS
/// boxes, in A300 words, then the engine instruments and the ECAM system pages
/// (<see cref="A300EcamPages"/>). A display panel has no controls; its lines are the keys listed
/// here, and the definition's display text reads each one.
/// </summary>
public static class A300DisplayPanels
{
    public const string Section = "Displays";

    /// <summary>The section comes right after this one.</summary>
    public const string AfterSection = "Instrument";

    private static readonly string[] Pfd =
    {
        A300FmaSources.ThrustModeKey, A300FmaSources.PitchModeKey, A300FmaSources.RollModeKey, A300FmaSources.ArmedKey,
        A300Readouts.SpeedKey, A300Readouts.HeadingKey, A300Readouts.AltitudeKey, A300Readouts.VerticalSpeedKey,
        "PLANE_PITCH_DEGREES", "PLANE_BANK_DEGREES", A300Readouts.PfdHeadingKey, A300Readouts.PfdAirspeedKey,
        "INDICATED_ALTITUDE", A300Readouts.PfdVerticalSpeedKey, A300Readouts.PfdRadioAltitudeKey,
        A300IlsDeviation.LocalizerKey, A300IlsDeviation.GlideslopeKey,
        A300TakeoffCallouts.V1Key, A300TakeoffCallouts.VrKey, A300TakeoffCallouts.V2Key,
        A300Readouts.VlsKey, A300Readouts.VmaxKey, A300Readouts.GreenDotKey, A300Readouts.SSpeedKey, A300Readouts.FSpeedKey,
        A300Readouts.VsSpeedKey, A300Readouts.MinimumsKey,
    };

    private static readonly string[] Nd =
    {
        "A300_EFIS_MODE_CPT", "A300_EFIS_RANGE_CPT", A300Readouts.WaypointDistanceKey,
        "GROUND_VELOCITY", A300Readouts.TrueAirspeedKey, A300Readouts.WindDirectionKey, A300Readouts.WindSpeedKey,
        A300Readouts.Vor1FrequencyKey, A300Readouts.Dme1Key, A300Readouts.Vor2FrequencyKey, A300Readouts.Dme2Key,
        A300Readouts.IlsFrequencyKey, A300Readouts.LocalizerKey, A300Readouts.GlideslopeKey,
        A300Readouts.Adf1FrequencyKey, A300Readouts.Adf2FrequencyKey,
    };

    private static readonly string[] Standby =
    {
        "PLANE_PITCH_DEGREES", "PLANE_BANK_DEGREES", A300Readouts.PfdAirspeedKey,
        A300Readouts.StandbyAltitudeKey, "A300_RO_BARO_STBY", A300Readouts.StandbyCompassKey,
    };

    /// <summary>The panels, in the order the section lists them, with their lines. The standby instruments
    /// are not here: they are the Instrument section's Standby Instruments panel (<see cref="SystemLines"/>),
    /// the fleet's ISIS panel, and MainForm keys a panel by its name alone.</summary>
    private static readonly (string Panel, IReadOnlyList<string> Keys)[] Ordered =
        new (string, IReadOnlyList<string>)[]
        {
            ("PFD", Pfd),
            ("ND", Nd),
            ("Engine Instruments", A300EcamPages.EngineInstruments),
            (A300EwdMemos.Panel, new[] { A300EwdMemos.LineKey }),
        }
        .Concat(A300EcamPages.Pages)
        .ToArray();

    public static readonly IReadOnlyList<string> Panels = Ordered.Select(p => p.Panel).ToArray();

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Lines =
        Ordered.ToDictionary(p => p.Panel, p => p.Keys);

    public static bool IsDisplayPanel(string panel) => Lines.ContainsKey(panel);

    /// <summary>The display lines a system panel's status box carries after its lights (owner decision
    /// 2026-10-09, as the FBW A320's panels carry their system's values): its ECAM page's values, and the
    /// standby instruments' readings on their own panel. The Displays section keeps the ECAM pages too.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SystemLines =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Standby Instruments"] = Standby,
            ["Electrical"] = Lines["ECAM Electrical AC"].Concat(Lines["ECAM Electrical DC"]).ToArray(),
            ["APU"] = Lines["ECAM APU"],
            ["Hydraulics"] = Lines["ECAM Hydraulics"],
            ["Fuel"] = Lines["ECAM Fuel"],
            ["Air Conditioning"] = Lines["ECAM Air Conditioning"],
            ["Bleed"] = Lines["ECAM Bleed"],
            ["Pressurization"] = Lines["ECAM Pressurization"],
            // A start is flown on N2 and the ECAM ENG page (the manual's engine start).
            ["Engine Start"] = A300EcamPages.EngineInstruments.Concat(Lines["ECAM Engine"]).ToArray(),
        };
}

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>What the center of the ADI shows: the horizon, the ATT flag, or nothing (blank while the IRS aligns).</summary>
public enum Bbs757AdiAttitudeState { Valid, Flag, Blank }

/// <summary>What a deviation scale shows: nothing, its flag, the scale with its pointer, or the scale alone.</summary>
public enum Bbs757AdiPointer { None, Flag, Pointer, ScaleOnly }

/// <summary>
/// The center of the ADI as drawn. <see cref="PitchUp"/> is the pitch in degrees nose up, held at the horizon's stop;
/// <see cref="BankLeft"/> the bank in degrees left wing down. Each is null unless the horizon is drawn
/// (<see cref="Bbs757AdiAttitudeState.Valid"/>) and its simulator value was given. <see cref="AttWord"/> is the flag's
/// word, the screen's own.
/// </summary>
public sealed record Bbs757AdiAttitudeReading(
    Bbs757AdiAttitudeState State,
    double? PitchUp,
    double? BankLeft,
    string AttWord);

/// <summary>
/// The speed tape as drawn. With <see cref="SpdFlag"/> nothing else of it is drawn, and every other value is null or
/// false. <see cref="Airspeed"/> is the airspeed shown (never below the tape's minimum); <see cref="TargetSpeed"/> the
/// MCP's target, null while the FMC's is the source (not read); <see cref="V1Inop"/> the V1 INOP legend;
/// <see cref="V1"/> and <see cref="Vr"/> the speeds the tape marks or prints, null while it does not;
/// <see cref="FlapSpeed"/> the F bug's speed, null while it is not drawn; <see cref="Mach"/> null while the tape shows
/// dashes; <see cref="GroundSpeed"/> null while the attitude is not valid. The words are the screen's own.
/// </summary>
public sealed record Bbs757AdiSpeedReading(
    bool SpdFlag,
    double? Airspeed,
    double? TargetSpeed,
    bool V1Inop,
    int? V1,
    int? Vr,
    double? FlapSpeed,
    double? Mach,
    double? GroundSpeed,
    string SpdWord,
    string V1Word,
    string InopWord);

/// <summary>
/// The glideslope (right) and the localizer (bottom) as drawn: what each scale shows, its pointer in dots (the
/// glideslope's positive up, the localizer's positive right of center), whether BlueBird pins the pointer at the end of
/// its scale, whether the localizer scale is the expanded one, and whether the rising runway is drawn. The words are the
/// flags', the screen's own.
/// </summary>
public sealed record Bbs757AdiIlsReading(
    Bbs757AdiPointer Glideslope,
    double GlideslopeDots,
    bool GlideslopePinned,
    Bbs757AdiPointer Localizer,
    double LocalizerDots,
    bool LocalizerPinned,
    bool Expanded,
    bool RisingRunway,
    string LocWord,
    string GlideslopeWord);

/// <summary>
/// The radio altitude and the decision height as drawn: the RA flag, the radio altitude as its digits read (null above
/// its range, and with the flag), the decision height (null while it is not drawn) and whether ALT is drawn. The words
/// are the screen's own.
/// </summary>
public sealed record Bbs757AdiRadioAltitudeReading(
    bool RaFlag,
    int? RadioAltitude,
    int? DecisionHeight,
    bool Alt,
    string DhWord,
    string RaWord,
    string AltWord);

/// <summary>The flight director bars: whether they are drawn, the pitch bar's place in degrees above the aircraft
/// symbol (negative: below) and the roll bar's in degrees of bank right of center (negative: left).</summary>
public sealed record Bbs757AdiFlightDirectorReading(
    bool Drawn,
    double PitchBar,
    double RollBar);

/// <summary>The TCAS resolution advisory as drawn: the pitch to stay above and the pitch to stay below, each in degrees
/// nose up, null while that outline is not drawn.</summary>
public sealed record Bbs757AdiTcasReading(
    double? KeepAbove,
    double? KeepBelow);

/// <summary>
/// One read of the Captain's ADI besides its flight mode annunciator (plan 27): each part as drawn, null when the layout
/// has no such part (its <c>…Missing</c> reason then says why) and in a dark or corrupt reading. <see cref="Vref30"/> and
/// <see cref="Vref25"/> are the FMC's reference speeds in knots, which the ADI does not draw: read whatever the
/// display's power for the flap speed readouts, null when their part is missing or their read is lost, and 0 until the
/// FMC has computed them. <see cref="Corrupt"/> is an out-of-range reading, never spoken, shown or a baseline.
/// </summary>
public sealed record Bbs757AdiReading(
    bool Powered,
    Bbs757AdiAttitudeReading? Attitude,
    Bbs757AdiSpeedReading? Speed,
    Bbs757AdiIlsReading? Ils,
    Bbs757AdiRadioAltitudeReading? RadioAltitude,
    Bbs757AdiFlightDirectorReading? FlightDirector,
    Bbs757AdiTcasReading? Tcas,
    double? Vref30,
    double? Vref25,
    bool Corrupt) : IBbs757Reading
{
    /// <summary>Why <see cref="Attitude"/> is not read, in a lit reading whose layout lacks the part; else null.</summary>
    public string? AttitudeMissing { get; init; }

    public string? SpeedMissing { get; init; }

    public string? IlsMissing { get; init; }

    public string? RadioAltitudeMissing { get; init; }

    public string? FlightDirectorMissing { get; init; }

    public string? TcasMissing { get; init; }
}

// ---- as read from memory, before BlueBird's draw rules (Bbs757AdiReader.Decode) ----

/// <summary>The selected irs: its attitude byte, its mode and its state, as stored.</summary>
public sealed record Bbs757AdiAttitudeRaw(bool IrsAttitude, int IrsMode, int IrsState);

/// <summary>
/// The speed tape's sources as stored: whether the selected adc is failed or unpowered, its airspeed and altitude, the
/// MCP's speed, the AFDS pitch mode and speed-intervention byte, the FMC's V1 and VR and its V1 INOP byte, the AFDS
/// timer (the time kept, its start as the clock read then, whether it is stopped) with the clock, and the flap
/// maneuvering speed.
/// </summary>
public sealed record Bbs757AdiSpeedRaw(
    bool AdcFailed,
    double Airspeed,
    double Altitude,
    double McpSpeed,
    int PitchMode,
    bool SpeedIntervene,
    int V1,
    int Vr,
    bool V1Inop,
    double TimerKept,
    long TimerStart,
    bool TimerStopped,
    double Clock,
    double FlapSpeed);

/// <summary>The PFD's ILS block as stored (the pointers and the bytes the rows use), and the AFDS roll mode.</summary>
public sealed record Bbs757AdiIlsRaw(
    double LocPointer,
    double GsPointer,
    bool LocScale,
    bool GsScale,
    bool LocValid,
    bool GsValid,
    bool Expanded,
    bool LocFlag,
    bool GsFlag,
    int RollMode);

/// <summary>The selected ra as stored (failed or unpowered, its altitude, its self-test byte, state and altitude), the
/// decision height and its shown byte from the PFD's ILS block, and the adc's vertical speed.</summary>
public sealed record Bbs757AdiRadioAltitudeRaw(
    bool RaFailed,
    double RaAltitude,
    bool RaTest,
    int RaTestState,
    double RaTestAltitude,
    int DhValue,
    bool DhShown,
    double VerticalSpeed);

/// <summary>The Captain's F/D switch and the AFDS's pitch and roll commands, in degrees.</summary>
public sealed record Bbs757AdiFlightDirectorRaw(bool FdCaptain, double Pitch, double Roll);

/// <summary>The TCAS object's four values in the PFD's ILS block, as stored.</summary>
public sealed record Bbs757AdiTcasRaw(double Above, double Below, double AbovePitch, double BelowPitch);

/// <summary>The FMC's VREF 30 and VREF 25, as stored.</summary>
public sealed record Bbs757AdiReferenceSpeedsRaw(double Vref30, double Vref25);

/// <summary>Everything <see cref="Bbs757AdiReader.Decode"/> needs from memory: whether the display draws, and each
/// part's values (null when the layout has no such part; the reference speeds also when their read was lost).</summary>
public sealed record Bbs757AdiRaw(
    bool Lit,
    Bbs757AdiAttitudeRaw? Attitude,
    Bbs757AdiSpeedRaw? Speed,
    Bbs757AdiIlsRaw? Ils,
    Bbs757AdiRadioAltitudeRaw? RadioAltitude,
    Bbs757AdiFlightDirectorRaw? FlightDirector,
    Bbs757AdiTcasRaw? Tcas,
    Bbs757AdiReferenceSpeedsRaw? ReferenceSpeeds);

/// <summary>This tick's simulator inputs (S10), each null when it was not given or is not a number: the pitch and the
/// bank in radians and in the simulator's signs (pitch positive nose down, bank positive left), the ground speed in
/// knots and the Mach number.</summary>
public readonly record struct Bbs757AdiSim(double? Pitch, double? Bank, double? GroundSpeed, double? Mach);

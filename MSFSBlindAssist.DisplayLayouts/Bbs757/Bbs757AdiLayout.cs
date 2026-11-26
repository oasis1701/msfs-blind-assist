namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The ADI's attitude, an optional part of <see cref="Bbs757AdiLayout"/> (plan 27), all from the layout file. Every
/// offset is in the irs the instrument source selector gives (<see cref="Bbs757AdiLayout.SourceToIrs"/>). The horizon
/// is drawn while the i32 at <see cref="IrsMode"/> holds <see cref="IrsModeNav"/>, the byte at
/// <see cref="IrsAttitude"/> is set and the i32 at <see cref="IrsState"/> holds <see cref="IrsStateValid"/>. Otherwise
/// the <see cref="AttWord"/> flag is drawn, except while the state holds <see cref="IrsStateAligning"/>: then the
/// center is blank, no horizon and no flag. The pitch and bank themselves are simulator inputs, never memory. The word
/// is the screen's own. Records compare by content.
/// </summary>
public sealed record Bbs757AdiAttitude(
    int IrsAttitude,
    int IrsMode,
    int IrsModeNav,
    int IrsState,
    int IrsStateValid,
    int IrsStateAligning,
    string AttWord);

/// <summary>
/// The ADI's speed tape, an optional part of <see cref="Bbs757AdiLayout"/> (plan 27), all from the layout file.
/// <see cref="AdcAirspeed"/> and <see cref="AdcAltitude"/> (f64) are offsets in the adc's data block (the adc the
/// source selector gives at <see cref="Bbs757AdiLayout.SourceToAdc"/>, plus <see cref="Bbs757AdiLayout.AdcBlock"/>);
/// the airspeed is shown never below <see cref="MinAirspeed"/>. The target speed is the f64 at
/// <see cref="McpSpeed"/> in the mcp (<see cref="Bbs757AdiLayout.PfdToMcp"/>), unless the i32 at
/// <see cref="AfdsPitchMode"/> in the afds (<see cref="Bbs757AdiLayout.PfdToAfds"/>) is one of
/// <see cref="VnavModes"/> with the afds byte at <see cref="AfdsSpeedIntervene"/> clear: then it is the FMC's target,
/// which this layout does not reach. <see cref="FmsV1"/> and <see cref="FmsVr"/> (i32) and the byte at
/// <see cref="FmsV1Inop"/> are offsets in the fms (<see cref="Bbs757AdiLayout.PfdToFms"/>): V1 0 with the byte set
/// draws <see cref="V1Word"/> over <see cref="InopWord"/>. The AFDS timer is a stopwatch at <see cref="AfdsTimer"/>
/// in the afds: the f64 at its start is the time kept, the i64 at <see cref="TimerStart"/> inside it its start, and
/// the byte at <see cref="TimerStopped"/> inside it is set while it is stopped; running, it reads the time kept plus
/// the f64 clock at the static address <see cref="Clock"/> minus the start. V1 and the <see cref="V1BugWord"/> and
/// <see cref="VrBugWord"/> bugs are drawn while the timer reads under <see cref="BugSeconds"/>. The
/// <see cref="FlapBugWord"/> bug sits at the f64 at <see cref="AfdsFlapSpeed"/> in the afds, drawn while that is not
/// <see cref="NoFlapSpeed"/>, the adc's altitude is at most <see cref="FlapSpeedMaxAltitude"/> feet and the timer
/// reads at least <see cref="FlapSpeedAfterSeconds"/>; the V1 and VR bugs are hidden while the flap speed is not
/// <see cref="NoFlapSpeed"/> and VR is within <see cref="FlapSpeedNearVr"/> knots of it. A bug or the target is on the
/// tape within <see cref="TapeHalfRange"/> knots of the airspeed shown, and printed as a number above or below the
/// tape beyond that. Mach is shown from <see cref="MachShownFrom"/> up and, once shown, until it falls below
/// <see cref="MachShownUntil"/>; otherwise <see cref="NoMachWord"/>. <see cref="GroundSpeedWord"/> labels the ground
/// speed, and <see cref="SpdWord"/> is the flag drawn in place of the whole tape while the adc is failed or unpowered
/// (the screen stacks its letters). The words are the screen's own and the doubles the draw's constants. Equality
/// compares every field by content.
/// </summary>
public sealed record Bbs757AdiSpeed(
    int AdcAirspeed,
    int AdcAltitude,
    double MinAirspeed,
    int McpSpeed,
    int AfdsPitchMode,
    IReadOnlyList<int> VnavModes,
    int AfdsSpeedIntervene,
    int FmsV1,
    int FmsVr,
    int FmsV1Inop,
    int AfdsTimer,
    int TimerStart,
    int TimerStopped,
    uint Clock,
    double BugSeconds,
    double FlapSpeedAfterSeconds,
    int AfdsFlapSpeed,
    double NoFlapSpeed,
    double FlapSpeedMaxAltitude,
    double FlapSpeedNearVr,
    double TapeHalfRange,
    double MachShownFrom,
    double MachShownUntil,
    string SpdWord,
    string V1Word,
    string InopWord,
    string V1BugWord,
    string VrBugWord,
    string FlapBugWord,
    string GroundSpeedWord,
    string NoMachWord)
{
    public bool Equals(Bbs757AdiSpeed? other) =>
        other is not null
        && AdcAirspeed == other.AdcAirspeed
        && AdcAltitude == other.AdcAltitude
        && MinAirspeed.Equals(other.MinAirspeed)
        && McpSpeed == other.McpSpeed
        && AfdsPitchMode == other.AfdsPitchMode
        && VnavModes.SequenceEqual(other.VnavModes)
        && AfdsSpeedIntervene == other.AfdsSpeedIntervene
        && FmsV1 == other.FmsV1
        && FmsVr == other.FmsVr
        && FmsV1Inop == other.FmsV1Inop
        && AfdsTimer == other.AfdsTimer
        && TimerStart == other.TimerStart
        && TimerStopped == other.TimerStopped
        && Clock == other.Clock
        && BugSeconds.Equals(other.BugSeconds)
        && FlapSpeedAfterSeconds.Equals(other.FlapSpeedAfterSeconds)
        && AfdsFlapSpeed == other.AfdsFlapSpeed
        && NoFlapSpeed.Equals(other.NoFlapSpeed)
        && FlapSpeedMaxAltitude.Equals(other.FlapSpeedMaxAltitude)
        && FlapSpeedNearVr.Equals(other.FlapSpeedNearVr)
        && TapeHalfRange.Equals(other.TapeHalfRange)
        && MachShownFrom.Equals(other.MachShownFrom)
        && MachShownUntil.Equals(other.MachShownUntil)
        && string.Equals(SpdWord, other.SpdWord, StringComparison.Ordinal)
        && string.Equals(V1Word, other.V1Word, StringComparison.Ordinal)
        && string.Equals(InopWord, other.InopWord, StringComparison.Ordinal)
        && string.Equals(V1BugWord, other.V1BugWord, StringComparison.Ordinal)
        && string.Equals(VrBugWord, other.VrBugWord, StringComparison.Ordinal)
        && string.Equals(FlapBugWord, other.FlapBugWord, StringComparison.Ordinal)
        && string.Equals(GroundSpeedWord, other.GroundSpeedWord, StringComparison.Ordinal)
        && string.Equals(NoMachWord, other.NoMachWord, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AdcAirspeed);
        h.Add(AdcAltitude);
        h.Add(MinAirspeed);
        h.Add(McpSpeed);
        h.Add(AfdsPitchMode);
        foreach (int m in VnavModes) h.Add(m);
        h.Add(AfdsSpeedIntervene);
        h.Add(FmsV1);
        h.Add(FmsVr);
        h.Add(FmsV1Inop);
        h.Add(AfdsTimer);
        h.Add(TimerStart);
        h.Add(TimerStopped);
        h.Add(Clock);
        h.Add(BugSeconds);
        h.Add(FlapSpeedAfterSeconds);
        h.Add(AfdsFlapSpeed);
        h.Add(NoFlapSpeed);
        h.Add(FlapSpeedMaxAltitude);
        h.Add(FlapSpeedNearVr);
        h.Add(TapeHalfRange);
        h.Add(MachShownFrom);
        h.Add(MachShownUntil);
        h.Add(SpdWord, StringComparer.Ordinal);
        h.Add(V1Word, StringComparer.Ordinal);
        h.Add(InopWord, StringComparer.Ordinal);
        h.Add(V1BugWord, StringComparer.Ordinal);
        h.Add(VrBugWord, StringComparer.Ordinal);
        h.Add(FlapBugWord, StringComparer.Ordinal);
        h.Add(GroundSpeedWord, StringComparer.Ordinal);
        h.Add(NoMachWord, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>
/// The ADI's localizer and glideslope, an optional part of <see cref="Bbs757AdiLayout"/> (plan 27), all from the layout
/// file. <see cref="LocPointer"/> to <see cref="Test"/> are offsets in the PFD's ILS block (the pfd plus
/// <see cref="Bbs757AdiLayout.IlsBlock"/>), which the ILS receiver writes whether or not the display draws. Three are
/// f64: the localizer pointer at <see cref="LocPointer"/> (−1 to 1 of the scale's half width, positive right of
/// center), the localizer deviation in dots at <see cref="LocDots"/> and the glideslope pointer in dots at
/// <see cref="GsPointer"/> (positive up). The rest are bytes: <see cref="LocScale"/> and <see cref="GsScale"/> (the
/// scale is drawn), <see cref="LocValid"/> and <see cref="GsValid"/> (the pointer is valid), <see cref="Expanded"/>
/// (the expanded localizer scale), <see cref="LocFlag"/> and <see cref="GsFlag"/> (the <see cref="LocWord"/> and
/// <see cref="GlideslopeWord"/> flags; the screen stacks the latter's letters) and <see cref="Test"/> (the self-test).
/// The rising runway, drawn in place of the localizer pointer, needs among the draw's other conditions the i32 at
/// <see cref="AfdsRollMode"/> in the afds (<see cref="Bbs757AdiLayout.PfdToAfds"/>) to be one of
/// <see cref="RunwayModes"/> and a radio altitude of at most <see cref="RunwayBelow"/> feet. The words are the
/// screen's own. Equality compares every field by content.
/// </summary>
public sealed record Bbs757AdiIls(
    int LocPointer,
    int LocDots,
    int GsPointer,
    int LocScale,
    int GsScale,
    int LocValid,
    int GsValid,
    int Expanded,
    int LocFlag,
    int GsFlag,
    int Test,
    int AfdsRollMode,
    IReadOnlyList<int> RunwayModes,
    double RunwayBelow,
    string LocWord,
    string GlideslopeWord)
{
    public bool Equals(Bbs757AdiIls? other) =>
        other is not null
        && LocPointer == other.LocPointer
        && LocDots == other.LocDots
        && GsPointer == other.GsPointer
        && LocScale == other.LocScale
        && GsScale == other.GsScale
        && LocValid == other.LocValid
        && GsValid == other.GsValid
        && Expanded == other.Expanded
        && LocFlag == other.LocFlag
        && GsFlag == other.GsFlag
        && Test == other.Test
        && AfdsRollMode == other.AfdsRollMode
        && RunwayModes.SequenceEqual(other.RunwayModes)
        && RunwayBelow.Equals(other.RunwayBelow)
        && string.Equals(LocWord, other.LocWord, StringComparison.Ordinal)
        && string.Equals(GlideslopeWord, other.GlideslopeWord, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(LocPointer);
        h.Add(LocDots);
        h.Add(GsPointer);
        h.Add(LocScale);
        h.Add(GsScale);
        h.Add(LocValid);
        h.Add(GsValid);
        h.Add(Expanded);
        h.Add(LocFlag);
        h.Add(GsFlag);
        h.Add(Test);
        h.Add(AfdsRollMode);
        foreach (int m in RunwayModes) h.Add(m);
        h.Add(RunwayBelow);
        h.Add(LocWord, StringComparer.Ordinal);
        h.Add(GlideslopeWord, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>
/// The ADI's radio altitude and decision height, an optional part of <see cref="Bbs757AdiLayout"/> (plan 27), all from
/// the layout file. <see cref="RaAltitude"/> (f64), <see cref="RaTest"/> (a byte, its self-test),
/// <see cref="RaTestState"/> (i32) and <see cref="RaTestAltitude"/> (f64) are offsets in the ra the source selector
/// gives (<see cref="Bbs757AdiLayout.SourceToRa"/>). The altitude is the f64 at <see cref="RaAltitude"/> minus
/// <see cref="RaOffset"/>; in the self-test with a state in <see cref="RaTestAlone"/> it is the f64 at
/// <see cref="RaTestAltitude"/> instead, and with the state <see cref="RaTestAdded"/> the sum of both. It is shown
/// while at most <see cref="RaMaxShown"/> feet, as a whole number, rounded to the nearest 10 from
/// <see cref="RaRoundFrom"/> up; <see cref="RaWord"/> is the flag drawn in its place while the ra is failed or
/// unpowered. <see cref="DhValue"/> (i32) and <see cref="DhShown"/> (a byte) are offsets in the PFD's ILS block (the
/// pfd plus <see cref="Bbs757AdiLayout.IlsBlock"/>): the decision height and the byte that shows it (with the attitude
/// valid), after <see cref="DhWord"/>. <see cref="AltWord"/> is drawn while the radio altitude is from <see cref="AltFrom"/> to
/// <see cref="RaMaxShown"/> feet and the adc's vertical speed, the f64 at <see cref="AdcVerticalSpeed"/> in the adc's
/// data block (<see cref="Bbs757AdiLayout.SourceToAdc"/>, plus <see cref="Bbs757AdiLayout.AdcBlock"/>), is
/// <see cref="AltDescent"/> feet per minute or less. The words are the screen's own and the doubles the draw's
/// constants. Equality compares every field by content.
/// </summary>
public sealed record Bbs757AdiRadioAltitude(
    int RaAltitude,
    double RaOffset,
    int RaTest,
    int RaTestState,
    int RaTestAltitude,
    IReadOnlyList<int> RaTestAlone,
    int RaTestAdded,
    double RaMaxShown,
    double RaRoundFrom,
    int DhValue,
    int DhShown,
    int AdcVerticalSpeed,
    double AltDescent,
    double AltFrom,
    string DhWord,
    string RaWord,
    string AltWord)
{
    public bool Equals(Bbs757AdiRadioAltitude? other) =>
        other is not null
        && RaAltitude == other.RaAltitude
        && RaOffset.Equals(other.RaOffset)
        && RaTest == other.RaTest
        && RaTestState == other.RaTestState
        && RaTestAltitude == other.RaTestAltitude
        && RaTestAlone.SequenceEqual(other.RaTestAlone)
        && RaTestAdded == other.RaTestAdded
        && RaMaxShown.Equals(other.RaMaxShown)
        && RaRoundFrom.Equals(other.RaRoundFrom)
        && DhValue == other.DhValue
        && DhShown == other.DhShown
        && AdcVerticalSpeed == other.AdcVerticalSpeed
        && AltDescent.Equals(other.AltDescent)
        && AltFrom.Equals(other.AltFrom)
        && string.Equals(DhWord, other.DhWord, StringComparison.Ordinal)
        && string.Equals(RaWord, other.RaWord, StringComparison.Ordinal)
        && string.Equals(AltWord, other.AltWord, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(RaAltitude);
        h.Add(RaOffset);
        h.Add(RaTest);
        h.Add(RaTestState);
        h.Add(RaTestAltitude);
        foreach (int s in RaTestAlone) h.Add(s);
        h.Add(RaTestAdded);
        h.Add(RaMaxShown);
        h.Add(RaRoundFrom);
        h.Add(DhValue);
        h.Add(DhShown);
        h.Add(AdcVerticalSpeed);
        h.Add(AltDescent);
        h.Add(AltFrom);
        h.Add(DhWord, StringComparer.Ordinal);
        h.Add(RaWord, StringComparer.Ordinal);
        h.Add(AltWord, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>
/// The ADI's flight director bars, an optional part of <see cref="Bbs757AdiLayout"/> (plan 27), all from the layout
/// file. Every offset is in the afds (<see cref="Bbs757AdiLayout.PfdToAfds"/>): the bars are drawn while the attitude
/// is valid and the byte at <see cref="FdCaptain"/> (the Captain's F/D) is set, the pitch command is the f64 at
/// <see cref="FdPitch"/> (degrees, positive up) and the roll command the f64 at <see cref="FdRoll"/> (degrees).
/// Records compare by content.
/// </summary>
public sealed record Bbs757AdiFlightDirector(
    int FdCaptain,
    int FdPitch,
    int FdRoll);

/// <summary>
/// The ADI's TCAS resolution advisory, an optional part of <see cref="Bbs757AdiLayout"/> (plan 27), all from the layout
/// file. Every offset is in the PFD's ILS block (the pfd plus <see cref="Bbs757AdiLayout.IlsBlock"/>), where the TCAS
/// object writes four f64: one at <see cref="TcasAbove"/> that is not 0 while the pitch is to be kept above the angle
/// at <see cref="TcasAbovePitch"/>, and one at <see cref="TcasBelow"/> that is not 0 while it is to be kept below the
/// angle at <see cref="TcasBelowPitch"/>; all cleared when the advisory ends, and drawn only while the attitude is
/// valid. Records compare by content.
/// </summary>
public sealed record Bbs757AdiTcas(
    int TcasAbove,
    int TcasBelow,
    int TcasAbovePitch,
    int TcasBelowPitch);

/// <summary>
/// The FMC's reference speeds, an optional part of <see cref="Bbs757AdiLayout"/> (plan 27), all from the layout file.
/// The ADI does not draw these two values: they feed the app's Shift+1 to Shift+6 flap speed readouts. Both offsets are
/// in the fms, reached through the pfd (<see cref="Bbs757AdiLayout.PfdToFms"/>): the FMC's own VREF 30 at
/// <see cref="FmsVref30"/> and VREF 25 at <see cref="FmsVref25"/>, each an f64 in knots that reads 0 until PERF INIT
/// has a gross weight. Records compare by content.
/// </summary>
public sealed record Bbs757AdiReferenceSpeeds(
    int FmsVref30,
    int FmsVref25);

/// <summary>
/// Where one installed BBS752.wasm keeps what the Captain's ADI draws besides the flight mode annunciator (plan 27),
/// all from that file's layout file, never hardcoded (BBS757-D2). The object is the FMA's: <see cref="ManagerBase"/>
/// and <see cref="ManagerSpan"/> bound the gauge manager's pointers searched for the first whose object carries
/// <see cref="PfdVptr"/>, the Captain's pfd, found again here and never shared with the FMA's layout. The display draws
/// while the pfd's byte at <see cref="Powered"/> is set and its bytes at <see cref="Failed"/> and
/// <see cref="Blanked"/> are clear. <see cref="PfdToSource"/> is the pfd's pointer to its side's instrument source
/// selector, whose pointers at <see cref="SourceToIrs"/>, <see cref="SourceToAdc"/> and <see cref="SourceToRa"/> give
/// the selected irs, adc and ra; a device (the adc, the ra) is failed or unpowered while its byte at
/// <see cref="DeviceFailed"/> is set or its byte at <see cref="DevicePowered"/> is clear. <see cref="PfdToMcp"/>,
/// <see cref="PfdToAfds"/> and <see cref="PfdToFms"/> are the pfd's pointers to the mcp, the afds and the fms;
/// <see cref="IlsBlock"/> is the offset in the pfd of its ILS block and <see cref="AdcBlock"/> the offset in the adc of
/// its data block: several parts read through these five, so they are here. Each part (<see cref="Attitude"/> to
/// <see cref="ReferenceSpeeds"/>) is optional, null with its reason; the last, <see cref="ReferenceSpeeds"/>, is not
/// drawn on the ADI (it feeds the flap speed readouts). Equality compares <see cref="Anchor"/> and every part by
/// content and ignores <see cref="SourceFile"/>, so the same build installed twice gives one layout.
/// </summary>
public sealed record Bbs757AdiLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    uint ManagerBase,
    uint ManagerSpan,
    uint PfdVptr,
    int Powered,
    int Failed,
    int Blanked,
    int PfdToSource,
    int SourceToIrs,
    int SourceToAdc,
    int SourceToRa,
    int DevicePowered,
    int DeviceFailed,
    int PfdToMcp,
    int PfdToAfds,
    int PfdToFms,
    int IlsBlock,
    int AdcBlock,
    Bbs757AdiAttitude? Attitude,
    string? AttitudeMissing,
    Bbs757AdiSpeed? Speed,
    string? SpeedMissing,
    Bbs757AdiIls? Ils,
    string? IlsMissing,
    Bbs757AdiRadioAltitude? RadioAltitude,
    string? RadioAltitudeMissing,
    Bbs757AdiFlightDirector? FlightDirector,
    string? FlightDirectorMissing,
    Bbs757AdiTcas? Tcas,
    string? TcasMissing,
    Bbs757AdiReferenceSpeeds? ReferenceSpeeds,
    string? ReferenceSpeedsMissing)
{
    public bool Equals(Bbs757AdiLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && ManagerBase == other.ManagerBase
        && ManagerSpan == other.ManagerSpan
        && PfdVptr == other.PfdVptr
        && Powered == other.Powered
        && Failed == other.Failed
        && Blanked == other.Blanked
        && PfdToSource == other.PfdToSource
        && SourceToIrs == other.SourceToIrs
        && SourceToAdc == other.SourceToAdc
        && SourceToRa == other.SourceToRa
        && DevicePowered == other.DevicePowered
        && DeviceFailed == other.DeviceFailed
        && PfdToMcp == other.PfdToMcp
        && PfdToAfds == other.PfdToAfds
        && PfdToFms == other.PfdToFms
        && IlsBlock == other.IlsBlock
        && AdcBlock == other.AdcBlock
        && Equals(Attitude, other.Attitude)
        && string.Equals(AttitudeMissing, other.AttitudeMissing, StringComparison.Ordinal)
        && Equals(Speed, other.Speed)
        && string.Equals(SpeedMissing, other.SpeedMissing, StringComparison.Ordinal)
        && Equals(Ils, other.Ils)
        && string.Equals(IlsMissing, other.IlsMissing, StringComparison.Ordinal)
        && Equals(RadioAltitude, other.RadioAltitude)
        && string.Equals(RadioAltitudeMissing, other.RadioAltitudeMissing, StringComparison.Ordinal)
        && Equals(FlightDirector, other.FlightDirector)
        && string.Equals(FlightDirectorMissing, other.FlightDirectorMissing, StringComparison.Ordinal)
        && Equals(Tcas, other.Tcas)
        && string.Equals(TcasMissing, other.TcasMissing, StringComparison.Ordinal)
        && Equals(ReferenceSpeeds, other.ReferenceSpeeds)
        && string.Equals(ReferenceSpeedsMissing, other.ReferenceSpeedsMissing, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(ManagerBase);
        h.Add(ManagerSpan);
        h.Add(PfdVptr);
        h.Add(Powered);
        h.Add(Failed);
        h.Add(Blanked);
        h.Add(PfdToSource);
        h.Add(SourceToIrs);
        h.Add(SourceToAdc);
        h.Add(SourceToRa);
        h.Add(DevicePowered);
        h.Add(DeviceFailed);
        h.Add(PfdToMcp);
        h.Add(PfdToAfds);
        h.Add(PfdToFms);
        h.Add(IlsBlock);
        h.Add(AdcBlock);
        h.Add(Attitude);
        h.Add(AttitudeMissing, StringComparer.Ordinal);
        h.Add(Speed);
        h.Add(SpeedMissing, StringComparer.Ordinal);
        h.Add(Ils);
        h.Add(IlsMissing, StringComparer.Ordinal);
        h.Add(RadioAltitude);
        h.Add(RadioAltitudeMissing, StringComparer.Ordinal);
        h.Add(FlightDirector);
        h.Add(FlightDirectorMissing, StringComparer.Ordinal);
        h.Add(Tcas);
        h.Add(TcasMissing, StringComparer.Ordinal);
        h.Add(ReferenceSpeeds);
        h.Add(ReferenceSpeedsMissing, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>The ADI's result in a layout file: a layout, or the detailed reason (for the log) there is none.</summary>
public sealed record Bbs757AdiDerivation(Bbs757AdiLayout? Layout, string? Failure);

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The ND's APP and VOR part, an optional part of <see cref="Pmdg737NavDisplayLayout"/>: the ND's mode
/// flags (u8, set by PMDG from the panel's mode) under which the ND draws the ILS/VOR block and the
/// course with its deviation bar (<see cref="VorFlag"/> or <see cref="AppFlag"/>) and the glideslope
/// (<see cref="AppFlag"/> alone); the block itself
/// (<see cref="Block"/>); the course pointer and deviation bar (<see cref="Cdi"/>); the glideslope in APP
/// (<see cref="Glideslope"/>); the messages these modes add and whether traffic is drawn (<see cref="Messages"/>).
/// </summary>
public sealed record NdApproachFields(uint VorFlag, uint AppFlag, NdApproachBlockFields Block, NdCdiFields Cdi, NdGlideslopeFields Glideslope,
    NdApproachMessageFields Messages);

/// <summary>
/// What APP and VOR add to the ND's messages (PMDG's failure flags): the receiver flags (<see cref="VorFlag"/> in VOR,
/// <see cref="LocFlag"/> in APP) and <see cref="Disagree"/>, drawn in VOR while the receiver's frequency is an ILS
/// one exactly when <see cref="DisagreeInVorWhenIls"/>, in APP exactly when <see cref="DisagreeInAppWhenIls"/>;
/// and whether the ND draws the traffic in APP and VOR while not centred (<see cref="Traffic"/>).
/// </summary>
public sealed record NdApproachMessageFields(NdReceiverFlag VorFlag, NdReceiverFlag LocFlag, string Disagree,
    bool DisagreeInVorWhenIls, bool DisagreeInAppWhenIls, bool Traffic);

/// <summary>A receiver's amber flag PMDG draws in <see cref="Mode"/> while the glass data's
/// <see cref="Failed"/> (per receiver) is set: <see cref="FullLabels"/> by the receiver in the full format, else
/// <see cref="HalfLabel"/>.</summary>
public sealed record NdReceiverFlag(int Mode, GlassField Failed, NdLabelPair FullLabels, string HalfLabel);

/// <summary>
/// The glideslope, at the right side in APP: the scale (dots) unless the receiver's G/S fail flag
/// (<see cref="Failed"/>, glass data per receiver) is set while <see cref="FmcSource"/> (per side) is not the
/// block's FMC value, else PMDG's amber <see cref="FlagText"/> (its stacked letters, boxed when
/// <see cref="FlagBoxed"/>); on the scale the pointer while the receiver's state (the CDI's) is
/// <see cref="PointerState"/>, at the receiver's glass data <see cref="Deviation"/> clamped by PMDG to
/// ±<see cref="FullScale"/>, <see cref="DotsPerUnit"/> dots per unit, plus below.
/// </summary>
public sealed record NdGlideslopeFields(
    GlassField Deviation,
    double FullScale,
    double DotsPerUnit,
    int PointerState,
    GlassField Failed,
    GlassField FmcSource,
    string FlagText,
    bool FlagBoxed);

/// <summary>
/// The selected course and CDI, drawn only while the side's readout is valid: the course pointer, turned by the
/// side's course (glass data <see cref="PointerCourse"/> by side, the int PMDG truncates it to) off PMDG's
/// up direction; the deviation bar, drawn unless the receiver's VOR fail flag is set (ignored while
/// <see cref="FmcSource"/>, per side, is the block's FMC value) or its state (glass data <see cref="ReceiverState"/>,
/// per receiver) is below <see cref="ReceiverStateFrom"/>: APP the glass data <see cref="ApproachDeviation"/> of
/// the record the block's ident uses (the FMC index with the FMC source, else the receiver's), VOR
/// <see cref="VorDeviation"/> of the receiver, both clamped by PMDG to ±<see cref="FullScale"/> and drawn
/// <see cref="DotsPerUnit"/> dots per unit, plus to the right of the pointer; filled in APP while the deviation's
/// magnitude is below <see cref="FilledBelow"/>, else outlined.
/// </summary>
public sealed record NdCdiFields(
    GlassField PointerCourse,
    GlassField FmcSource,
    GlassField ReceiverState,
    int ReceiverStateFrom,
    GlassField ApproachDeviation,
    GlassField VorDeviation,
    double FullScale,
    double DotsPerUnit,
    double FilledBelow);

/// <summary>Two texts PMDG chooses between by an index: <see cref="WhenZero"/> while it is 0, else
/// <see cref="Otherwise"/>.</summary>
public sealed record NdLabelPair(string WhenZero, string Otherwise)
{
    public string For(int index) => index == 0 ? WhenZero : Otherwise;
}

/// <summary>
/// The ILS/VOR block PMDG draws at the top right in APP and VOR, for the receiver the side
/// shows (EFIS object <see cref="NavIndex"/>, per side): its label (VOR mode <see cref="VorLabels"/> by that index; APP
/// <see cref="FmcLabels"/> by side while either of the glass data's <see cref="FmcSources"/> (per side) is
/// <see cref="FmcSourceValue"/>, else <see cref="IlsLabels"/> by the index); the ident (APP, while an FMC source
/// is set or the receiver's frequency is an ILS one: the C string at glass data <see cref="ApproachIdents"/> +
/// k × <see cref="ApproachIdentStride"/>, k the EFIS object's <see cref="FmcIndex"/> with an FMC source, else the
/// receiver's; VOR, while the frequency is not an ILS one: the tuned station's ident at simulator interface
/// <see cref="Stations"/> + index × <see cref="StationStride"/> + <see cref="Ident"/>, with none its frequency
/// text at <see cref="FrequencyText"/>; otherwise <see cref="Dashes"/>); in the full format the course
/// (<see cref="CourseLabel"/>, glass data <see cref="Course"/> per receiver, truncated, "%03d"); the DME
/// (<see cref="DmeLabel"/>, glass data <see cref="Dme"/> per receiver, from <see cref="DmeShownFrom"/>, tenths
/// below <see cref="DmeTenthsBelow"/>, else <see cref="DmeDashes"/>; while <see cref="DmeFailed"/>, per receiver,
/// is set the amber <see cref="DmeFailedLabels"/> by the index (the half format's
/// <see cref="DmeFailedHalfLabel"/>), boxed when <see cref="DmeFailedBoxed"/>); in VOR, unless
/// <see cref="VorFailed"/> (per receiver), <see cref="ToLabel"/> or <see cref="FromLabel"/> while glass data
/// <see cref="ToFrom"/> (per receiver) is <see cref="ToValue"/> or <see cref="FromValue"/>. A frequency is an
/// ILS one when PMDG calls it one: the int at <see cref="Frequency"/> in the station is one of
/// <see cref="IlsFrequencies"/>. Equality compares the lists by
/// content.
/// </summary>
public sealed record NdApproachBlockFields(
    GlassField NavIndex,
    GlassField FmcIndex,
    IReadOnlyList<GlassField> FmcSources,
    int FmcSourceValue,
    int Stations,
    int StationStride,
    int Ident,
    int FrequencyText,
    int Frequency,
    IReadOnlyList<int> IlsFrequencies,
    int ApproachIdents,
    int ApproachIdentStride,
    string Dashes,
    string CourseLabel,
    GlassField Course,
    string DmeLabel,
    GlassField Dme,
    double DmeShownFrom,
    double DmeTenthsBelow,
    string DmeDashes,
    GlassField DmeFailed,
    NdLabelPair DmeFailedLabels,
    string DmeFailedHalfLabel,
    bool DmeFailedBoxed,
    GlassField VorFailed,
    GlassField ToFrom,
    int ToValue,
    string ToLabel,
    int FromValue,
    string FromLabel,
    NdLabelPair VorLabels,
    NdLabelPair FmcLabels,
    NdLabelPair IlsLabels)
{
    public bool Equals(NdApproachBlockFields? other) =>
        other is not null
        && NavIndex == other.NavIndex && FmcIndex == other.FmcIndex && FmcSources.SequenceEqual(other.FmcSources) && FmcSourceValue == other.FmcSourceValue
        && Stations == other.Stations && StationStride == other.StationStride && Ident == other.Ident && FrequencyText == other.FrequencyText
        && Frequency == other.Frequency && IlsFrequencies.SequenceEqual(other.IlsFrequencies)
        && ApproachIdents == other.ApproachIdents && ApproachIdentStride == other.ApproachIdentStride && Dashes == other.Dashes
        && CourseLabel == other.CourseLabel && Course == other.Course && DmeLabel == other.DmeLabel && Dme == other.Dme
        && DmeShownFrom.Equals(other.DmeShownFrom) && DmeTenthsBelow.Equals(other.DmeTenthsBelow) && DmeDashes == other.DmeDashes
        && DmeFailed == other.DmeFailed && DmeFailedLabels == other.DmeFailedLabels && DmeFailedHalfLabel == other.DmeFailedHalfLabel
        && DmeFailedBoxed == other.DmeFailedBoxed && VorFailed == other.VorFailed && ToFrom == other.ToFrom
        && ToValue == other.ToValue && ToLabel == other.ToLabel && FromValue == other.FromValue && FromLabel == other.FromLabel
        && VorLabels == other.VorLabels && FmcLabels == other.FmcLabels && IlsLabels == other.IlsLabels;

    public override int GetHashCode() => HashCode.Combine(NavIndex, Stations, StationStride, Frequency, IlsFrequencies.Count, Course, Dme, ToFrom);

    /// <summary>Whether PMDG calls this frequency an ILS one.</summary>
    public bool IsIls(int frequency) => IlsFrequencies.Contains(frequency);
}

/// <summary>
/// What the ND's APP and VOR part reads in one sample, raw, read only while the Captain's ND is in APP or VOR
/// and the layout has the part: the receiver the block shows, the FMC index and sources, the receiver's
/// frequency; the APP ident (null unless APP prints it), the VOR ident and frequency text (null unless VOR
/// prints one); the course, the DME and its fail flag; the VOR's fail flag, in VOR TO/FROM; the course pointer's
/// course, the receiver's state and the mode's deviation (raw, as the glass data holds it); in APP the glideslope
/// deviation and its fail flag, and the LOC fail flag.
/// </summary>
public sealed record NdApproachSample(
    int NavIndex,
    int FmcIndex,
    IReadOnlyList<int> Sources,
    int Frequency,
    string? ApproachIdent,
    string? VorIdent,
    string? VorFrequency,
    double Course,
    double DmeNm,
    bool DmeFailed,
    bool VorFailed,
    int ToFrom,
    double PointerCourse,
    int ReceiverState,
    double Deviation,
    double Glideslope,
    bool GlideslopeFailed,
    bool LocFailed)
{
    public bool Equals(NdApproachSample? other) =>
        other is not null && NavIndex == other.NavIndex && FmcIndex == other.FmcIndex && Sources.SequenceEqual(other.Sources)
        && Frequency == other.Frequency && ApproachIdent == other.ApproachIdent && VorIdent == other.VorIdent && VorFrequency == other.VorFrequency
        && Course.Equals(other.Course) && DmeNm.Equals(other.DmeNm) && DmeFailed == other.DmeFailed && VorFailed == other.VorFailed && ToFrom == other.ToFrom
        && PointerCourse.Equals(other.PointerCourse) && ReceiverState == other.ReceiverState && Deviation.Equals(other.Deviation)
        && Glideslope.Equals(other.Glideslope) && GlideslopeFailed == other.GlideslopeFailed && LocFailed == other.LocFailed;

    public override int GetHashCode() => HashCode.Combine(NavIndex, Frequency, ApproachIdent, VorIdent, Course, DmeNm, ToFrom);
}

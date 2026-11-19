namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The singletons the Captain's navigation display reads, each behind its global: the EFIS object (the
/// EFIS control panels and display units), the FMS (the route), the glass data (heading, track, the
/// readouts' flags), the simulator interface (wind, airspeed, magnetic variation), the TCAS, the GPWS, the
/// weather radar, the IRS (the IRUs), the options (display options) and the systems object (the receivers'
/// power). Read from the layout file, never hardcoded.
/// </summary>
public sealed record NdObjects(
    GlassObject Efis,
    GlassObject Fms,
    GlassObject Glass,
    GlassObject Sim,
    GlassObject Tcas,
    GlassObject Gpws,
    GlassObject Weather,
    GlassObject Isdu,
    GlassObject Options,
    GlassObject Misc);

/// <summary>
/// The Captain's EFIS control panel: at EFIS + <see cref="PanelBase"/> + i[EFIS +
/// <see cref="PanelIndexOffset"/>] × <see cref="PanelStride"/> (the panel index of side 0, which PMDG's
/// EFIS source switching changes). Every field is relative to the panel, named as PMDG saves the panel's
/// state. <see cref="RangeLabels"/> is the table the ND prints
/// its ring label from: index range + <see cref="ExpandedLabelShift"/> in expanded MAP, range +
/// <see cref="CentredLabelShift"/> when centred (the ring there spans half as much). Equality compares the
/// labels by content.
/// </summary>
public sealed record NdPanelFields(
    int PanelBase,
    int PanelStride,
    int PanelIndexOffset,
    GlassField Mode,
    GlassField Range,
    GlassField VorAdfLeft,
    GlassField VorAdfRight,
    GlassField Centered,
    GlassField ShowTfc,
    GlassField ShowWxr,
    GlassField ShowSta,
    GlassField ShowWpt,
    GlassField ShowArpt,
    GlassField ShowData,
    GlassField ShowPos,
    GlassField ShowTerr,
    GlassField ShowVsd,
    GlassField Powered,
    IReadOnlyList<string> RangeLabels,
    int ExpandedLabelShift,
    int CentredLabelShift)
{
    public bool Equals(NdPanelFields? other) =>
        other is not null
        && PanelBase == other.PanelBase && PanelStride == other.PanelStride && PanelIndexOffset == other.PanelIndexOffset
        && Mode == other.Mode && Range == other.Range && VorAdfLeft == other.VorAdfLeft && VorAdfRight == other.VorAdfRight
        && Centered == other.Centered && ShowTfc == other.ShowTfc && ShowWxr == other.ShowWxr && ShowSta == other.ShowSta
        && ShowWpt == other.ShowWpt && ShowArpt == other.ShowArpt && ShowData == other.ShowData && ShowPos == other.ShowPos
        && ShowTerr == other.ShowTerr && ShowVsd == other.ShowVsd && Powered == other.Powered
        && RangeLabels.SequenceEqual(other.RangeLabels, StringComparer.Ordinal)
        && ExpandedLabelShift == other.ExpandedLabelShift && CentredLabelShift == other.CentredLabelShift;

    public override int GetHashCode() => HashCode.Combine(PanelBase, PanelStride, PanelIndexOffset, Mode, Range, RangeLabels.Count);
}

/// <summary>A display unit that, showing <see cref="Page"/>, draws the Captain's ND: always, or only while
/// the lower DU selector holds <see cref="WhenSelector"/> (PMDG's lower centre unit on the full ND page).</summary>
public readonly record struct NdCaptainUnit(int Unit, int Page, int? WhenSelector);

/// <summary>
/// The EFIS object's display units (<see cref="UnitCount"/> units <see cref="UnitStride"/> bytes apart, the
/// page value at <see cref="PageOffset"/>, the unit index at <see cref="IndexOffset"/>); the page values
/// that draw the ND in full (the ND page) or as a map page (the inboard EFIS/MAP pages), those that leave
/// the unit dark (the off and blank pages); which unit and page draw the Captain's ND, as PMDG's display
/// unit decides; the lower DU selector that decides it for the lower unit (on the EFIS object, element
/// <see cref="SelectorSide"/>); the unit's DEU at <see cref="Deu"/> (the field the unit passes to the page it
/// draws, which the page keeps). Equality compares the lists by content.
/// </summary>
public sealed record NdUnitFields(
    int UnitCount,
    int UnitStride,
    int PageOffset,
    int IndexOffset,
    IReadOnlyList<int> FullNdPages,
    IReadOnlyList<int> MapPages,
    IReadOnlyList<int> DarkPages,
    IReadOnlyList<NdCaptainUnit> CaptainUnits,
    GlassField LowerSelector,
    int SelectorSide,
    int Deu)
{
    public bool Equals(NdUnitFields? other) =>
        other is not null
        && UnitCount == other.UnitCount && UnitStride == other.UnitStride && PageOffset == other.PageOffset
        && IndexOffset == other.IndexOffset && FullNdPages.SequenceEqual(other.FullNdPages) && MapPages.SequenceEqual(other.MapPages)
        && DarkPages.SequenceEqual(other.DarkPages) && CaptainUnits.SequenceEqual(other.CaptainUnits)
        && LowerSelector == other.LowerSelector && SelectorSide == other.SelectorSide && Deu == other.Deu;

    public override int GetHashCode() => HashCode.Combine(UnitCount, UnitStride, PageOffset, IndexOffset, CaptainUnits.Count, LowerSelector);
}

/// <summary>The EFIS mode selector's values (the ND's mode flags, told apart by what each gates in its
/// drawing).</summary>
public sealed record NdModeValues(int App, int Vor, int Map, int Plan);

/// <summary>How PMDG clips the map in one view: a circle, an arc closed by a bottom line, or a union of rectangles.</summary>
public enum NdClipShape { Circle, ArcAndBottom, Rectangles }

/// <summary>A rectangle in the ND's screen units (y down).</summary>
public readonly record struct NdRect(double X, double Y, double Width, double Height);

/// <summary>
/// One full-format view's screen (units of a <see cref="NdGeometry.ScreenSize"/> square, y down): the map
/// centre (the aircraft, or the PLN centre), the compass radius in screen units for a distance of
/// <see cref="RangeFactor"/> × the selected range (PMDG halves the radius when centred and in PLN), and
/// the clip PMDG sets for the map: a circle of <see cref="ClipRadius"/>, an arc of that radius from
/// <see cref="ArcStartDeg"/> sweeping <see cref="ArcSweepDeg"/> (GDI+ angles: clockwise from +x) then the line
/// at <see cref="BottomY"/> from <see cref="BottomFromX"/> to <see cref="BottomToX"/> (GDI+ joins the arc's end
/// to it and closes the figure back to the arc's start), or <see cref="Rectangles"/>. Equality compares the
/// rectangles by content.
/// </summary>
public sealed record NdViewport(
    double CentreX,
    double CentreY,
    double CompassRadius,
    double RangeFactor,
    NdClipShape Clip,
    double ClipRadius,
    double ArcStartDeg,
    double ArcSweepDeg,
    double BottomY,
    double BottomFromX,
    double BottomToX,
    IReadOnlyList<NdRect> Rectangles)
{
    public bool Equals(NdViewport? other) =>
        other is not null && CentreX == other.CentreX && CentreY == other.CentreY && CompassRadius == other.CompassRadius
        && RangeFactor == other.RangeFactor && Clip == other.Clip && ClipRadius == other.ClipRadius && ArcStartDeg == other.ArcStartDeg
        && ArcSweepDeg == other.ArcSweepDeg && BottomY == other.BottomY && BottomFromX == other.BottomFromX && BottomToX == other.BottomToX
        && Rectangles.SequenceEqual(other.Rectangles);

    public override int GetHashCode() => HashCode.Combine(CentreX, CentreY, CompassRadius, RangeFactor, Clip, ClipRadius, Rectangles.Count);
}

/// <summary>
/// The full-format ND's screen: its size, the three map views (expanded MAP, centred MAP, PLN), the mode
/// values, the selected range in NM for each range selector index, and the cull PMDG passes
/// a symbol through before drawing it. Equality compares the ranges by content.
/// </summary>
public sealed record NdGeometry(double ScreenSize, NdViewport Expanded, NdViewport Centred, NdViewport Plan, NdModeValues Modes, IReadOnlyList<int> SelectedRangesNm,
    NdCull Cull)
{
    public bool Equals(NdGeometry? other) =>
        other is not null && ScreenSize == other.ScreenSize && Expanded.Equals(other.Expanded) && Centred.Equals(other.Centred)
        && Plan.Equals(other.Plan) && Modes == other.Modes && SelectedRangesNm.SequenceEqual(other.SelectedRangesNm) && Cull == other.Cull;

    public override int GetHashCode() => HashCode.Combine(ScreenSize, Expanded, Centred, Plan, Modes, SelectedRangesNm.Count, Cull);
}

/// <summary>
/// The cull PMDG passes a symbol through before drawing it (the nearby lists count what passes toward
/// EXCESS DATA): a point, from the view's centre, is drawn unless it lies more than <see cref="HalfWidth"/> to
/// either side (<see cref="HalfWidthHalfFormat"/> in the half format), more than the view's compass radius (×
/// <see cref="CentredRadiusFactor"/> on the centred map) plus <see cref="TopMargin"/> above the centre, or, with
/// the centre's height added, below <see cref="BottomLimit"/>. The view's clip then cuts what is drawn.
/// </summary>
public sealed record NdCull(double HalfWidth, double HalfWidthHalfFormat, double TopMargin, double BottomLimit, double CentredRadiusFactor);

/// <summary>
/// The top-left corner: the ground speed (glass data) printed while the side's IRU is in its NAV condition
/// (<see cref="GroundSpeedNavCondition"/>), else "---"; the true airspeed (simulator interface; PMDG smooths it) from
/// <see cref="TasShownFrom"/> knots, else "---"; the wind (simulator interface direction and speed, smoothed) as
/// "%03d" + <see cref="WindSeparator"/> + "%3d" once the TAS is above <see cref="WindTasAbove"/>, the wind
/// above <see cref="WindShownAbove"/> (and until it drops below <see cref="WindHiddenBelow"/>) and the track
/// valid; <see cref="WindDashes"/> at or below that TAS (or with no track); nothing while calm.
/// </summary>
public sealed record NdCornerFields(
    GlassField GroundSpeed,
    int GroundSpeedNavCondition,
    GlassField TrueAirspeed,
    double TasShownFrom,
    GlassField WindDirection,
    GlassField WindSpeed,
    double WindTasAbove,
    double WindShownAbove,
    double WindHiddenBelow,
    string WindDashes,
    string WindSeparator);

/// <summary>
/// The nearby navaids, fixes and airports PMDG keeps on the EFIS object (vectors at <see cref="NavaidVector"/>,
/// <see cref="FixVector"/>, <see cref="AirportVector"/>: begin and end pointers at <see cref="VectorBegin"/> and
/// <see cref="VectorEnd"/>, elements of <see cref="ElementSize"/> bytes, the object pointer at
/// <see cref="ObjectPointer"/>); an object's name (C string) and position; a navaid's flags (the VOR and
/// DME bits PMDG draws the hexagon and the DME shape for; PMDG tests the bits in
/// <see cref="TestedFlags"/> and draws a navaid only for the values of them set as bits of
/// <see cref="DrawnFlagValues"/>, see <see cref="DrawsStation"/>) and class (above
/// <see cref="HighClassAbove"/> NM only <see cref="HighClass"/> is drawn); fixes only up to
/// <see cref="FixesUpTo"/> NM; each list stops at <see cref="MaxElements"/> drawn (EXCESS DATA).
/// </summary>
public sealed record NdListFields(
    int NavaidVector,
    int FixVector,
    int AirportVector,
    int VectorBegin,
    int VectorEnd,
    int ElementSize,
    int ObjectPointer,
    int ObjectName,
    int ObjectPosition,
    int StationFlags,
    int VorFlag,
    int DmeFlag,
    int TestedFlags,
    long DrawnFlagValues,
    int StationClass,
    int HighClass,
    double HighClassAbove,
    double FixesUpTo,
    int MaxElements)
{
    /// <summary>Whether PMDG's flag tests let a navaid with these flags be drawn.</summary>
    public bool DrawsStation(int flags) => ((DrawnFlagValues >> (flags & TestedFlags)) & 1) != 0;
}

/// <summary>
/// The tuned VORs PMDG draws: <see cref="Count"/> receivers on the simulator interface at
/// <see cref="Stations"/> + k × <see cref="Stride"/>, tuned while the state at <see cref="State"/> is
/// <see cref="TunedState"/> and the systems object's <see cref="Powered"/> byte (per receiver) is set (PMDG's
/// tuned flag), a VOR unless it is <see cref="NotVorState"/>, a DME while the float at
/// <see cref="DmeDistance"/> is not negative; ident (C string) and position; the course line (glass data
/// <see cref="Course"/>, per receiver) drawn while the options' <see cref="CourseOption"/> is set and the side's
/// VOR/ADF switch for that receiver is on VOR.
/// </summary>
public sealed record NdTunedFields(
    int Stations,
    int Stride,
    int Count,
    int Ident,
    int Position,
    int State,
    int TunedState,
    int NotVorState,
    int DmeDistance,
    GlassField Course,
    GlassField CourseOption,
    GlassField Powered);

/// <summary>
/// The bottom corners (the selected receivers' data): per receiver k the side's VOR/ADF switch (glass data
/// <see cref="Switch"/> + k × its stride + side × <see cref="SwitchSideStride"/>), on VOR the VOR receiver's
/// ident (or, with none, its frequency text at <see cref="VorFrequencyText"/>), on ADF the ADF receiver's
/// (simulator interface <see cref="AdfStations"/> + k × <see cref="AdfStride"/>, valid while the int at
/// <see cref="AdfValid"/> is above 0, else "------"); the DME distance (glass data <see cref="Dme"/>, per
/// receiver) printed with tenths below <see cref="DmeTenthsBelow"/>. While a receiver's fail flag
/// (<see cref="VorFailed"/>, <see cref="DmeFailed"/>, <see cref="AdfFailed"/>: glass data, per receiver) is set
/// PMDG draws its label in another colour and no value, and a rectangle around the label when
/// <see cref="VorFailedBoxed"/>, <see cref="DmeFailedBoxed"/> or <see cref="AdfFailedBoxed"/> (the failed arm
/// draws one); the VOR prints "------" unless the systems object's <see cref="Powered"/> byte (per receiver) is set
/// and the VOR's state is one of <see cref="VorShownStates"/>. Equality compares the states by content.
/// </summary>
public sealed record NdReceiverFields(
    GlassField Switch,
    int SwitchSideStride,
    int VorPosition,
    int AdfPosition,
    int VorFrequencyText,
    int AdfStations,
    int AdfStride,
    int AdfIdent,
    int AdfFrequencyText,
    int AdfValid,
    GlassField Dme,
    double DmeTenthsBelow,
    GlassField VorFailed,
    GlassField DmeFailed,
    GlassField AdfFailed,
    GlassField Powered,
    IReadOnlyList<int> VorShownStates,
    bool VorFailedBoxed,
    bool DmeFailedBoxed,
    bool AdfFailedBoxed)
{
    public bool Equals(NdReceiverFields? other) =>
        other is not null
        && Switch == other.Switch && SwitchSideStride == other.SwitchSideStride && VorPosition == other.VorPosition
        && AdfPosition == other.AdfPosition && VorFrequencyText == other.VorFrequencyText && AdfStations == other.AdfStations
        && AdfStride == other.AdfStride && AdfIdent == other.AdfIdent && AdfFrequencyText == other.AdfFrequencyText
        && AdfValid == other.AdfValid && Dme == other.Dme && DmeTenthsBelow == other.DmeTenthsBelow && VorFailed == other.VorFailed
        && DmeFailed == other.DmeFailed && AdfFailed == other.AdfFailed && Powered == other.Powered
        && VorShownStates.SequenceEqual(other.VorShownStates)
        && VorFailedBoxed == other.VorFailedBoxed && DmeFailedBoxed == other.DmeFailedBoxed && AdfFailedBoxed == other.AdfFailedBoxed;

    public override int GetHashCode() => HashCode.Combine(Switch, VorPosition, AdfStations, Dme, VorFailed, Powered, VorShownStates.Count);
}

/// <summary>
/// The TCAS traffic PMDG draws: the list pointer on the TCAS object (<see cref="List"/>); nothing at all unless
/// the list's byte at <see cref="ListShown"/> and the TCAS object's <see cref="Powered"/> are set, nor while the
/// <see cref="SelfTest"/> runs before <see cref="SelfTestShown"/>; the list's count (byte) and target
/// pointers; a target's relative altitude (feet), range (NM) and bearing (radians from the nose) floats, its
/// threat (the byte at <see cref="ThreatAndTrend"/> shifted by <see cref="ThreatShift"/>: 0 other, 1 proximate,
/// 2 TA, 3 RA) and trend (that byte's low nibble, signed), drawn only while its byte at <see cref="Present"/>
/// is set; other traffic only within the options' <see cref="FilterRange"/> (NM) and
/// <see cref="FilterSeparation"/> (feet) unless <see cref="ShowAllTraffic"/> or the test; threats up to
/// <see cref="TfcThreatsUpTo"/> only while the panel's Show TFC or the self-test is on; an RA shown as a TA
/// while the TCAS object's <see cref="TaOnly"/> is set; the list in the TCAS test while its int at
/// <see cref="TestField"/> is <see cref="TestValue"/>: then the filter is skipped and a target's range and
/// bearing are <see cref="TestPositions"/>' by its index in the list (counting the targets not present), its
/// own beyond them; a TA or RA beyond <see cref="OffscaleBeyond"/> of the
/// visible range, or one PMDG's cull leaves undrawn while <see cref="OffscaleWhenCulled"/>, is OFFSCALE; the printed relative altitude (hundreds) clamped at ±
/// <see cref="HundredsLimit"/>; <see cref="MaxTargets"/> caps the reading. The ND draws the traffic in the
/// one mode <see cref="CentredMode"/> centred too (MAP; -1 none), the others not centred.
/// </summary>
public sealed record NdTrafficFields(
    int List,
    int ListShown,
    GlassField Powered,
    GlassField SelfTest,
    GlassField SelfTestShown,
    int Count,
    int Targets,
    int RelativeAltitude,
    int Range,
    int Bearing,
    int ThreatAndTrend,
    int ThreatShift,
    int Present,
    GlassField ShowAllTraffic,
    GlassField FilterRange,
    GlassField FilterSeparation,
    int TfcThreatsUpTo,
    GlassField TaOnly,
    int TestField,
    int TestValue,
    IReadOnlyList<NdTestPosition> TestPositions,
    double OffscaleBeyond,
    bool OffscaleWhenCulled,
    int HundredsLimit,
    int MaxTargets,
    int CentredMode)
{
    public bool Equals(NdTrafficFields? other) =>
        other is not null
        && List == other.List && ListShown == other.ListShown && Powered == other.Powered && SelfTest == other.SelfTest
        && SelfTestShown == other.SelfTestShown && Count == other.Count && Targets == other.Targets
        && RelativeAltitude == other.RelativeAltitude && Range == other.Range && Bearing == other.Bearing
        && ThreatAndTrend == other.ThreatAndTrend && ThreatShift == other.ThreatShift && Present == other.Present
        && ShowAllTraffic == other.ShowAllTraffic && FilterRange == other.FilterRange && FilterSeparation == other.FilterSeparation
        && TfcThreatsUpTo == other.TfcThreatsUpTo && TaOnly == other.TaOnly && TestField == other.TestField
        && TestValue == other.TestValue && TestPositions.SequenceEqual(other.TestPositions) && OffscaleBeyond == other.OffscaleBeyond
        && OffscaleWhenCulled == other.OffscaleWhenCulled && HundredsLimit == other.HundredsLimit && MaxTargets == other.MaxTargets && CentredMode == other.CentredMode;

    public override int GetHashCode() => HashCode.Combine(List, Powered, Range, Bearing, TestValue, TestPositions.Count, CentredMode);
}

/// <summary>Where PMDG draws a target in the TCAS test: its range (NM) and bearing (radians from the nose).</summary>
public sealed record NdTestPosition(double RangeNm, double BearingRad);

/// <summary>
/// The ND symbols added after the flight, which the first build did not read, each null when the layout has
/// none for it (the reason in <see cref="Failures"/>): a PMDG change there leaves that symbol's row out, never
/// the rest of the ND. Equality compares the failures by content.
/// </summary>
public sealed record NdDetailFields(NdTcasRingFields? TcasRing, NdTrendVectorFields? TrendVector, NdTrackLineFields? TrackLine, NdHoldFields? Hold,
    IReadOnlyList<string> Failures)
{
    public bool Equals(NdDetailFields? other) =>
        other is not null && Equals(TcasRing, other.TcasRing) && Equals(TrendVector, other.TrendVector) && Equals(TrackLine, other.TrackLine)
        && Equals(Hold, other.Hold) && Failures.SequenceEqual(other.Failures, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(TcasRing, TrendVector, TrackLine, Hold, Failures.Count);
}

/// <summary>
/// A hold node's hold (the holding pattern PMDG draws, and the CDU's HOLD page for the legs), each offset on the node:
/// the hold record at <see cref="Record"/> (PMDG's pending copy, drawn only for a MOD's hold being flown, is not
/// read), its turn byte at <see cref="Turn"/> (the record's), left turns while it equals <see cref="TurnChar"/> (the
/// pattern drawn on the inbound course's left, inbound − 90°) and right turns otherwise; the inbound course at
/// <see cref="Inbound"/> (the record's, a true course: the HOLD page prints it less the node's variation, rounded); the
/// HOLD page's LEG TIME (minutes, <see cref="LegTimeFormat"/>) at <see cref="LegTime"/> and LEG DIST (NM,
/// <see cref="LegDistanceFormat"/>) at <see cref="LegDistance"/>, each printed only above <see cref="LegInvalidAtOrBelow"/>.
/// </summary>
public sealed record NdHoldFields(int Record, int Turn, char TurnChar, int Inbound, int LegTime, string LegTimeFormat, int LegDistance,
    string LegDistanceFormat, double LegInvalidAtOrBelow);

/// <summary>
/// The track line, drawn in every mode but PLN: centred in the modes of <see cref="PointerCentredIn"/> (APP and VOR) on
/// the half format, a track pointer at the compass while the readout is valid, and nothing else; otherwise, while the
/// track is valid, the line from the aircraft up toward the compass, turned by the track less the heading while the map
/// is heading-up and the readout valid (straight up otherwise); and its marks across it at <see cref="MarkFractions"/>
/// of the compass radius while PMDG's flag (the track valid, or WXR, TERR, TFC or the TCAS self-test on) is set and
/// its other flag (the options' <see cref="RangeArcsOption"/>, PMDG's [Displays] ND_RangeArcs, not PLN, not centred, and one
/// of those four on: the range arcs drawn instead) is clear. Equality compares the lists by content.
/// </summary>
public sealed record NdTrackLineFields(IReadOnlyList<int> PointerCentredIn, IReadOnlyList<double> MarkFractions, GlassField RangeArcsOption)
{
    public bool Equals(NdTrackLineFields? other) =>
        other is not null && PointerCentredIn.SequenceEqual(other.PointerCentredIn) && MarkFractions.SequenceEqual(other.MarkFractions)
        && RangeArcsOption == other.RangeArcsOption;

    public override int GetHashCode() => HashCode.Combine(PointerCentredIn.Count, MarkFractions.Count, RangeArcsOption);
}

/// <summary>
/// The trend vector's path ahead of the aircraft: drawn while PMDG's copy of the ground speed (the corners'
/// <see cref="NdCornerFields.GroundSpeed"/>, knots) is above <see cref="GroundSpeedAbove"/> and the map valid, in
/// <see cref="SegmentsByRange"/>[the range selector index] segments, each <see cref="GapSeconds"/> left out then
/// <see cref="DrawnSeconds"/> drawn, so the last ends at segments × their sum. While the turn rate (simulator interface
/// <see cref="TurnRate"/>, radians a second, which PMDG smooths into degrees a second by dividing by
/// <see cref="RadiansPerDegree"/>) is at least <see cref="TurnFrom"/> °/s either way, arcs of radius (GS /
/// <see cref="SecondsPerHour"/>) / (the rate in °/s × RadiansPerDegree) NM, turning right for a positive rate while
/// <see cref="RightWhenPositive"/>; else straight up the screen. The ND draws it in MAP alone, centred or not.
/// Equality compares the list by content.
/// </summary>
public sealed record NdTrendVectorFields(double GroundSpeedAbove, IReadOnlyList<int> SegmentsByRange, double TurnFrom, GlassField TurnRate,
    double RadiansPerDegree, bool RightWhenPositive, double DrawnSeconds, double GapSeconds, double SecondsPerHour)
{
    public bool Equals(NdTrendVectorFields? other) =>
        other is not null && GroundSpeedAbove == other.GroundSpeedAbove && SegmentsByRange.SequenceEqual(other.SegmentsByRange)
        && TurnFrom == other.TurnFrom && TurnRate == other.TurnRate && RadiansPerDegree == other.RadiansPerDegree
        && RightWhenPositive == other.RightWhenPositive && DrawnSeconds == other.DrawnSeconds && GapSeconds == other.GapSeconds
        && SecondsPerHour == other.SecondsPerHour;

    public override int GetHashCode() => HashCode.Combine(GroundSpeedAbove, SegmentsByRange.Count, TurnFrom, TurnRate, RadiansPerDegree, DrawnSeconds, GapSeconds);
}

/// <summary>
/// The TCAS range ring of dots round the aircraft, <see cref="RadiusNm"/> NM out: drawn while the TCAS object's traffic
/// list exists, the panel's Show TFC or the TCAS self-test is on, the list's int at the traffic's
/// <see cref="NdTrafficFields.TestField"/> is one of <see cref="ListValues"/> (not the TCAS test's), the selected
/// range is at most <see cref="UpToRangeNm"/> and the options' <see cref="Option"/> (PMDG's [Displays] ND_TCAS_RangeRing)
/// is set; the ND draws it where it
/// draws the traffic. Equality compares the values by content.
/// </summary>
public sealed record NdTcasRingFields(double RadiusNm, IReadOnlyList<int> ListValues, int UpToRangeNm, GlassField Option)
{
    public bool Equals(NdTcasRingFields? other) =>
        other is not null && RadiusNm == other.RadiusNm && ListValues.SequenceEqual(other.ListValues) && UpToRangeNm == other.UpToRangeNm
        && Option == other.Option;

    public override int GetHashCode() => HashCode.Combine(RadiusNm, ListValues.Count, UpToRangeNm, Option);
}

/// <summary>
/// The symbols the map draws without a button: the selected heading (glass data) for the heading bug; the
/// altitude arc from the MCP altitude (glass data int), the altitude (glass data double by panel index) and
/// the vertical speed (simulator interface), drawn while |VS| is above <see cref="ArcVsAbove"/> ft/min and the
/// distance (ground speed × minutes / 60) is within <see cref="ArcWithinRadius"/> of the range radius. PLN's
/// aircraft symbol: at the FMS position, while the map is valid, the position valid
/// and its latitude's magnitude not above <see cref="PlanAircraftLatitudeLimit"/> degrees. The heading bug's dashed
/// line (<see cref="HeadingLine"/>).
/// </summary>
public sealed record NdSymbolFields(
    GlassField SelectedHeading,
    GlassField McpAltitude,
    GlassField Altitude,
    GlassField VerticalSpeed,
    double ArcVsAbove,
    double MinutesPerHour,
    double ArcWithinRadius,
    double PlanAircraftLatitudeLimit,
    NdHeadingLineFields HeadingLine);

/// <summary>
/// The heading bug's dashed line from the aircraft to the bug (to the compass, whether the bug is on the
/// screen or not): not drawn centred in the modes of <see cref="HiddenCentredIn"/> (VOR and APP), nor while the options'
/// <see cref="Option"/> (PMDG's [Displays] Glass_PFD_ND) is set, the FMA roll mode (glass data <see cref="RollMode"/>) is one of
/// <see cref="TimedRollModes"/> (LNAV, VOR/LOC) and more than <see cref="ShownForSeconds"/> have passed on the sim's
/// clock (simulator interface <see cref="SimTime"/>) since the selected heading last changed: the time PMDG keeps
/// on the ND's own symbol generator object (<see cref="Generator"/>, its <see cref="ChangedAt"/>), read there. Equality
/// compares the lists by content.
/// </summary>
public sealed record NdHeadingLineFields(IReadOnlyList<int> HiddenCentredIn, GlassField Option, GlassField RollMode, IReadOnlyList<int> TimedRollModes,
    double ShownForSeconds, GlassField SimTime, GlassObject Generator, GlassField ChangedAt)
{
    public bool Equals(NdHeadingLineFields? other) =>
        other is not null && HiddenCentredIn.SequenceEqual(other.HiddenCentredIn) && Option == other.Option && RollMode == other.RollMode
        && TimedRollModes.SequenceEqual(other.TimedRollModes) && ShownForSeconds == other.ShownForSeconds && SimTime == other.SimTime
        && Generator == other.Generator && ChangedAt == other.ChangedAt;

    public override int GetHashCode() => HashCode.Combine(HiddenCentredIn.Count, Option, RollMode, TimedRollModes.Count, ShownForSeconds, SimTime, Generator, ChangedAt);
}

/// <summary>
/// The CDU FIX page PMDG draws on the map: the side's CDU object (pointer array at <see cref="CduGlobal"/>, objects of
/// <see cref="CduSize"/> bytes), its page pointers (CDU + <see cref="Pages"/> + page × 4) for
/// <see cref="FixPage"/>; <see cref="EntryCount"/> entry pointers at page + <see cref="Entries"/>; an entry's
/// fix object (name, position, type: <see cref="FixType"/>, <see cref="AirportType"/>,
/// <see cref="NavaidType"/>), its <see cref="RingCount"/> distance circles (doubles at <see cref="Rings"/>) and
/// <see cref="RadialCount"/> radials (doubles at <see cref="Radials"/>), each above
/// <see cref="InvalidAtOrBelow"/>. The fix object's name is inline at the object, its position at
/// <see cref="ObjectPosition"/>. The entry's abeam point (a position at <see cref="Abeam"/>, the FIX page's fourth
/// intersection, which its ABEAM key fills) gets a line from the fix while valid.
/// </summary>
public sealed record NdFixPageFields(
    uint CduGlobal,
    uint CduSize,
    int Pages,
    int FixPage,
    int Entries,
    int EntryCount,
    int FixPointer,
    int Rings,
    int RingCount,
    int Radials,
    int RadialCount,
    double InvalidAtOrBelow,
    int ObjectType,
    int FixType,
    int AirportType,
    int NavaidType,
    int ObjectPosition,
    int Abeam);

/// <summary>
/// Where one installed 737NG3.wasm keeps what the Captain's navigation display shows, all from that file's
/// layout file, never hardcoded. The anchor locates the module's memory even
/// when the FMA's and the engine displays' cannot. Equality compares the anchor and every part by content and
/// ignores <see cref="SourceFile"/>, so the same build installed twice gives one layout. The APP/VOR part
/// (<see cref="Approach"/>) is optional: null when the layout file has none, with <see cref="ApproachFailure"/>
/// the reason, and the rest of the layout works without it; so are the bearing pointers
/// (<see cref="Pointers"/>, <see cref="PointersFailure"/>) and each of the <see cref="Details"/> (null: none derived).
/// </summary>
public sealed record Pmdg737NavDisplayLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    NdObjects Objects,
    NdPanelFields Panel,
    NdUnitFields Units,
    NdOrientationFields Orientation,
    NdRouteFields Route,
    NdMapValidity MapValidity,
    NdGeometry Geometry,
    NdCornerFields Corners,
    NdMessageFields Messages,
    NdXtkFields Xtk,
    NdListFields Lists,
    NdTunedFields Tuned,
    NdReceiverFields Receivers,
    NdTrafficFields Traffic,
    NdSymbolFields Symbols,
    NdFixPageFields FixPage,
    NdApproachFields? Approach = null,
    string? ApproachFailure = null,
    NdPointerFields? Pointers = null,
    string? PointersFailure = null,
    NdDetailFields? Details = null)
{
    public bool Equals(Pmdg737NavDisplayLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && Objects == other.Objects && Panel.Equals(other.Panel) && Units.Equals(other.Units) && Orientation == other.Orientation
        && Route.Equals(other.Route) && MapValidity == other.MapValidity && Geometry.Equals(other.Geometry) && Corners == other.Corners
        && Messages.Equals(other.Messages) && Xtk == other.Xtk
        && Lists == other.Lists && Tuned == other.Tuned && Receivers == other.Receivers && Traffic == other.Traffic
        && Symbols == other.Symbols && FixPage == other.FixPage
        && Approach == other.Approach && ApproachFailure == other.ApproachFailure
        && Pointers == other.Pointers && PointersFailure == other.PointersFailure && Equals(Details, other.Details);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(Objects);
        h.Add(Panel);
        h.Add(Route);
        h.Add(Traffic);
        return h.ToHashCode();
    }
}

/// <summary>The navigation display's result in a layout file: a layout, or the detailed reason there is none.</summary>
public sealed record NavDisplayDerivation(Pmdg737NavDisplayLayout? Layout, string? Failure);

/// <summary>
/// What makes the map invalid besides the readout: PMDG's map-invalid flag is set while the FMC's position is not
/// valid or the readout's valid flag is clear, the FMC being the EFIS object's entry <see cref="FmcIndex"/> for the
/// DEU of the unit drawing (<see cref="NdUnitFields.Deu"/>); the FMC's position is valid while the FMS position (the
/// route's <see cref="NdRouteFields.FmsPosition"/>) is valid and the FMS object's byte <see cref="FmcValid"/> + the
/// EFIS object's entry <see cref="FmcIndex"/> for that FMC is set, the entry at <see cref="MasterFmc"/> (the master
/// FMC's) when the FMC is <see cref="NoFmc"/>. While the flag is set PMDG draws the MAP flag and no route (but in PLN), TO waypoint,
/// map-option label, nearby list, tuned station or CDU fix.
/// </summary>
public sealed record NdMapValidity(GlassField FmcIndex, int MasterFmc, int NoFmc, int FmcValid);

/// <summary>What a message condition reads.</summary>
public enum NdSourceKind { Object, Panel, FullFormat, Centred, Mode, Valid, MapValid, ByFmc }

/// <summary>
/// A value a message condition reads: a field of one of the ND's objects (<see cref="Owner"/>,
/// <see cref="Field"/>), the element of such a field for the FMC the ND shows (<see cref="NdSourceKind.ByFmc"/>:
/// <see cref="Field"/>'s offset + its stride × the FMC, as the ND page copies it), a field of the Captain's EFIS panel
/// (<see cref="Field"/>, relative to the panel), or a fact about the ND being drawn: the full format, centred, the
/// mode being <see cref="Mode"/>, the side valid (the readout's valid flag), the map valid (the readout's flag and
/// an FMS position: <see cref="NdMapValidity"/>).
/// </summary>
public readonly record struct NdSource(NdSourceKind Kind, GlassObject Owner, GlassField Field, int Mode);

/// <summary>One test of a message: the source's value compared (a wasm compare opcode, 0 for "not zero")
/// with <see cref="Constant"/>; <see cref="Negated"/> when the message needs the test false.</summary>
public readonly record struct NdCondition(NdSource Source, byte CompareOpcode, long? Constant, bool Negated);

/// <summary>A text the ND prints, shown while all the conditions of any one alternative hold (PMDG draws most
/// messages in several places, one per layout). Equality compares the alternatives by content.</summary>
public sealed record NdMessage(string Text, IReadOnlyList<IReadOnlyList<NdCondition>> AnyOf)
{
    public bool Equals(NdMessage? other) =>
        other is not null && Text == other.Text && AnyOf.Count == other.AnyOf.Count
        && AnyOf.Zip(other.AnyOf).All(p => p.First.SequenceEqual(p.Second));

    public override int GetHashCode() => HashCode.Combine(Text, AnyOf.Count);
}

/// <summary>A message the reader leaves out, and why (a gate it cannot read).</summary>
public readonly record struct NdDroppedMessage(string Text, string Reason);

/// <summary>The bottom's RNP and ANP (while the options' <see cref="Option"/> is set): the CDU
/// position data (FMS + <see cref="PosData"/>) doubles at <see cref="Rnp"/> and <see cref="Anp"/>, printed
/// <see cref="Format"/> above <see cref="InvalidAtOrBelow"/>, else <see cref="Dashes"/>.</summary>
public sealed record NdRnpFields(GlassField Option, int PosData, int Rnp, int Anp, double InvalidAtOrBelow, string Format, string Dashes,
    string RnpLabel, string AnpLabel);

/// <summary>The right side's VNAV deviation scale: the number printed from <see cref="NumberFrom"/> feet, the
/// pointer pinned beyond <see cref="ScaleLimit"/> feet, and drawn down the screen for a positive deviation
/// while <see cref="PointerDownWhenPositive"/> (up for a negative one); drawn only while every condition of
/// <see cref="DrawnWhile"/> holds (the deviation's own validity is the reader's test). <see cref="Flag"/> is the
/// text PMDG's failure flags draw in the scale's place while one of those gates is set (VTK; none when it draws
/// none), amber, with a rectangle around it while <see cref="FlagBoxed"/>; it is not among the messages.
/// Equality compares the conditions by content.</summary>
public sealed record NdVdevFields(double NumberFrom, double ScaleLimit, bool PointerDownWhenPositive, IReadOnlyList<NdCondition> DrawnWhile, NdMessage? Flag,
    bool FlagBoxed, NdVerticalRnpFields VerticalRnp)
{
    public bool Equals(NdVdevFields? other) =>
        other is not null && NumberFrom == other.NumberFrom && ScaleLimit == other.ScaleLimit
        && PointerDownWhenPositive == other.PointerDownWhenPositive && DrawnWhile.SequenceEqual(other.DrawnWhile)
        && Equals(Flag, other.Flag) && FlagBoxed == other.FlagBoxed && VerticalRnp == other.VerticalRnp;

    public override int GetHashCode() => HashCode.Combine(NumberFrom, ScaleLimit, PointerDownWhenPositive, DrawnWhile.Count, Flag?.Text, FlagBoxed);
}

/// <summary>
/// The vertical RNP and ANP PMDG prints in the VDEV scale while the bottom RNP's
/// option is set: <see cref="RnpLabel"/> over the CDU position data's int <see cref="Rnp"/> and
/// <see cref="AnpLabel"/> over its int <see cref="Anp"/> (the bottom RNP's position data), each <see cref="Format"/>
/// above <see cref="InvalidAtOrBelow"/>, else <see cref="RnpDashes"/> and <see cref="AnpDashes"/>. PMDG picks each
/// value's colour by a glass data byte by side, not read.
/// </summary>
public sealed record NdVerticalRnpFields(int Rnp, int Anp, double InvalidAtOrBelow, string Format, string RnpDashes, string AnpDashes, string RnpLabel,
    string AnpLabel);

/// <summary>
/// The radar lines PMDG draws under the WXR label: the tilt, the weather radar's int
/// <see cref="Tilt"/> for the panel drawn, printed <see cref="TiltFormat"/> ("%+d%c"), or <see cref="ZeroFormat"/>
/// ("0%c") at 0, with <see cref="AutoLetter"/> while the radar's byte <see cref="Auto"/> is set, else
/// <see cref="ManualLetter"/>; then <see cref="VarFull"/> ("VAR"; <see cref="VarHalf"/> in the half format) while the
/// radar's byte <see cref="Variable"/> for the panel drawn is set (<see cref="VarWhileSet"/>) or clear. PMDG enters the
/// block under its own per-unit copy of the WXR switch and the radar's two failure tests, which the reader
/// does not read: it lists the lines with the <see cref="Label"/> it adds for the panel's WXR switch.
/// </summary>
public sealed record NdWxrLineFields(string Label, GlassField Tilt, string TiltFormat, string ZeroFormat, GlassField Auto, char AutoLetter, char ManualLetter,
    GlassField Variable, bool VarWhileSet, string VarFull, string VarHalf);

/// <summary>
/// The terrain numbers PMDG draws, listed with the <see cref="Label"/> label (the panel's TERR switch stands for the
/// ND's per-unit TERR copy, as the label does): while the options' <see cref="Option"/> is set and the GPWS object's
/// <see cref="GpwsTest"/> clear, the ND's terrain record for the drawing unit's slot (by the unit's
/// index: <see cref="SlotByUnitIndex"/>; ints at <see cref="Highest"/>, <see cref="HighestColour"/>,
/// <see cref="Lowest"/>, <see cref="LowestColour"/>, each + slot × their stride, on the ND's symbol generator object
/// <see cref="Generator"/>, as nothing else keeps them) prints the highest
/// above the lowest: <see cref="Format"/> of floor(feet × <see cref="Scale"/> + 0.5) while above
/// <see cref="InvalidAtOrBelow"/>, the lowest only while the highest's colour code is not 0; a colour code of
/// <see cref="RedCode"/> picks the red brush, <see cref="AmberCode"/> the amber, any other the green. PMDG's terrain block
/// returns before the radar lines: once it is entered, no radar line is drawn, numbers or not. Equality compares the slots by content.
/// </summary>
public sealed record NdTerrainFields(string Label, GlassField Option, GlassField GpwsTest, GlassObject Generator, IReadOnlyList<int> SlotByUnitIndex,
    GlassField Highest, GlassField HighestColour, GlassField Lowest, GlassField LowestColour, double InvalidAtOrBelow, double Scale, string Format,
    int RedCode, int AmberCode)
{
    public bool Equals(NdTerrainFields? other) =>
        other is not null && Label == other.Label && Option == other.Option && GpwsTest == other.GpwsTest && Generator == other.Generator
        && SlotByUnitIndex.SequenceEqual(other.SlotByUnitIndex) && Highest == other.Highest && HighestColour == other.HighestColour
        && Lowest == other.Lowest && LowestColour == other.LowestColour && InvalidAtOrBelow == other.InvalidAtOrBelow && Scale == other.Scale
        && Format == other.Format && RedCode == other.RedCode && AmberCode == other.AmberCode;

    public override int GetHashCode() => HashCode.Combine(Label, Option, GpwsTest, Generator, SlotByUnitIndex.Count, Highest, Lowest, Format);
}

/// <summary>
/// The ND's messages (warnings, TCAS, failure flags) and the left side's map-option labels, each with the
/// conditions PMDG draws it under; the texts of the messages the reader computes itself (EXCESS DATA from
/// the lists, TRAFFIC and OFFSCALE from the traffic); RNP/ANP; the VDEV scale; what was left out; the label
/// PMDG draws at the bottom left for the FMC the ND shows (<see cref="FmcLabel"/>, by the FMC); the radar's
/// lines under the WXR label (<see cref="Wxr"/>); the terrain numbers under the TERR label (<see cref="Terrain"/>).
/// Equality compares the lists by content.
/// </summary>
public sealed record NdMessageFields(
    IReadOnlyList<NdMessage> Messages,
    IReadOnlyList<NdMessage> MapOptionLabels,
    string ExcessData,
    string Traffic,
    string Offscale,
    NdRnpFields Rnp,
    NdVdevFields Vdev,
    IReadOnlyList<NdDroppedMessage> Dropped,
    NdLabelPair FmcLabel,
    NdWxrLineFields Wxr,
    NdTerrainFields Terrain)
{
    public bool Equals(NdMessageFields? other) =>
        other is not null && Messages.SequenceEqual(other.Messages) && MapOptionLabels.SequenceEqual(other.MapOptionLabels)
        && ExcessData == other.ExcessData && Traffic == other.Traffic && Offscale == other.Offscale && Rnp == other.Rnp
        && Vdev == other.Vdev && Dropped.SequenceEqual(other.Dropped) && FmcLabel == other.FmcLabel && Wxr == other.Wxr
        && Terrain == other.Terrain;

    public override int GetHashCode() => HashCode.Combine(Messages.Count, MapOptionLabels.Count, ExcessData, Rnp, Vdev);
}

/// <summary>
/// The cross-track error PMDG prints under the aircraft in MAP (with the RNP option, while the reference
/// route is active, its magnitude below <see cref="ShownBelow"/>): the FMS's cross-track error of the TO waypoint's
/// plan (the route's ToNodePlan while active, else the reference route), its lateral path's double
/// <see cref="Value"/>; none while the FMS position is not valid, the path's int <see cref="PathInvalid"/> is
/// negative, the plan's key node <see cref="KeySlot"/> is null, or that node has its byte <see cref="NodeFlag"/>
/// set and its int <see cref="NodeCount"/> below <see cref="NodeCountBelow"/>; for a conditional node PMDG
/// computes it, which the reader does not. Printed as <see cref="CloseFormat"/> at range index
/// <see cref="CloseAtMost"/> and below, else <see cref="FarFormat"/>, of the magnitude and <see cref="Right"/>
/// when positive, else <see cref="Left"/>; <see cref="CloseZero"/> or <see cref="FarZero"/> below
/// <see cref="CloseZeroBelow"/> or <see cref="FarZeroBelow"/>.
/// </summary>
public sealed record NdXtkFields(int KeySlot, int PathInvalid, int Value, int NodeFlag, int NodeCount, int NodeCountBelow, double ShownBelow,
    int CloseAtMost, string CloseFormat, string FarFormat, string CloseZero, string FarZero, double CloseZeroBelow, double FarZeroBelow, char Right,
    char Left);

/// <summary>A PMDG clock: the valid byte, hours, minutes and seconds (ints), as PMDG prints them
/// ("%02u%02u" and the minute's tenths from the seconds, never above <see cref="TenthsAtMost"/>: PMDG
/// prints that while the tenths round above it, at 57-59 s).</summary>
public sealed record NdClockFields(int Valid, int Hours, int Minutes, int Seconds, int TenthsAtMost);

/// <summary>A PMDG position: latitude and longitude in radians, the magnetic variation in degrees, the valid byte.</summary>
public sealed record NdPositionFields(int LatitudeRad, int LongitudeRad, int MagneticVariationDeg, int Valid);

/// <summary>
/// Which route waypoints PMDG draws on the map: not <see cref="Hidden"/>, not the no-data leg type, not <see cref="Conditional"/> unless
/// <see cref="ConditionalShown"/>, the position valid; none whose <see cref="Kind"/> is <see cref="UndrawnKind"/>;
/// the walk starts at the first node with neither <see cref="StartSkip"/> nor <see cref="Conditional"/> set that is
/// valid; and of the nodes after it only the TO node is drawn while its byte <see cref="UndrawnUnlessTo"/> is set. A
/// node with its byte <see cref="Hold"/> set and <see cref="HoldSkip"/> clear (a hold PMDG made: a copy of its fix,
/// inserted after it) is drawn as a holding pattern, never as a waypoint.
/// </summary>
public sealed record NdNodeValidity(int Hidden, int Conditional, int ConditionalShown, int Kind, int UndrawnKind, int StartSkip, int UndrawnUnlessTo,
    int Hold = 0, int HoldSkip = 0);

/// <summary>
/// The PLN centre: the route the side's CDU edits, i[FMS + <see cref="EditedRoute"/> + 4 ×
/// side], or its MOD (+ the route fields' ModOffset) when that is active; that plan's route info (plan +
/// <see cref="RouteInfo"/>) holds the stepped-to node at <see cref="CentreSlot"/> + <see cref="CentreSlotStride"/> ×
/// side; the aircraft when there is none or its position is not valid.
/// </summary>
public sealed record NdPlnCentreFields(int EditedRoute, int EditedRouteStride, int RouteInfo, int CentreSlot, int CentreSlotStride);

/// <summary>
/// The route's two airports PMDG draws with the reference route and its MOD: in the plan's route info (plan +
/// the PLN centre's RouteInfo) the departure airport and runway pointers (<see cref="Departure"/>,
/// <see cref="DepartureRunway"/>; PMDG's own PROC_DIAG print names the airport "dep") and the arrival's
/// (<see cref="Arrival"/>, <see cref="ArrivalRunway"/>, "arr"); nothing while the airport is null. The symbol
/// sits at the runway's position when a runway is set, else at the airport's. PMDG draws a circle and the
/// airport's name unless a runway is set and the range index is at or below <see cref="RunwayUpToRange"/>
/// (then the runway's outline alone), and the runway's name whenever one is set. Names are inline. The outline runs
/// from the runway's position along its heading (the int at <see cref="RunwayHeading"/>, turned as a course) for its
/// length (the int at <see cref="RunwayLength"/>, feet: <see cref="FeetPerNm"/> to the NM), with a dashed centreline
/// <see cref="CentrelineNm"/> NM beyond each end.
/// </summary>
public sealed record NdRouteAirportFields(int Departure, int DepartureRunway, int Arrival, int ArrivalRunway, int AirportName,
    int AirportPosition, int RunwayName, int RunwayPosition, int RunwayUpToRange, int RunwayHeading, int RunwayLength, double FeetPerNm,
    double CentrelineNm);

/// <summary>
/// The VNAV profile points PMDG marks on the route: positions at lateral path (plan + <see cref="LateralPath"/>)
/// + <see cref="Array"/> + k × <see cref="Stride"/> for k from <see cref="FirstIndex"/> to <see cref="LastIndex"/>,
/// drawn when valid with the label PMDG gives k (T/C, T/D, DECEL …; a k drawing a label PMDG
/// fills at run time is absent). Equality compares the labels by content.
/// </summary>
public sealed record NdMarkerFields(int LateralPath, int Array, int Stride, int FirstIndex, int LastIndex, IReadOnlyDictionary<int, string> Labels)
{
    public bool Equals(NdMarkerFields? other) =>
        other is not null && LateralPath == other.LateralPath && Array == other.Array && Stride == other.Stride
        && FirstIndex == other.FirstIndex && LastIndex == other.LastIndex && Labels.Count == other.Labels.Count
        && Labels.All(kv => other.Labels.TryGetValue(kv.Key, out var label) && label == kv.Value);

    public override int GetHashCode() => HashCode.Combine(LateralPath, Array, Stride, FirstIndex, LastIndex, Labels.Count);
}

/// <summary>
/// The route line PMDG draws for a plan, from its lateral path (plan + the markers'
/// <see cref="NdMarkerFields.LateralPath"/>): nothing while the path's byte <see cref="Invalid"/> is set
/// (the path not valid); else its points 0 to the count (<see cref="Count"/>) at <see cref="Points"/> + k ×
/// <see cref="PointStride"/>, the draw leaving at the first whose <see cref="InUse"/> byte is clear. A point draws a
/// turn arc (<see cref="Arc"/>; none while the arc's <see cref="ArcZeroSpan"/> byte is set) and two straight lines
/// (<see cref="FirstLine"/>, <see cref="SecondLine"/>), each while its length (<see cref="LineLength"/>) is above
/// <see cref="LineMinLength"/>, from the position at <see cref="LineStart"/> to the one at <see cref="LineEnd"/>.
/// <see cref="Toward"/> holds the node the point's line heads for: PMDG stores the leg's start node's next
/// node there. A discontinuity is no piece at all: PMDG starts the next point elsewhere and draws nothing between.
/// </summary>
public sealed record NdLateralPathFields(int Invalid, int Count, int Points, int PointStride, int InUse, int Arc, int ArcZeroSpan,
    int FirstLine, int SecondLine, int LineStart, int LineEnd, int LineLength, double LineMinLength, int Toward);

/// <summary>
/// The DATA text under a map waypoint, as PMDG formats it: the constraint
/// type byte (<see cref="WindowType"/>: two lines, the upper altitude then the lower; <see cref="AtType"/>,
/// <see cref="AboveType"/>, <see cref="BelowType"/>: the at altitude), each altitude printed only above
/// <see cref="InvalidAtOrBelow"/>, as <see cref="FlightLevelFormat"/> (÷ 100) at or above the transition
/// altitude, else <see cref="AltitudeFormat"/>, with the suffix A, B or a space; the transition altitude is
/// the FMS performance data's (FMS + <see cref="Performance"/>) field for the node's flight phase
/// (<see cref="TransitionAltitude"/> below phase <see cref="ClimbPhasesBelow"/>, else
/// <see cref="TransitionLevel"/>); a third line <see cref="ThirdLineFormat"/> of <see cref="ThirdLine"/> when
/// not 0; then the ETA ("HHMM" + "Z") unless <see cref="EtaHidden"/>. PMDG prints none of it for
/// the plans set as bits of <see cref="NoDataPlanBits"/> (the MODs, 2 and 3), see <see cref="DrawsDataFor"/>.
/// It prints all of it with DATA on; with DATA off only the constraint lines and the third line, of one node:
/// the plan's key node at <see cref="ActiveSlot"/>, while the options' <see cref="ActiveOption"/> (PMDG's
/// ND_ActWptData) is set.
/// </summary>
public sealed record NdDataTextFields(
    int ConstraintType,
    char WindowType,
    char AtType,
    char AboveType,
    char BelowType,
    int AtAltitude,
    int UpperAltitude,
    int LowerAltitude,
    int InvalidAtOrBelow,
    int Phase,
    int ClimbPhasesBelow,
    int Performance,
    int TransitionAltitude,
    int TransitionLevel,
    string FlightLevelFormat,
    string AltitudeFormat,
    int ThirdLine,
    string ThirdLineFormat,
    int EtaHidden,
    int NoDataPlanBits,
    GlassField ActiveOption,
    int ActiveSlot)
{
    /// <summary>Whether PMDG prints DATA text for a waypoint of this plan.</summary>
    public bool DrawsDataFor(int plan) => plan is < 0 or > 30 || ((NoDataPlanBits >> plan) & 1) == 0;
}

/// <summary>
/// The route, on the FMS object: the reference route (an index of the flight plans at <see cref="PlanBase"/> +
/// plan × <see cref="PlanStride"/>, each active while its byte <see cref="PlanActive"/> is set); the plans
/// PMDG draws besides the reference (its MOD, + <see cref="ModOffset"/>, and
/// <see cref="AlsoDrawnPlans"/>, each when active); the top-right corner's TO waypoint comes from
/// <see cref="ToNodePlan"/> while that plan is active, else from the reference route: the key nodes (plan +
/// <see cref="KeyNodes"/>) slot <see cref="ToNodeSlot"/>, skipping nodes whose name starts with
/// <see cref="PseudoPrefix"/>; the map walks from slot <see cref="FirstNodeSlot"/> through
/// <see cref="NodeNext"/>. A node (<see cref="NodeSize"/> bytes read) holds its name (C string, at most
/// <see cref="NodeNameLength"/> bytes), the name the map prints, its leg type (<see cref="NoDataLegType"/>:
/// no ETA or distance), its distance, its ETA clock, its position. The FMS position; the VNAV deviation,
/// invalid at or below <see cref="VnavInvalidAtOrBelow"/> and blanked by PMDG while the VNAV phase is
/// below <see cref="VnavPhaseBelow"/> or the reference route's perf info (plan + <see cref="PerfInfo"/>) has
/// its VNAV path byte <see cref="PerfPath"/> clear (set: a path exists; PMDG draws the markers
/// only then). The route's airports (<see cref="Airports"/>), its line (<see cref="LateralPath"/>).
/// <see cref="MaxNodes"/> caps every walk. Equality compares the lists by content.
/// </summary>
public sealed record NdRouteFields(
    int RefRoute,
    int PlanBase,
    int PlanStride,
    int PlanActive,
    int ModOffset,
    IReadOnlyList<int> AlsoDrawnPlans,
    int ToNodePlan,
    int KeyNodes,
    int FirstNodeSlot,
    int ToNodeSlot,
    byte PseudoPrefix,
    int NodeSize,
    int NodeName,
    int NodeNameLength,
    int NodeMapName,
    int NodeNext,
    int NodeType,
    int NoDataLegType,
    int NodeDistance,
    int NodeEta,
    NdClockFields Clock,
    int NodePosition,
    NdPositionFields Position,
    NdNodeValidity Validity,
    int FmsPosition,
    int VnavDeviation,
    double VnavInvalidAtOrBelow,
    int VnavPhase,
    int VnavPhaseBelow,
    int PerfInfo,
    int PerfPath,
    NdPlnCentreFields PlnCentre,
    NdMarkerFields Markers,
    NdDataTextFields DataText,
    NdRouteAirportFields Airports,
    NdLateralPathFields LateralPath,
    int MaxNodes)
{
    public bool Equals(NdRouteFields? other) =>
        other is not null
        && RefRoute == other.RefRoute && PlanBase == other.PlanBase && PlanStride == other.PlanStride && PlanActive == other.PlanActive
        && ModOffset == other.ModOffset && AlsoDrawnPlans.SequenceEqual(other.AlsoDrawnPlans) && ToNodePlan == other.ToNodePlan
        && KeyNodes == other.KeyNodes && FirstNodeSlot == other.FirstNodeSlot && ToNodeSlot == other.ToNodeSlot
        && PseudoPrefix == other.PseudoPrefix && NodeSize == other.NodeSize && NodeName == other.NodeName
        && NodeNameLength == other.NodeNameLength && NodeMapName == other.NodeMapName && NodeNext == other.NodeNext
        && NodeType == other.NodeType && NoDataLegType == other.NoDataLegType && NodeDistance == other.NodeDistance
        && NodeEta == other.NodeEta && Clock == other.Clock && NodePosition == other.NodePosition && Position == other.Position
        && Validity == other.Validity && FmsPosition == other.FmsPosition && VnavDeviation == other.VnavDeviation
        && VnavInvalidAtOrBelow == other.VnavInvalidAtOrBelow && VnavPhase == other.VnavPhase && VnavPhaseBelow == other.VnavPhaseBelow
        && PerfInfo == other.PerfInfo && PerfPath == other.PerfPath && PlnCentre == other.PlnCentre
        && Markers.Equals(other.Markers) && DataText == other.DataText && Airports == other.Airports && LateralPath == other.LateralPath
        && MaxNodes == other.MaxNodes;

    public override int GetHashCode() => HashCode.Combine(RefRoute, PlanBase, PlanStride, KeyNodes, NodeNext, NodeDistance, NodeEta, Markers);
}

/// <summary>
/// Which way the map points, as PMDG decides it and prints its readout: the heading and the
/// track of the side drawn (glass data, per side); the magnetic-reference flag (EFIS object: clear means true
/// north, and PMDG then adds the magnetic variation, on the simulator interface, to both, the track only above
/// <see cref="TrackInvalidAtOrBelow"/>); the IRU the side uses (an index on the EFIS object into the IRS object's
/// IRU pointers, each IRU <see cref="IruSize"/> bytes, its condition field). The map is track-up only while
/// the options' <see cref="TrackUpOption"/> is set, the mode is <see cref="TrackUpMode"/> (MAP) and the
/// condition is below <see cref="DegradedIruCondition"/>; else heading-up (APP and VOR always). The readout's labels as PMDG
/// prints them, its number shown only while <see cref="ReadoutValid"/> is set ("---" otherwise) and
/// nothing at all while <see cref="ReadoutFailed"/> is (both glass data, per side). In PLN, PMDG draws its range
/// circles in the readout's place: <see cref="PlanCompass"/> holds the compass letters it
/// draws around the centre, the top one with an arrow's lines beside it.
/// </summary>
public sealed record NdOrientationFields(
    GlassField Heading,
    GlassField Track,
    double TrackInvalidAtOrBelow,
    GlassField Magnetic,
    GlassField MagneticVariation,
    GlassField IruIndex,
    GlassField IruPointers,
    uint IruSize,
    GlassField IruCondition,
    int DegradedIruCondition,
    GlassField TrackUpOption,
    int TrackUpMode,
    string TrackLabel,
    string HeadingLabel,
    GlassField ReadoutValid,
    GlassField ReadoutFailed,
    string TrueLabel,
    string MagneticLabel,
    string MapMagneticLabel,
    NdPlanCompass PlanCompass);

/// <summary>
/// The compass letters PMDG draws in PLN, by where each sits from the map's centre: above it
/// (the top centre, with the arrow's lines), right of it and left of it at its height (the screen's edges),
/// and below it (the bottom centre); and its two range rings' labels, above and below the centre on each ring:
/// the panel's range label table at the range + each of <see cref="RingLabelShifts"/>, the inner ring's first.
/// Equality compares the shifts by content.
/// </summary>
public sealed record NdPlanCompass(string North, string East, string South, string West, IReadOnlyList<int> RingLabelShifts)
{
    public bool Equals(NdPlanCompass? other) =>
        other is not null && North == other.North && East == other.East && South == other.South && West == other.West
        && RingLabelShifts.SequenceEqual(other.RingLabelShifts);

    public override int GetHashCode() => HashCode.Combine(North, East, South, West, RingLabelShifts.Count);
}

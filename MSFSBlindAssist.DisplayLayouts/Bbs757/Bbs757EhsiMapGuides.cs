namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The EHSI map's FIX page guides, off-path descent circles, altitude range arc and VNAV path deviation, an optional
/// part of <see cref="Bbs757EhsiLayout"/> (plan 28), all from the layout file. The FMS is at <see cref="NdToFms"/> in
/// the nd; each FIX has an on byte, a leg-shaped fix object (its kind at the layout's <c>LegKind</c>, its name the
/// narrow string at <see cref="LegNavaidName"/> for a kind in <see cref="NavaidLegKinds"/>, else at
/// <see cref="LegName"/>) and a vector of <see cref="FixEntryStride"/>-byte entries (radial and distance,
/// <see cref="FixEntryNone"/> for none; the draw takes <see cref="FixEntries"/>). The off-path fields are offsets in the
/// FMS (the point's latitude and longitude as the draw takes them, the two radii in NM); the arc's in the MCP (the
/// layout's <c>NdToMcp</c>), the adc (<see cref="SourceToAdc"/> in the instrument source), the air/ground system
/// (<see cref="NdToAirGround"/> in the nd) and the selected IRS; the deviation's in the path object. The pixel sizes,
/// thresholds, phase value and clamp are the draw's constants. Equality compares every field by content.
/// </summary>
public sealed record Bbs757EhsiMapGuides(
    int NdToFms,
    int FmsFix1On,
    int FmsFix2On,
    int FmsFix1,
    int FmsFix2,
    int FmsFix1Entries,
    int FmsFix2Entries,
    int FixEntryStride,
    int FixEntryRadial,
    int FixEntryDistance,
    int FixEntryNone,
    int FixEntries,
    int LegName,
    int LegNavaidName,
    IReadOnlyList<int> NavaidLegKinds,
    double RadialStart,
    int RadialDashes,
    double RadialDashStep,
    double RadialDashLength,
    int OffpathOn,
    int OffpathValidA,
    int OffpathValidB,
    int OffpathLatitude,
    int OffpathLongitude,
    int OffpathCleanRadius,
    int OffpathSpeedbrakeRadius,
    int McpAltitude,
    int SourceToAdc,
    int AdcIndicatedAltitude,
    int AdcVerticalSpeed,
    int NdToAirGround,
    int AirGroundOnGround,
    int IrsTrueTrack,
    double ArcMinAltitudeDifference,
    double ArcMinVerticalSpeed,
    int PathPhase,
    int DescentPhase,
    int PathDeviation,
    double DeviationClamp)
{
    public bool Equals(Bbs757EhsiMapGuides? other) =>
        other is not null
        && NdToFms == other.NdToFms
        && FmsFix1On == other.FmsFix1On
        && FmsFix2On == other.FmsFix2On
        && FmsFix1 == other.FmsFix1
        && FmsFix2 == other.FmsFix2
        && FmsFix1Entries == other.FmsFix1Entries
        && FmsFix2Entries == other.FmsFix2Entries
        && FixEntryStride == other.FixEntryStride
        && FixEntryRadial == other.FixEntryRadial
        && FixEntryDistance == other.FixEntryDistance
        && FixEntryNone == other.FixEntryNone
        && FixEntries == other.FixEntries
        && LegName == other.LegName
        && LegNavaidName == other.LegNavaidName
        && NavaidLegKinds.SequenceEqual(other.NavaidLegKinds)
        && RadialStart == other.RadialStart
        && RadialDashes == other.RadialDashes
        && RadialDashStep == other.RadialDashStep
        && RadialDashLength == other.RadialDashLength
        && OffpathOn == other.OffpathOn
        && OffpathValidA == other.OffpathValidA
        && OffpathValidB == other.OffpathValidB
        && OffpathLatitude == other.OffpathLatitude
        && OffpathLongitude == other.OffpathLongitude
        && OffpathCleanRadius == other.OffpathCleanRadius
        && OffpathSpeedbrakeRadius == other.OffpathSpeedbrakeRadius
        && McpAltitude == other.McpAltitude
        && SourceToAdc == other.SourceToAdc
        && AdcIndicatedAltitude == other.AdcIndicatedAltitude
        && AdcVerticalSpeed == other.AdcVerticalSpeed
        && NdToAirGround == other.NdToAirGround
        && AirGroundOnGround == other.AirGroundOnGround
        && IrsTrueTrack == other.IrsTrueTrack
        && ArcMinAltitudeDifference == other.ArcMinAltitudeDifference
        && ArcMinVerticalSpeed == other.ArcMinVerticalSpeed
        && PathPhase == other.PathPhase
        && DescentPhase == other.DescentPhase
        && PathDeviation == other.PathDeviation
        && DeviationClamp == other.DeviationClamp;

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(NdToFms);
        h.Add(FmsFix1On);
        h.Add(FmsFix2On);
        h.Add(FmsFix1);
        h.Add(FmsFix2);
        h.Add(FmsFix1Entries);
        h.Add(FmsFix2Entries);
        h.Add(FixEntryStride);
        h.Add(FixEntryRadial);
        h.Add(FixEntryDistance);
        h.Add(FixEntryNone);
        h.Add(FixEntries);
        h.Add(LegName);
        h.Add(LegNavaidName);
        foreach (int k in NavaidLegKinds) h.Add(k);
        h.Add(RadialStart);
        h.Add(RadialDashes);
        h.Add(RadialDashStep);
        h.Add(RadialDashLength);
        h.Add(OffpathOn);
        h.Add(OffpathValidA);
        h.Add(OffpathValidB);
        h.Add(OffpathLatitude);
        h.Add(OffpathLongitude);
        h.Add(OffpathCleanRadius);
        h.Add(OffpathSpeedbrakeRadius);
        h.Add(McpAltitude);
        h.Add(SourceToAdc);
        h.Add(AdcIndicatedAltitude);
        h.Add(AdcVerticalSpeed);
        h.Add(NdToAirGround);
        h.Add(AirGroundOnGround);
        h.Add(IrsTrueTrack);
        h.Add(ArcMinAltitudeDifference);
        h.Add(ArcMinVerticalSpeed);
        h.Add(PathPhase);
        h.Add(DescentPhase);
        h.Add(PathDeviation);
        h.Add(DeviationClamp);
        return h.ToHashCode();
    }
}

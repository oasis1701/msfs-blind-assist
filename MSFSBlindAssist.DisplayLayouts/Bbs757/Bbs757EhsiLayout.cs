using System.Text.Json;
using System.Text.Json.Serialization;
using MSFSBlindAssist.DisplayLayouts;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>One entry of <see cref="Bbs757EhsiLayout.LegPositions"/>, which holds one per leg kind whose position is not
/// at <see cref="Bbs757EhsiLayout.LegLatitude"/> and <see cref="Bbs757EhsiLayout.LegLongitude"/>: the kind value it
/// applies to and the offsets of that kind's latitude and longitude in the leg.</summary>
public readonly record struct Bbs757EhsiLegPosition(int Kind, int Latitude, int Longitude);

/// <summary>
/// Where one installed BBS752.wasm keeps the Captain's EHSI (the navigation display <c>bbs::nd</c>: its mode, range,
/// heading and track, the route it draws and the top line), all from that file's layout file, never hardcoded
/// (BBS757-D2). The object is found through the gauge manager as the FMA's PFD is: <see cref="ManagerBase"/> is the
/// manager's static address and the Captain's nd is the first pointer in its <see cref="ManagerSpan"/> bytes whose target
/// starts with <see cref="NdVptr"/>. The <c>NdTo...</c> fields are offsets in the nd of pointers to the panels it draws
/// from and <see cref="SourceToIrs"/> is the pointer from the instrument source panel to the selected IRS; the route's
/// vectors hold <see cref="LegStride"/>-byte legs; <see cref="PathRegistry"/>, <see cref="Clock"/> are static addresses
/// and the other offsets are into the object named in their group (the nd, the EFIS panel, the MCP, the IRS, a leg, the
/// path object or an airport record). <see cref="RadiusFactor"/> to <see cref="MinEtaGroundSpeed"/> are the draw's own
/// constants, as values. Plan 28's parts (<see cref="Approach"/> to <see cref="MapGuides"/>) are optional, each null
/// with its reason. Equality compares every field by content (the lists, tables and parts too) except
/// <see cref="SourceFile"/>, so the same build installed twice gives one layout.
/// </summary>
public sealed record Bbs757EhsiLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    uint ManagerBase,
    uint ManagerSpan,
    uint NdVptr,
    int Powered,
    int Failed,
    int Blanked,
    int NdToEfis,
    int NdToSource,
    int NdToMcp,
    int NdToRoute,
    int SourceToIrs,
    int EfisMode,
    int EfisCentered,
    Bbs757WordTable Modes,
    int Range,
    int RangeSelected,
    int RangeChanging,
    int Valid,
    int Aligning,
    int Number,
    int TrackUp,
    int TrueReference,
    int Width,
    int Height,
    int Longitude,
    int Latitude,
    int CenterLatitude,
    int CenterLongitude,
    int Rotation,
    int ScreenX,
    int ScreenY,
    int DataSwitch,
    int ExcessCount,
    int ExcessLimit,
    int OriginAirport,
    int DestinationAirport,
    int AirportLongitude,
    int AirportLatitude,
    int McpHeading,
    int IrsGroundSpeed,
    uint Clock,
    double RadiusFactor,
    double AircraftYFactor,
    double ClipInset,
    double MarkerMargin,
    double MinEtaGroundSpeed,
    int RouteLegs,
    int RouteFlownLegs,
    int LegStride,
    int MaxLegs,
    int RouteActive,
    int RouteModified,
    int RouteModification,
    int RouteId,
    int LegLabel,
    int LegKind,
    int DiscontinuityKind,
    IReadOnlyList<Bbs757EhsiLegPosition> LegPositions,
    int LegLatitude,
    int LegLongitude,
    int LegTerminator,
    IReadOnlyList<int> NoSymbolTerminators,
    IReadOnlyList<int> ManualTerminators,
    int LegConstraintKind,
    int LegConstraintFirst,
    int LegConstraintSecond,
    int LegEta,
    uint PathRegistry,
    int PathRegistryFirst,
    int PathNodeKey,
    int PathNodeValue,
    int TopOfClimbFlag,
    int TopOfClimbLatitude,
    int TopOfClimbLongitude,
    int TopOfDescentFlagA,
    int TopOfDescentFlagB,
    int TopOfDescentLatitude,
    int TopOfDescentLongitude,
    int EndOfDescentLeg,
    string HdgWord,
    string TrkWord,
    string MagWord,
    string TrueWord,
    string MapWord,
    string NmWord,
    string EtaSuffix,
    string TopOfClimbWord,
    string TopOfDescentWord,
    string EndOfDescentWord,
    string ExcessWord) : IJsonOnSerializing, IJsonOnDeserialized
{
    /// <summary>The reason a part is missing when the file was written before the parts existed (or the record was
    /// built without them).</summary>
    public const string NotInFile = "not in this layout file";

    /// <summary>Plan 28's five optional parts, each null exactly when its reason (<c>…Missing</c>) is not. They are the
    /// only members a layout file may lack (<see cref="LayoutFileOptionalAttribute"/>): a file without them loads with
    /// every part null and the reason <see cref="NotInFile"/>.</summary>
    [LayoutFileOptional] public Bbs757EhsiApproach? Approach { get; init; }
    [LayoutFileOptional] public string? ApproachMissing { get; init; } = NotInFile;
    [LayoutFileOptional] public Bbs757EhsiTcas? Tcas { get; init; }
    [LayoutFileOptional] public string? TcasMissing { get; init; } = NotInFile;
    [LayoutFileOptional] public Bbs757EhsiMapOptions? MapOptions { get; init; }
    [LayoutFileOptional] public string? MapOptionsMissing { get; init; } = NotInFile;
    [LayoutFileOptional] public Bbs757EhsiMapExtras? MapExtras { get; init; }
    [LayoutFileOptional] public string? MapExtrasMissing { get; init; } = NotInFile;
    [LayoutFileOptional] public Bbs757EhsiMapGuides? MapGuides { get; init; }
    [LayoutFileOptional] public string? MapGuidesMissing { get; init; } = NotInFile;

    void IJsonOnSerializing.OnSerializing() => CheckParts();

    void IJsonOnDeserialized.OnDeserialized() => CheckParts();

    /// <summary>Refuses a layout whose part and reason are both set or both null, so neither a generator nor a damaged
    /// file can give a part with no reason to be missing, or a missing one with no reason.</summary>
    private void CheckParts()
    {
        static void Check(object? part, string? missing, string name)
        {
            if ((part is null) == (missing is null))
                throw new JsonException($"EHSI part {name}: {(part is null ? "neither the part nor why it is missing" : "both the part and a reason it is missing")}");
        }
        Check(Approach, ApproachMissing, "approach");
        Check(Tcas, TcasMissing, "tcas");
        Check(MapOptions, MapOptionsMissing, "mapOptions");
        Check(MapExtras, MapExtrasMissing, "mapExtras");
        Check(MapGuides, MapGuidesMissing, "mapGuides");
    }

    public bool Equals(Bbs757EhsiLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && ManagerBase == other.ManagerBase
        && ManagerSpan == other.ManagerSpan
        && NdVptr == other.NdVptr
        && Powered == other.Powered
        && Failed == other.Failed
        && Blanked == other.Blanked
        && NdToEfis == other.NdToEfis
        && NdToSource == other.NdToSource
        && NdToMcp == other.NdToMcp
        && NdToRoute == other.NdToRoute
        && SourceToIrs == other.SourceToIrs
        && EfisMode == other.EfisMode
        && EfisCentered == other.EfisCentered
        && Modes.Equals(other.Modes)
        && Range == other.Range
        && RangeSelected == other.RangeSelected
        && RangeChanging == other.RangeChanging
        && Valid == other.Valid
        && Aligning == other.Aligning
        && Number == other.Number
        && TrackUp == other.TrackUp
        && TrueReference == other.TrueReference
        && Width == other.Width
        && Height == other.Height
        && Longitude == other.Longitude
        && Latitude == other.Latitude
        && CenterLatitude == other.CenterLatitude
        && CenterLongitude == other.CenterLongitude
        && Rotation == other.Rotation
        && ScreenX == other.ScreenX
        && ScreenY == other.ScreenY
        && DataSwitch == other.DataSwitch
        && ExcessCount == other.ExcessCount
        && ExcessLimit == other.ExcessLimit
        && OriginAirport == other.OriginAirport
        && DestinationAirport == other.DestinationAirport
        && AirportLongitude == other.AirportLongitude
        && AirportLatitude == other.AirportLatitude
        && McpHeading == other.McpHeading
        && IrsGroundSpeed == other.IrsGroundSpeed
        && Clock == other.Clock
        && RadiusFactor == other.RadiusFactor
        && AircraftYFactor == other.AircraftYFactor
        && ClipInset == other.ClipInset
        && MarkerMargin == other.MarkerMargin
        && MinEtaGroundSpeed == other.MinEtaGroundSpeed
        && RouteLegs == other.RouteLegs
        && RouteFlownLegs == other.RouteFlownLegs
        && LegStride == other.LegStride
        && MaxLegs == other.MaxLegs
        && RouteActive == other.RouteActive
        && RouteModified == other.RouteModified
        && RouteModification == other.RouteModification
        && RouteId == other.RouteId
        && LegLabel == other.LegLabel
        && LegKind == other.LegKind
        && DiscontinuityKind == other.DiscontinuityKind
        && LegPositions.SequenceEqual(other.LegPositions)
        && LegLatitude == other.LegLatitude
        && LegLongitude == other.LegLongitude
        && LegTerminator == other.LegTerminator
        && NoSymbolTerminators.SequenceEqual(other.NoSymbolTerminators)
        && ManualTerminators.SequenceEqual(other.ManualTerminators)
        && LegConstraintKind == other.LegConstraintKind
        && LegConstraintFirst == other.LegConstraintFirst
        && LegConstraintSecond == other.LegConstraintSecond
        && LegEta == other.LegEta
        && PathRegistry == other.PathRegistry
        && PathRegistryFirst == other.PathRegistryFirst
        && PathNodeKey == other.PathNodeKey
        && PathNodeValue == other.PathNodeValue
        && TopOfClimbFlag == other.TopOfClimbFlag
        && TopOfClimbLatitude == other.TopOfClimbLatitude
        && TopOfClimbLongitude == other.TopOfClimbLongitude
        && TopOfDescentFlagA == other.TopOfDescentFlagA
        && TopOfDescentFlagB == other.TopOfDescentFlagB
        && TopOfDescentLatitude == other.TopOfDescentLatitude
        && TopOfDescentLongitude == other.TopOfDescentLongitude
        && EndOfDescentLeg == other.EndOfDescentLeg
        && string.Equals(HdgWord, other.HdgWord, StringComparison.Ordinal)
        && string.Equals(TrkWord, other.TrkWord, StringComparison.Ordinal)
        && string.Equals(MagWord, other.MagWord, StringComparison.Ordinal)
        && string.Equals(TrueWord, other.TrueWord, StringComparison.Ordinal)
        && string.Equals(MapWord, other.MapWord, StringComparison.Ordinal)
        && string.Equals(NmWord, other.NmWord, StringComparison.Ordinal)
        && string.Equals(EtaSuffix, other.EtaSuffix, StringComparison.Ordinal)
        && string.Equals(TopOfClimbWord, other.TopOfClimbWord, StringComparison.Ordinal)
        && string.Equals(TopOfDescentWord, other.TopOfDescentWord, StringComparison.Ordinal)
        && string.Equals(EndOfDescentWord, other.EndOfDescentWord, StringComparison.Ordinal)
        && string.Equals(ExcessWord, other.ExcessWord, StringComparison.Ordinal)
        && Equals(Approach, other.Approach)
        && string.Equals(ApproachMissing, other.ApproachMissing, StringComparison.Ordinal)
        && Equals(Tcas, other.Tcas)
        && string.Equals(TcasMissing, other.TcasMissing, StringComparison.Ordinal)
        && Equals(MapOptions, other.MapOptions)
        && string.Equals(MapOptionsMissing, other.MapOptionsMissing, StringComparison.Ordinal)
        && Equals(MapExtras, other.MapExtras)
        && string.Equals(MapExtrasMissing, other.MapExtrasMissing, StringComparison.Ordinal)
        && Equals(MapGuides, other.MapGuides)
        && string.Equals(MapGuidesMissing, other.MapGuidesMissing, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(ManagerBase);
        h.Add(ManagerSpan);
        h.Add(NdVptr);
        h.Add(Powered);
        h.Add(Failed);
        h.Add(Blanked);
        h.Add(NdToEfis);
        h.Add(NdToSource);
        h.Add(NdToMcp);
        h.Add(NdToRoute);
        h.Add(SourceToIrs);
        h.Add(EfisMode);
        h.Add(EfisCentered);
        h.Add(Modes);
        h.Add(Range);
        h.Add(RangeSelected);
        h.Add(RangeChanging);
        h.Add(Valid);
        h.Add(Aligning);
        h.Add(Number);
        h.Add(TrackUp);
        h.Add(TrueReference);
        h.Add(Width);
        h.Add(Height);
        h.Add(Longitude);
        h.Add(Latitude);
        h.Add(CenterLatitude);
        h.Add(CenterLongitude);
        h.Add(Rotation);
        h.Add(ScreenX);
        h.Add(ScreenY);
        h.Add(DataSwitch);
        h.Add(ExcessCount);
        h.Add(ExcessLimit);
        h.Add(OriginAirport);
        h.Add(DestinationAirport);
        h.Add(AirportLongitude);
        h.Add(AirportLatitude);
        h.Add(McpHeading);
        h.Add(IrsGroundSpeed);
        h.Add(Clock);
        h.Add(RadiusFactor);
        h.Add(AircraftYFactor);
        h.Add(ClipInset);
        h.Add(MarkerMargin);
        h.Add(MinEtaGroundSpeed);
        h.Add(RouteLegs);
        h.Add(RouteFlownLegs);
        h.Add(LegStride);
        h.Add(MaxLegs);
        h.Add(RouteActive);
        h.Add(RouteModified);
        h.Add(RouteModification);
        h.Add(RouteId);
        h.Add(LegLabel);
        h.Add(LegKind);
        h.Add(DiscontinuityKind);
        foreach (var p in LegPositions) h.Add(p);
        h.Add(LegLatitude);
        h.Add(LegLongitude);
        h.Add(LegTerminator);
        foreach (int t in NoSymbolTerminators) h.Add(t);
        foreach (int t in ManualTerminators) h.Add(t);
        h.Add(LegConstraintKind);
        h.Add(LegConstraintFirst);
        h.Add(LegConstraintSecond);
        h.Add(LegEta);
        h.Add(PathRegistry);
        h.Add(PathRegistryFirst);
        h.Add(PathNodeKey);
        h.Add(PathNodeValue);
        h.Add(TopOfClimbFlag);
        h.Add(TopOfClimbLatitude);
        h.Add(TopOfClimbLongitude);
        h.Add(TopOfDescentFlagA);
        h.Add(TopOfDescentFlagB);
        h.Add(TopOfDescentLatitude);
        h.Add(TopOfDescentLongitude);
        h.Add(EndOfDescentLeg);
        h.Add(HdgWord, StringComparer.Ordinal);
        h.Add(TrkWord, StringComparer.Ordinal);
        h.Add(MagWord, StringComparer.Ordinal);
        h.Add(TrueWord, StringComparer.Ordinal);
        h.Add(MapWord, StringComparer.Ordinal);
        h.Add(NmWord, StringComparer.Ordinal);
        h.Add(EtaSuffix, StringComparer.Ordinal);
        h.Add(TopOfClimbWord, StringComparer.Ordinal);
        h.Add(TopOfDescentWord, StringComparer.Ordinal);
        h.Add(EndOfDescentWord, StringComparer.Ordinal);
        h.Add(ExcessWord, StringComparer.Ordinal);
        h.Add(Approach);
        h.Add(ApproachMissing, StringComparer.Ordinal);
        h.Add(Tcas);
        h.Add(TcasMissing, StringComparer.Ordinal);
        h.Add(MapOptions);
        h.Add(MapOptionsMissing, StringComparer.Ordinal);
        h.Add(MapExtras);
        h.Add(MapExtrasMissing, StringComparer.Ordinal);
        h.Add(MapGuides);
        h.Add(MapGuidesMissing, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

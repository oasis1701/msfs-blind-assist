namespace MSFSBlindAssist.Services.BBS757;

/// <summary>One entry of <see cref="Bbs757EhsiMapExtras.CapabilityChoices"/>, BlueBird's rule for where a leg of kind
/// <see cref="Bbs757EhsiMapExtras.CapabilityLegKind"/> is: the first entry whose <paramref name="Capability"/> is in the
/// leg's capability set gives the offsets of its latitude and longitude in the leg; with
/// <paramref name="UnlessZero"/>, a position reading (0, 0) there falls back to the kind's own entry of the layout's
/// <c>LegPositions</c>, as it does when no entry matches.</summary>
public readonly record struct Bbs757EhsiCapabilityChoice(int Capability, int Latitude, int Longitude, bool UnlessZero);

/// <summary>
/// The rest of the EHSI map, an optional part of <see cref="Bbs757EhsiLayout"/> (plan 28), all from the layout file:
/// TERR (the button's L:var handle at <see cref="TerrHandle"/> in the nd, its value an f64 at
/// <see cref="HandleValue"/>; the picture byte; the two elevations, s16 feet, in the record at the static
/// <see cref="TerrainStatic"/> + <see cref="TerrainRecords"/> + side × <see cref="TerrainSideStride"/>, side 0 while
/// the nd's <see cref="DisplaySide"/> holds <see cref="CaptainSide"/>; their colors from the adc's altitude and vertical
/// speed and the draw's thresholds), the wind (the static show byte <see cref="WindShown"/>, into which BlueBird's draw
/// folds its minimum ground speed test: the reader decides by the byte alone),
/// the two runway records in the nd, the deceleration points (vectors of <see cref="DecelerationStride"/>-byte points in
/// the path object, at most <see cref="MaxDecelerationPoints"/> in the walk) and the leg capability set plan 26's leg
/// positions lacked (an unordered set in the leg: first node and size, the node's next pointer and value, at most
/// <see cref="MaxCapabilities"/> nodes) with BlueBird's rule over it. The words are the screen's own. Equality compares
/// every field by content.
/// </summary>
public sealed record Bbs757EhsiMapExtras(
    int TerrHandle,
    int HandleValue,
    int TerrainPicture,
    int DisplaySide,
    int CaptainSide,
    uint TerrainStatic,
    int TerrainRecords,
    int TerrainSideStride,
    int TerrainHighest,
    int TerrainLowest,
    int TerrainDivisor,
    int SourceToAdc,
    int AdcAltitude,
    int AdcVerticalSpeed,
    double RedAbove,
    double AmberAbove,
    double AmberAboveGearDown,
    double GearDown,
    double LookAheadBelow,
    double LookAheadSeconds,
    double LookAheadFloor,
    uint WindShown,
    int DepartureRunway,
    int ArrivalRunway,
    int RunwayFirstLongitude,
    int RunwayFirstLatitude,
    int RunwaySecondLongitude,
    int RunwaySecondLatitude,
    int RunwayName,
    int DecelerationStart,
    int DecelerationEnd,
    int DecelerationStride,
    int DecelerationLongitude,
    int DecelerationLatitude,
    int MaxDecelerationPoints,
    int LegCapabilityFirst,
    int LegCapabilityCount,
    int CapabilityNodeNext,
    int CapabilityNodeValue,
    int MaxCapabilities,
    int CapabilityLegKind,
    IReadOnlyList<Bbs757EhsiCapabilityChoice> CapabilityChoices,
    string TerrWord,
    string NoWindWord)
{
    public bool Equals(Bbs757EhsiMapExtras? other) =>
        other is not null
        && TerrHandle == other.TerrHandle
        && HandleValue == other.HandleValue
        && TerrainPicture == other.TerrainPicture
        && DisplaySide == other.DisplaySide
        && CaptainSide == other.CaptainSide
        && TerrainStatic == other.TerrainStatic
        && TerrainRecords == other.TerrainRecords
        && TerrainSideStride == other.TerrainSideStride
        && TerrainHighest == other.TerrainHighest
        && TerrainLowest == other.TerrainLowest
        && TerrainDivisor == other.TerrainDivisor
        && SourceToAdc == other.SourceToAdc
        && AdcAltitude == other.AdcAltitude
        && AdcVerticalSpeed == other.AdcVerticalSpeed
        && RedAbove == other.RedAbove
        && AmberAbove == other.AmberAbove
        && AmberAboveGearDown == other.AmberAboveGearDown
        && GearDown == other.GearDown
        && LookAheadBelow == other.LookAheadBelow
        && LookAheadSeconds == other.LookAheadSeconds
        && LookAheadFloor == other.LookAheadFloor
        && WindShown == other.WindShown
        && DepartureRunway == other.DepartureRunway
        && ArrivalRunway == other.ArrivalRunway
        && RunwayFirstLongitude == other.RunwayFirstLongitude
        && RunwayFirstLatitude == other.RunwayFirstLatitude
        && RunwaySecondLongitude == other.RunwaySecondLongitude
        && RunwaySecondLatitude == other.RunwaySecondLatitude
        && RunwayName == other.RunwayName
        && DecelerationStart == other.DecelerationStart
        && DecelerationEnd == other.DecelerationEnd
        && DecelerationStride == other.DecelerationStride
        && DecelerationLongitude == other.DecelerationLongitude
        && DecelerationLatitude == other.DecelerationLatitude
        && MaxDecelerationPoints == other.MaxDecelerationPoints
        && LegCapabilityFirst == other.LegCapabilityFirst
        && LegCapabilityCount == other.LegCapabilityCount
        && CapabilityNodeNext == other.CapabilityNodeNext
        && CapabilityNodeValue == other.CapabilityNodeValue
        && MaxCapabilities == other.MaxCapabilities
        && CapabilityLegKind == other.CapabilityLegKind
        && CapabilityChoices.SequenceEqual(other.CapabilityChoices)
        && string.Equals(TerrWord, other.TerrWord, StringComparison.Ordinal)
        && string.Equals(NoWindWord, other.NoWindWord, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(TerrHandle);
        h.Add(HandleValue);
        h.Add(TerrainPicture);
        h.Add(DisplaySide);
        h.Add(CaptainSide);
        h.Add(TerrainStatic);
        h.Add(TerrainRecords);
        h.Add(TerrainSideStride);
        h.Add(TerrainHighest);
        h.Add(TerrainLowest);
        h.Add(TerrainDivisor);
        h.Add(SourceToAdc);
        h.Add(AdcAltitude);
        h.Add(AdcVerticalSpeed);
        h.Add(RedAbove);
        h.Add(AmberAbove);
        h.Add(AmberAboveGearDown);
        h.Add(GearDown);
        h.Add(LookAheadBelow);
        h.Add(LookAheadSeconds);
        h.Add(LookAheadFloor);
        h.Add(WindShown);
        h.Add(DepartureRunway);
        h.Add(ArrivalRunway);
        h.Add(RunwayFirstLongitude);
        h.Add(RunwayFirstLatitude);
        h.Add(RunwaySecondLongitude);
        h.Add(RunwaySecondLatitude);
        h.Add(RunwayName);
        h.Add(DecelerationStart);
        h.Add(DecelerationEnd);
        h.Add(DecelerationStride);
        h.Add(DecelerationLongitude);
        h.Add(DecelerationLatitude);
        h.Add(MaxDecelerationPoints);
        h.Add(LegCapabilityFirst);
        h.Add(LegCapabilityCount);
        h.Add(CapabilityNodeNext);
        h.Add(CapabilityNodeValue);
        h.Add(MaxCapabilities);
        h.Add(CapabilityLegKind);
        foreach (var c in CapabilityChoices) h.Add(c);
        h.Add(TerrWord, StringComparer.Ordinal);
        h.Add(NoWindWord, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

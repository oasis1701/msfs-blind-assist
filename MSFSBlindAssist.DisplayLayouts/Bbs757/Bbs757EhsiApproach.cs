namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The EHSI's VOR and ILS picture, an optional part of <see cref="Bbs757EhsiLayout"/> (plan 28), all from the layout
/// file. The VOR and ILS fields (<see cref="VorCourse"/> to <see cref="GlideslopeFlag"/>) and <see cref="DisplaySide"/>
/// are offsets in the nd; the side is the Captain's while it holds <see cref="CaptainSide"/>. The glideslope is in the
/// PFD's ILS block (nd at <see cref="NdToPfd"/>, block at <see cref="PfdToIls"/>); the DME object is at
/// <see cref="NdToDme"/>, its station record's kind at <see cref="StationKind"/> (an ILS DME while
/// <see cref="IlsStationKind"/>). The ILS frequency is shown while the receiver the instrument source selects
/// (<see cref="SourceToIlsReceiver"/>) has the localizer: <c>[receiver + ReceiverToSignal] + SignalBlock</c>, then
/// <see cref="LocalizerReceived"/>. The bearing pointers' selectors are the L:var handles at the static
/// <see cref="RmiLeftHandle"/> and <see cref="RmiRightHandle"/>, the value an f64 at <see cref="HandleValue"/>. A VOR
/// panel's station is at <see cref="PanelStation"/>, in ILS mode at <see cref="PanelIlsStation"/> (drawn only while that
/// station's <see cref="StationFrequency"/>, MHz × 100, equals the panel's manual frequency:
/// <see cref="PanelManualWhole"/> × 100 + <see cref="PanelManualFraction"/>); a pointer is drawn only to a station whose
/// kind is one of <see cref="VorKinds"/>. A station record holds its ident (<see cref="StationIdentLength"/> UTF-32
/// characters), frequency, longitude and latitude. The ADF stations' positions are read only while
/// <see cref="AdfPositions"/>: <c>[adfPanel + AdfLeft]</c> (or <c>AdfRight</c>) is the station object, its position at
/// <see cref="AdfLongitude"/> and <see cref="AdfLatitude"/>. The words are the screen's own and the last three values the
/// draw's constants. Equality compares every field by content.
/// </summary>
public sealed record Bbs757EhsiApproach(
    int VorCourse,
    int VorDeviation,
    int VorDeviationValid,
    int ToFrom,
    int ToValue,
    int FromValue,
    int VorDme,
    int VorDmeValid,
    int IlsCourse,
    int LocalizerDeviation,
    int LocalizerBar,
    int IlsDots,
    int IlsTest,
    int IlsFrequency,
    double NoIlsFrequency,
    int LocFlag,
    int GlideslopeFlag,
    int DisplaySide,
    int CaptainSide,
    int NdToPfd,
    int PfdToIls,
    int GlideslopeDeviation,
    int GlideslopeScale,
    int GlideslopePointer,
    int GlideslopeTest,
    int NdToDme,
    int DmePowered,
    int DmeFailed,
    int DmeStation,
    int DmeDistance,
    int StationKind,
    int IlsStationKind,
    IReadOnlyList<int> VorKinds,
    int SourceToIlsReceiver,
    int ReceiverToSignal,
    int SignalBlock,
    int LocalizerReceived,
    uint RmiLeftHandle,
    uint RmiRightHandle,
    int HandleValue,
    int RmiVor,
    int RmiAdf,
    int NdToVorLeft,
    int NdToVorRight,
    int PanelStation,
    int PanelIlsStation,
    int PanelManualFraction,
    int PanelManualWhole,
    int StationIdent,
    int StationIdentLength,
    int StationFrequency,
    int StationLongitude,
    int StationLatitude,
    int NdToAdf,
    bool AdfPositions,
    int AdfLeft,
    int AdfRight,
    int AdfLongitude,
    int AdfLatitude,
    string VorLeftWord,
    string VorRightWord,
    string IlsWord,
    string ToWord,
    string FromWord,
    string DmeWord,
    string NoFrequencyWord,
    string NoDmeWord,
    string LocWord,
    string GlideslopeWord,
    double DeviationFullScale,
    double DotsPerUnit,
    double GlideslopePin)
{
    public bool Equals(Bbs757EhsiApproach? other) =>
        other is not null
        && VorCourse == other.VorCourse
        && VorDeviation == other.VorDeviation
        && VorDeviationValid == other.VorDeviationValid
        && ToFrom == other.ToFrom
        && ToValue == other.ToValue
        && FromValue == other.FromValue
        && VorDme == other.VorDme
        && VorDmeValid == other.VorDmeValid
        && IlsCourse == other.IlsCourse
        && LocalizerDeviation == other.LocalizerDeviation
        && LocalizerBar == other.LocalizerBar
        && IlsDots == other.IlsDots
        && IlsTest == other.IlsTest
        && IlsFrequency == other.IlsFrequency
        && NoIlsFrequency.Equals(other.NoIlsFrequency)
        && LocFlag == other.LocFlag
        && GlideslopeFlag == other.GlideslopeFlag
        && DisplaySide == other.DisplaySide
        && CaptainSide == other.CaptainSide
        && NdToPfd == other.NdToPfd
        && PfdToIls == other.PfdToIls
        && GlideslopeDeviation == other.GlideslopeDeviation
        && GlideslopeScale == other.GlideslopeScale
        && GlideslopePointer == other.GlideslopePointer
        && GlideslopeTest == other.GlideslopeTest
        && NdToDme == other.NdToDme
        && DmePowered == other.DmePowered
        && DmeFailed == other.DmeFailed
        && DmeStation == other.DmeStation
        && DmeDistance == other.DmeDistance
        && StationKind == other.StationKind
        && IlsStationKind == other.IlsStationKind
        && VorKinds.SequenceEqual(other.VorKinds)
        && SourceToIlsReceiver == other.SourceToIlsReceiver
        && ReceiverToSignal == other.ReceiverToSignal
        && SignalBlock == other.SignalBlock
        && LocalizerReceived == other.LocalizerReceived
        && RmiLeftHandle == other.RmiLeftHandle
        && RmiRightHandle == other.RmiRightHandle
        && HandleValue == other.HandleValue
        && RmiVor == other.RmiVor
        && RmiAdf == other.RmiAdf
        && NdToVorLeft == other.NdToVorLeft
        && NdToVorRight == other.NdToVorRight
        && PanelStation == other.PanelStation
        && PanelIlsStation == other.PanelIlsStation
        && PanelManualFraction == other.PanelManualFraction
        && PanelManualWhole == other.PanelManualWhole
        && StationIdent == other.StationIdent
        && StationIdentLength == other.StationIdentLength
        && StationFrequency == other.StationFrequency
        && StationLongitude == other.StationLongitude
        && StationLatitude == other.StationLatitude
        && NdToAdf == other.NdToAdf
        && AdfPositions == other.AdfPositions
        && AdfLeft == other.AdfLeft
        && AdfRight == other.AdfRight
        && AdfLongitude == other.AdfLongitude
        && AdfLatitude == other.AdfLatitude
        && VorLeftWord == other.VorLeftWord
        && VorRightWord == other.VorRightWord
        && IlsWord == other.IlsWord
        && ToWord == other.ToWord
        && FromWord == other.FromWord
        && DmeWord == other.DmeWord
        && NoFrequencyWord == other.NoFrequencyWord
        && NoDmeWord == other.NoDmeWord
        && LocWord == other.LocWord
        && GlideslopeWord == other.GlideslopeWord
        && DeviationFullScale.Equals(other.DeviationFullScale)
        && DotsPerUnit.Equals(other.DotsPerUnit)
        && GlideslopePin.Equals(other.GlideslopePin);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(VorCourse);
        h.Add(VorDeviation);
        h.Add(VorDeviationValid);
        h.Add(ToFrom);
        h.Add(ToValue);
        h.Add(FromValue);
        h.Add(VorDme);
        h.Add(VorDmeValid);
        h.Add(IlsCourse);
        h.Add(LocalizerDeviation);
        h.Add(LocalizerBar);
        h.Add(IlsDots);
        h.Add(IlsTest);
        h.Add(IlsFrequency);
        h.Add(NoIlsFrequency);
        h.Add(LocFlag);
        h.Add(GlideslopeFlag);
        h.Add(DisplaySide);
        h.Add(CaptainSide);
        h.Add(NdToPfd);
        h.Add(PfdToIls);
        h.Add(GlideslopeDeviation);
        h.Add(GlideslopeScale);
        h.Add(GlideslopePointer);
        h.Add(GlideslopeTest);
        h.Add(NdToDme);
        h.Add(DmePowered);
        h.Add(DmeFailed);
        h.Add(DmeStation);
        h.Add(DmeDistance);
        h.Add(StationKind);
        h.Add(IlsStationKind);
        foreach (int k in VorKinds) h.Add(k);
        h.Add(SourceToIlsReceiver);
        h.Add(ReceiverToSignal);
        h.Add(SignalBlock);
        h.Add(LocalizerReceived);
        h.Add(RmiLeftHandle);
        h.Add(RmiRightHandle);
        h.Add(HandleValue);
        h.Add(RmiVor);
        h.Add(RmiAdf);
        h.Add(NdToVorLeft);
        h.Add(NdToVorRight);
        h.Add(PanelStation);
        h.Add(PanelIlsStation);
        h.Add(PanelManualFraction);
        h.Add(PanelManualWhole);
        h.Add(StationIdent);
        h.Add(StationIdentLength);
        h.Add(StationFrequency);
        h.Add(StationLongitude);
        h.Add(StationLatitude);
        h.Add(NdToAdf);
        h.Add(AdfPositions);
        h.Add(AdfLeft);
        h.Add(AdfRight);
        h.Add(AdfLongitude);
        h.Add(AdfLatitude);
        h.Add(VorLeftWord);
        h.Add(VorRightWord);
        h.Add(IlsWord);
        h.Add(ToWord);
        h.Add(FromWord);
        h.Add(DmeWord);
        h.Add(NoFrequencyWord);
        h.Add(NoDmeWord);
        h.Add(LocWord);
        h.Add(GlideslopeWord);
        h.Add(DeviationFullScale);
        h.Add(DotsPerUnit);
        h.Add(GlideslopePin);
        return h.ToHashCode();
    }
}

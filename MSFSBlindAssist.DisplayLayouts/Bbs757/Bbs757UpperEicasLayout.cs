using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// Where one installed BBS752.wasm keeps the upper EICAS (engine indications and message list), all from that file's
/// layout file, never hardcoded (BBS757-D2). The object is found through the gauge manager as the FMA's PFD is:
/// <see cref="ManagerBase"/> is the manager's static address and the upper EICAS is the one pointer in its
/// <see cref="ManagerSpan"/> bytes whose target starts with <see cref="UpperEicasVptr"/>. The <c>To...</c> fields are
/// offsets of pointers to the objects it draws from; <see cref="EnvelopeRowFl"/>, <see cref="EnvelopeRowMin"/> and
/// <see cref="EnvelopeRowMax"/> are into one in-flight start row (<see cref="EnvelopeRowStride"/> bytes from
/// <see cref="EnvelopeRows"/>); every other offset is into the object named in its group;
/// <see cref="MainList"/>, <see cref="CommunicationList"/> and <see cref="Clock"/> are static addresses. Equality
/// compares every field by content (the arrays and word tables too) except <see cref="SourceFile"/>, so the same
/// build installed twice gives one layout.
/// </summary>
public sealed record Bbs757UpperEicasLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    uint ManagerBase,
    uint ManagerSpan,
    uint UpperEicasVptr,
    int ToComputer,
    int ToEngineLeft,
    int ToEngineRight,
    int ToLower,
    int ToPneumatic,
    int ToFms,
    int ToFire,
    int ToReverserLeft,
    int ToReverserRight,
    int ToTmc,
    int ToTat,
    int Powered,
    int Failed,
    int TimedBlank,
    int Compact,
    int EgtRed,
    int EgtAmber,
    int N1Red,
    int N1Amber,
    int MaxN1Left,
    int MaxN1Right,
    int MaxEgtLeft,
    int MaxEgtRight,
    int EnvelopeRows,
    int EnvelopeRowStride,
    int EnvelopeRowCount,
    int EnvelopeRowFl,
    int EnvelopeRowMin,
    int EnvelopeRowMax,
    uint MainList,
    uint CommunicationList,
    int EntryText,
    int EntryLength,
    int EntryId,
    int EntryCancelled,
    int EntryLevel,
    int MaxMessages,
    int MaxTextLength,
    Bbs757WordTable Levels,
    int LinesPerPage,
    int CommunicationLinesBase,
    int ComputerPage,
    int ComputerRecallTime,
    int ComputerBlank,
    uint Clock,
    double RecallShowSeconds,
    string PageWord,
    string RecallWord,
    int EngineBlock,
    int BlockEgt,
    int BlockN1,
    int BlockEpr,
    int EngineN1Alternate,
    int[] EngineN1AlternateFlags,
    int EngineValid,
    int EngineInvalidValue,
    int EngineType,
    int EngineTypePw,
    int EngineCombustion,
    double PwBlankBelow,
    int BlockN2Pw,
    int ThrustMode,
    int ThrustNumber,
    int Derate,
    int[] DtoFlags,
    int DerateShown,
    int ReferenceEpr,
    int ManualEprLeft,
    int ManualEprRight,
    int TmcFailed,
    int TmcChannelLeft,
    int TmcChannelRight,
    Bbs757WordTable ThrustWords,
    string DtoWord,
    string ManualWord,
    int[] NumberedModes,
    Bbs757WordTable ThrustNumbers,
    int[] InhibitModes,
    int TatValue,
    int ReverserState,
    int ReverserMoving,
    int ReverserDeployed,
    string RevWord,
    int PneumaticComponents,
    int ComponentStride,
    int ComponentId,
    int ComponentPosition,
    int ComponentState,
    int TaiLeftId,
    int TaiRightId,
    double TaiMinOpen,
    int[] TaiStates,
    string TaiWord,
    int MaxComponents,
    int FmsOnGround,
    int FireSwitchLeft,
    int FireSwitchRight,
    int LowerMode,
    int EnvelopeLowerMode,
    string EnvelopeTitle,
    string EnvelopeFl,
    string EnvelopeKts)
{
    public bool Equals(Bbs757UpperEicasLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && ManagerBase == other.ManagerBase
        && ManagerSpan == other.ManagerSpan
        && UpperEicasVptr == other.UpperEicasVptr
        && ToComputer == other.ToComputer
        && ToEngineLeft == other.ToEngineLeft
        && ToEngineRight == other.ToEngineRight
        && ToLower == other.ToLower
        && ToPneumatic == other.ToPneumatic
        && ToFms == other.ToFms
        && ToFire == other.ToFire
        && ToReverserLeft == other.ToReverserLeft
        && ToReverserRight == other.ToReverserRight
        && ToTmc == other.ToTmc
        && ToTat == other.ToTat
        && Powered == other.Powered
        && Failed == other.Failed
        && TimedBlank == other.TimedBlank
        && Compact == other.Compact
        && EgtRed == other.EgtRed
        && EgtAmber == other.EgtAmber
        && N1Red == other.N1Red
        && N1Amber == other.N1Amber
        && MaxN1Left == other.MaxN1Left
        && MaxN1Right == other.MaxN1Right
        && MaxEgtLeft == other.MaxEgtLeft
        && MaxEgtRight == other.MaxEgtRight
        && EnvelopeRows == other.EnvelopeRows
        && EnvelopeRowStride == other.EnvelopeRowStride
        && EnvelopeRowCount == other.EnvelopeRowCount
        && EnvelopeRowFl == other.EnvelopeRowFl
        && EnvelopeRowMin == other.EnvelopeRowMin
        && EnvelopeRowMax == other.EnvelopeRowMax
        && MainList == other.MainList
        && CommunicationList == other.CommunicationList
        && EntryText == other.EntryText
        && EntryLength == other.EntryLength
        && EntryId == other.EntryId
        && EntryCancelled == other.EntryCancelled
        && EntryLevel == other.EntryLevel
        && MaxMessages == other.MaxMessages
        && MaxTextLength == other.MaxTextLength
        && Levels.Equals(other.Levels)
        && LinesPerPage == other.LinesPerPage
        && CommunicationLinesBase == other.CommunicationLinesBase
        && ComputerPage == other.ComputerPage
        && ComputerRecallTime == other.ComputerRecallTime
        && ComputerBlank == other.ComputerBlank
        && Clock == other.Clock
        && RecallShowSeconds == other.RecallShowSeconds
        && string.Equals(PageWord, other.PageWord, StringComparison.Ordinal)
        && string.Equals(RecallWord, other.RecallWord, StringComparison.Ordinal)
        && EngineBlock == other.EngineBlock
        && BlockEgt == other.BlockEgt
        && BlockN1 == other.BlockN1
        && BlockEpr == other.BlockEpr
        && EngineN1Alternate == other.EngineN1Alternate
        && EngineN1AlternateFlags.AsSpan().SequenceEqual(other.EngineN1AlternateFlags)
        && EngineValid == other.EngineValid
        && EngineInvalidValue == other.EngineInvalidValue
        && EngineType == other.EngineType
        && EngineTypePw == other.EngineTypePw
        && EngineCombustion == other.EngineCombustion
        && PwBlankBelow == other.PwBlankBelow
        && BlockN2Pw == other.BlockN2Pw
        && ThrustMode == other.ThrustMode
        && ThrustNumber == other.ThrustNumber
        && Derate == other.Derate
        && DtoFlags.AsSpan().SequenceEqual(other.DtoFlags)
        && DerateShown == other.DerateShown
        && ReferenceEpr == other.ReferenceEpr
        && ManualEprLeft == other.ManualEprLeft
        && ManualEprRight == other.ManualEprRight
        && TmcFailed == other.TmcFailed
        && TmcChannelLeft == other.TmcChannelLeft
        && TmcChannelRight == other.TmcChannelRight
        && ThrustWords.Equals(other.ThrustWords)
        && string.Equals(DtoWord, other.DtoWord, StringComparison.Ordinal)
        && string.Equals(ManualWord, other.ManualWord, StringComparison.Ordinal)
        && NumberedModes.AsSpan().SequenceEqual(other.NumberedModes)
        && ThrustNumbers.Equals(other.ThrustNumbers)
        && InhibitModes.AsSpan().SequenceEqual(other.InhibitModes)
        && TatValue == other.TatValue
        && ReverserState == other.ReverserState
        && ReverserMoving == other.ReverserMoving
        && ReverserDeployed == other.ReverserDeployed
        && string.Equals(RevWord, other.RevWord, StringComparison.Ordinal)
        && PneumaticComponents == other.PneumaticComponents
        && ComponentStride == other.ComponentStride
        && ComponentId == other.ComponentId
        && ComponentPosition == other.ComponentPosition
        && ComponentState == other.ComponentState
        && TaiLeftId == other.TaiLeftId
        && TaiRightId == other.TaiRightId
        && TaiMinOpen == other.TaiMinOpen
        && TaiStates.AsSpan().SequenceEqual(other.TaiStates)
        && string.Equals(TaiWord, other.TaiWord, StringComparison.Ordinal)
        && MaxComponents == other.MaxComponents
        && FmsOnGround == other.FmsOnGround
        && FireSwitchLeft == other.FireSwitchLeft
        && FireSwitchRight == other.FireSwitchRight
        && LowerMode == other.LowerMode
        && EnvelopeLowerMode == other.EnvelopeLowerMode
        && string.Equals(EnvelopeTitle, other.EnvelopeTitle, StringComparison.Ordinal)
        && string.Equals(EnvelopeFl, other.EnvelopeFl, StringComparison.Ordinal)
        && string.Equals(EnvelopeKts, other.EnvelopeKts, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(ManagerBase);
        h.Add(ManagerSpan);
        h.Add(UpperEicasVptr);
        h.Add(ToComputer);
        h.Add(ToEngineLeft);
        h.Add(ToEngineRight);
        h.Add(ToLower);
        h.Add(ToPneumatic);
        h.Add(ToFms);
        h.Add(ToFire);
        h.Add(ToReverserLeft);
        h.Add(ToReverserRight);
        h.Add(ToTmc);
        h.Add(ToTat);
        h.Add(Powered);
        h.Add(Failed);
        h.Add(TimedBlank);
        h.Add(Compact);
        h.Add(EgtRed);
        h.Add(EgtAmber);
        h.Add(N1Red);
        h.Add(N1Amber);
        h.Add(MaxN1Left);
        h.Add(MaxN1Right);
        h.Add(MaxEgtLeft);
        h.Add(MaxEgtRight);
        h.Add(EnvelopeRows);
        h.Add(EnvelopeRowStride);
        h.Add(EnvelopeRowCount);
        h.Add(EnvelopeRowFl);
        h.Add(EnvelopeRowMin);
        h.Add(EnvelopeRowMax);
        h.Add(MainList);
        h.Add(CommunicationList);
        h.Add(EntryText);
        h.Add(EntryLength);
        h.Add(EntryId);
        h.Add(EntryCancelled);
        h.Add(EntryLevel);
        h.Add(MaxMessages);
        h.Add(MaxTextLength);
        h.Add(Levels);
        h.Add(LinesPerPage);
        h.Add(CommunicationLinesBase);
        h.Add(ComputerPage);
        h.Add(ComputerRecallTime);
        h.Add(ComputerBlank);
        h.Add(Clock);
        h.Add(RecallShowSeconds);
        h.Add(PageWord, StringComparer.Ordinal);
        h.Add(RecallWord, StringComparer.Ordinal);
        h.Add(EngineBlock);
        h.Add(BlockEgt);
        h.Add(BlockN1);
        h.Add(BlockEpr);
        h.Add(EngineN1Alternate);
        h.AddBytes(MemoryMarshal.AsBytes(EngineN1AlternateFlags.AsSpan()));
        h.Add(EngineValid);
        h.Add(EngineInvalidValue);
        h.Add(EngineType);
        h.Add(EngineTypePw);
        h.Add(EngineCombustion);
        h.Add(PwBlankBelow);
        h.Add(BlockN2Pw);
        h.Add(ThrustMode);
        h.Add(ThrustNumber);
        h.Add(Derate);
        h.AddBytes(MemoryMarshal.AsBytes(DtoFlags.AsSpan()));
        h.Add(DerateShown);
        h.Add(ReferenceEpr);
        h.Add(ManualEprLeft);
        h.Add(ManualEprRight);
        h.Add(TmcFailed);
        h.Add(TmcChannelLeft);
        h.Add(TmcChannelRight);
        h.Add(ThrustWords);
        h.Add(DtoWord, StringComparer.Ordinal);
        h.Add(ManualWord, StringComparer.Ordinal);
        h.AddBytes(MemoryMarshal.AsBytes(NumberedModes.AsSpan()));
        h.Add(ThrustNumbers);
        h.AddBytes(MemoryMarshal.AsBytes(InhibitModes.AsSpan()));
        h.Add(TatValue);
        h.Add(ReverserState);
        h.Add(ReverserMoving);
        h.Add(ReverserDeployed);
        h.Add(RevWord, StringComparer.Ordinal);
        h.Add(PneumaticComponents);
        h.Add(ComponentStride);
        h.Add(ComponentId);
        h.Add(ComponentPosition);
        h.Add(ComponentState);
        h.Add(TaiLeftId);
        h.Add(TaiRightId);
        h.Add(TaiMinOpen);
        h.AddBytes(MemoryMarshal.AsBytes(TaiStates.AsSpan()));
        h.Add(TaiWord, StringComparer.Ordinal);
        h.Add(MaxComponents);
        h.Add(FmsOnGround);
        h.Add(FireSwitchLeft);
        h.Add(FireSwitchRight);
        h.Add(LowerMode);
        h.Add(EnvelopeLowerMode);
        h.Add(EnvelopeTitle, StringComparer.Ordinal);
        h.Add(EnvelopeFl, StringComparer.Ordinal);
        h.Add(EnvelopeKts, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>The upper EICAS's result in a layout file: a layout, or the detailed reason (for the log) there is none.</summary>
public sealed record Bbs757UpperEicasDerivation(Bbs757UpperEicasLayout? Layout, string? Failure);

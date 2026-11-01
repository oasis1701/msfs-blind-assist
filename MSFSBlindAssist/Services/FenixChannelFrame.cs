namespace MSFSBlindAssist.Services;

/// <summary>
/// The flight director as the channel drew it. A bar that is not drawn arrives as NaN, so a value is
/// never read as a position without a check. A flag or bar the push left out is <see cref="Default"/>'s:
/// 0 and false.
/// </summary>
internal sealed record FenixFdState(double Pitch, double Fpa, double Roll, double Yawbar, bool FdOnCapt, bool FdOnFo,
    bool TrkFpa, bool RollGuidance, bool FdFlash, bool FdInhibit, bool InhibitRoll, bool InhibitPitch)
{
    /// <summary>What a push that leaves the flight director out reads as: every bar 0 and every flag off.</summary>
    public static readonly FenixFdState Default =
        new(0, 0, 0, 0, false, false, false, false, false, false, false, false);
}

/// <summary>One slot of the FMA: its coded text exactly as sent, and whether the slot's change box is on.</summary>
internal readonly record struct FenixFmaSlot(string Text, bool ChangeBox);

/// <summary>
/// One decoded display push (AircraftDataPush). Text is exactly as sent, format codes and spacers
/// included. A value the push left out is its default (0, "" or an empty list).
/// </summary>
/// <param name="Malformed">
/// True when the bytes were not valid protobuf anywhere in the message, a nested part included. Every
/// other member is then its default: nothing is read out of bytes that might be damaged.
/// </param>
/// <param name="WrongShapeFields">
/// The path of each field that arrived in another wire shape than the contract gives it ("110", "110.5",
/// "173.1"), each once, in the order they were found; the field took its default and the others still
/// decoded. "111" is also here when the FMA has a slot count other than 16.
/// </param>
internal sealed record FenixChannelFrame(bool Malformed, IReadOnlyList<string> WrongShapeFields, FenixFdState Fd,
    IReadOnlyList<FenixFmaSlot> Fma, IReadOnlyList<string> UpperEcamLeft, IReadOnlyList<string> UpperEcamRight,
    int FwcPage, int FwcPhase, double Altitude, double MagHeading, string FobText)
{
    /// <summary>True when the message was valid protobuf and every field it carried had its contract's shape.</summary>
    public bool ShapesOk => !Malformed && WrongShapeFields.Count == 0;

    /// <summary>The frame of a malformed message: <see cref="Malformed"/> and every value its default.</summary>
    public static readonly FenixChannelFrame MalformedFrame = new(
        true, Array.Empty<string>(), FenixFdState.Default, Array.Empty<FenixFmaSlot>(),
        Array.Empty<string>(), Array.Empty<string>(), 0, 0, 0, 0, "");
}

/// <summary>One entry of the ECAM list, text exactly as sent.</summary>
internal readonly record struct FenixEcamEntry(string Ata, string Name, string Title, bool IsAlert);

/// <summary>
/// One decoded ECAM entries message (EcamUpdate). <paramref name="Malformed"/> and
/// <paramref name="WrongShapeFields"/> mean what they do on <see cref="FenixChannelFrame"/>; a malformed
/// message has no entries and no heartbeat.
/// </summary>
internal sealed record FenixEcamEntries(bool Malformed, IReadOnlyList<string> WrongShapeFields,
    IReadOnlyList<FenixEcamEntry> Entries, bool Heartbeat)
{
    /// <summary>True when the message was valid protobuf and every field it carried had its contract's shape.</summary>
    public bool ShapesOk => !Malformed && WrongShapeFields.Count == 0;
}

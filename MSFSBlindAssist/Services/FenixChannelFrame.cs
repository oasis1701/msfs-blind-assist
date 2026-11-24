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
/// The path of each field that arrived in another shape than the contract gives it, by wire type or, for a
/// string, because its bytes are not valid UTF-8 ("110", "110.5", "173.1"), each once, in the order they
/// were found; the field took its default and the others still decoded. "111" is also here when the FMA
/// has 1 to 15 or 17 or more slots; an FMA with none is not a wrong shape (see <see cref="FenixChannelDecoder"/>).
/// </param>
internal sealed record FenixChannelFrame(bool Malformed, IReadOnlyList<string> WrongShapeFields, FenixFdState Fd,
    IReadOnlyList<FenixFmaSlot> Fma, IReadOnlyList<string> UpperEcamLeft, IReadOnlyList<string> UpperEcamRight,
    int FwcPage, int FwcPhase, double MagHeading, string FobText)
{
    /// <summary>True when the message was valid protobuf and every field it carried had its contract's shape.</summary>
    public bool ShapesOk => !Malformed && WrongShapeFields.Count == 0;

    /// <summary>
    /// False only for a push with no display data: no PrimaryData, or an empty one. Such a push is neither
    /// damaged nor reshaped, there is just nothing in it to read, so the trust gate and the monitor skip
    /// it. True for every other frame, a malformed or wrong-shaped one included. An init property rather
    /// than a constructor parameter, so a frame built positionally has data.
    /// </summary>
    public bool HasData { get; init; } = true;

    /// <summary>
    /// True while a powered display unit shows the captain's PFD (duData: a unit whose Power is on and whose Content
    /// is PFDCapt). False when none does: the PFD is dark, the push has no duData, or the PFD is shown only on another
    /// unit (the first officer's), which counts as off. Like <see cref="EwdLive"/>, <see cref="SdLive"/>,
    /// <see cref="NdLive"/>, <see cref="DcduLive"/> and <see cref="IsisPowered"/>, an init property that is false
    /// unless set, so a frame built positionally shows no display.
    /// </summary>
    public bool PfdLive { get; init; }

    /// <summary>
    /// True while a powered display unit shows the upper ECAM, the E/WD (Content ECAMUpper); false otherwise, an ECAM
    /// shown only on an ND included, as for <see cref="PfdLive"/>.
    /// </summary>
    public bool EwdLive { get; init; }

    /// <summary>
    /// True while a powered display unit shows the lower ECAM, the SD (Content ECAMLower); false otherwise, an ECAM
    /// shown only on an ND included, as for <see cref="PfdLive"/>.
    /// </summary>
    public bool SdLive { get; init; }

    /// <summary>
    /// True while a powered display unit shows the captain's ND (Content NDCapt); false otherwise, an ND shown only on
    /// the first officer's unit included, as for <see cref="PfdLive"/>.
    /// </summary>
    public bool NdLive { get; init; }

    /// <summary>
    /// True while a powered display unit shows the captain's DCDU (Content DCDUCapt); false otherwise, a DCDU shown
    /// only on another unit included, as for <see cref="PfdLive"/>.
    /// </summary>
    public bool DcduLive { get; init; }

    /// <summary>
    /// True while the ISIS says it is powered: the Powered flag of its own message (field 182.1). The ISIS is not one
    /// of the display units duData lists, so no Content names it, and a push with no such message leaves this false,
    /// which is not a wrong shape. It plays the part for the ISIS that <see cref="PfdLive"/> plays for the PFD.
    /// </summary>
    public bool IsisPowered { get; init; }

    /// <summary>The frame of a malformed message: <see cref="Malformed"/> and every value its default.</summary>
    public static readonly FenixChannelFrame MalformedFrame = new(
        true, Array.Empty<string>(), FenixFdState.Default, Array.Empty<FenixFmaSlot>(),
        Array.Empty<string>(), Array.Empty<string>(), 0, 0, 0, "");

    /// <summary>
    /// The frame of a push with no display data: every value its default, not malformed, no wrong shape,
    /// and <see cref="HasData"/> false.
    /// </summary>
    public static readonly FenixChannelFrame NoData = new(
        false, Array.Empty<string>(), FenixFdState.Default, Array.Empty<FenixFmaSlot>(),
        Array.Empty<string>(), Array.Empty<string>(), 0, 0, 0, "")
    {
        HasData = false
    };
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

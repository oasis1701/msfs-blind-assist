namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The lower EICAS's engine page, all from the layout file (BBS757-D2): the engine data block's fields (offsets into
/// the block <see cref="Bbs757LowerEicasLayout.EngineToData"/> reaches), the four max boxes (offsets into the lower
/// EICAS object), the settings object's pounds flag, the engine's start flags and the fields of BlueBird's start-hold
/// test for the fuel flow shown while starting. The words and number formats are the app's (R17).
/// </summary>
public sealed record Bbs757LowerEicasEnginePage(
    int N2,
    int N3,
    int FuelFlow,
    int OilPressure,
    int OilTemperature,
    int OilQuantity,
    int Vibration,
    int MaxN2Left,
    int MaxN2Right,
    int MaxN3Left,
    int MaxN3Right,
    int LowerToSettings,
    int PoundsFlag,
    int StartFlagA,
    int StartFlagB,
    int StartFlagC,
    int HoldTimer,
    int HoldDelay,
    int HoldByteA,
    int HoldByteB);

/// <summary>
/// The lower EICAS's STATUS page: the message list (a vector of record pointers at the static
/// <see cref="ListAddress"/>: begin and end pointers at <see cref="ListBegin"/> and <see cref="ListEnd"/>, entries
/// <see cref="ListStride"/> bytes apart, at most <see cref="ListCap"/> records in the walk, which is all-or-nothing),
/// a record's text pointer, length and sequence stamp, and the objects the page prints beside it: hydraulics, APU,
/// oxygen, the four flight control pointers (offsets into the lower EICAS object, f32) and the computer's brake
/// temperatures (u32 per wheel, <see cref="BrakeWheels"/> of them). Each <c>LowerTo*</c> is the pointer's offset in
/// the lower EICAS object. <c>HydraulicQuantities</c> is the offset of a std::vector's BEGIN POINTER in the hydraulics
/// object: system i's record is at <c>*(hydraulics + HydraulicQuantities) + HydraulicQuantityStride * i</c>, with
/// <c>HydraulicCapacity</c> and <c>HydraulicQuantity</c> (f64) the offsets inside that record (BlueBird's 23018 calls
/// 23032). The pressures are inline: <c>hydraulics + HydraulicPressures + HydraulicPressureStride * i</c>.
/// </summary>
public sealed record Bbs757LowerEicasStatusPage(
    uint ListAddress,
    int ListBegin,
    int ListEnd,
    int ListStride,
    int ListCap,
    int RecordText,
    int RecordLength,
    int RecordSequence,
    int LowerToHydraulics,
    int HydraulicPressures,
    int HydraulicPressureStride,
    int HydraulicQuantities,
    int HydraulicQuantityStride,
    int HydraulicCapacity,
    int HydraulicQuantity,
    int LowerToApu,
    int ApuEgt,
    int ApuRpm,
    int ApuOilQuantity,
    int LowerToOxygen,
    int OxygenShown,
    int OxygenCapacity,
    int OxygenActive,
    int OxygenFull,
    int OxygenLevel,
    int Rudder,
    int AileronLeft,
    int Elevator,
    int AileronRight,
    int LowerToComputer,
    int BrakeTemperatures,
    int BrakeWheels);

/// <summary>
/// Where one installed BBS752.wasm keeps the lower EICAS, all from that file's layout file, never hardcoded. Like the
/// FMA it reaches its object through the gauge manager: <see cref="ManagerBase"/> and <see cref="ManagerSpan"/> bound
/// the pointers searched for the first whose object carries <see cref="LowerVptr"/>. The display draws while the
/// byte at <see cref="PowerSet"/> is set and the bytes at <see cref="PowerClear"/> and <see cref="BlankClear"/> are
/// clear; <see cref="Page"/> then holds the page shown (<see cref="EnginePage"/>, <see cref="StatusPage"/>,
/// <see cref="EngineOff"/> by kind, any other blank); <see cref="NLatch"/> is the N2/N3 exceedance latch byte. The
/// engines are at <see cref="LeftEngine"/> and <see cref="RightEngine"/>, their data at
/// <see cref="EngineToData"/>, the type (0 = RR) at <see cref="EngineType"/>, the engine invalid while the field at
/// <see cref="EngineInvalid"/> is <see cref="EngineInvalidValue"/>, and the gate's HP spool is
/// <see cref="HpSpoolRr"/> in the data block of an RR, <see cref="HpSpoolPw"/> of a PW. Each page is an optional part
/// (null with its reason). Equality compares <see cref="Anchor"/> and every part by content and ignores
/// <see cref="SourceFile"/>. No word table and no format constant lives here (R17).
/// </summary>
public sealed record Bbs757LowerEicasLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    uint ManagerBase,
    uint ManagerSpan,
    uint LowerVptr,
    int PowerSet,
    int PowerClear,
    int BlankClear,
    int Page,
    int EnginePage,
    int StatusPage,
    int EngineOff,
    int NLatch,
    int LeftEngine,
    int RightEngine,
    int EngineToData,
    int EngineType,
    int EngineInvalid,
    int EngineInvalidValue,
    int HpSpoolRr,
    int HpSpoolPw,
    Bbs757LowerEicasEnginePage? Engine,
    string? EngineMissing,
    Bbs757LowerEicasStatusPage? Status,
    string? StatusMissing)
{
    public bool Equals(Bbs757LowerEicasLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && ManagerBase == other.ManagerBase
        && ManagerSpan == other.ManagerSpan
        && LowerVptr == other.LowerVptr
        && PowerSet == other.PowerSet
        && PowerClear == other.PowerClear
        && BlankClear == other.BlankClear
        && Page == other.Page
        && EnginePage == other.EnginePage
        && StatusPage == other.StatusPage
        && EngineOff == other.EngineOff
        && NLatch == other.NLatch
        && LeftEngine == other.LeftEngine
        && RightEngine == other.RightEngine
        && EngineToData == other.EngineToData
        && EngineType == other.EngineType
        && EngineInvalid == other.EngineInvalid
        && EngineInvalidValue == other.EngineInvalidValue
        && HpSpoolRr == other.HpSpoolRr
        && HpSpoolPw == other.HpSpoolPw
        && Equals(Engine, other.Engine)
        && string.Equals(EngineMissing, other.EngineMissing, StringComparison.Ordinal)
        && Equals(Status, other.Status)
        && string.Equals(StatusMissing, other.StatusMissing, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(ManagerBase);
        h.Add(ManagerSpan);
        h.Add(LowerVptr);
        h.Add(PowerSet);
        h.Add(PowerClear);
        h.Add(BlankClear);
        h.Add(Page);
        h.Add(EnginePage);
        h.Add(StatusPage);
        h.Add(EngineOff);
        h.Add(NLatch);
        h.Add(LeftEngine);
        h.Add(RightEngine);
        h.Add(EngineToData);
        h.Add(EngineType);
        h.Add(EngineInvalid);
        h.Add(EngineInvalidValue);
        h.Add(HpSpoolRr);
        h.Add(HpSpoolPw);
        h.Add(Engine);
        h.Add(EngineMissing, StringComparer.Ordinal);
        h.Add(Status);
        h.Add(StatusMissing, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>The lower EICAS's result in a layout file: a layout, or the detailed reason (for the log) there is none.</summary>
public sealed record Bbs757LowerEicasDerivation(Bbs757LowerEicasLayout? Layout, string? Failure);

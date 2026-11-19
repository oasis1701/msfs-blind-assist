namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// One read of the 757's lower EICAS: the page shown (<see cref="PageValue"/> as stored, <see cref="Page"/> its word:
/// "engine", "STATUS", "engine off", "blank"; null while the display is not drawing), the rows of that page as drawn
/// (<see cref="Bbs757LowerEicasText.PageRows"/>), the STATUS list's messages newest first (read whatever the page), and
/// whether the display draws. <see cref="Corrupt"/> is a torn or out-of-range reading, never spoken, shown or a baseline.
/// </summary>
public sealed record Bbs757LowerEicasReading(
    int PageValue,
    string? Page,
    IReadOnlyList<string> PageRows,
    IReadOnlyList<string> StatusMessages,
    bool Powered,
    bool Corrupt) : IBbs757Reading;

/// <summary>One engine's engine-page values as read from memory. <see cref="Valid"/> is false while the engine's invalid
/// field holds the layout's invalid value (every cell of it then reads blank).</summary>
public sealed record Bbs757LowerEicasEngineRaw(
    bool Valid,
    double N2,
    double N3,
    double FuelFlow,
    double OilPressure,
    double OilTemperature,
    double OilQuantity,
    double Vibration,
    double MaxN2,
    double MaxN3,
    bool Starting,
    bool Hold,
    double HpSpool);

/// <summary>The STATUS page's values as read from memory: three hydraulic systems (left, center, right), the APU, the
/// oxygen object, the four flight control pointers' stored values, and the eight brake temperature digits.</summary>
public sealed record Bbs757LowerEicasStatusRaw(
    IReadOnlyList<double> HydraulicCapacity,
    IReadOnlyList<double> HydraulicQuantity,
    IReadOnlyList<double> HydraulicPressure,
    double ApuEgt,
    double ApuRpm,
    double ApuOilQuantity,
    bool OxygenShown,
    bool OxygenActive,
    int OxygenCapacity,
    double OxygenFull,
    double OxygenLevel,
    double Rudder,
    double AileronLeft,
    double Elevator,
    double AileronRight,
    IReadOnlyList<uint> BrakeTemperatures);

/// <summary>Everything <see cref="Bbs757LowerEicasText.PageRows"/> needs: the page value, the engine type, the N latch,
/// the settings' pounds flag, each part's values (null when the layout has no such part) and the status messages.</summary>
public sealed record Bbs757LowerEicasRaw(
    int PageValue,
    bool Rr,
    bool LatchSet,
    bool Pounds,
    IReadOnlyList<Bbs757LowerEicasEngineRaw>? Engines,
    Bbs757LowerEicasStatusRaw? Status,
    IReadOnlyList<string> Messages);

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Decides, from position samples, when a First Officer window first sees the aircraft FLYING
/// without having watched it take off — MSFSBA or the window started in the air, or a flight
/// loaded in the air — so the departure checklists can be marked complete (owner, 2026-10-14;
/// FoDepartureGroups). Fires at most once per time in the air:
/// <list type="bullet">
/// <item>never seen yet: fires at ≥ <see cref="FlyingMinAglFt"/> and ≥ <see cref="FlyingMinIasKt"/>;
/// lower or slower waits (the window opened just after liftoff), on the ground it never fires;</item>
/// <item>seen on the ground: fires only if, within <see cref="PutInAirWindow"/> of the first airborne
/// sample, it reads ≥ <see cref="PutInAirMinAglFt"/> and ≥ <see cref="FlyingMinIasKt"/> — put into the
/// air, not climbed there (the first airborne sample after a flight load can still carry the old
/// ground AGL, hence a window, not one sample); after the window it was a takeoff;</item>
/// <item>unknown AGL or IAS (NaN) changes nothing.</item>
/// </list>
/// Thresholds are judgements: an airliner is far below 500 ft 5 s after liftoff, and the main
/// menu or a parked aircraft reads on the ground or 0 kt. Not thread-safe: one caller thread.
/// </summary>
public sealed class InFlightStartDetector
{
    public const double FlyingMinAglFt   = 100;
    public const double FlyingMinIasKt   = 50;
    public const double PutInAirMinAglFt = 500;
    public static readonly TimeSpan PutInAirWindow = TimeSpan.FromSeconds(5);

    private enum Seen { Unknown, OnGround, LeftGround, Airborne }

    private Seen _seen = Seen.Unknown;
    private DateTime _leftGroundUtc;

    /// <summary>One sample. True exactly when the departure checklists should be marked now.</summary>
    public bool Update(bool onGround, double aglFt, double iasKt, DateTime nowUtc)
    {
        if (onGround) { _seen = Seen.OnGround; return false; }

        if (_seen == Seen.OnGround)
        {
            _seen = Seen.LeftGround;
            _leftGroundUtc = nowUtc;
        }

        if (double.IsNaN(aglFt) || double.IsNaN(iasKt)) return false;

        switch (_seen)
        {
            case Seen.Unknown:
                if (aglFt >= FlyingMinAglFt && iasKt >= FlyingMinIasKt) { _seen = Seen.Airborne; return true; }
                return false;

            case Seen.LeftGround:
                if (nowUtc - _leftGroundUtc > PutInAirWindow) { _seen = Seen.Airborne; return false; }
                if (aglFt >= PutInAirMinAglFt && iasKt >= FlyingMinIasKt) { _seen = Seen.Airborne; return true; }
                return false;

            default:
                return false;
        }
    }
}

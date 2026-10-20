using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The C172's take-off and landing numbers: the landing profile visual guidance and the manual
/// landing assist fly, the taxi turn lead, and output Shift+1 to 6 (<see cref="CessnaC172Speeds"/>).
/// The airframe's, so they serve any C172 variant.
/// </summary>
public abstract partial class CessnaC172BaseDefinition
{
    /// <summary>
    /// Visual guidance and the manual landing assist. The inherited defaults are A320 numbers (Vref 140,
    /// a 30 ft flare to 6 degrees), wrong for an aircraft that crosses the fence at 60-70 knots.
    ///
    /// MEASURED, parked on the G1000 (2026-10-18): the reference datum sits 3.9 ft above the ground on
    /// the wheeled aircraft ("C172SP G1000 Passengers": PLANE ALT ABOVE GROUND 3.92, PLANE ALTITUDE
    /// minus GROUND ALTITUDE the same, STATIC CG TO GROUND 3.95) and 3.8 on skis (3.83), so one figure
    /// serves both. It stands 2.25 degrees nose up on its wheels (PLANE PITCH DEGREES -2.25; SimConnect
    /// reads nose up negative, as SimConnectManager's pitch readout negates it).
    ///
    /// ESTIMATED, not flown on the C172 (the owner will fly it later; a recorded landing then replaces
    /// them): the flare height, flare pitch and the flare law's numbers are a light single's (the
    /// DA40's, which were flown), with the flare pitch set above the C172's own ground stance so the
    /// nose wheel is not flown onto the runway first. The tone ranges and rate caps are the DA40's
    /// light-single envelope, a judgement, not a measurement.
    /// </summary>
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg     = 5.0,    // only when the live AoA is unusable; estimated
        ReferenceVrefKnots        = 65.0,   // POH: 60-70 KIAS on a full-flap approach
        MaxPitchRateDegPerSec     = 5.0,
        MaxBankRateDegPerSec      = 8.0,
        TonePitchRangeDeg         = 10.0,
        ToneBankRangeDeg          = 10.0,
        GlideslopeAltitudeBiasFt  = 50.0,   // the ILS threshold crossing height only
        FlareAltitudeBiasFt       = 3.9,    // measured, above
        FlareTriggerWheelHeightFt = 15.0,   // FAA Airplane Flying Handbook: 10-20 ft; estimated
        FlareTargetPitchDeg       = 4.0,    // about 1.75 degrees above the ground stance; estimated
        FlareSinkTauSec           = 3.0,    // estimated
        FlareTouchdownSinkFpm     = 80.0,   // estimated
        FlareHoldsEntrySink       = true,
        // Touchdown is at about 45-50 knots (VS0 40); the jets' 40 kt end would leave a few seconds of tone.
        RolloutGuidanceEndKnots   = 20.0
    };

    /// <summary>
    /// A short-wheelbase single steered by its pedals: well below the A320's 1.2 s default. Estimated,
    /// the DA40's figure, until a guided taxi's turn rollouts measure it (docs/taxi-guidance.md).
    /// </summary>
    public override double TaxiTurnLeadSeconds => 0.8;

    /// <summary>
    /// Output Shift+1 to 6, the C172's speeds on the keys the Airbus speeds are on, as the DA40 carries
    /// its own: Shift+1 best glide, 2 the airspeed limits, 3 the flap limits, 4 the landing set, 5 the
    /// take-off set, 6 the flap limit for the flaps set now. The flaps and the weight are read fresh
    /// (C172-19). False for any other action.
    /// </summary>
    private bool HandleSpeedReadout(HotkeyAction action, SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (action)
        {
            case HotkeyAction.ReadSpeedGD:
                announcer.AnnounceImmediate(CessnaC172Speeds.DescribeBestGlide());
                return true;

            case HotkeyAction.ReadSpeedS:
                _ = SayLimitsAsync(simConnect, announcer);
                return true;

            case HotkeyAction.ReadSpeedF:
                announcer.AnnounceImmediate(CessnaC172Speeds.DescribeFlapLimits());
                return true;

            case HotkeyAction.ReadSpeedVLS:
                announcer.AnnounceImmediate(CessnaC172Speeds.DescribeLandingSpeeds());
                return true;

            case HotkeyAction.ReadSpeedVS:
                announcer.AnnounceImmediate(CessnaC172Speeds.DescribeTakeoffSpeeds());
                return true;

            case HotkeyAction.ReadSpeedVFE:
                _ = SayFreshAsync(simConnect, announcer, "C172_FLAPS",
                    flaps => CessnaC172Speeds.DescribeFlapLimitNow(flaps).TrimEnd('.'),
                    CessnaC172Speeds.DescribeFlapLimits());
                return true;
        }
        return false;
    }

    /// <summary>Output Shift+2, with the manoeuvring speed for the weight read now. On the UI thread.</summary>
    private static async Task SayLimitsAsync(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        double? pounds = await simConnect.ReadFreshAsync("C172_TOTAL_WEIGHT", 1500);
        announcer.AnnounceImmediate(CessnaC172Speeds.DescribeLimits(pounds));
    }
}

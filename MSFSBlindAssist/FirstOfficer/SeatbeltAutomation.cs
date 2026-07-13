using System;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>Auto seat-belt-sign mode (persisted as int in UserSettings.FOAutoSeatbeltMode).</summary>
public enum FoSeatbeltMode { Disabled = 0, TenThousand = 1, TocTod = 2 }

/// <summary>
/// Shared seat-belt-sign automation state machine. Aircraft-agnostic LOGIC; the actual
/// sign is set through the injected <paramref name="setSign"/> sink (true = signs ON),
/// which each aircraft's phase monitor wires to its own SetSeatbeltSign(bool). Fed
/// (altitudeFt MSL, vsFpm) once per ~1 s tick from the FirstOfficer position feed.
///
/// 10k mode: OFF climbing through 10,000 ft, ON descending through it (300 ft hysteresis).
/// TOC/TOD mode: OFF at a sustained level-off above 10,000 ft (Top of Climb); ON at a
///   sustained descent that has lost >1000 ft from the cruise peak (Top of Descent). The
///   altitude-loss confirmation defeats turbulence VS spikes. Never sets signs on the
///   ground; re-arms for the next leg when below the floor.
/// </summary>
public sealed class SeatbeltAutomation
{
    private readonly Action<bool> _setSign;
    private readonly Action<string> _announce;

    public FoSeatbeltMode Mode { get; set; } = FoSeatbeltMode.Disabled;

    private const int FloorFt = 10_000;
    private const int HysteresisFt = 300;
    private const int TocLevelTicks = 20;        // ~20 s of |VS| < 200 fpm
    private const int TodDescendTicks = 15;      // ~15 s of VS < -500 fpm
    private const double TocLevelVsFpm = 200;
    private const double TodDescendVsFpm = -500;
    private const double TodAltLossFt = 1000;

    private bool? _prevAbove10k;   // 10k-mode crossing latch
    private bool _tocDone;
    private bool _todDone;
    private double _peakAltFt;
    private int _levelTicks;
    private int _descendTicks;

    public SeatbeltAutomation(Action<bool> setSign, Action<string> announce)
    {
        _setSign = setSign;
        _announce = announce;
    }

    public void Reset()
    {
        _prevAbove10k = null;
        _tocDone = false;
        _todDone = false;
        _peakAltFt = 0;
        _levelTicks = 0;
        _descendTicks = 0;
    }

    public void Update(double altitudeFt, double vsFpm)
    {
        switch (Mode)
        {
            case FoSeatbeltMode.TenThousand: Update10k(altitudeFt, vsFpm); break;
            case FoSeatbeltMode.TocTod:      UpdateTocTod(altitudeFt, vsFpm); break;
            default: break; // Disabled
        }
    }

    private void Update10k(double alt, double vs)
    {
        bool climbing   = vs >  150;
        bool descending = vs < -150;
        bool nowAbove = alt > FloorFt + HysteresisFt;
        bool nowBelow = alt < FloorFt - HysteresisFt;

        if (!descending && nowAbove && _prevAbove10k == false)
        {
            _setSign(false);
            _announce("Above ten thousand. Seat belt signs off.");
        }
        else if (!climbing && nowBelow && _prevAbove10k == true)
        {
            _setSign(true);
            _announce("Below ten thousand. Seat belt signs on.");
        }

        if (nowAbove)      _prevAbove10k = true;
        else if (nowBelow) _prevAbove10k = false;
    }

    private void UpdateTocTod(double alt, double vs)
    {
        // Below the floor: on the ground or on final — re-arm for the next climb and track
        // the peak from here up. Never actuates signs while low (belts already ON for
        // takeoff/landing are the pilot's/pre-flight flow's job).
        if (alt < FloorFt)
        {
            _tocDone = false;
            _todDone = false;
            _peakAltFt = alt;
            _levelTicks = 0;
            _descendTicks = 0;
            return;
        }

        if (alt > _peakAltFt) _peakAltFt = alt;

        if (!_tocDone)
        {
            if (Math.Abs(vs) < TocLevelVsFpm) _levelTicks++; else _levelTicks = 0;
            if (_levelTicks >= TocLevelTicks)
            {
                _setSign(false);
                _announce("Cruise. Seat belt signs off.");
                _tocDone = true;
            }
            return; // TOD is only evaluated after TOC
        }

        if (!_todDone)
        {
            if (vs < TodDescendVsFpm) _descendTicks++; else _descendTicks = 0;
            if (_descendTicks >= TodDescendTicks && (_peakAltFt - alt) >= TodAltLossFt)
            {
                _setSign(true);
                _announce("Top of descent. Seat belt signs on.");
                _todDone = true;
            }
        }
    }
}

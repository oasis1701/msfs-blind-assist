using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Final-approach PM callouts (plan W4, pilot-guide standards): localizer alive +
/// within-1-dot cue (the vectored-ILS APPR timing aid, A220-FOT-22-30-012),
/// Stabilized/Not-stabilized at the 1000 ft gate, Speed/Sink-rate exceedances on
/// final, and Bank/Pitch in the flare (the A220 tail-strike-protection calls).
///
/// Driven at the 1 Hz batch cadence off RADIO HEIGHT updates — a deliberate
/// stopgap resolution (the flare calls especially are coarse at 1 Hz); none of
/// these duplicate a native aural (verified against the AuralVars list: the
/// aircraft speaks only V1, radio heights, minimums and warnings).
/// Everything is latched per approach and resets above 2500 ft RA or on ground.
/// </summary>
public partial class SynapticA220Definition
{
    private double _apprVsi, _apprPitch, _apprBank, _apprCdi, _apprIas;
    private bool _apprHasLoc;
    private double _apprPrevRa = double.NaN;
    private bool _locAliveCalled, _locOneDotCalled, _stabGateCalled;
    private long _sinkCallTicks, _speedCallTicks, _flareCallTicks;

    private void ObserveApproachCallouts(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        switch (varName)
        {
            case "A22X_VSI": _apprVsi = value; return;
            case "A22X_PITCH": _apprPitch = value; return;
            case "A22X_BANK": _apprBank = value; return;
            case "A22X_NAV1_CDI": _apprCdi = value; return;
            case "A22X_NAV1_HAS_LOC": _apprHasLoc = value > 0.5; return;
            case "A22X_IAS": _apprIas = value; return;
            case "SIM_ON_GROUND" when value > 0.5:
                ResetApproachLatches();
                return;
            case "A22X_RA":
                break; // the 1 Hz evaluation tick
            default:
                return;
        }

        double ra = value;
        double prevRa = _apprPrevRa;
        _apprPrevRa = ra;
        try
        {
            if (ra > 2500 || double.IsNaN(prevRa))
            {
                if (ra > 2500) ResetApproachLatches();
                return;
            }
            if (_sim != null && Cached(_sim, "SIM_ON_GROUND") > 0.5) return;

            // ---- Localizer alive / within one dot (any altitude below 2500) -----
            // Stock CDI is ±127 full-scale (≈2.5 dots); "alive" = needle off the peg,
            // "one dot" ≈ 51. Gated on a tuned localizer so a no-signal 0 can't fake it.
            if (_apprHasLoc)
            {
                double cdi = Math.Abs(_apprCdi);
                if (!_locAliveCalled && cdi < 120 && cdi > 2)
                {
                    _locAliveCalled = true;
                    announcer.Announce("Localizer alive");
                }
                if (_locAliveCalled && !_locOneDotCalled && cdi <= 51)
                {
                    _locOneDotCalled = true;
                    announcer.Announce("Localizer within one dot");
                }
            }

            bool descending = _apprVsi < -200;

            // ---- Stabilized gate: first descent through 1000 ft RA ---------------
            if (!_stabGateCalled && descending && prevRa > 1000 && ra <= 1000)
            {
                _stabGateCalled = true;
                var problems = new List<string>();
                if (-_apprVsi > 1000) problems.Add("sink rate high");
                if (_sim != null && Cached(_sim, "A22X_GEAR_EXT") < 0.9) problems.Add("gear not down");
                if (_sim != null && Cached(_sim, "A22X_FLAP_LEVER") < 4) problems.Add("flaps not landing flap");
                if (TryGetApproachTargetSpeed(out int vtgt) && _apprIas > 0)
                {
                    double dev = _apprIas - vtgt;
                    if (dev > 20) problems.Add("speed high");
                    else if (dev < -5) problems.Add("speed low");
                }
                announcer.AnnounceImmediate(problems.Count == 0
                    ? "Stabilized"
                    : "Not stabilized: " + string.Join(", ", problems));
            }

            // ---- Continuous exceedances below 1000 ft (rate-limited) -------------
            long now = Environment.TickCount64;
            if (ra < 1000 && descending)
            {
                if (-_apprVsi > 1100 && now - _sinkCallTicks > 12000)
                {
                    _sinkCallTicks = now;
                    announcer.Announce("Sink rate");
                }
                if (TryGetApproachTargetSpeed(out int tgt) && _apprIas > 0 && now - _speedCallTicks > 10000)
                {
                    double dev = _apprIas - tgt;
                    if (dev > 20 || dev < -5)
                    {
                        _speedCallTicks = now;
                        announcer.Announce("Speed");
                    }
                }
            }

            // ---- Flare band: tail-strike-protection calls ------------------------
            // MSFS pitch is positive NOSE-DOWN — invert for the nose-up test.
            // Gated on RA not increasing: without it every TAKEOFF rotation (nose-up
            // 12°+ climbing through 65 ft) fired a spurious "Pitch" call. RA-based
            // (not VSI): late-flare VSI shallows past the -200 descending gate.
            if (ra < 65 && ra <= prevRa && now - _flareCallTicks > 5000)
            {
                double noseUp = -_apprPitch;
                if (Math.Abs(_apprBank) > 7) { _flareCallTicks = now; announcer.Announce("Bank"); }
                else if (noseUp > 7.5 || noseUp < -1) { _flareCallTicks = now; announcer.Announce("Pitch"); }
            }
        }
        catch { /* callouts must never take down the update path */ }
    }

    /// <summary>VAPP preferred, VREF fallback, from the PFD-scraped V-speed bugs — never guessed.</summary>
    private bool TryGetApproachTargetSpeed(out int target)
    {
        var v = _vSpeeds;
        if (v.TryGetValue("VAPP", out target)) return true;
        if (v.TryGetValue("VREF", out target)) return true;
        target = 0;
        return false;
    }

    private void ResetApproachLatches()
    {
        _locAliveCalled = false;
        _locOneDotCalled = false;
        _stabGateCalled = false;
    }
}

using MSFSBlindAssist.Services;

int failures = 0;
void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {n}"); if (!ok) failures++; }
bool Near(double a, double b, double eps = 1e-6) => Math.Abs(a - b) < eps;

Check("normalize 190 -> -170", Near(DockingGeometry.NormalizeDeg180(190), -170));
Check("normalize -190 -> 170", Near(DockingGeometry.NormalizeDeg180(-190), 170));
Check("along-track straight ahead = dist", Near(DockingGeometry.AlongTrackMetres(50, 0), 50, 1e-9));
Check("along-track 90deg off ~ 0", Near(DockingGeometry.AlongTrackMetres(50, 90), 0, 1e-6));
Check("along-track overshoot negative", DockingGeometry.AlongTrackMetres(50, 180) < 0);

// Stop / overshoot — OvershootMetres == 1.0 now
Check("stop true at tol edge 0.3m", DockingGeometry.IsStop(0.3));
Check("stop false just above tightened tol 0.5m", !DockingGeometry.IsStop(0.5));
Check("stop false just above tol 0.8m", !DockingGeometry.IsStop(0.8));
Check("stop false beyond tol", !DockingGeometry.IsStop(5.0));
Check("stop true at -0.5m (within [-1, 0.3])", DockingGeometry.IsStop(-0.5));
Check("overshoot true at -3.0m", DockingGeometry.IsOvershoot(-3.0));
Check("overshoot false ahead", !DockingGeometry.IsOvershoot(5.0));
Check("overshoot false at -0.5m (not yet past 1m)", !DockingGeometry.IsOvershoot(-0.5));
Check("overshoot true at -1.5m", DockingGeometry.IsOvershoot(-1.5));

// Beep — BeepFarMetres == 30.0 now; BeepIntervalMs(40) clamps to 30 -> 1000ms
Check("beep interval far >= near", DockingGeometry.BeepIntervalMs(40) > DockingGeometry.BeepIntervalMs(5));
Check("beep interval clamped far", DockingGeometry.BeepIntervalMs(1000) <= 1000.0 + 1e-9);
Check("beep interval clamped near", DockingGeometry.BeepIntervalMs(0) >= 80.0 - 1e-9);

// Engage — EngageRangeMetres == 50.0; unified signature takes absCross + engageRange.
const double R = DockingGeometry.EngageRangeMetres;
Check("engage yes", DockingGeometry.ShouldEngage(10, 40, 20, 0.0, R));
Check("engage no - too fast", !DockingGeometry.ShouldEngage(40, 40, 20, 0.0, R));
Check("engage no - too far (200m)", !DockingGeometry.ShouldEngage(10, 200, 20, 0.0, R));
Check("engage no - too far (55m, new tighter engage)", !DockingGeometry.ShouldEngage(10, 55, 20, 0.0, R));
Check("engage no - behind", !DockingGeometry.ShouldEngage(10, -5, 20, 0.0, R));
Check("engage no - off cone", !DockingGeometry.ShouldEngage(10, 40, 120, 0.0, R));

Check("engage no - taxi speed 20kt (new 15kt ceiling)", !DockingGeometry.ShouldEngage(20, 40, 20, 0.0, R));
Check("engage yes at 12kt", DockingGeometry.ShouldEngage(12, 40, 20, 0.0, R));

// Engage cross-track FEASIBILITY — KATL C55 regression (docking.log 2026-06-10).
// Docking engaged at along 25 m with 38.4 m of cross-track (hdgErr −57°, inside the
// ±70° cone) — a geometry the 35°-max intercept can never close. Must NOT engage.
Check("engage no - infeasible cross (KATL C55: along 25m, cross 38.4m)",
    !DockingGeometry.ShouldEngage(1.3, 25.0, -57.0, 38.4, R));
// Teleport/reposition straight onto the stop: tiny along, on the line — must engage
// (the feasibility bound floors at StopMaxCrossMetres, it never goes negative).
Check("engage yes - teleport on the line (along 1.8m, cross 0.03m)",
    DockingGeometry.ShouldEngage(0.2, 1.8, 0.8, 0.03, R));
// Mid gate-turn: engage once the turn brings cross within what the profile closes.
Check("engage yes - mid-turn feasible (along 20m, cross 9m)",
    DockingGeometry.ShouldEngage(5, 20, 30, 9.0, R));
Check("engage no - mid-turn infeasible (along 20m, cross 15m)",
    !DockingGeometry.ShouldEngage(5, 20, 30, 15.0, R));
Check("MaxEngageCross floors at StopMaxCross at the squared-up point",
    Near(DockingGeometry.MaxEngageCrossMetres(DockingGeometry.SquareUpEndMetres),
         DockingGeometry.StopMaxCrossMetres));

// Lateral miss — verbal stop-and-retry once the approach can no longer converge.
// KATL C55: along 5.7 m, cross 72.8 ft = 22.2 m → miss (this frame is where the old
// fade snapped the tone to a hard right pan).
Check("lateral miss - KATL C55 (along 5.7m, cross 22.2m)",
    DockingGeometry.IsLateralMiss(5.7, 22.2));
Check("no lateral miss - salvageable residual (along 2.5m, cross 2.2m)",
    !DockingGeometry.IsLateralMiss(2.5, 2.2));
Check("no lateral miss - outside the squaring zone (along 20m, cross 10m)",
    !DockingGeometry.IsLateralMiss(20.0, 10.0));
Check("lateral miss - at the stop band off the line (along 0.3m, cross 3m)",
    DockingGeometry.IsLateralMiss(0.3, 3.0));
// Invariant: an engage-passing geometry can never lateral-miss on the same frame.
foreach (double a in new[] { 0.0, 2.5, 6.0, 25.0, 50.0 })
    Check($"engage bound tighter than miss bound at along={a}m",
        DockingGeometry.MaxEngageCrossMetres(a)
            <= DockingGeometry.StopMaxCrossMetres
               + DockingGeometry.MaxCrossClosurePerMetre * Math.Max(0.0, a) + 1e-9);

// Lineup intercept + cross-gated squaring fade (LineupInterceptDeg).
// KATL C55 trace: cross −73 ft (right of line), along 5.7 m. The old along-only fade
// rotated the cue toward the raw gate heading here (the hard pan). Now: far off the
// line the fade is DISABLED — full saturated intercept, steady tone.
Check("lineup keeps full intercept off-line in the squaring zone (no pan snap)",
    Near(DockingGeometry.LineupInterceptDeg(-73.0, 5.7), -35.0, 1e-9));
Check("lineup full intercept far out, off-line",
    Near(DockingGeometry.LineupInterceptDeg(-73.0, 14.5), -35.0, 1e-9));
// Near the line, the squaring fade still applies exactly as before: squared by 2.5 m.
Check("lineup squared to gate heading near the line at fade end",
    Near(DockingGeometry.LineupInterceptDeg(-2.0, 2.0), 0.0, 1e-9));
// Near the line far out: normal sqrt intercept, no fade. −35·sqrt((2−1)/39) ≈ −5.6°.
Check("lineup sqrt intercept near line, far out",
    Near(DockingGeometry.LineupInterceptDeg(-2.0, 7.0), -35.0 * Math.Sqrt(1.0 / 39.0), 1e-9));
// Blend midpoint (cross 6 ft, along ≤ fade end): half the fade applies.
// intercept = −35·sqrt(5/39) ≈ −12.53; nearLine = 0.5 → multiplier 0.5 → ≈ −6.27.
Check("lineup half-blend at cross 6ft",
    Near(DockingGeometry.LineupInterceptDeg(-6.0, 2.5), -35.0 * Math.Sqrt(5.0 / 39.0) * 0.5, 1e-9));
// Sign convention: + cross (left of line) → + intercept (aim left of gate heading,
// i.e. converge from the left side back to the line).
Check("lineup sign follows cross sign",
    DockingGeometry.LineupInterceptDeg(30.0, 20.0) > 0
    && DockingGeometry.LineupInterceptDeg(-30.0, 20.0) < 0);
// Deadband: within 1 ft of the line the cue is exactly the gate heading.
Check("lineup deadband", Near(DockingGeometry.LineupInterceptDeg(0.8, 20.0), 0.0, 1e-9));

// Constant value asserts
Check("stop tolerance 0.3m: IsStop(0.3) true", DockingGeometry.IsStop(0.3));
Check("stop tolerance 0.3m: IsStop(0.5) false", !DockingGeometry.IsStop(0.5));
Check("BeepNearMetres == StopToleranceMetres (no plateau)", DockingGeometry.BeepNearMetres == DockingGeometry.StopToleranceMetres);
Check("slow-down zone const", DockingGeometry.SlowDownMetres == 6.0);
Check("SlowDownSpeedKts == 5.0", DockingGeometry.SlowDownSpeedKts == 5.0);
Check("EngageRangeMetres == 50.0", DockingGeometry.EngageRangeMetres == 50.0);
Check("BeepFarMetres == 30.0", DockingGeometry.BeepFarMetres == 30.0);

// ShiftStopMetres — the GSX per-aircraft stop-offset math (pure; pins the signs + no-op).
{
    const double lat0 = 50.0, lon0 = 8.0;
    DockingGeometry.ShiftStopMetres(lat0, lon0, 137.0, 0.0, 0.0, out double zLat, out double zLon);
    Check("shift Zero no-op (lat)", zLat == lat0);
    Check("shift Zero no-op (lon)", zLon == lon0);
    // +longitudinal at heading 0 (north) -> stop moves NORTH (lat up), lon unchanged.
    DockingGeometry.ShiftStopMetres(lat0, lon0, 0.0, 100.0, 0.0, out double nLat, out double nLon);
    Check("shift +100m long @hdg0 -> north", Near(nLat, lat0 + 100.0 / 111320.0, 1e-9) && Near(nLon, lon0, 1e-9));
    // +lateral (right) at heading 0 (north) -> stop moves EAST (lon up), lat ~unchanged.
    DockingGeometry.ShiftStopMetres(lat0, lon0, 0.0, 0.0, 100.0, out double eLat, out double eLon);
    Check("shift +100m lat(right) @hdg0 -> east", Near(eLat, lat0, 1e-9) && eLon > lon0);
    // -lateral (left) at heading 0 -> WEST.
    DockingGeometry.ShiftStopMetres(lat0, lon0, 0.0, 0.0, -100.0, out _, out double wLon);
    Check("shift -100m lat(left) @hdg0 -> west", wLon < lon0);
    // +longitudinal at heading 90 (east) -> forward follows heading -> EAST.
    DockingGeometry.ShiftStopMetres(lat0, lon0, 90.0, 100.0, 0.0, out double exLat, out double exLon);
    Check("shift +100m long @hdg90 -> east", exLon > lon0 && Near(exLat, lat0, 1e-9));
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

using MSFSBlindAssist.Navigation;
using G = MSFSBlindAssist.Navigation.WaypointFlightDirectorGeometry;

int failures = 0;
void Check(string name, bool cond, string detail = "")
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    if (!cond) failures++;
}
bool Near(double a, double b, double tol = 0.1) => Math.Abs(a - b) <= tol;

// --- NormalizeSigned / TrackError ---
Check("normalize wraps +350 -> -10", Near(G.NormalizeSigned(350), -10));
Check("normalize wraps -350 -> +10", Near(G.NormalizeSigned(-350), 10));
// Bearing to fix 350, ground track 010 -> fix is 20 deg LEFT -> negative track error.
double te = G.TrackError(350, 10);
Check("track error 350 vs 010 = -20 (fix left)", Near(te, -20), $"got {te:F1}");
Check("track error 010 vs 350 = +20 (fix right)", Near(G.TrackError(10, 350), 20));

// --- CommandedBank: proportional + clamp + rate lead ---
double b1 = G.CommandedBankDeg(trackErrorDeg: 10, yawRateDegPerSec: 0, kRoll: 1.1, bankRateLeadSec: 1.0, maxBankDeg: 25);
Check("bank: 10 deg err * 1.1 = 11 deg right", Near(b1, 11), $"got {b1:F1}");
double b2 = G.CommandedBankDeg(trackErrorDeg: 40, yawRateDegPerSec: 0, kRoll: 1.1, bankRateLeadSec: 1.0, maxBankDeg: 25);
Check("bank: clamps to +25", Near(b2, 25), $"got {b2:F1}");
// Rolling right (yaw +5/s) toward a right error reduces the command (anticipation).
double bNoLead = G.CommandedBankDeg(20, 0, 1.1, 1.3, 25);
double bLead = G.CommandedBankDeg(20, 5, 1.1, 1.3, 25);
Check("bank: rate lead reduces command when turning toward target", bLead < bNoLead, $"{bLead:F1} < {bNoLead:F1}");

// --- RequiredFpa / CommandedPitch ---
// Climb 3000 ft over 10 NM: atan(3000 / (10*6076.12)) ~ +2.83 deg.
double fpa = G.RequiredFpaDeg(targetAltFt: 8000, altMslFt: 5000, distToFixNm: 10);
Check("required FPA 3000ft/10NM ~ +2.83", Near(fpa, 2.83, 0.05), $"got {fpa:F2}");
Check("required FPA guarded near overhead = 0", Near(G.RequiredFpaDeg(8000, 5000, 0.01), 0));
double pitch = G.CommandedPitchDeg(requiredFpaDeg: fpa, aoaDeg: 3.0, maxPitchDeg: 12);
Check("commanded pitch = FPA + AoA", Near(pitch, fpa + 3.0), $"got {pitch:F2}");
Check("commanded pitch clamps to +12", Near(G.CommandedPitchDeg(30, 5, 12), 12));

// --- Projected crossing altitude ---
// Descending 1000 fpm, 6 NM out at 180 kt -> 2 min -> -2000 ft.
double proj = G.ProjectedCrossingAltFt(altMslFt: 10000, vsFpm: -1000, distToFixNm: 6, groundSpeedKts: 180);
Check("projected crossing alt descends 2000 ft", Near(proj, 8000, 1), $"got {proj:F0}");

// --- Constraint resolution ---
var (a1, _) = G.ResolveVerticalTarget(AltitudeConstraintType.AtOrAbove, 6000, null, projectedCrossingAltFt: 7000);
Check("AT_OR_ABOVE neutral when projected above", !a1);
var (a2, t2) = G.ResolveVerticalTarget(AltitudeConstraintType.AtOrAbove, 6000, null, projectedCrossingAltFt: 5000);
Check("AT_OR_ABOVE commands up to 6000 when low", a2 && Near(t2, 6000));
var (a3, _) = G.ResolveVerticalTarget(AltitudeConstraintType.AtOrBelow, 6000, null, projectedCrossingAltFt: 5000);
Check("AT_OR_BELOW neutral when projected below", !a3);
var (a4, t4) = G.ResolveVerticalTarget(AltitudeConstraintType.Between, 5000, 7000, projectedCrossingAltFt: 8000);
Check("BETWEEN commands down to upper bound when above", a4 && Near(t4, 7000));
var (a5, _) = G.ResolveVerticalTarget(AltitudeConstraintType.Between, 5000, 7000, projectedCrossingAltFt: 6000);
Check("BETWEEN neutral inside window", !a5);
var (a6, t6) = G.ResolveVerticalTarget(AltitudeConstraintType.At, 4000, null, projectedCrossingAltFt: 9000);
Check("AT always commands toward target", a6 && Near(t6, 4000));

// --- Course / radial tracking ---
Check("cross-track: east of a north course = right (+)", Near(G.CrossTrackNm(6, 90, 0), 6, 0.05), $"got {G.CrossTrackNm(6,90,0):F2}");
Check("cross-track: west of a north course = left (-)", Near(G.CrossTrackNm(6, 270, 0), -6, 0.05));
Check("cross-track: on course = 0", Near(G.CrossTrackNm(6, 0, 0), 0, 0.01));
// Non-zero course: aircraft bearing 360 (north) from a fix on a 270 course is to the RIGHT of it.
Check("cross-track: right of a 270 course = +", G.CrossTrackNm(6, 360, 270) > 0, $"got {G.CrossTrackNm(6,360,270):F2}");
Check("cross-track: left of a 270 course = -", G.CrossTrackNm(6, 180, 270) < 0);
// Right of course (xt>0) -> fly left of the 270 course (track < 270); capped at 40.
Check("course intercept: right of course turns left, capped", Near(G.CourseInterceptTrackDeg(270, 2, 40, 20), 230));
Check("course intercept: left of course turns right", Near(G.CourseInterceptTrackDeg(270, -1, 40, 20), 290));
Check("course intercept: on course holds course", Near(G.CourseInterceptTrackDeg(270, 0, 40, 20), 270));
Check("course intercept: wraps below 0", Near(G.CourseInterceptTrackDeg(10, 1, 40, 20), 350));

// --- Arrival ---
Check("arrival: inside capture radius", G.HasArrived(0.3, 100, 100, 0.5));
Check("arrival: abeam (bearing >90 off track)", G.HasArrived(2.0, 200, 100, 0.5));
Check("arrival: not yet (ahead, outside radius)", !G.HasArrived(2.0, 105, 100, 0.5));

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

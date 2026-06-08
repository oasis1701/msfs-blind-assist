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
Check("stop true at tol edge 0.5m", DockingGeometry.IsStop(0.5));
Check("stop false just above tol 0.8m", !DockingGeometry.IsStop(0.8));
Check("stop false beyond tol", !DockingGeometry.IsStop(5.0));
Check("stop true at -0.5m (within [-1, 0.5])", DockingGeometry.IsStop(-0.5));
Check("overshoot true at -3.0m", DockingGeometry.IsOvershoot(-3.0));
Check("overshoot false ahead", !DockingGeometry.IsOvershoot(5.0));
Check("overshoot false at -0.5m (not yet past 1m)", !DockingGeometry.IsOvershoot(-0.5));
Check("overshoot true at -1.5m", DockingGeometry.IsOvershoot(-1.5));

// Beep — BeepFarMetres == 30.0 now; BeepIntervalMs(40) clamps to 30 -> 1000ms
Check("beep interval far >= near", DockingGeometry.BeepIntervalMs(40) > DockingGeometry.BeepIntervalMs(5));
Check("beep interval clamped far", DockingGeometry.BeepIntervalMs(1000) <= 1000.0 + 1e-9);
Check("beep interval clamped near", DockingGeometry.BeepIntervalMs(0) >= 80.0 - 1e-9);

// Engage — EngageRangeMetres == 50.0 now
Check("engage yes", DockingGeometry.ShouldEngage(10, 40, 20));
Check("engage no - too fast", !DockingGeometry.ShouldEngage(40, 40, 20));
Check("engage no - too far (200m)", !DockingGeometry.ShouldEngage(10, 200, 20));
Check("engage no - too far (55m, new tighter engage)", !DockingGeometry.ShouldEngage(10, 55, 20));
Check("engage no - behind", !DockingGeometry.ShouldEngage(10, -5, 20));
Check("engage no - off cone", !DockingGeometry.ShouldEngage(10, 40, 120));

Check("engage no - taxi speed 20kt (new 15kt ceiling)", !DockingGeometry.ShouldEngage(20, 40, 20));
Check("engage yes at 12kt", DockingGeometry.ShouldEngage(12, 40, 20));

// Constant value asserts
Check("stop tolerance 0.5m: IsStop(0.5) true", DockingGeometry.IsStop(0.5));
Check("stop tolerance 0.5m: IsStop(0.8) false", !DockingGeometry.IsStop(0.8));
Check("slow-down zone const", DockingGeometry.SlowDownMetres == 6.0);
Check("SlowDownSpeedKts == 5.0", DockingGeometry.SlowDownSpeedKts == 5.0);
Check("EngageRangeMetres == 50.0", DockingGeometry.EngageRangeMetres == 50.0);
Check("BeepFarMetres == 30.0", DockingGeometry.BeepFarMetres == 30.0);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

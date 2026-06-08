using MSFSBlindAssist.Services;

int failures = 0;
void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {n}"); if (!ok) failures++; }
bool Near(double a, double b, double eps = 1e-6) => Math.Abs(a - b) < eps;

Check("normalize 190 -> -170", Near(DockingGeometry.NormalizeDeg180(190), -170));
Check("normalize -190 -> 170", Near(DockingGeometry.NormalizeDeg180(-190), 170));
Check("along-track straight ahead = dist", Near(DockingGeometry.AlongTrackMetres(50, 0), 50, 1e-9));
Check("along-track 90deg off ~ 0", Near(DockingGeometry.AlongTrackMetres(50, 90), 0, 1e-6));
Check("along-track overshoot negative", DockingGeometry.AlongTrackMetres(50, 180) < 0);
Check("stop true within tol", DockingGeometry.IsStop(1.0));
Check("stop false beyond tol", !DockingGeometry.IsStop(5.0));
Check("overshoot true", DockingGeometry.IsOvershoot(-3.0));
Check("overshoot false ahead", !DockingGeometry.IsOvershoot(5.0));
Check("beep interval far >= near", DockingGeometry.BeepIntervalMs(40) > DockingGeometry.BeepIntervalMs(5));
Check("beep interval clamped far", DockingGeometry.BeepIntervalMs(1000) <= 1000.0 + 1e-9);
Check("beep interval clamped near", DockingGeometry.BeepIntervalMs(0) >= 80.0 - 1e-9);
Check("engage yes", DockingGeometry.ShouldEngage(10, 40, 20));
Check("engage no - too fast", !DockingGeometry.ShouldEngage(40, 40, 20));
Check("engage no - too far", !DockingGeometry.ShouldEngage(10, 200, 20));
Check("engage no - behind", !DockingGeometry.ShouldEngage(10, -5, 20));
Check("engage no - off cone", !DockingGeometry.ShouldEngage(10, 40, 120));

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

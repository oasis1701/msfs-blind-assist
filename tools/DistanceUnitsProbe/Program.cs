using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

int failures = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}"); if (!ok) failures++; }

// --- Metres mode ---
DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
Check("metres 152m -> 150 metres", DistanceFormatter.FromMetres(152) == "150 metres");
Check("metres 47m -> 45 metres",   DistanceFormatter.FromMetres(47)  == "45 metres");
Check("metres short 250m -> 250 m", DistanceFormatter.FromMetres(250, shortForm: true) == "250 m");
Check("metres from 1500ft -> 460 metres", DistanceFormatter.FromFeet(1500) == "460 metres");
Check("metres unit word", DistanceFormatter.UnitWord() == "metres");
Check("metres negative clamps", DistanceFormatter.FromMetres(-5) == "0 metres");

// --- Feet mode ---
DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
Check("feet 1490ft -> 1500 feet", DistanceFormatter.FromFeet(1490) == "1500 feet");
Check("feet 60ft -> 50 feet",     DistanceFormatter.FromFeet(60)   == "50 feet");
Check("feet short 500ft -> 500 ft", DistanceFormatter.FromFeet(500, shortForm: true) == "500 ft");
Check("feet from 150m -> 500 feet", DistanceFormatter.FromMetres(150) == "500 feet");
Check("feet unit word", DistanceFormatter.UnitWord() == "feet");

// --- Milestones ---
DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
var pm = DistanceMilestones.ParkingArrival();
Check("parking metres labels 15/10/5", pm[0].Label == "15 metres" && pm[1].Label == "10 metres" && pm[2].Label == "5 metres");
Check("parking metres triggers", Math.Abs(pm[0].TriggerMetres - 15) < 0.01);
Check("parking metres triggers 15/10/5", Math.Abs(pm[1].TriggerMetres - 10) < 0.01 && Math.Abs(pm[2].TriggerMetres - 5) < 0.01);
var xm = DistanceMilestones.ExitApproach();
Check("exit metres labels 500/300/150", xm[0].Label == "500 metres" && xm[1].Label == "300 metres" && xm[2].Label == "150 metres");

DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
var pf = DistanceMilestones.ParkingArrival();
Check("parking feet labels 50/20/10", pf[0].Label == "50 feet" && pf[1].Label == "20 feet" && pf[2].Label == "10 feet");
Check("parking feet trigger metres", Math.Abs(pf[0].TriggerMetres - 50 * 0.3048) < 0.01);
var xf = DistanceMilestones.ExitApproach();
Check("exit feet labels 1500/900/500", xf[0].Label == "1500 feet" && xf[1].Label == "900 feet" && xf[2].Label == "500 feet");
Check("exit feet trigger metres", Math.Abs(xf[0].TriggerMetres - 1500 * 0.3048) < 0.01);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

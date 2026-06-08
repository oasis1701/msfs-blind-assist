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

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

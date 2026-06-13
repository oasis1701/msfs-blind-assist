using MSFSBlindAssist.Services.Gsx;
using MSFSBlindAssist.Database.Models;

string eddfPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Virtuali", "GSX", "MSFS", "EDDF-Aerosoft.ini");

var gates = GsxProfileParser.Parse(eddfPath);
var marsGates = gates.Where(g => g.RawSectionName.Contains("_mars", StringComparison.OrdinalIgnoreCase)).ToList();
Console.WriteLine($"MARS gates found: {marsGates.Count}");
foreach (var g in marsGates)
    Console.WriteLine($"  [{g.RawSectionName}] -> Concourse='{g.Concourse}' Number={g.Number} Suffix='{g.Suffix}'");

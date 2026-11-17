using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What the 757's load takes from a SimBrief OFP, in pounds whatever the plan's units (<c>params/units</c>, converted as the
/// plan is read): the block fuel (<c>fuel/plan_ramp</c>), the passengers (<c>weights/pax_count_actual</c>, else
/// <c>pax_count</c>) and their standard weight (<c>pax_weight</c>), and the payload. Flight, origin
/// and destination name the plan, the aircraft type (<c>aircraft/icaocode</c>) must be the
/// 757-200's, and any of them may be empty.
/// </summary>
public sealed record Bbs757SimBriefPlan(string Flight, string Origin, string Destination, string AircraftIcao,
    double BlockFuelLbs, int Passengers, double PassengerWeightLbs, double PayloadLbs);

/// <summary>What goes in each wing tank, the center tank, the cabin and each hold, in whole pounds.</summary>
public sealed record Bbs757Loads(double LeftLbs, double RightLbs, double CenterLbs, double PassengerLbs,
    double ForwardLbs, double AftLbs, int PlannedPassengers, int SeatedPassengers)
{
    /// <summary>The plan has more passengers than the cabin seats.</summary>
    public bool PassengersCapped => PlannedPassengers > SeatedPassengers;
}

/// <summary>
/// The tanks (gallons), the sim's fuel weight per gallon and the four payload stations (pounds), as
/// read; a value that could not be read is null.
/// </summary>
public sealed record Bbs757LoadReadBack(double? LeftGallons, double? RightGallons, double? CenterGallons,
    double? PoundsPerGallon, double? FirstClassLbs, double? EconomyLbs, double? ForwardHoldLbs, double? AftHoldLbs)
{
    /// <summary>The three tanks in pounds, at the sim's weight per gallon; null when any of the four is unread.</summary>
    public double? FuelLbs => (LeftGallons + RightGallons + CenterGallons) * PoundsPerGallon;

    /// <summary>The cabin, First and Economy together; null when either is unread.</summary>
    public double? PassengerLbs => FirstClassLbs + EconomyLbs;

    /// <summary>Both holds together; null when either is unread.</summary>
    public double? CargoLbs => ForwardHoldLbs + AftHoldLbs;
}

/// <summary>One stock variable the read-back needs: the key it is registered and read under.</summary>
public sealed record Bbs757ReadBackVariable(string Key, string SimVar, string Units, string Label);

/// <summary>
/// The 757's fuel and payload from a SimBrief plan, which BlueBird's own EFB loads when the pilot presses
/// IMPORT FROM SIMBRIEF or START BOARDING on the tablet: BlueBird fuels and boards at its own pace.
/// The targets here are how BlueBird spreads a plan (measured 2026-10-25;
/// docs/b757.md, "What the app adds around a press"): the block evenly over both wings and the
/// rest in the center, every passenger the cabin seats at the plan's standard weight, and the rest of
/// the payload as cargo, the forward hold first. The load watch (<see cref="Bbs757LoadWatch"/>) judges
/// the read tanks and stations against them; every target is whole pounds.
///
/// Pure: parse the plan, compute the targets, read the tanks and stations by key, and the sentences
/// and log lines the watch uses. The app fetches the plan; the definition reads.
/// </summary>
public static class Bbs757Loadsheet
{
    /// <summary>The 757-200's ICAO type, the only one a plan may be for.</summary>
    public const string AircraftType = "B752";

    /// <summary>One wing tank: 2,176 gallons (<c>flight_model.cfg</c>) at 6.7 pounds.</summary>
    public const double WingTankLbs = 14579;

    /// <summary>The center tank: 6,924 gallons at 6.7 pounds.</summary>
    public const double CenterTankLbs = 46390;

    /// <summary>The cabin: 16 First and 160 Economy seats (BlueBird's EFB showed 15/16 and 151/160).</summary>
    public const int Seats = 176;

    /// <summary>
    /// The forward hold, which BlueBird fills first up to this (measured: 20,659 lb went 10,300 forward
    /// and 10,359 aft; 3,990 lb all forward).
    /// </summary>
    public const double ForwardHoldLbs = 10300;

    /// <summary>The aft hold, as measured with BlueBird's cargo setter (session 5).</summary>
    public const double AftHoldLbs = 14700;

    /// <summary>Pounds per kilogram, for a plan in kilograms and an aircraft that shows them.</summary>
    public const double PoundsPerKilogram = 2.20462;

    /// <summary>
    /// The fuel holds its target within the larger of this share of it and <see cref="FuelToleranceLbs"/>.
    /// BlueBird converts at about 6.72 lb/gal and the sim reads 6.70: 10,000 lb set read 9,973, 20,000
    /// read 19,919, and the evening's 16,400 lb plan 16,346 (all within 0.5%).
    /// </summary>
    public const double FuelToleranceShare = 0.01;
    public const double FuelToleranceLbs = 100;

    /// <summary>The cabin and the cargo hold their targets within the larger of this share and <see cref="PayloadToleranceLbs"/> (set exactly, measured).</summary>
    public const double PayloadToleranceShare = 0.005;
    public const double PayloadToleranceLbs = 20;

    public const string FuelTooMuchText = "The plan's fuel is more than the tanks hold.";
    public const string CargoTooMuchText = "The plan's cargo is more than the holds take.";
    public const string NoAircraftTypeText = "Your latest SimBrief plan names no aircraft type. Create a B752 plan, then try again.";

    /// <summary>The refusal of a plan for another aircraft.</summary>
    public static string WrongAircraftText(string icao) =>
        $"Your latest SimBrief plan is for {icao}, not a 757-200. Create a B752 plan, then try again.";

    public const string LeftFuelKey = "BBS757_FUEL_LEFT_GALLONS";
    public const string RightFuelKey = "BBS757_FUEL_RIGHT_GALLONS";
    public const string CenterFuelKey = "BBS757_FUEL_CENTER_GALLONS";
    public const string FuelDensityKey = "BBS757_FUEL_POUNDS_PER_GALLON";
    public const string FirstClassKey = "BBS757_PAYLOAD_FIRST_CLASS";
    public const string EconomyKey = "BBS757_PAYLOAD_ECONOMY";
    public const string ForwardHoldKey = "BBS757_PAYLOAD_FORWARD_HOLD";
    public const string AftHoldKey = "BBS757_PAYLOAD_AFT_HOLD";

    /// <summary>
    /// The stock variables the load watch reads; the definition registers each under its key.
    /// Payload stations 1 and 2 are the pilots (200 lb each) and are not read.
    /// </summary>
    public static IReadOnlyList<Bbs757ReadBackVariable> ReadBackVariables { get; } = new Bbs757ReadBackVariable[]
    {
        new(LeftFuelKey, "FUEL TANK LEFT MAIN QUANTITY", "gallons", "Left Wing Tank"),
        new(RightFuelKey, "FUEL TANK RIGHT MAIN QUANTITY", "gallons", "Right Wing Tank"),
        new(CenterFuelKey, "FUEL TANK CENTER QUANTITY", "gallons", "Center Tank"),
        new(FuelDensityKey, "FUEL WEIGHT PER GALLON", "pounds", "Fuel Weight Per Gallon"),
        new(FirstClassKey, "PAYLOAD STATION WEIGHT:3", "pounds", "First Class Payload"),
        new(EconomyKey, "PAYLOAD STATION WEIGHT:4", "pounds", "Economy Payload"),
        new(ForwardHoldKey, "PAYLOAD STATION WEIGHT:5", "pounds", "Forward Hold Payload"),
        new(AftHoldKey, "PAYLOAD STATION WEIGHT:6", "pounds", "Aft Hold Payload"),
    };

    // A passenger count past this is not a 757's (nor any airliner's) and is not read.
    private const int MaxPassengers = 10000;

    // ---- the plan ---------------------------------------------------------------------------

    /// <summary>
    /// The plan from a SimBrief OFP's XML, or null when it is not one, or lacks the block fuel, the
    /// passengers, their weight, the payload (stood in for by passengers × weight + <c>cargo</c>) or
    /// units of <c>lbs</c> or <c>kgs</c>; a negative weight or a passenger count that is not whole
    /// is refused too. Units are never guessed: a kilogram plan read as pounds would be 2.2 times
    /// too light. Numbers are read culture-proof (CORE-17). No DTD is processed. The aircraft type
    /// is read as it is (empty when missing) and judged by <see cref="Compute"/>.
    /// </summary>
    public static Bbs757SimBriefPlan? ParsePlan(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XElement? root;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            root = XDocument.Load(reader).Root;
        }
        catch (XmlException)
        {
            return null;
        }
        if (root == null) return null;

        Bbs757WeightUnits? planUnits = Text(root, "params", "units")?.ToLowerInvariant() switch
        {
            "lbs" => Bbs757WeightUnits.Pounds,
            "kgs" => Bbs757WeightUnits.Kilograms,
            _ => null,
        };
        if (planUnits is not Bbs757WeightUnits units) return null;
        double f = units == Bbs757WeightUnits.Kilograms ? PoundsPerKilogram : 1.0;

        // A field stood in for only when it is missing: one that is there but unreadable means the
        // plan is not what this expects, and nothing is loaded from it.
        double? block = Weight(root, "fuel", "plan_ramp");
        double? paxWeight = Weight(root, "weights", "pax_weight");
        int? pax = Text(root, "weights", "pax_count_actual") != null
            ? Count(root, "weights", "pax_count_actual")
            : Count(root, "weights", "pax_count");
        if (block is not double b || paxWeight is not double w || pax is not int n) return null;

        double? payload = Text(root, "weights", "payload") != null
            ? Weight(root, "weights", "payload")
            : Weight(root, "weights", "cargo") is double cargo ? n * w + cargo : null;
        if (payload is not double p) return null;

        string airline = Text(root, "general", "icao_airline") ?? "";
        string number = Text(root, "general", "flight_number") ?? "";
        string flight = string.Join(" ", new[] { airline, number }.Where(s => s.Length > 0));
        return new Bbs757SimBriefPlan(flight, Text(root, "origin", "icao_code") ?? "",
            Text(root, "destination", "icao_code") ?? "", Text(root, "aircraft", "icaocode") ?? "",
            b * f, n, w * f, p * f);
    }

    /// <summary>A child's child's text, trimmed with inner runs of white space made one space; null when missing or empty.</summary>
    private static string? Text(XElement root, string parent, string child)
    {
        string? text = root.Element(parent)?.Element(child)?.Value;
        if (string.IsNullOrWhiteSpace(text)) return null;
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A weight in the plan's units, or null when missing, not a number or negative.</summary>
    private static double? Weight(XElement root, string parent, string child)
        => NumberParsing.TryParseExternal(Text(root, parent, child), out double v) && v >= 0 ? v : null;

    /// <summary>A whole count from 0 to <see cref="MaxPassengers"/>, or null.</summary>
    private static int? Count(XElement root, string parent, string child)
        => Weight(root, parent, child) is double v && v == Math.Floor(v) && v <= MaxPassengers ? (int)v : null;

    // ---- the loads --------------------------------------------------------------------------

    /// <summary>
    /// The loads in whole pounds, or a refusal (and no loads): first when the plan is not for a
    /// B752, then when it does not fit. With the block rounded: up to what both wings take
    /// (2 × 14,579), the right wing <c>floor(block / 2)</c>, the left the rest (an odd pound goes
    /// to the left wing, never alone in the center: a 1 lb center tank is unmeasured on BlueBird and
    /// could trip a fuel-configuration message) and the center empty; past it, both wings full and
    /// the center the rest, refused past its 46,390. Passengers: the seated ones (at most 176) at the
    /// plan's standard weight, rounded. Cargo: the payload less the passengers' weight (none when
    /// they weigh more), rounded; the forward hold first, up to 10,300, the aft the rest; refused
    /// past 25,000. The measured plan (25,550 block, 224 × 175, payload 51,459) gives wings 12,775,
    /// center 0, passengers 30,800, holds 10,300 and 10,359.
    /// </summary>
    public static (Bbs757Loads? Loads, string? Refusal) Compute(Bbs757SimBriefPlan plan)
    {
        if (plan.AircraftIcao.Length == 0) return (null, NoAircraftTypeText);
        if (!string.Equals(plan.AircraftIcao, AircraftType, StringComparison.OrdinalIgnoreCase))
            return (null, WrongAircraftText(plan.AircraftIcao));

        double block = Whole(plan.BlockFuelLbs);
        double left, right, center;
        if (block <= 2 * WingTankLbs)
        {
            right = Math.Floor(block / 2);
            left = block - right;
            center = 0;
        }
        else
        {
            left = right = WingTankLbs;
            center = block - 2 * WingTankLbs;
        }
        if (center > CenterTankLbs) return (null, FuelTooMuchText);

        int seated = Math.Min(plan.Passengers, Seats);
        double passengers = Whole(seated * plan.PassengerWeightLbs);

        double cargo = Whole(Math.Max(0, plan.PayloadLbs - passengers));
        if (cargo > ForwardHoldLbs + AftHoldLbs) return (null, CargoTooMuchText);
        double forward = Math.Min(cargo, ForwardHoldLbs);
        double aft = cargo - forward;

        return (new Bbs757Loads(left, right, center, passengers, forward, aft, plan.Passengers, seated), null);
    }

    private static double Whole(double lbs) => Math.Round(lbs, MidpointRounding.AwayFromZero);

    /// <summary>A fuel target's tolerance: the larger of 1% of it and 100 lb.</summary>
    internal static double FuelTolerance(double target) => Math.Max(FuelToleranceShare * target, FuelToleranceLbs);

    /// <summary>A cabin or cargo target's tolerance: the larger of 0.5% of it and 20 lb.</summary>
    internal static double PayloadTolerance(double target) => Math.Max(PayloadToleranceShare * target, PayloadToleranceLbs);

    // ---- the read-back ----------------------------------------------------------------------

    /// <summary>The read-back from the values read under each <see cref="ReadBackVariables"/> key; a missing key is null.</summary>
    public static Bbs757LoadReadBack ReadBack(IReadOnlyDictionary<string, double?> values)
    {
        double? Get(string key) => values.TryGetValue(key, out double? v) && v is double d && double.IsFinite(d) ? d : null;
        return new Bbs757LoadReadBack(Get(LeftFuelKey), Get(RightFuelKey), Get(CenterFuelKey), Get(FuelDensityKey),
            Get(FirstClassKey), Get(EconomyKey), Get(ForwardHoldKey), Get(AftHoldKey));
    }

    // ---- what is said -----------------------------------------------------------------------
    //
    // What the aircraft holds, from the read-back, in the aircraft's units (pounds when they could not
    // be read), each number rounded to 10 and each total the sum of its spoken parts. A group with a
    // part unread has no sentence.

    /// <summary>"Fuel 25480 pounds: left 12740, right 12740, center 0.", or null when a tank or the weight per gallon is unread.</summary>
    public static string? FuelSentence(Bbs757LoadReadBack read, Bbs757WeightUnits? units)
    {
        if (read.LeftGallons is not double l || read.RightGallons is not double r || read.CenterGallons is not double c
            || read.PoundsPerGallon is not double perGallon)
            return null;
        long left = Spoken(l * perGallon, units), right = Spoken(r * perGallon, units), center = Spoken(c * perGallon, units);
        return $"Fuel {S(left + right + center)} {UnitWord(units)}: left {S(left)}, right {S(right)}, center {S(center)}.";
    }

    /// <summary>"Passengers 30800 pounds.", First and Economy together, or null when either is unread.</summary>
    public static string? PassengersSentence(Bbs757LoadReadBack read, Bbs757WeightUnits? units)
        => read.PassengerLbs is double p ? $"Passengers {S(Spoken(p, units))} {UnitWord(units)}." : null;

    /// <summary>"Cargo 20660 pounds: forward 10300, aft 10360.", or null when either hold is unread.</summary>
    public static string? CargoSentence(Bbs757LoadReadBack read, Bbs757WeightUnits? units)
    {
        if (read.ForwardHoldLbs is not double fw || read.AftHoldLbs is not double af) return null;
        long forward = Spoken(fw, units), aft = Spoken(af, units);
        return $"Cargo {S(forward + aft)} {UnitWord(units)}: forward {S(forward)}, aft {S(aft)}.";
    }

    private static string UnitWord(Bbs757WeightUnits? units) => Bbs757EfbLayout.UnitsName(units ?? Bbs757WeightUnits.Pounds);

    private static long Spoken(double lbs, Bbs757WeightUnits? units)
        => Round10(units == Bbs757WeightUnits.Kilograms ? lbs / PoundsPerKilogram : lbs);

    private static string S(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A weight rounded to the nearest 10 (half away from zero), as a whole number, so a read-back
    /// a hair below zero says "0", never "-0", and spoken parts add up to their spoken total.
    /// </summary>
    private static long Round10(double value) => (long)Math.Round(value / 10, MidpointRounding.AwayFromZero) * 10;

    // ---- the log ----------------------------------------------------------------------------

    /// <summary>
    /// Numbers only, whole pounds, for debug.log: the targets and, when given, what the tanks and
    /// stations hold now; "-" for a value not read. "SimBrief load fueling complete: targets
    /// 12775/12775/0, 30800, 10300/10359; now 12737/12737/0, 30800, 10300/-". Neither the plan's name
    /// nor the SimBrief ID is in it.
    /// </summary>
    public static string LogLine(string stage, Bbs757Loads loads, Bbs757LoadReadBack? now)
    {
        static string W(double? lbs) => lbs is double v ? ((long)Whole(v)).ToString(CultureInfo.InvariantCulture) : "-";
        static string Row(double? left, double? right, double? center, double? cabin, double? forward, double? aft)
            => $"{W(left)}/{W(right)}/{W(center)}, {W(cabin)}, {W(forward)}/{W(aft)}";

        string line = $"SimBrief load {stage}: targets " +
                      Row(loads.LeftLbs, loads.RightLbs, loads.CenterLbs, loads.PassengerLbs, loads.ForwardLbs, loads.AftLbs);
        if (now == null) return line;
        double? perGallon = now.PoundsPerGallon;
        return line + "; now " + Row(now.LeftGallons * perGallon, now.RightGallons * perGallon, now.CenterGallons * perGallon,
            now.PassengerLbs, now.ForwardHoldLbs, now.AftHoldLbs);
    }
}

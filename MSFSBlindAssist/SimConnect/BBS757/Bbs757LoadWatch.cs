using System.Globalization;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The parts of a SimBrief load that BlueBird starts on their own (plan 18, Task 6): FETCH SIMBRIEF OFP with its automatic boarding
/// on starts both, IMPORT FROM SIMBRIEF the fueling, START BOARDING the boarding. A <see cref="Bbs757LoadWatch"/> watches, and
/// speaks of, only the parts it was given.
/// </summary>
[Flags]
public enum Bbs757LoadParts { None = 0, Fuel = 1, Boarding = 2, Both = 3 }

/// <summary>
/// The load watch's judgment (task 11e), pure. Once IMPORT FROM SIMBRIEF and START BOARDING have been pressed
/// on BlueBird's tablet, BlueBird loads at its own pace with only a picture to show it: the
/// fuel in about two minutes, the boarding at its REALISTIC rate in under 40 minutes (measured
/// 2026-10-25). The definition reads the tanks and stations every
/// <see cref="IntervalMs"/> and hands each read to <see cref="Observe"/>, which says once each:
/// <list type="bullet">
/// <item>"Fueling complete." and the fuel, when the tanks' total is within the larger of 1% and 100 lb
/// of the plan's block and every tank moved less than <see cref="FuelSettledLbs"/> since the last read
/// (the APU burns fuel, so the tanks never read unchanged; and BlueBird can move fuel between tanks
/// while the total holds still);</item>
/// <item>"Boarding complete." with the passengers and the cargo, when the cabin and the cargo's TOTAL are each
/// within the larger of 0.5% and 20 lb of the plan and moved less than <see cref="PayloadSettledLbs"/> since the
/// last read (BlueBird's split between the holds is not judged, ruling R43). Nothing about the ACARS weight and
/// balance: the aircraft's systems request it by themselves once boarding completes (the owner's ruling of 2026-10-30);</item>
/// <item>at <see cref="NotStartedMs"/>, for each part not done whose tanks or stations have not moved
/// since the first read, that BlueBird has not started it, and that part is no longer watched: fueling
/// and boarding start at once (measured), and with BBS Nest closed BlueBird ignores every tap;</item>
/// <item>after <see cref="TimeoutMs"/> without both, what has not finished; then the watch ends.</item>
/// </list>
/// The first read only seeds; a group with a value unread is neither judged nor kept as the last read.
/// <para>
/// A watch speaks only of its <see cref="Parts"/>: the fuel alone (IMPORT FROM SIMBRIEF) never says the boarding has not started, and
/// the boarding alone (START BOARDING) never says the fueling is complete. A part pressed while the watch runs joins it
/// (<see cref="Include"/>) and is judged from its own start: its first read seeds, and its three minutes begin when it was added.
/// </para>
/// <para>
/// The weights are said in the aircraft's units as they are when a sentence is said: the watch asks its units reader for each
/// sentence that carries weights, never once for the whole watch. A fetch's units question can switch the aircraft's units after the
/// watch began (in-sim check 11, 2026-10-30: FETCH pressed in kilograms, the question's YES switched the aircraft to pounds, and
/// "Fueling complete." and "Boarding complete." were said in kilograms).
/// </para>
/// </summary>
public sealed class Bbs757LoadWatch
{
    /// <summary>How often the tanks and stations are read.</summary>
    public const int IntervalMs = 15_000;

    /// <summary>How long the watch waits for both: BlueBird's realistic boarding took under 40 minutes.</summary>
    public const long TimeoutMs = 60 * 60_000;

    /// <summary>The fuel counts as settled when every tank moved less than this since the last read.</summary>
    public const double FuelSettledLbs = 20;

    /// <summary>When the watch judges whether BlueBird started each part (fueling and boarding start at once, measured).</summary>
    public const long NotStartedMs = 3 * 60_000;

    /// <summary>A tank counts as moved since the first read once it changed by at least this (the APU burns far less in three minutes).</summary>
    public const double FuelMovedLbs = 100;

    /// <summary>The cabin and the cargo count as settled when they moved less than this since the last read.</summary>
    public const double PayloadSettledLbs = 1;

    private readonly Func<Bbs757WeightUnits?> _units;
    private (double Left, double Right, double Center)? _firstTanks, _lastTanks;
    private double? _firstCabin, _firstCargo, _lastPassengers, _lastCargo;
    // When each part's watch began, on the watch's clock: 0 for a part the watch was made with, the moment of the press for one
    // included later. The three minutes of NotStartedMs run from there, and each part's start is judged once.
    private long _fuelStartMs, _boardingStartMs;
    private bool _fuelJudged, _boardingJudged;

    /// <param name="loads">The targets (<see cref="Bbs757Loadsheet.Compute"/>).</param>
    /// <param name="units">Reads the aircraft's weight units now, for a sentence about to be said: null when they cannot be read,
    /// which is said in pounds. Asked only when a sentence carries weights; it must not throw.</param>
    /// <param name="parts">What to watch and speak of: both by default.</param>
    public Bbs757LoadWatch(Bbs757Loads loads, Func<Bbs757WeightUnits?> units, Bbs757LoadParts parts = Bbs757LoadParts.Both)
    {
        Loads = loads;
        _units = units;
        Parts = parts & Bbs757LoadParts.Both;
    }

    /// <summary>The targets the reads are judged against.</summary>
    public Bbs757Loads Loads { get; }

    /// <summary>The parts this watch watches, and the only ones it speaks of.</summary>
    public Bbs757LoadParts Parts { get; private set; }

    /// <summary>
    /// Adds parts to the watch, <paramref name="elapsedMs"/> after it started: IMPORT FROM SIMBRIEF or START BOARDING pressed while
    /// the other part is still being watched. A part that joins is judged from its own start (it is seeded by its own first read,
    /// and the three minutes of <see cref="NotStartedMs"/> run from <paramref name="elapsedMs"/>); one already watched keeps its
    /// start, and the watch keeps its targets and its units reader. The hour of <see cref="TimeoutMs"/> stays the watch's own.
    /// </summary>
    public void Include(Bbs757LoadParts parts, long elapsedMs)
    {
        var added = parts & Bbs757LoadParts.Both & ~Parts;
        if (added.HasFlag(Bbs757LoadParts.Fuel)) _fuelStartMs = elapsedMs;
        if (added.HasFlag(Bbs757LoadParts.Boarding)) _boardingStartMs = elapsedMs;
        Parts |= added;
    }

    public bool FuelDone { get; private set; }
    public bool BoardingDone { get; private set; }
    public bool TimedOut { get; private set; }

    /// <summary>Fueling had not started at <see cref="NotStartedMs"/> and is no longer watched.</summary>
    public bool FuelGaveUp { get; private set; }

    /// <summary>Boarding had not started at <see cref="NotStartedMs"/> and is no longer watched.</summary>
    public bool BoardingGaveUp { get; private set; }

    /// <summary>Nothing more to say: each part watched done or given up, or the hour is up.</summary>
    public bool Finished => TimedOut
        || (Resolved(Bbs757LoadParts.Fuel, FuelDone, FuelGaveUp) && Resolved(Bbs757LoadParts.Boarding, BoardingDone, BoardingGaveUp));

    /// <summary>Whether a part needs no more watching: it is not one of the watch's, or it is done, or it was given up.</summary>
    private bool Resolved(Bbs757LoadParts part, bool done, bool gaveUp) => !Parts.HasFlag(part) || done || gaveUp;

    /// <summary>
    /// One read, <paramref name="elapsedMs"/> after the watch started: what to say now (the fuel's
    /// sentence before the boarding's when both finish at one read), or null. Only the watch's <see cref="Parts"/> are judged.
    /// </summary>
    public string? Observe(Bbs757LoadReadBack read, long elapsedMs)
    {
        if (Finished) return null;
        var said = new List<string>();
        bool fuelWatched = Parts.HasFlag(Bbs757LoadParts.Fuel), boardingWatched = Parts.HasFlag(Bbs757LoadParts.Boarding);

        if (fuelWatched && Tanks(read) is { } tanks)
        {
            double fuel = tanks.Left + tanks.Right + tanks.Center;
            double target = Loads.LeftLbs + Loads.RightLbs + Loads.CenterLbs;
            if (!FuelDone && !FuelGaveUp && _lastTanks is { } last && MovedLess(tanks, last, FuelSettledLbs)
                && Math.Abs(fuel - target) <= Bbs757Loadsheet.FuelTolerance(target))
            {
                FuelDone = true;
                said.Add($"Fueling complete. {Bbs757Loadsheet.FuelSentence(read, _units())}");
            }
            _firstTanks ??= tanks;
            _lastTanks = tanks;
        }

        if (boardingWatched && read.PassengerLbs is double cabin && read.CargoLbs is double cargo)
        {
            double cargoTarget = Loads.ForwardLbs + Loads.AftLbs;
            if (!BoardingDone && !BoardingGaveUp && _lastPassengers is double lastCabin && _lastCargo is double lastCargo
                && Math.Abs(cabin - lastCabin) < PayloadSettledLbs && Math.Abs(cargo - lastCargo) < PayloadSettledLbs
                && Math.Abs(cabin - Loads.PassengerLbs) <= Bbs757Loadsheet.PayloadTolerance(Loads.PassengerLbs)
                && Math.Abs(cargo - cargoTarget) <= Bbs757Loadsheet.PayloadTolerance(cargoTarget))
            {
                BoardingDone = true;
                said.Add(BoardedPhrase(read));
            }
            _firstCabin ??= cabin;
            _firstCargo ??= cargo;
            _lastPassengers = cabin;
            _lastCargo = cargo;
        }

        // Each part's start is judged once, NotStartedMs after ITS OWN start: together for parts that began together (the usual
        // case), apart for one added later.
        bool fuelDue = fuelWatched && !_fuelJudged && elapsedMs - _fuelStartMs >= NotStartedMs;
        bool boardingDue = boardingWatched && !_boardingJudged && elapsedMs - _boardingStartMs >= NotStartedMs;
        if (fuelDue || boardingDue)
        {
            _fuelJudged |= fuelDue;
            _boardingJudged |= boardingDue;
            bool fuelStalled = fuelDue && !FuelDone && _firstTanks is { } firstTanks && _lastTanks is { } lastTanks
                && MovedLess(lastTanks, firstTanks, FuelMovedLbs);
            bool boardingStalled = boardingDue && !BoardingDone && _firstCabin is double firstCabin && _lastPassengers is double lastCabin
                && _firstCargo is double firstCargo && _lastCargo is double lastCargo
                && Math.Abs(lastCabin - firstCabin) < PayloadSettledLbs && Math.Abs(lastCargo - firstCargo) < PayloadSettledLbs;
            if (NotStartedPhrase(fuelStalled, boardingStalled) is { } notStarted) said.Add(notStarted);
            FuelGaveUp |= fuelStalled;
            BoardingGaveUp |= boardingStalled;
        }

        if (!Finished && elapsedMs >= TimeoutMs)
        {
            TimedOut = true;
            said.Add(UnfinishedPhrase(fuelPending: fuelWatched && !FuelDone && !FuelGaveUp,
                boardingPending: boardingWatched && !BoardingDone && !BoardingGaveUp));
        }
        return said.Count > 0 ? string.Join(" ", said) : null;
    }

    /// <summary>Each tank in pounds at the sim's weight per gallon, or null when any of the four is unread.</summary>
    private static (double Left, double Right, double Center)? Tanks(Bbs757LoadReadBack read) =>
        read.LeftGallons is double l && read.RightGallons is double r && read.CenterGallons is double c
            && read.PoundsPerGallon is double perGallon
            ? (l * perGallon, r * perGallon, c * perGallon)
            : null;

    /// <summary>Whether every tank moved less than <paramref name="lbs"/> from <paramref name="from"/>.</summary>
    private static bool MovedLess((double Left, double Right, double Center) now, (double Left, double Right, double Center) from,
        double lbs) =>
        Math.Abs(now.Left - from.Left) < lbs && Math.Abs(now.Right - from.Right) < lbs && Math.Abs(now.Center - from.Center) < lbs;

    /// <summary>
    /// "Boarding complete. Passengers 21090 pounds. Cargo 3990 pounds: forward 3990, aft 0.", then the cap when the plan has
    /// more passengers than the 757 seats (BlueBird puts the rest of the payload in the holds). Its weights are in the units read
    /// now, once for the phrase.
    /// </summary>
    private string BoardedPhrase(Bbs757LoadReadBack read)
    {
        var units = _units();
        var said = new List<string>
        {
            "Boarding complete.",
            Bbs757Loadsheet.PassengersSentence(read, units)!,
            Bbs757Loadsheet.CargoSentence(read, units)!,
        };
        if (Loads.PassengersCapped)
            said.Add($"The plan's {Loads.PlannedPassengers.ToString(CultureInfo.InvariantCulture)} passengers are capped at " +
                     $"the 757's {Bbs757Loadsheet.Seats.ToString(CultureInfo.InvariantCulture)} seats; the rest of the payload goes in the holds.");
        return string.Join(" ", said);
    }

    /// <summary>What has not finished after the hour, and the key that reads it (Output F, Output W).</summary>
    private static string UnfinishedPhrase(bool fuelPending, bool boardingPending) => (fuelPending, boardingPending) switch
    {
        (true, true) => "Fueling and boarding have not finished after an hour. Check the fuel with F and the weight with W.",
        (true, false) => "Fueling has not finished after an hour. Check the fuel with F.",
        _ => "Boarding has not finished after an hour. Check the weight with W.",
    };

    /// <summary>
    /// What has not started at <see cref="NotStartedMs"/>, or null. Neither moving is the BBS Nest case
    /// (BlueBird ignores every tap without it), so only that one names it.
    /// </summary>
    private static string? NotStartedPhrase(bool fuelStalled, bool boardingStalled) => (fuelStalled, boardingStalled) switch
    {
        (true, true) => "BlueBird has not started loading. Is BBS Nest running? Check the fuel with F and the weight with W.",
        (true, false) => "BlueBird has not started fueling. Check the fuel with F.",
        (false, true) => "BlueBird has not started boarding. Check the weight with W.",
        _ => null,
    };
}

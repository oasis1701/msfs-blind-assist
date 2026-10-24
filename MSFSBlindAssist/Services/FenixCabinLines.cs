using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>The Cabin window's tabs, in their order.</summary>
public enum FenixCabinSection { Status, Audio, Doors, Temperature, Water }

/// <summary>
/// One value as the pilot reads it: one ListBox line. <paramref name="Key"/> stays the same while the
/// text changes (<c>door.d1l</c>, <c>audio.volume</c>), so the read-back and the forms find a line by it.
/// <paramref name="State"/> is the part of the line a press changes, so a read-back can wait for it alone:
/// a door's own <c>open</c> or <c>closed</c> (not its slide), a cargo door's <c>open</c> or
/// <c>closed</c> (none while it moves), a zone's target and offset (<c>24.5|0.5</c>, invariant; not the
/// measured air). Null when that part is not known, and on every other line.
/// </summary>
public sealed record FenixCabinLine(FenixCabinSection Section, string Key, string Text, string? State = null);

/// <summary>
/// A cargo door as the Doors page lists and presses it (<see cref="FenixCabinLines.CargoDoors"/>).
/// </summary>
/// <param name="Key">Its line's <see cref="FenixCabinLine.Key"/>: <c>cargo.forward</c>.</param>
/// <param name="Name">Its line's label: <c>Forward cargo door</c>.</param>
/// <param name="SummaryName">Its word in the Status page's cargo summary: <c>forward</c>.</param>
/// <param name="Door">The door the EFB's toggle writes (<see cref="FenixCabinWrites.CargoDoor"/>).</param>
/// <param name="OpenFlag">The EFB's open flag: <see cref="FenixCabinNames.CargoForward"/>.</param>
/// <param name="MovingFlag">The EFB's movement flag that greys the toggle; null for the bulk door, which has none.</param>
public sealed record FenixCabinCargoDoor(
    string Key, string Name, string SummaryName, FenixCargoDoor Door, string OpenFlag, string? MovingFlag);

/// <summary>
/// The last value the Fenix data service gave for each cabin name. Not thread-safe: the Cabin window
/// applies values on its UI thread. A name never seen reads <see cref="FenixDataValue.Unknown"/>, and the
/// typed reads give a value only for their own kind, so an unknown name (<c>""</c>, [FNX-1]) or a JSON
/// null is never false, 0 or a text.
/// </summary>
public sealed class FenixCabinData
{
    private readonly Dictionary<string, FenixDataValue> _values = new(StringComparer.Ordinal);

    /// <summary>Stores a value; true when it differs from what <see cref="Get"/> gave before.</summary>
    public bool Apply(string name, FenixDataValue value)
    {
        if (Get(name).Equals(value)) return false;
        _values[name] = value;
        return true;
    }

    /// <summary>The last value for the name, or <see cref="FenixDataValue.Unknown"/> if it never came.</summary>
    public FenixDataValue Get(string name)
        => _values.TryGetValue(name, out FenixDataValue value) ? value : FenixDataValue.Unknown;

    /// <summary>The value when it is a Bool, else null.</summary>
    public bool? Bool(string name)
    {
        FenixDataValue value = Get(name);
        return value.Kind == FenixValueKind.Bool ? value.Bool : null;
    }

    /// <summary>The value when it is a finite Number, else null.</summary>
    public double? Number(string name)
    {
        FenixDataValue value = Get(name);
        return value.Kind == FenixValueKind.Number && double.IsFinite(value.Number) ? value.Number : null;
    }

    /// <summary>The value when it is a non-empty Text, else null.</summary>
    public string? Text(string name)
    {
        FenixDataValue value = Get(name);
        return value.Kind == FenixValueKind.Text ? value.Text : null;
    }

    /// <summary>True when any stored value is known (not <c>""</c> and not JSON null).</summary>
    public bool AnyKnown => _values.Values.Any(value => value.IsKnown);
}

/// <summary>
/// Turns the cabin values into the lines of the Cabin window's five tabs, the FAP's water and waste
/// cautions, its door-pressure messages and the connection status. Pure: no data service, no state.
/// Everything a line says is proven by a known value; an unknown one reads "no data" and is never
/// closed, disarmed, off, 0 percent or a caution ([FNX-1]). Numbers are invariant (a comma-decimal PC
/// still hears "24.5").
/// <para>
/// Evidence (Fenix 3.0.0.7426 FAP bundle). Doors read <c>y?"open":"closed"</c>
/// and slides <c>y?"armed":"disarmed"</c>.
/// The door message is <c>getDoorMessage</c>: slide pressure under 3000 first, then door pressure under
/// 1000, then "Slide In Park" for a closed door with a disarmed slide when the setting is
/// <c>SlideInPark</c>; the FAP also warns on a missing pressure,
/// which we do not. The water and waste cautions and their order are the water store's <c>d.showIf</c>
/// calls, the lavatory one with the details <c>LAV ${K}</c> and the waste-level one at <c>B&gt;30</c>;
/// each fires here only on what known values prove, where the FAP starts from
/// <c>vacuumSystemPowered:false</c> and shows the vacuum caution before any data. "Cautions: none" needs
/// a known caution input too.
/// A zone's target is <c>selectedTemperature+selectedOffset</c>, and temperature control is available only
/// while the zone controller, the lavatory and galley extraction fan and the CIDS all run
/// (<c>dataAvailable$</c>). The connection status follows the FAP's power-up screens: off, FAP power-up,
/// CIDS power-up. The loudspeaker volume is shown as the FAP Set-Up page shows it, and comes from the
/// data, not the pram state.
/// </para>
/// </summary>
public static class FenixCabinLines
{
    /// <summary>The FAP's "Check Slide Pressure" limit: a known slide pressure below this.</summary>
    public const double SlidePressureLow = 3000;
    /// <summary>The FAP's "Check Door Pressure" limit: a known door pressure below this.</summary>
    public const double DoorPressureLow = 1000;
    /// <summary>The FAP shows "WASTE TANK NOT EMPTY" when the waste tank is above this percentage.</summary>
    public const double WasteNotEmptyAbove = 30;

    private const string NoData = "no data";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---- Formatting -----------------------------------------------------------------------------

    /// <summary>A temperature with one decimal, invariant: <c>24.0</c>.</summary>
    public static string Degrees(double celsius)
    {
        string text = celsius.ToString("F1", Inv);
        return text == "-0.0" ? "0.0" : text;
    }

    /// <summary>A whole percentage, halves away from zero, invariant: <c>85 percent</c>.</summary>
    public static string Percent(double value)
    {
        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0; // -0 would print "-0"
        return rounded.ToString("0", Inv) + " percent";
    }

    /// <summary>A zone's temperature offset: <c>no offset</c>, <c>offset plus 0.5</c>, <c>offset minus 1.0</c>.</summary>
    public static string Offset(double offset)
    {
        double rounded = Math.Round(offset, 1, MidpointRounding.AwayFromZero);
        if (rounded == 0) return "no offset";
        return (rounded > 0 ? "offset plus " : "offset minus ") + Math.Abs(rounded).ToString("F1", Inv);
    }

    /// <summary>
    /// A door or slide by the FAP's designation: <c>1L</c> is <c>Door 1 left</c> (the FAP's numbering, so
    /// an A320's aft door is door 4), <c>OWL</c> is <c>Overwing slide left</c>. Anything else is returned
    /// unchanged.
    /// </summary>
    public static string DoorName(string designation)
    {
        if (TryOverwing(designation, out string side)) return "Overwing slide " + side;
        if (TryMainDoor(designation, out char number, out side)) return $"Door {number} {side}";
        return designation;
    }

    // The short form a summary lists: "1 left", "overwing left".
    private static string ShortName(string designation)
    {
        if (TryOverwing(designation, out string side)) return "overwing " + side;
        if (TryMainDoor(designation, out char number, out side)) return $"{number} {side}";
        return designation;
    }

    private static bool TryOverwing(string designation, out string side)
    {
        side = "";
        if (designation.Length != 3 || !designation.StartsWith("OW", StringComparison.Ordinal)) return false;
        return TrySide(designation[2], out side);
    }

    private static bool TryMainDoor(string designation, out char number, out string side)
    {
        number = designation.Length > 0 ? designation[0] : ' ';
        side = "";
        return designation.Length == 2 && char.IsAsciiDigit(number) && TrySide(designation[1], out side);
    }

    private static bool TrySide(char letter, out string side)
    {
        side = letter switch { 'L' => "left", 'R' => "right", _ => "" };
        return side.Length > 0;
    }

    // ---- Door pressure, cautions, connection -----------------------------------------------------

    /// <summary>
    /// The FAP's door-pressure message for a main door, in its order: <c>check slide pressure</c> when the
    /// slide pressure is known and under 3000, else <c>check door pressure</c> when the door pressure is
    /// known and under 1000, else null. An unknown pressure is not a low one ([FNX-1]).
    /// </summary>
    public static string? PressureMessage(double? doorPressure, double? slidePressure)
    {
        if (slidePressure is double slide && slide < SlidePressureLow) return "check slide pressure";
        if (doorPressure is double door && door < DoorPressureLow) return "check door pressure";
        return null;
    }

    /// <summary>
    /// The FAP's water and waste cautions that hold, in the FAP's order. Each fires only on what known
    /// values prove, never on an unknown one: the vacuum caution on either value known false (the FAP's
    /// <c>!B||!K</c>), the lavatory caution for the layout's lavatories whose flush is known not operational.
    /// </summary>
    public static IReadOnlyList<string> Cautions(FenixCabinData data, FenixCabinLayout layout)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(layout);

        var cautions = new List<string>();
        if (data.Bool(FenixCabinNames.WaterServicePanelOpen) == true)
            cautions.Add("Water service door open");
        if (data.Bool(FenixCabinNames.VacuumSystemPowered) == false
            || data.Bool(FenixCabinNames.VacuumGeneratorOperational) == false)
            cautions.Add("Vacuum system disabled on ground");
        if (data.Bool(FenixCabinNames.WasteServicePanelOpen) == true)
            cautions.Add("Vacuum system disabled: ground servicing");

        string[] lavatories = layout.Lavatories
            .Where(letter => data.Bool(FenixCabinNames.LavatoryFlush(letter)) == false)
            .Select(letter => "LAV " + letter)
            .ToArray();
        if (lavatories.Length > 0)
            cautions.Add("Check lavatory power supply: " + string.Join(", ", lavatories));

        if (data.Bool(FenixCabinNames.WasteTankFull) == true)
            cautions.Add("Vacuum system disabled: waste tank full");
        if (data.Number(FenixCabinNames.WasteTank) is double waste && waste > WasteNotEmptyAbove)
            cautions.Add("Waste tank not empty, check level: ground service required");
        return cautions.AsReadOnly();
    }

    /// <summary>
    /// The status box: the Fenix not answering, then the FAP's power-up screens in their order (panel off,
    /// FAP power-up, CIDS power-up), then ready only when all three flags are known true.
    /// </summary>
    /// <param name="serviceAnswering">The data service answered the last read with any known value.</param>
    public static string ConnectionStatus(bool serviceAnswering, FenixCabinData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!serviceAnswering) return "The Fenix is not answering. Load the Fenix and wait for it to start.";

        bool? fapOperational = data.Bool(FenixCabinNames.FapOperational);
        bool? fapReady = data.Bool(FenixCabinNames.FapReady);
        bool? cidsReady = data.Bool(FenixCabinNames.CidsReady);
        if (fapOperational == false) return "The flight attendant panel is off.";
        if (fapReady == false) return Starting("Flight attendant panel starting", data.Number(FenixCabinNames.FapPowerUp));
        if (cidsReady == false) return Starting("Cabin system starting", data.Number(FenixCabinNames.CidsPowerUp));
        if (fapOperational == true && fapReady == true && cidsReady == true) return "Cabin system ready.";
        return "Cabin system: no data.";
    }

    private static string Starting(string label, double? progress)
        => progress is double percent ? $"{label}: {Percent(percent)}." : label + ".";

    // ---- Lines ----------------------------------------------------------------------------------

    /// <summary>Every line of the five tabs, tab by tab in <see cref="FenixCabinSection"/> order.</summary>
    /// <param name="pram">The FAP audio state, or null when it has not been read.</param>
    public static IReadOnlyList<FenixCabinLine> Build(FenixCabinData data, FenixCabinPramState? pram, FenixCabinLayout layout)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(layout);

        var lines = new List<FenixCabinLine>();
        AddStatus(lines, data, pram, layout);
        AddAudio(lines, data, pram);
        AddDoors(lines, data, layout);
        AddTemperature(lines, data);
        AddWater(lines, data, layout);
        return lines.AsReadOnly();
    }

    private static void AddStatus(List<FenixCabinLine> lines, FenixCabinData data, FenixCabinPramState? pram, FenixCabinLayout layout)
    {
        void Add(string key, string text) => lines.Add(new FenixCabinLine(FenixCabinSection.Status, key, text));

        Add("status.cabinReady", "Cabin ready: " + CabinReadyState(data));
        Add("status.lights", "Cabin lights: " + LightsState(data));
        Add("status.music", "Boarding music: " + MusicSummary(pram));
        Add("status.announcing", "Announcement playing: "
            + (pram == null ? NoData : string.IsNullOrEmpty(pram.NowAnnouncing) ? "none" : pram.NowAnnouncing));
        Add("status.doors", DoorsSummary(data, layout));
        Add("status.slides", SlidesSummary(data, layout));
        Add("status.cargo", CargoSummary(data, layout));
        Add("status.temperature", TemperatureSummary(data));
        Add("status.water", WaterSummary(data));
        Add("status.cautions", CautionsSummary(data, layout));
    }

    private static string CabinReadyState(FenixCabinData data) => data.Bool(FenixCabinNames.CabinReady) switch
    {
        true => "yes",
        false when data.Bool(FenixCabinNames.CabinReadyAvailable) == false => "no, not available yet",
        false => "no",
        null => NoData
    };

    // The Fenix's own level name, spoken as the FAP labels it ("Dim 1"); another text is the Fenix's own word.
    private static string LightsState(FenixCabinData data)
    {
        string? wire = data.Text(FenixCabinNames.CabinLights);
        if (wire == null) return NoData;
        return FenixCabinWrites.ParseLight(wire) is FenixCabinLightLevel level ? FenixCabinWrites.LightName(level) : wire;
    }

    private static string MusicSummary(FenixCabinPramState? pram)
    {
        if (pram == null) return NoData;
        if (!pram.MusicOn) return "off";
        var parts = new List<string> { "on" };
        if (SelectedChannel(pram) is FenixCabinItem channel && channel.Name.Length > 0) parts.Add(channel.Name);
        parts.Add("volume " + pram.Volume.ToString(Inv));
        return string.Join(", ", parts);
    }

    private static FenixCabinItem? SelectedChannel(FenixCabinPramState pram)
        => pram.SelectedChannel >= 0 && pram.SelectedChannel < pram.Channels.Count ? pram.Channels[pram.SelectedChannel] : null;

    private static string DoorsSummary(FenixCabinData data, FenixCabinLayout layout)
    {
        var open = new List<string>();
        bool allKnown = layout.MainDoors.Count > 0;
        foreach (string door in layout.MainDoors)
        {
            bool? isOpen = data.Bool(FenixCabinNames.Door(FenixCabinLayout.DataKey(door)));
            if (isOpen == true) open.Add(ShortName(door));
            else if (isOpen == null) allKnown = false;
        }
        if (open.Count > 0) return "Doors open: " + string.Join(", ", open);
        return allKnown ? "Doors: all closed" : "Doors: no data";
    }

    private static string SlidesSummary(FenixCabinData data, FenixCabinLayout layout)
    {
        var disarmed = new List<string>();
        bool allKnown = layout.Slides.Count > 0;
        foreach (string slide in layout.Slides)
        {
            bool? armed = data.Bool(FenixCabinNames.Slide(FenixCabinLayout.DataKey(slide)));
            if (armed == false) disarmed.Add(ShortName(slide));
            else if (armed == null) allKnown = false;
        }
        if (disarmed.Count > 0) return "Slides disarmed: " + string.Join(", ", disarmed);
        return allKnown ? "Slides: all armed" : "Slides: no data";
    }

    // Each cargo door by the same rule as its own line; the summary names only those not closed.
    private static string CargoSummary(FenixCabinData data, FenixCabinLayout layout)
    {
        var notClosed = new List<string>();
        bool allKnown = true;
        foreach (FenixCabinCargoDoor door in CargoDoorsOf(layout))
        {
            string? state = CargoState(data, door);
            if (state == null) allKnown = false;
            else if (state != "closed") notClosed.Add($"{door.SummaryName} {state}");
        }
        if (notClosed.Count > 0) return "Cargo doors: " + string.Join(", ", notClosed);
        return allKnown ? "Cargo doors: all closed" : "Cargo doors: no data";
    }

    private static string TemperatureSummary(FenixCabinData data)
    {
        double? forward = data.Number(FenixCabinNames.TempFwdAir);
        double? aft = data.Number(FenixCabinNames.TempAftAir);
        if (forward is double f && aft is double a) return $"Cabin temperature: forward {Degrees(f)}, aft {Degrees(a)} degrees";
        if (forward == null && aft == null) return "Cabin temperature: " + NoData;
        return $"Cabin temperature: forward {DegreesOrNoData(forward)}, aft {DegreesOrNoData(aft)}";
    }

    private static string WaterSummary(FenixCabinData data)
    {
        double? water = data.Number(FenixCabinNames.WaterTank);
        double? waste = data.Number(FenixCabinNames.WasteTank);
        if (water == null && waste == null) return "Water and waste: " + NoData;
        return $"Water {PercentOrNoData(water)}, waste {PercentOrNoData(waste)}";
    }

    /// <summary>
    /// What the Status page's "Cautions:" line lists, in its order: the water and waste cautions
    /// (<see cref="Cautions"/>), then each main door's pressure message ("Door 4 right: check slide pressure").
    /// Empty only when nothing fires and at least one caution input is known; null when no caution input
    /// is known, which the line reads as "no data". The app-wide cabin cautions say the same items.
    /// </summary>
    public static IReadOnlyList<string>? StatusCautions(FenixCabinData data, FenixCabinLayout layout)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(layout);

        var cautions = new List<string>(Cautions(data, layout));
        foreach (string door in layout.MainDoors)
        {
            if (DoorPressureMessage(data, door) is string message)
                cautions.Add($"{DoorName(door)}: {message}");
        }
        if (cautions.Count == 0 && !AnyCautionInputKnown(data, layout)) return null;
        return cautions.AsReadOnly();
    }

    // "none" needs proof too: nothing fires and at least one caution input is known; otherwise "no data".
    private static string CautionsSummary(FenixCabinData data, FenixCabinLayout layout)
        => StatusCautions(data, layout) switch
        {
            null => "Cautions: " + NoData,
            { Count: 0 } => "Cautions: none",
            var cautions => "Cautions: " + string.Join("; ", cautions)
        };

    // A value some caution reads, known in its own kind: the water and waste tanks, waste tank full,
    // the vacuum system and generator, either service panel, a flush of the layout's lavatories, or a
    // listed main door's door or slide pressure.
    private static bool AnyCautionInputKnown(FenixCabinData data, FenixCabinLayout layout)
    {
        string[] numbers = { FenixCabinNames.WaterTank, FenixCabinNames.WasteTank };
        string[] flags =
        {
            FenixCabinNames.WasteTankFull, FenixCabinNames.VacuumSystemPowered, FenixCabinNames.VacuumGeneratorOperational,
            FenixCabinNames.WaterServicePanelOpen, FenixCabinNames.WasteServicePanelOpen
        };
        IEnumerable<string> pressures = layout.MainDoors
            .Select(FenixCabinLayout.DataKey)
            .SelectMany(key => new[] { FenixCabinNames.DoorPressure(key), FenixCabinNames.SlidePressure(key) });

        return numbers.Concat(pressures).Any(name => data.Number(name) != null)
            || flags.Concat(layout.Lavatories.Select(FenixCabinNames.LavatoryFlush)).Any(name => data.Bool(name) != null);
    }

    private static void AddAudio(List<FenixCabinLine> lines, FenixCabinData data, FenixCabinPramState? pram)
    {
        void Add(string key, string text) => lines.Add(new FenixCabinLine(FenixCabinSection.Audio, key, text));

        if (pram == null)
        {
            Add("audio.unavailable", "Audio: no data from the Fenix");
        }
        else
        {
            Add("audio.music", "Boarding music: " + (pram.MusicOn ? "on" : "off"));
            Add("audio.channel", "Music channel: " + ChannelState(pram));
            Add("audio.volume", "Volume: " + pram.Volume.ToString(Inv));
            Add("audio.announcing", "Now announcing: " + (string.IsNullOrEmpty(pram.NowAnnouncing) ? "nothing" : pram.NowAnnouncing));
            Add("audio.memo", "Memo: " + (pram.Memo.Count == 0 ? "empty" : string.Join(", ", pram.Memo.Select(item => item.Name))));
            Add("audio.playAll", "Playing all memo announcements: " + (pram.PlayingAllInMemo ? "yes" : "no"));
            if (pram.MaxVariant > 0)
                Add("audio.set", $"Announcement set: {pram.PreferredVariant.ToString(Inv)} (0 to {pram.MaxVariant.ToString(Inv)})");
        }

        // From the data, not the pram state, so it is there when the pram state is not.
        double? speaker = data.Number(FenixCabinNames.FapSpeakerVolume);
        Add("audio.speakerVolume", "FAP loudspeaker volume: "
            + (speaker is double fraction
                ? FenixCabinWrites.ShownSpeakerPercent(fraction).ToString(Inv) + " percent"
                : NoData));
    }

    private static string ChannelState(FenixCabinPramState pram)
    {
        if (pram.Channels.Count == 0) return "none";
        if (SelectedChannel(pram) is not FenixCabinItem channel) return NoData;
        string position = $"{(pram.SelectedChannel + 1).ToString(Inv)} of {pram.Channels.Count.ToString(Inv)}";
        return channel.Name.Length > 0 ? $"{channel.Name}, {position}" : position;
    }

    // The FAP's slide order (front to back, the overwing slides between the doors), then any main door
    // the slide list lacks, then the cargo doors.
    private static void AddDoors(List<FenixCabinLine> lines, FenixCabinData data, FenixCabinLayout layout)
    {
        void Add(string key, string text, string? state = null)
            => lines.Add(new FenixCabinLine(FenixCabinSection.Doors, key, text, state));
        void AddDoor(string door)
            => Add(DoorLineKey(door), DoorLine(data, layout, door), DoorState(data, door));

        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string slide in layout.Slides)
        {
            if (TryOverwing(slide, out _))
                Add("slide." + FenixCabinLayout.DataKey(slide), $"{DoorName(slide)}: {ArmedState(data, slide)}");
            else if (layout.MainDoors.Contains(slide) && listed.Add(slide))
                AddDoor(slide);
        }
        foreach (string door in layout.MainDoors)
        {
            if (listed.Add(door))
                AddDoor(door);
        }
        foreach (FenixCabinCargoDoor door in CargoDoorsOf(layout))
        {
            string? state = CargoState(data, door);
            // A moving door has not settled: no state until it is open or closed.
            Add(door.Key, $"{door.Name}: {state ?? NoData}", state is "open" or "closed" ? state : null);
        }
    }

    /// <summary>A main door's Doors line key by its FAP designation: <c>1L</c> is <c>door.d1l</c>.</summary>
    public static string DoorLineKey(string designation) => "door." + FenixCabinLayout.DataKey(designation);

    // A main door's own word, the part of its line a door press changes; null when unknown.
    private static string? DoorState(FenixCabinData data, string door)
        => data.Bool(FenixCabinNames.Door(FenixCabinLayout.DataKey(door))) switch
        {
            true => "open",
            false => "closed",
            null => null
        };

    private static string ArmedState(FenixCabinData data, string slide)
        => data.Bool(FenixCabinNames.Slide(FenixCabinLayout.DataKey(slide))) switch
        {
            true => "armed",
            false => "disarmed",
            null => NoData
        };

    private static string DoorLine(FenixCabinData data, FenixCabinLayout layout, string door)
    {
        string key = FenixCabinLayout.DataKey(door);
        bool? open = data.Bool(FenixCabinNames.Door(key));
        bool? armed = data.Bool(FenixCabinNames.Slide(key));

        var parts = new List<string>();
        if (open == null && armed == null)
        {
            parts.Add("no status");
        }
        else
        {
            parts.Add(open switch { true => "open", false => "closed", null => NoData });
            parts.Add(armed switch
            {
                true => "slide armed",
                false when open == false && layout.SlideInPark => "slide in park",
                false => "slide disarmed",
                null => "slide " + NoData
            });
        }
        if (DoorPressureMessage(data, door) is string message) parts.Add(message);
        return $"{DoorName(door)}: {string.Join(", ", parts)}";
    }

    private static string? DoorPressureMessage(FenixCabinData data, string door)
    {
        string key = FenixCabinLayout.DataKey(door);
        return PressureMessage(data.Number(FenixCabinNames.DoorPressure(key)), data.Number(FenixCabinNames.SlidePressure(key)));
    }

    /// <summary>
    /// The cargo doors, forward, aft and bulk: the one table the lines and the Cabin window's Doors button
    /// both read, so a line key or flag name cannot differ between them. A layout lists the bulk door only
    /// when the airframe has one (<see cref="FenixCabinLayout.HasBulkCargo"/>).
    /// </summary>
    public static IReadOnlyList<FenixCabinCargoDoor> CargoDoors { get; } = Array.AsReadOnly(new FenixCabinCargoDoor[]
    {
        new("cargo.forward", "Forward cargo door", "forward", FenixCargoDoor.Forward, FenixCabinNames.CargoForward, FenixCabinNames.CargoFwdMoving),
        new("cargo.aft", "Aft cargo door", "aft", FenixCargoDoor.Aft, FenixCabinNames.CargoAft, FenixCabinNames.CargoAftMoving),
        new("cargo.bulk", "Bulk cargo door", "bulk", FenixCargoDoor.Bulk, FenixCabinNames.CargoBulk, null)
    });

    /// <summary>The cargo doors the layout's airframe has, in <see cref="CargoDoors"/> order: the bulk one only when it has one.</summary>
    public static IEnumerable<FenixCabinCargoDoor> CargoDoorsOf(FenixCabinLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return CargoDoors.Where(door => door.Door != FenixCargoDoor.Bulk || layout.HasBulkCargo);
    }

    // "moving" while the EFB's movement flag is true (the bulk door has none), else open or closed; null when unknown.
    private static string? CargoState(FenixCabinData data, FenixCabinCargoDoor door)
    {
        if (door.MovingFlag != null && data.Bool(door.MovingFlag) == true) return "moving";
        return data.Bool(door.OpenFlag) switch { true => "open", false => "closed", null => null };
    }

    /// <summary>
    /// A cargo door's state once it has settled, by the same rule as its line: true open, false closed; null
    /// while the EFB's movement flag says it moves, and when its open flag is not known ([FNX-1]).
    /// </summary>
    public static bool? SettledCargoOpen(FenixCabinData data, FenixCabinCargoDoor door)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(door);
        return CargoState(data, door) switch { "open" => true, "closed" => false, _ => null };
    }

    /// <summary>
    /// True when the Fenix says temperature control is not available: the zone controller, the lavatory and
    /// galley extraction fan or the CIDS is known not to run (the FAP's <c>dataAvailable$</c> needs all
    /// three). An unknown flag is not "unavailable" ([FNX-1]); the temperature line and the Cabin window's
    /// temperature buttons share this one rule.
    /// </summary>
    public static bool TemperatureUnavailable(FenixCabinData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Bool(FenixCabinNames.ZoneControllerOperational) == false
            || data.Bool(FenixCabinNames.LavGalleyFanRunning) == false
            || data.Bool(FenixCabinNames.CidsOperational) == false;
    }

    private static void AddTemperature(List<FenixCabinLine> lines, FenixCabinData data)
    {
        void Add(string key, string text, string? state = null)
            => lines.Add(new FenixCabinLine(FenixCabinSection.Temperature, key, text, state));

        if (TemperatureUnavailable(data)) Add("temp.available", "Temperature control: not available");

        Add("temp.forward", "Forward zone: "
                + ZoneState(data, FenixCabinNames.TempFwdAir, FenixCabinNames.TempFwdSelected, FenixCabinNames.TempFwdOffset),
            ZoneSetting(data, FenixCabinNames.TempFwdSelected, FenixCabinNames.TempFwdOffset));
        Add("temp.aft", "Aft zone: "
                + ZoneState(data, FenixCabinNames.TempAftAir, FenixCabinNames.TempAftSelected, FenixCabinNames.TempAftOffset),
            ZoneSetting(data, FenixCabinNames.TempAftSelected, FenixCabinNames.TempAftOffset));
    }

    // The part of a zone's line a temperature press changes: the target and the offset, invariant
    // ("24.5|0.5"), never the measured air temperature; null when either is unknown.
    private static string? ZoneSetting(FenixCabinData data, string selectedName, string offsetName)
        => data.Number(selectedName) is double selected && data.Number(offsetName) is double offset
            ? Degrees(selected + offset) + "|" + Degrees(offset)
            : null;

    private static string ZoneState(FenixCabinData data, string airName, string selectedName, string offsetName)
    {
        double? air = data.Number(airName);
        double? selected = data.Number(selectedName);
        double? offset = data.Number(offsetName);
        if (air == null && selected == null && offset == null) return NoData;

        string target = selected is double s && offset is double o ? $"target {Degrees(s + o)} degrees" : "target " + NoData;
        string offsetText = offset is double value ? Offset(value) : "offset " + NoData;
        return $"{DegreesOrNoData(air)}, {target}, {offsetText}";
    }

    private static void AddWater(List<FenixCabinLine> lines, FenixCabinData data, FenixCabinLayout layout)
    {
        void Add(string key, string text) => lines.Add(new FenixCabinLine(FenixCabinSection.Water, key, text));

        Add("water.water", "Water: " + PercentOrNoData(data.Number(FenixCabinNames.WaterTank)));
        Add("water.preselect", "Water preselect: " + PercentOrNoData(data.Number(FenixCabinNames.WaterPreselect)));
        Add("water.waste", "Waste: " + PercentOrNoData(data.Number(FenixCabinNames.WasteTank)));
        Add("water.wasteFull", "Waste tank full: " + data.Bool(FenixCabinNames.WasteTankFull) switch
        {
            true => "yes",
            false => "no",
            null => NoData
        });
        Add("water.waterPanel", "Water service panel: " + OpenState(data.Bool(FenixCabinNames.WaterServicePanelOpen)));
        Add("water.wastePanel", "Waste service panel: " + OpenState(data.Bool(FenixCabinNames.WasteServicePanelOpen)));
        Add("water.vacuum", "Vacuum system: " + VacuumState(data));

        foreach (string letter in layout.Lavatories)
        {
            Add("water.lav." + letter, $"Lavatory {letter}: " + data.Bool(FenixCabinNames.LavatoryFlush(letter)) switch
            {
                true => "flush operational",
                false => "flush not operational",
                null => NoData
            });
        }

        IReadOnlyList<string> cautions = Cautions(data, layout);
        for (int i = 0; i < cautions.Count; i++)
            Add("water.caution." + (i + 1).ToString(Inv), "Caution: " + cautions[i]);
    }

    private static string VacuumState(FenixCabinData data)
    {
        bool? powered = data.Bool(FenixCabinNames.VacuumSystemPowered);
        bool? generator = data.Bool(FenixCabinNames.VacuumGeneratorOperational);
        if (powered == null && generator == null) return NoData;

        string poweredText = powered switch { true => "powered", false => "not powered", null => NoData };
        string generatorText = generator switch
        {
            true => "generator operational",
            false => "generator not operational",
            null => "generator " + NoData
        };
        return $"{poweredText}, {generatorText}";
    }

    private static string OpenState(bool? open) => open switch { true => "open", false => "closed", null => NoData };

    private static string DegreesOrNoData(double? celsius) => celsius is double value ? Degrees(value) + " degrees" : NoData;

    private static string PercentOrNoData(double? value) => value is double percent ? Percent(percent) : NoData;
}

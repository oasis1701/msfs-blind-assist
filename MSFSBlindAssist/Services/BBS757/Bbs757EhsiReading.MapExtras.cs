namespace MSFSBlindAssist.Services.BBS757;

/// <summary>A TERR elevation's color as the screen draws it; <see cref="None"/> where the reader cannot tell (the gear input
/// not delivered yet and the number between the two amber thresholds, or no air data computer).</summary>
public enum Bbs757EhsiTerrainColor { None, Red, Amber, Green }

/// <summary>One TERR elevation: the number as drawn (feet ÷ the layout's divisor, never below 0; three digits on the
/// screen) and its color.</summary>
public readonly record struct Bbs757EhsiTerrainNumber(int Shown, Bbs757EhsiTerrainColor Color);

/// <summary>One runway the map draws: its name as stored (no "RW"), its selected threshold (end 1) and its far end.</summary>
public readonly record struct Bbs757EhsiRunway(string Name, double FirstLatitude, double FirstLongitude,
    double SecondLatitude, double SecondLongitude);

/// <summary>A point the map draws with no label of its own (a deceleration ring).</summary>
public readonly record struct Bbs757EhsiMapPoint(double Latitude, double Longitude);

/// <summary>
/// The rest of the map (plan 28's map extras) as the nd draws it. Light, in every reading: the TERR button
/// (<see cref="Terr"/>, its L:var's cached value non-zero), the terrain picture's byte, the two elevations with their
/// colors (none while the button is off: the adc is not read then); the wind's show flag as BlueBird left it (BlueBird's
/// draw clears it under its minimum ground speed, so it alone decides) and the two wind inputs (null: not delivered yet).
/// Full, in MAP and PLAN only (null otherwise): the departure and arrival
/// runways (an empty name draws none, so is not in the list) and the deceleration points: where the next deceleration
/// starts and where it ends. The words are the layout's.
/// </summary>
public sealed record Bbs757EhsiMapExtrasReading(
    bool Terr,
    bool TerrainPicture,
    Bbs757EhsiTerrainNumber Highest,
    Bbs757EhsiTerrainNumber Lowest,
    bool WindShown,
    double? WindSpeed,
    double? WindDirection,
    IReadOnlyList<Bbs757EhsiRunway>? Runways,
    IReadOnlyList<Bbs757EhsiMapPoint>? DecelerationStarts,
    IReadOnlyList<Bbs757EhsiMapPoint>? DecelerationEnds,
    string TerrWord,
    string NoWindWord);

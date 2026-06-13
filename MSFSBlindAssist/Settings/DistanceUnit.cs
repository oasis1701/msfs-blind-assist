namespace MSFSBlindAssist.Settings;

/// <summary>Unit for user-facing horizontal/ground distance readouts.</summary>
public enum DistanceUnit
{
    Metres = 0, // default — ICAO / rest-of-world, matches GSX
    Feet = 1,   // North America / FAA
}

// MSFSBlindAssist/Aircraft/C172/Cessna172Magnetos.cs
namespace MSFSBlindAssist.Aircraft.C172;

/// <summary>
/// The C172's key switch. The sim exposes it as three bools — RECIP ENG LEFT MAGNETO:1,
/// RECIP ENG RIGHT MAGNETO:1 and GENERAL ENG STARTER:1 — and takes it back as ONE event,
/// MAGNETO1_SET with 0 Off / 1 Right / 2 Left / 3 Both / 4 Start. The position numbering
/// here IS that parameter, so a combo pick's key goes on the wire unchanged.
///
/// START is deliberately not selectable: it is momentary, and the Start Engine button's state
/// machine (<see cref="Cessna172EngineStart"/>) owns the walk to START and back to BOTH.
/// </summary>
public static class Cessna172Magnetos
{
    public const int Off = 0;
    public const int Right = 1;
    public const int Left = 2;
    public const int Both = 3;
    public const int Start = 4;

    /// <summary>The event name a position is written with.</summary>
    public const string SetEvent = "MAGNETO1_SET";

    /// <summary>The four positions a pilot may pick from the panel combo, keyed on the event parameter.</summary>
    public static readonly IReadOnlyDictionary<double, string> SelectablePositions = new Dictionary<double, string>
    {
        [Off] = "Off",
        [Right] = "Right",
        [Left] = "Left",
        [Both] = "Both",
    };

    /// <summary>One position from the three sim bools. An engaged starter outranks the magnetos.</summary>
    public static int Position(bool leftMagneto, bool rightMagneto, bool starter)
    {
        if (starter) return Start;
        return (leftMagneto, rightMagneto) switch
        {
            (false, false) => Off,
            (false, true) => Right,
            (true, false) => Left,
            _ => Both,
        };
    }

    /// <summary>Spoken/display text for a position.</summary>
    public static string Text(int position) => position switch
    {
        Off => "Off",
        Right => "Right",
        Left => "Left",
        Both => "Both",
        Start => "Start",
        _ => "Unknown",
    };

    public static bool IsSelectable(double value) => SelectablePositions.ContainsKey(value);
}

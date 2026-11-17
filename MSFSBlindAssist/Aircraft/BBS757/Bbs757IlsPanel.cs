using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>The ILS control panel's two knobs: the whole megahertz (with PARK) and the fraction.</summary>
public enum Bbs757IlsKnob { Whole, Fraction }

/// <summary>
/// The ILS control panel as the app counts it. <paramref name="Whole"/> is the whole-megahertz knob's place
/// on its ring of five: 0 to 3 are 108 to 111, <see cref="Park"/> (4) is PARK. <paramref name="Fraction"/> is
/// the fraction knob's place on its ring of ten ILS channels, 0 to 9 for .10 to .95
/// (<see cref="FractionHundredths"/>); PARK keeps it (measured: the panel remembers the fraction while parked).
/// </summary>
public readonly record struct Bbs757IlsDial(int Whole, int Fraction)
{
    /// <summary>The whole knob's PARK, between 111 and 108 on its ring.</summary>
    public const int Park = 4;

    /// <summary>The fraction knob's ten channels in hundredths of a megahertz, in the order +1 turns them (measured).</summary>
    public static IReadOnlyList<int> FractionHundredths { get; } = new[] { 10, 15, 30, 35, 50, 55, 70, 75, 90, 95 };

    public int Whole { get; } = Whole is >= 0 and <= Park
        ? Whole
        : throw new ArgumentOutOfRangeException(nameof(Whole), Whole, "The whole knob has 108 to 111 (0 to 3) and PARK (4).");

    public int Fraction { get; } = Fraction >= 0 && Fraction < 10
        ? Fraction
        : throw new ArgumentOutOfRangeException(nameof(Fraction), Fraction, "The fraction knob has ten channels, .10 to .95 (0 to 9).");

    /// <summary>
    /// The panel as a flight loads it: PARK, remembering .10 (the owner's flight, in its starting panel
    /// state; the first step proved the fraction: PARK -1 gave 111.10). Other start types (on a runway, in
    /// the air, a saved flight) are unmeasured.
    /// </summary>
    public static Bbs757IlsDial LoadState { get; } = new(Park, 0);

    public bool IsParked => Whole == Park;

    /// <summary>The frequency this dial stands for, 108.10 to 111.95; null when parked.</summary>
    public double? Mhz => IsParked ? null : (10800 + 100 * Whole + FractionHundredths[Fraction]) / 100.0;
}

/// <summary>
/// What the ILS control panel's display shows: PARK (<paramref name="Parked"/>, the display blank and
/// <paramref name="Mhz"/> null; the panel remembers the fraction it hides), or a frequency on an ILS channel
/// (<paramref name="Mhz"/>, 108.10 to 111.95), and the course in whole degrees (<paramref name="Course"/>;
/// null when the variable did not give one).
/// </summary>
public sealed record Bbs757IlsReading(bool Parked, double? Mhz, int? Course);

/// <summary>
/// The 757's ILS control panel (aft pedestal), which the FMC does not tune and no instrument view shows:
/// the knob model, the clicks that reach a frequency or PARK, and the words. Pure; BBS757Definition.Radios.cs
/// keeps the count and turns the knobs.
///
/// Measured on the ground on 2026-10-26 (docs/b757.md, "The ILS control panel"):
///   • No variable holds the frequency. The knobs are step requests, <c>L:B757_ILS_FREQ_WHOLE_INC</c> and
///     <c>L:B757_ILS_FREQ_FRAC_INC</c>, written +1 or -1 and set back to 0 by BlueBird within a frame.
///   • The whole knob is a ring of five, 108, 109, 110, 111, PARK, then 108 again (+1), the reverse for -1,
///     with no end stop. It keeps the fraction.
///   • The fraction knob steps through the ILS channels only, .10 .15 .30 .35 .50 .55 .70 .75 .90 .95 (+1
///     goes right), .95 to .10 with no carry into the megahertz. While parked it does nothing, and PARK
///     remembers the fraction.
///   • A write of 2 moves one channel: one write is one click. Two writes landing in one frame count once.
/// The panel's display is read from BlueBird's own variable, <c>L:BBS_757_ILS_FREQ</c> (-1 while parked, the
/// panel's frequency otherwise; added by BlueBird's 2026-10-31 update, not in its public table; measured
/// 2026-11-05 in cruise, [B757-10]): a set reads it, clicks and reads it again, and what it says is
/// what the display shows (<see cref="ReadingFrom"/>). When the display cannot be read the app
/// counts the clicks it sends from a state it knows (<see cref="Bbs757IlsDial.LoadState"/>, or the last
/// reading) and its words say it went by the count (<see cref="ByCount"/>, <see cref="CountPhrase"/>).
/// </summary>
public static class Bbs757IlsPanel
{
    public const string WholeVar = "B757_ILS_FREQ_WHOLE_INC", FractionVar = "B757_ILS_FREQ_FRAC_INC";

    /// <summary>What an empty ILS frequency box says.</summary>
    public const string EmptyMessage = "Type a frequency first.";

    /// <summary>What a typed frequency that is not an ILS channel says.</summary>
    public const string InvalidMessage = "ILS frequency must be 108.10 to 111.95, on an ILS channel.";

    /// <summary>A set refused because no calculator write can land.</summary>
    public const string UnavailablePhrase = "ILS unavailable";

    /// <summary>
    /// A set refused because the app does not know the panel's state: it has no count (it has not seen the
    /// flight load) and the display could not be read.
    /// </summary>
    public const string UnknownRefusal = "ILS tuning unknown: the ILS display could not be read.";

    /// <summary>A set refused because another is still turning the knobs.</summary>
    public const string InProgressPhrase = "ILS tuning in progress";

    private const int WholePlaces = 5;
    private const int FractionPlaces = 10;

    /// <summary>
    /// A typed ILS frequency, read on any Windows culture (<see cref="NumberParsing.TryParseTyped"/>): digits
    /// and one "." or "," only, 108.10 to 111.95 on an ILS channel ("110.5" is 110.50, "110,55" 110.55). An
    /// empty box says <see cref="EmptyMessage"/>; anything else refused says <see cref="InvalidMessage"/>.
    /// </summary>
    public static bool TryParseIls(string? text, out double mhz, out string error)
    {
        mhz = 0;
        error = "";
        string typed = (text ?? "").Trim();
        if (typed.Length == 0)
        {
            error = EmptyMessage;
            return false;
        }
        if (!typed.All(c => char.IsAsciiDigit(c) || c is '.' or ',')
            || !NumberParsing.TryParseTyped(typed, out double value)
            || !TryDialFor(value, out var dial))
        {
            error = InvalidMessage;
            return false;
        }
        mhz = dial.Mhz!.Value;
        return true;
    }

    /// <summary>
    /// The panel showing <paramref name="mhz"/>: true only for an ILS channel, 108.10 to 111.95, a whole
    /// number of hundredths ending in one of <see cref="Bbs757IlsDial.FractionHundredths"/>.
    /// </summary>
    public static bool TryDialFor(double mhz, out Bbs757IlsDial dial)
    {
        dial = default;
        if (!double.IsFinite(mhz)) return false;
        double scaled = mhz * 100.0;
        double hundredths = Math.Round(scaled);
        if (Math.Abs(scaled - hundredths) > 1e-6 || hundredths < 10800 || hundredths >= 11200) return false;

        int h = (int)hundredths;
        int fraction = IndexOf(Bbs757IlsDial.FractionHundredths, h % 100);
        if (fraction < 0) return false;
        dial = new Bbs757IlsDial(h / 100 - 108, fraction);
        return true;
    }

    /// <summary>
    /// One click of a knob, by the measured model: the whole knob moves round its ring of five through PARK
    /// and keeps the fraction; the fraction knob moves round its ring of ten with no carry, and does nothing
    /// while parked. <paramref name="direction"/> is +1 or -1 (a write of 2 still moves one channel).
    /// </summary>
    public static Bbs757IlsDial Step(Bbs757IlsDial dial, Bbs757IlsKnob knob, int direction)
    {
        int d = Click(direction);
        return knob switch
        {
            Bbs757IlsKnob.Whole => new Bbs757IlsDial(Ring(dial.Whole + d, WholePlaces), dial.Fraction),
            Bbs757IlsKnob.Fraction => dial.IsParked ? dial : new Bbs757IlsDial(dial.Whole, Ring(dial.Fraction + d, FractionPlaces)),
            _ => throw new ArgumentOutOfRangeException(nameof(knob), knob, "The ILS panel has a whole and a fraction knob."),
        };
    }

    /// <summary>
    /// The clicks, one per write, from <paramref name="from"/> to <paramref name="to"/> (null, or a parked
    /// dial: PARK, whatever fraction it names). The fewest by the model, with one rule: the ILS is parked only
    /// where the plan starts or ends, never on the way.
    ///   • To PARK: the whole knob only, the shorter way round its ring of five (never a tie); PARK keeps the
    ///     fraction. Parked already: no click.
    ///   • Out of PARK: the whole knob first, the shorter way (the fraction knob does nothing until then),
    ///     then the fraction the shorter way round its ring of ten.
    ///   • Between two frequencies: the fraction first, the shorter way, then the whole knob straight to the
    ///     megahertz, never through PARK. The way round through PARK is never shorter by more than one click
    ///     (108 to 111: two against three), and it would park the ILS for a moment, which the manual warns can
    ///     raise an RW/ILS alert while approach guidance is selected (Complete Flight Guide, p. 192).
    /// A tie round the fraction ring (five clicks either way) goes up (+1).
    /// </summary>
    public static IReadOnlyList<(Bbs757IlsKnob Knob, int Direction)> PlanSteps(Bbs757IlsDial from, Bbs757IlsDial? to)
    {
        var plan = new List<(Bbs757IlsKnob Knob, int Direction)>();
        if (to is not { IsParked: false } target)
        {
            if (!from.IsParked) Add(plan, Bbs757IlsKnob.Whole, Shorter(from.Whole, Bbs757IlsDial.Park, WholePlaces));
            return plan;
        }

        var fraction = Shorter(from.Fraction, target.Fraction, FractionPlaces);
        if (from.IsParked)
        {
            Add(plan, Bbs757IlsKnob.Whole, Shorter(Bbs757IlsDial.Park, target.Whole, WholePlaces));
            Add(plan, Bbs757IlsKnob.Fraction, fraction);
        }
        else
        {
            Add(plan, Bbs757IlsKnob.Fraction, fraction);
            Add(plan, Bbs757IlsKnob.Whole, (Math.Sign(target.Whole - from.Whole), Math.Abs(target.Whole - from.Whole)));
        }
        return plan;
    }

    /// <summary>The step-request variable a knob's click is written to.</summary>
    public static string Variable(Bbs757IlsKnob knob) => knob switch
    {
        Bbs757IlsKnob.Whole => WholeVar,
        Bbs757IlsKnob.Fraction => FractionVar,
        _ => throw new ArgumentOutOfRangeException(nameof(knob), knob, "The ILS panel has a whole and a fraction knob."),
    };

    /// <summary>
    /// One click as the calculator write that makes it: "-1 (>L:B757_ILS_FREQ_WHOLE_INC)". No culture in it
    /// (sv-SE would write -1 with a U+2212 minus). Send it through <c>ExecuteCalculatorCodeUnique</c>: a click
    /// repeats the same string, and MobiFlight drops an identical repeat.
    /// </summary>
    public static string StepCode(Bbs757IlsKnob knob, int direction)
        => $"{Click(direction).ToString(CultureInfo.InvariantCulture)} (>L:{Variable(knob)})";

    /// <summary>A frequency as spoken: two decimals and a dot whatever the PC's culture ("110.50").</summary>
    public static string Frequency(double mhz) => mhz.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// The panel as counted: "ILS 110.50", "ILS parked", or "ILS tuning unknown" when the app has no count.
    /// What the count says, never more, and only inside words that say it is the count (<see cref="SetPhrase(Bbs757IlsDial)"/>
    /// with <see cref="ByCount"/>, <see cref="CountPhrase"/>); a read is said through <see cref="ReadPhrase"/>.
    /// </summary>
    public static string Phrase(Bbs757IlsDial? dial)
    {
        if (dial is not Bbs757IlsDial d) return "ILS tuning unknown";
        return d.Mhz is double mhz ? $"ILS {Frequency(mhz)}" : "ILS parked";
    }

    /// <summary>
    /// The count as output N and the Ctrl+N window's status say it when the display cannot be read: "ILS 110.50
    /// by count", "ILS parked by count", or "ILS tuning unknown" when the app has no count. The qualifier keeps
    /// the count from sounding like a reading (<see cref="ReadPhrase"/>: "ILS 110.50"), which it is not: it is
    /// the app's own tally of the clicks it sent.
    /// </summary>
    public static string CountPhrase(Bbs757IlsDial? dial) => dial is null ? Phrase(null) : $"{Phrase(dial)} by count";

    /// <summary>
    /// A set that stopped (a read the sim did not answer, a write that failed): "ILS unavailable" before any
    /// click went out, else how far it got, the clicks sent and the clicks planned: "ILS unavailable after 3 of 5
    /// clicks." (one click planned: "after 1 of 1 click.").
    /// </summary>
    public static string UnavailableAfterPhrase(int sent, int planned)
    {
        if (sent <= 0) return UnavailablePhrase;
        string clicks = planned == 1 ? "click" : "clicks";
        return $"{UnavailablePhrase} after {sent.ToString(CultureInfo.InvariantCulture)} of {planned.ToString(CultureInfo.InvariantCulture)} {clicks}.";
    }

    /// <summary>
    /// The display could not be read after the click that took the ILS out of PARK (the learning click): the
    /// panel is no longer parked, so the words say so, "ILS display could not be read: the ILS left PARK, now
    /// 108.75 by count." with the counted frequency, or "ILS display could not be read: the ILS left PARK." when
    /// the count does not know the fraction (<paramref name="count"/> null or parked).
    /// </summary>
    public static string LeftParkUnreadPhrase(Bbs757IlsDial? count)
        => count is { Mhz: double mhz }
            ? $"ILS display could not be read: the ILS left PARK, now {Frequency(mhz)} by count."
            : "ILS display could not be read: the ILS left PARK.";

    /// <summary>A set that sent its clicks: "ILS 110.50 set", "ILS parked".</summary>
    public static string SetPhrase(Bbs757IlsDial dial) => dial.IsParked ? Phrase(dial) : $"{Phrase(dial)} set";

    /// <summary>A set that needed no click: "ILS already 110.50", "ILS already parked".</summary>
    public static string AlreadyPhrase(Bbs757IlsDial dial)
        => dial.Mhz is double mhz ? $"ILS already {Frequency(mhz)}" : "ILS already parked";

    /// <summary>
    /// A set's words when it went by the count alone, the display unreadable: "ILS 110.50 set by count: the ILS
    /// display could not be read."
    /// </summary>
    public static string ByCount(string phrase) => $"{phrase} by count: the ILS display could not be read.";

    // ---- The display (read from BlueBird's L:BBS_757_ILS_FREQ) ---------------------------------------------

    /// <summary>What <c>L:BBS_757_ILS_FREQ</c> holds while the panel is parked.</summary>
    public const int ParkedValue = -1;

    /// <summary>
    /// The display from the frequency variable and the course variable: parked within 0.001 of
    /// <see cref="ParkedValue"/>; an ILS channel (<see cref="TryDialFor"/>) as its frequency; anything else
    /// (not read, 0, NaN, a frequency off the channels) null. A frequency within 0.003 of a hundredth is
    /// rounded to it first (a 32-bit float stores 108.15 a little off). The course is rounded to whole degrees 0 to 359,
    /// else null.
    /// </summary>
    public static Bbs757IlsReading? ReadingFrom(double? frequency, double? course)
    {
        if (frequency is not double f || !double.IsFinite(f)) return null;
        int? shownCourse = course is double c && double.IsFinite(c) && Math.Round(c) is >= 0 and <= 359 ? (int)Math.Round(c) : null;
        if (Math.Abs(f - ParkedValue) <= 0.001) return new Bbs757IlsReading(true, null, shownCourse);
        // A frequency stored as a 32-bit float reads a little off (108.15f is 108.1500015...): round to the
        // hundredth when it is within 0.003 of it; 110.52 stays as it is and fails as no channel.
        double hundredth = Math.Round(f, 2);
        if (Math.Abs(f - hundredth) <= 0.003) f = hundredth;
        return TryDialFor(f, out var dial) ? new Bbs757IlsReading(false, dial.Mhz, shownCourse) : null;
    }

    /// <summary>What the display shows, as said: "ILS 110.50", "ILS parked". Only what the panel shows, never a station received.</summary>
    public static string ReadPhrase(Bbs757IlsReading reading)
        => !reading.Parked && reading.Mhz is double mhz ? $"ILS {Frequency(mhz)}" : "ILS parked";

    /// <summary>A set the display shows done: "ILS 110.50 set", "ILS parked".</summary>
    public static string SetPhrase(Bbs757IlsReading reading) => reading.Parked ? "ILS parked" : $"{ReadPhrase(reading)} set";

    /// <summary>A set the display shows needed no click: "ILS already 110.50", "ILS already parked".</summary>
    public static string AlreadyPhrase(Bbs757IlsReading reading)
        => !reading.Parked && reading.Mhz is double mhz ? $"ILS already {Frequency(mhz)}" : "ILS already parked";

    /// <summary>
    /// A set the display shows short of its target after the second try: "ILS shows 110.55, not 110.50", "ILS
    /// shows parked, not 110.50", "ILS shows 110.55, not parked".
    /// </summary>
    public static string ShowsPhrase(Bbs757IlsReading reading, Bbs757IlsDial? target)
    {
        string shown = !reading.Parked && reading.Mhz is double mhz ? Frequency(mhz) : "parked";
        string wanted = target is { Mhz: double t } ? Frequency(t) : "parked";
        return $"ILS shows {shown}, not {wanted}";
    }

    /// <summary>Whether the display shows <paramref name="target"/> (null, or a parked dial: PARK, whatever fraction it names).</summary>
    public static bool Shows(Bbs757IlsReading reading, Bbs757IlsDial? target)
    {
        if (target is not { IsParked: false } frequency) return reading.Parked;
        return !reading.Parked && reading.Mhz is double mhz && TryDialFor(mhz, out var shown) && shown == frequency;
    }

    /// <summary>
    /// The count a reading gives: a frequency, its whole dial; PARK, the fraction <paramref name="count"/> had
    /// (the display hides it while parked), or null, unknown, when there was no count.
    /// </summary>
    public static Bbs757IlsDial? DialFrom(Bbs757IlsReading reading, Bbs757IlsDial? count)
    {
        if (reading.Parked) return count is Bbs757IlsDial c ? new Bbs757IlsDial(Bbs757IlsDial.Park, c.Fraction) : null;
        return reading.Mhz is double mhz && TryDialFor(mhz, out var dial) ? dial : null;
    }

    // A click is one step either way.
    private static int Click(int direction)
        => direction is 1 or -1
            ? direction
            : throw new ArgumentOutOfRangeException(nameof(direction), direction, "A knob click is +1 or -1: one write is one click.");

    private static int Ring(int place, int places) => ((place % places) + places) % places;

    // The shorter way round a ring from one place to another: up on a tie.
    private static (int Direction, int Clicks) Shorter(int from, int to, int places)
    {
        int up = Ring(to - from, places);
        return up <= places - up ? (+1, up) : (-1, places - up);
    }

    private static void Add(List<(Bbs757IlsKnob Knob, int Direction)> plan, Bbs757IlsKnob knob, (int Direction, int Clicks) turn)
    {
        for (int i = 0; i < turn.Clicks; i++) plan.Add((knob, turn.Direction));
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }
}

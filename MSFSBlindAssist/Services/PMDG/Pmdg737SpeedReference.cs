using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>One turn of the speed reference inner knob: a coarse step either way (a mouse click) or a fine one (a wheel turn).</summary>
public enum SpeedReferenceClick { CoarseUp, CoarseDown, FineUp, FineDown }

/// <summary>
/// The PMDG 737 speed reference inner knob (EVT_MPM_SPEED_REFERENCE_CONTROL), swept live at ground session 2
/// (2026-10-30): a click moves the selected position's value by <see cref="CoarseSteps"/> of its derived step at any
/// spacing (VREF 5 knots, WT 5000), a wheel turn by one step when at least 150 ms apart (faster turns accelerate);
/// PMDG keeps the value in the position's limits, which the layout gives. The gaps leave margin over
/// what was measured, and the settle waits out a reading of the PFD reader (every 500 ms); the setter gives up after
/// <see cref="MaxClicks"/> clicks or <see cref="TimeoutMs"/>.
/// </summary>
public sealed record SpeedReferenceKnob(int CoarseSteps, int MaxClicks, long TimeoutMs, long CoarseGapMs, long FineGapMs, long SettleMs)
{
    public static SpeedReferenceKnob Ng3 { get; } = new(CoarseSteps: 5, MaxClicks: 400, TimeoutMs: 90_000, CoarseGapMs: 150,
        FineGapMs: 250, SettleMs: 1_000);
}

/// <summary>The speed reference value's pure parts: the typed value, the clicks to a target, the read-back.</summary>
public static class Pmdg737SpeedReference
{
    public const string NeedsReader = "Speed reference needs the PFD reader";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A typed value for the selector's <paramref name="position"/> ([CORE-17]): a whole number in that
    /// position's derived limits. A position where the knob sets nothing is refused, naming the ones where it does.</summary>
    public static bool TryParse(string? typed, int position, PfdSpeedReferenceFields f, IReadOnlyList<string> positionNames,
        out double value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(positionNames);
        error = null;
        if (f.Positions.FirstOrDefault(p => p.Position == position) is not { } knob)
        {
            value = 0;
            var names = f.Positions.Select(p => Name(p.Position, positionNames)).ToList();
            error = "Set the speed reference selector to "
                + (names.Count > 1 ? string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1] : string.Concat(names)) + " first";
            return false;
        }
        if (NumberParsing.TryParseTyped(typed, out value))
        {
            value = Math.Round(value, MidpointRounding.AwayFromZero);
            if (value >= knob.Min && value <= knob.Max) return true;
        }
        error = string.Create(Inv, $"Enter a {Name(position, positionNames)} between {knob.Min:0} and {knob.Max:0}");
        return false;
    }

    /// <summary>The coarse clicks and fine turns (signed: up is positive) from <paramref name="current"/> to the nearest
    /// value to <paramref name="target"/> the position's <paramref name="step"/> reaches: whole steps, as many as fit
    /// taken as clicks of <paramref name="coarseSteps"/> steps.</summary>
    public static (int Coarse, int Fine) ClicksTo(double current, double target, double step, int coarseSteps)
    {
        int steps = (int)Math.Round((target - current) / step, MidpointRounding.AwayFromZero);
        int coarse = steps / coarseSteps; // toward zero: never past the target
        return (coarse, steps - coarse * coarseSteps);
    }

    /// <summary>"VREF 142", "WT 147850": the position's name and its value, whole.</summary>
    public static string Readback(int position, double value, IReadOnlyList<string> positionNames) =>
        Name(position, positionNames) + " " + Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", Inv);

    private static string Name(int position, IReadOnlyList<string> names) =>
        position >= 0 && position < names.Count ? names[position] : position.ToString(Inv);
}

/// <summary>
/// Sets the speed reference value by turning the inner knob, closed-loop as Manual N1's: it reads PMDG's own value for
/// the position, clicks the coarse steps toward the target, reads again, turns the fine steps, and repeats until the
/// value is the nearest the position's step reaches. One
/// setting at a time: a new call, or <see cref="Cancel"/> (the window closing), stops the running one, which then
/// returns null. It stops at the knob's click and time limits, after a round that left the value no closer to the
/// target, or without the PFD reader. Clicks and delays come from
/// the caller, so the definition sends them on the UI thread ([SIM-20]) and the tests run it against a fake knob.
/// </summary>
public sealed class Pmdg737SpeedReferenceSetter
{
    private readonly SpeedReferenceKnob _knob;
    private int _generation;

    public Pmdg737SpeedReferenceSetter(SpeedReferenceKnob knob) => _knob = knob;

    /// <summary>Stops the running setting (it returns null).</summary>
    public void Cancel() => _generation++;

    /// <summary>The read-back once the value is reached ("VREF 142"), "… could not reach 142" at a limit, why it cannot
    /// start or go on, or null when a newer setting or <see cref="Cancel"/> replaced this one.</summary>
    public async Task<string?> SetAsync(double target, PfdKnobPosition position, IReadOnlyList<string> positionNames, Func<double?> read,
        Action<SpeedReferenceClick> click, Func<long> nowMs, Func<long, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(position);
        int generation = ++_generation;
        long start = nowMs();
        int clicks = 0;
        double? lastDistance = null;
        while (true)
        {
            if (generation != _generation) return null;
            if (read() is not { } current) return Pmdg737SpeedReference.NeedsReader;
            var (coarse, fine) = Pmdg737SpeedReference.ClicksTo(current, target, position.Step, _knob.CoarseSteps);
            string readback = Pmdg737SpeedReference.Readback(position.Position, current, positionNames);
            if (coarse == 0 && fine == 0) return readback;
            // A round that left the value no closer (a knob stepping other than the derived step, a stuck knob) would
            // only hunt until the limits: stop at once.
            double distance = Math.Abs(target - current);
            if (clicks >= _knob.MaxClicks || nowMs() - start >= _knob.TimeoutMs || distance >= lastDistance)
                return readback + ", could not reach " + target.ToString("0", CultureInfo.InvariantCulture);
            lastDistance = distance;

            // The clicks first, then (read again) the wheel turns, each batch at its own spacing.
            bool coarsePhase = coarse != 0;
            int count = Math.Abs(coarsePhase ? coarse : fine);
            var turn = coarsePhase
                ? coarse > 0 ? SpeedReferenceClick.CoarseUp : SpeedReferenceClick.CoarseDown
                : fine > 0 ? SpeedReferenceClick.FineUp : SpeedReferenceClick.FineDown;
            for (int i = 0; i < count && clicks < _knob.MaxClicks; i++)
            {
                if (generation != _generation) return null;
                click(turn);
                clicks++;
                await delay(coarsePhase ? _knob.CoarseGapMs : _knob.FineGapMs);
            }
            if (generation != _generation) return null;
            await delay(_knob.SettleMs);
        }
    }
}

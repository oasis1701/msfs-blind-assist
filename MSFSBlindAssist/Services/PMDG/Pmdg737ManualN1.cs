using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>One turn of the N1 SET inner knob: a coarse step either way (a mouse click) or a fine one (a wheel turn).</summary>
public enum ManualN1Click { CoarseUp, CoarseDown, FineUp, FineDown }

/// <summary>What the setter reads: N1 SET's position (SDK 391: 0 "2", 1 "1", 2 AUTO, 3 BOTH) and each engine's manual N1.</summary>
public readonly record struct ManualN1Reading(int N1SetPosition, IReadOnlyList<double> Values);

/// <summary>
/// The PMDG 737 N1 SET inner knob (EVT_MPM_N1SET_CONTROL), swept live 2026-10-24 (docs/pmdg-737.md, Engine
/// displays): a click moves the manual N1 by <see cref="CoarseStep"/> at any spacing down to 120 ms, a wheel
/// turn by <see cref="FineStep"/> when at least 300 ms apart (faster turns accelerate); PMDG keeps the value
/// in <see cref="Minimum"/> to <see cref="Maximum"/>. The gaps leave margin over what was measured; the
/// setter gives up after <see cref="MaxClicks"/> clicks or <see cref="TimeoutMs"/>.
/// </summary>
public sealed record ManualN1Knob(double CoarseStep, double FineStep, double Minimum, double Maximum,
    int MaxClicks, long TimeoutMs, long CoarseGapMs, long FineGapMs, long SettleMs)
{
    public static ManualN1Knob Ng3 { get; } = new(CoarseStep: 0.5, FineStep: 0.1, Minimum: 0, Maximum: 105,
        MaxClicks: 300, TimeoutMs: 90_000, CoarseGapMs: 150, FineGapMs: 350, SettleMs: 300);
}

/// <summary>The manual N1's pure parts: which engines a position sets, the clicks to a target, the read-back, the typed value.</summary>
public static class Pmdg737ManualN1
{
    public const string NeedsReader = "Manual N1 needs the engine display reader";
    public const string NeedsManualN1Set = "Set N1 SET to 1, 2 or BOTH first";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The engines (0 is engine 1) N1 SET's position sets, as PMDG's manual N1 adjustment does: "1"
    /// engine 1, "2" engine 2, BOTH both, AUTO none.</summary>
    public static IReadOnlyList<int> Engines(int n1SetPosition) => n1SetPosition switch
    {
        0 => new[] { 1 },
        1 => new[] { 0 },
        3 => new[] { 0, 1 },
        _ => Array.Empty<int>(),
    };

    /// <summary>The coarse clicks and fine turns (signed: up is positive) from <paramref name="current"/> to
    /// <paramref name="target"/>: whole fine steps, as many as fit taken as coarse ones.</summary>
    public static (int Coarse, int Fine) ClicksTo(double current, double target, ManualN1Knob knob)
    {
        int fineSteps = (int)Math.Round((target - current) / knob.FineStep);
        int ratio = (int)Math.Round(knob.CoarseStep / knob.FineStep);
        int coarse = fineSteps / ratio; // toward zero: never past the target
        return (coarse, fineSteps - coarse * ratio);
    }

    /// <summary>"N1 reference 95.5", or each value when the engines differ: "N1 reference 95.5, 95.0".</summary>
    public static string Readback(IReadOnlyList<double> values)
    {
        var shown = values.Select(v => v.ToString("0.0", Inv)).ToList();
        return "N1 reference " + (shown.Distinct().Count() == 1 ? shown[0] : string.Join(", ", shown));
    }

    /// <summary>A typed manual N1 ([CORE-17]): a number in the knob's range, to one decimal.</summary>
    public static bool TryParse(string? typed, ManualN1Knob knob, out double value, out string? error)
    {
        error = null;
        if (NumberParsing.TryParseTyped(typed, out value) && value >= knob.Minimum && value <= knob.Maximum)
        {
            value = Math.Round(value, 1);
            return true;
        }
        error = string.Create(Inv, $"Enter an N1 between {knob.Minimum:0} and {knob.Maximum:0}");
        return false;
    }
}

/// <summary>
/// Sets the manual N1 by turning the N1 SET inner knob, closed-loop: it reads PMDG's own value, clicks the
/// coarse steps toward the target, reads again, turns the fine steps, and repeats until the selected
/// engines read the target. One setting at a time: a new call replaces the running one, which then stops
/// and returns null. It stops at the knob's click and time limits, without the engine display reader, or
/// when N1 SET leaves 1, 2 or BOTH. Clicks and delays come from the caller, so the definition sends them on
/// the UI thread ([SIM-20]) and the tests run it against a fake knob.
/// </summary>
public sealed class Pmdg737ManualN1Setter
{
    private readonly ManualN1Knob _knob;
    private int _generation;

    public Pmdg737ManualN1Setter(ManualN1Knob knob) => _knob = knob;

    /// <summary>The read-back once the target is reached ("N1 reference 95.5"), "… could not reach 95.5" at a
    /// limit, why it cannot start or go on, or null when a newer setting replaced this one.</summary>
    public async Task<string?> SetAsync(double target, Func<ManualN1Reading?> read, Action<ManualN1Click> click,
        Func<long> nowMs, Func<long, Task> delay)
    {
        int generation = ++_generation;
        long start = nowMs();
        int clicks = 0;
        while (true)
        {
            if (generation != _generation) return null;
            if (read() is not { } reading || reading.Values.Count < 2) return Pmdg737ManualN1.NeedsReader;
            var engines = Pmdg737ManualN1.Engines(reading.N1SetPosition);
            if (engines.Count == 0) return Pmdg737ManualN1.NeedsManualN1Set;
            var values = engines.Select(e => reading.Values[e]).ToList();
            var (coarse, fine) = Pmdg737ManualN1.ClicksTo(values[0], target, _knob);
            if (coarse == 0 && fine == 0) return Pmdg737ManualN1.Readback(values);
            if (clicks >= _knob.MaxClicks || nowMs() - start >= _knob.TimeoutMs)
                return Pmdg737ManualN1.Readback(values) + ", could not reach " + target.ToString("0.0", CultureInfo.InvariantCulture);

            // The coarse clicks first, then (read again) the fine turns, each batch at its own spacing.
            bool coarsePhase = coarse != 0;
            int count = Math.Abs(coarsePhase ? coarse : fine);
            var turn = coarsePhase
                ? coarse > 0 ? ManualN1Click.CoarseUp : ManualN1Click.CoarseDown
                : fine > 0 ? ManualN1Click.FineUp : ManualN1Click.FineDown;
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

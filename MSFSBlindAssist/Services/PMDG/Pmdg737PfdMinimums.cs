using System.Globalization;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The Captain's minimums for one mode as a full PFD reading holds them: BARO or RADIO, PMDG's value, and
/// whether the PFD shows it.</summary>
public readonly record struct PfdMinimums(bool Baro, int Value, bool Shown);

/// <summary>
/// The Captain's minimums knob (EVT_EFIS_CPT_MINIMUMS), measured at ground session 2 (2026-10-30): a click moves the
/// minimums 1 ft in either mode at any spacing, and every click lands (200 clicks back to back in about a second); MINS
/// RST never zeroes them (it toggles their display). The setter sends the clicks in batches of
/// <see cref="BatchClicks"/>, <see cref="BatchGapMs"/> apart, waits <see cref="SettleMs"/> for the PFD reader's next
/// reading (every 500 ms), and gives up after <see cref="MaxRounds"/> turns that did not reach the target.
/// </summary>
public sealed record MinimumsKnob(int BatchClicks, long BatchGapMs, long SettleMs, int MaxRounds)
{
    public static MinimumsKnob Ng3 { get; } = new(BatchClicks: 50, BatchGapMs: 30, SettleMs: 1_000, MaxRounds: 3);
}

/// <summary>The Captain's minimums from the PFD reader, for the minimums setter: the reading and the read-back. Pure.</summary>
public static class Pmdg737PfdMinimums
{
    public const string NeedsReader = "Minimums need the PFD reader";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The minimums for the mode in a full reading: the value of the attitude's readout PMDG labels BARO or
    /// RADIO, shown while PMDG draws it (RADIO also in its option's place); null without a full reading.</summary>
    public static PfdMinimums? Read(PfdSample s, Pmdg737PfdLayout l, bool baro)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(l);
        string label = baro ? "BARO" : "RADIO";
        if (s.Reading is not { } values
            || l.Altitude.Readouts.FirstOrDefault(r => r.Place == PfdPlace.Attitude && r.Label == label) is not { } readout)
            return null;
        try
        {
            bool shown = values.Holds(readout.Shown) || (!baro && values.Holds(l.Attitude.RadioMinimums.Shown));
            return new PfdMinimums(baro, (int)Math.Round(values.Value(readout.Value)), shown);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>"Minimums: BARO 300", with ", not shown" while the PFD does not show them.</summary>
    public static string Readback(PfdMinimums m) =>
        "Minimums: " + (m.Baro ? "BARO " : "RADIO ") + m.Value.ToString(Inv) + (m.Shown ? "" : ", not shown");
}

/// <summary>
/// Sets the Captain's minimums by turning the knob, closed-loop: it reads PMDG's own value from the PFD reader, turns
/// the difference (1 ft a click, up or down), reads again, and repeats until the value is the target. One setting at a
/// time (the caller refuses a second). Clicks and delays come from the caller, so the definition sends them on the UI
/// thread ([SIM-20]) and the tests run it against a fake knob.
/// </summary>
public sealed class Pmdg737MinimumsSetter
{
    private readonly MinimumsKnob _knob;

    public Pmdg737MinimumsSetter(MinimumsKnob knob) => _knob = knob;

    /// <summary>The read-back once the target is reached ("Minimums: BARO 300"), "… could not reach 300" after the
    /// knob's rounds, or why it cannot start.</summary>
    public async Task<string> SetAsync(int target, Func<PfdMinimums?> read, Action<int> click, Func<long, Task> delay)
    {
        for (int round = 0; ; round++)
        {
            if (read() is not { } now) return Pmdg737PfdMinimums.NeedsReader;
            int difference = target - now.Value;
            if (difference == 0) return Pmdg737PfdMinimums.Readback(now);
            if (round >= _knob.MaxRounds)
                return Pmdg737PfdMinimums.Readback(now) + ", could not reach " + target.ToString(CultureInfo.InvariantCulture);

            int direction = Math.Sign(difference);
            for (int i = 1; i <= Math.Abs(difference); i++)
            {
                click(direction);
                if (i % _knob.BatchClicks == 0 && i < Math.Abs(difference)) await delay(_knob.BatchGapMs);
            }
            await delay(_knob.SettleMs);
        }
    }
}

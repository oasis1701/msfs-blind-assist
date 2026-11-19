namespace MSFSBlindAssist.Services;

/// <summary>
/// An expectation that ended in <see cref="FenixCabinReadback.CollectEnded"/>: its line key, and what it said,
/// or null when it ended silently (its window ended with the line unchanged and nothing to say).
/// </summary>
public sealed record FenixCabinReadbackEnd(string Key, string? Text);

/// <summary>
/// Decides what the Cabin window says after a press: the resulting line once when it changes, or for a
/// numeric press (volume, temperature, preselect) a short "&lt;line&gt;, unchanged" when the window ends
/// with nothing changed; any other press ends silently. It never says what the screen reader already
/// says (CORE-7): a button's name, a checkbox's state, a combo's value.
/// <para>
/// The caller passes the clock (<c>now</c>); there are no timers. <see cref="Expect"/> is called when a
/// button is pressed, <see cref="Collect"/> each time the lines are rebuilt (the window polls once a
/// second). An expectation ends the first time it speaks or times out, so a key never speaks twice for
/// one press. A changed line is looked for first, so a change that is there at the first look at or after
/// the window's end is still the press's answer. Not thread-safe: the window calls it on its UI thread
/// only.
/// </para>
/// <para>
/// Two ways to tell a change. Text mode, the default: any change of the line's text. State
/// mode: a change of the line's <see cref="FenixCabinLine.State"/>, the part the press changes, so a door
/// press is not answered by its slide disarming, a cargo press by "moving", or a temperature press by the
/// measured air drifting. A null state is not settled yet (moving, unknown) and keeps the press waiting;
/// once the state changes, the line's whole current text is said.
/// </para>
/// </summary>
public sealed class FenixCabinReadback
{
    private const string UnchangedSuffix = ", unchanged";

    private sealed record Expectation(
        string Key, string? PressText, DateTime PressedAt, TimeSpan Window, bool SpeakIfUnchanged, string? PressState, bool ByState);

    // In press order: a repeated press for a key leaves its old place and joins the end.
    private readonly List<Expectation> _pending = new();

    /// <summary>
    /// Starts waiting for the line <paramref name="key"/> to change after a press. A second call for the
    /// same key replaces the first and counts as the latest press.
    /// </summary>
    /// <param name="key">The line's <see cref="FenixCabinLine.Key"/>.</param>
    /// <param name="currentText">
    /// The line's text when it was pressed, or null when the line did not exist: then, in text mode, the
    /// first text that appears for the key counts as a change.
    /// </param>
    /// <param name="now">The time of the press.</param>
    /// <param name="window">How long to wait for the change: 5 s for most, 30 s for doors and cargo doors.</param>
    /// <param name="speakIfUnchanged">
    /// True for the numeric presses (volume, temperature, preselect): when the window ends with the line
    /// unchanged or missing, say "&lt;text&gt;, unchanged". False for the others: end silently.
    /// </param>
    /// <param name="currentState">
    /// State mode only: the line's <see cref="FenixCabinLine.State"/> when it was pressed, or null when it
    /// was not known: then the first state that appears counts as a change.
    /// </param>
    /// <param name="byState">
    /// True to wait for the line's state rather than its text (state mode); false, the default, for text mode.
    /// </param>
    public void Expect(string key, string? currentText, DateTime now, TimeSpan window, bool speakIfUnchanged,
        string? currentState = null, bool byState = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        _pending.RemoveAll(expectation => string.Equals(expectation.Key, key, StringComparison.Ordinal));
        _pending.Add(new Expectation(key, currentText, now, window, speakIfUnchanged, currentState, byState));
    }

    /// <summary>
    /// What to say now, in press order: the current text of each expected line that changed, and the
    /// "unchanged" confirmation of each numeric press whose window has ended with its line unchanged or
    /// missing. Every expectation that spoke or timed out is dropped; the rest keep waiting.
    /// </summary>
    /// <param name="lines">The current lines of the window.</param>
    /// <param name="now">The current time.</param>
    public IReadOnlyList<string> Collect(IReadOnlyList<FenixCabinLine> lines, DateTime now)
        => CollectEnded(lines, now).Where(end => end.Text != null).Select(end => end.Text!).ToArray();

    /// <summary>
    /// As <see cref="Collect"/>, but names every expectation that ended, in press order: with what it said,
    /// or a null text when it ended silently. So the Cabin window learns when a door press's read-back can no
    /// longer speak (<see cref="FenixCabinOwnPresses"/>).
    /// </summary>
    public IReadOnlyList<FenixCabinReadbackEnd> CollectEnded(IReadOnlyList<FenixCabinLine> lines, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var ended = new List<FenixCabinReadbackEnd>();
        var waiting = new List<Expectation>();
        foreach (Expectation expectation in _pending)
        {
            FenixCabinLine? line = LineOf(lines, expectation.Key);
            if (line != null && Changed(expectation, line))
            {
                ended.Add(new FenixCabinReadbackEnd(expectation.Key, line.Text));
            }
            else if (now - expectation.PressedAt >= expectation.Window)
            {
                // The line is unchanged (or, in state mode, not settled) or missing here: what it says now,
                // or the press-time text when it is gone.
                string? text = expectation.SpeakIfUnchanged && (line?.Text ?? expectation.PressText) is string current
                    ? current + UnchangedSuffix
                    : null;
                ended.Add(new FenixCabinReadbackEnd(expectation.Key, text));
            }
            else
            {
                waiting.Add(expectation);
            }
        }

        _pending.Clear();
        _pending.AddRange(waiting);
        return ended.AsReadOnly();
    }

    /// <summary>True while a press of the line <paramref name="key"/> is waiting for its read-back.</summary>
    public bool IsWaiting(string key)
        => _pending.Exists(expectation => string.Equals(expectation.Key, key, StringComparison.Ordinal));

    // Text mode: the text differs from the press-time text. State mode: the state is settled (not null)
    // and differs from the press-time state.
    private static bool Changed(Expectation expectation, FenixCabinLine line)
        => expectation.ByState
            ? line.State != null && !string.Equals(line.State, expectation.PressState, StringComparison.Ordinal)
            : !string.Equals(line.Text, expectation.PressText, StringComparison.Ordinal);

    // The first line with the key, or null when there is none.
    private static FenixCabinLine? LineOf(IReadOnlyList<FenixCabinLine> lines, string key)
    {
        foreach (FenixCabinLine line in lines)
        {
            if (string.Equals(line.Key, key, StringComparison.Ordinal)) return line;
        }
        return null;
    }
}

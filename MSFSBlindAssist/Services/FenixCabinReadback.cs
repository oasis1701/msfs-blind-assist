namespace MSFSBlindAssist.Services;

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
/// </summary>
public sealed class FenixCabinReadback
{
    private const string UnchangedSuffix = ", unchanged";

    private sealed record Expectation(string Key, string? PressText, DateTime PressedAt, TimeSpan Window, bool SpeakIfUnchanged);

    // In press order: a repeated press for a key leaves its old place and joins the end.
    private readonly List<Expectation> _pending = new();

    /// <summary>
    /// Starts waiting for the line <paramref name="key"/> to change after a press. A second call for the
    /// same key replaces the first and counts as the latest press.
    /// </summary>
    /// <param name="key">The line's <see cref="FenixCabinLine.Key"/>.</param>
    /// <param name="currentText">
    /// The line's text when it was pressed, or null when the line did not exist: then the first text that
    /// appears for the key counts as a change.
    /// </param>
    /// <param name="now">The time of the press.</param>
    /// <param name="window">How long to wait for the change: 5 s for most, 30 s for doors and cargo doors.</param>
    /// <param name="speakIfUnchanged">
    /// True for the numeric presses (volume, temperature, preselect): when the window ends with the line
    /// unchanged or missing, say "&lt;text&gt;, unchanged". False for the others: end silently.
    /// </param>
    public void Expect(string key, string? currentText, DateTime now, TimeSpan window, bool speakIfUnchanged)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        _pending.RemoveAll(expectation => string.Equals(expectation.Key, key, StringComparison.Ordinal));
        _pending.Add(new Expectation(key, currentText, now, window, speakIfUnchanged));
    }

    /// <summary>
    /// What to say now, in press order: the new text of each expected line that changed, and the
    /// "unchanged" confirmation of each numeric press whose window has ended with its line unchanged or
    /// missing. Every expectation that spoke or timed out is dropped; the rest keep waiting.
    /// </summary>
    /// <param name="lines">The current lines of the window.</param>
    /// <param name="now">The current time.</param>
    public IReadOnlyList<string> Collect(IReadOnlyList<FenixCabinLine> lines, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var spoken = new List<string>();
        var waiting = new List<Expectation>();
        foreach (Expectation expectation in _pending)
        {
            string? current = TextOf(lines, expectation.Key);
            if (current != null && !string.Equals(current, expectation.PressText, StringComparison.Ordinal))
            {
                spoken.Add(current);
            }
            else if (now - expectation.PressedAt >= expectation.Window)
            {
                // The line is unchanged or missing here, so the press-time text is what it still says.
                if (expectation.SpeakIfUnchanged && (current ?? expectation.PressText) is string text)
                    spoken.Add(text + UnchangedSuffix);
            }
            else
            {
                waiting.Add(expectation);
            }
        }

        _pending.Clear();
        _pending.AddRange(waiting);
        return spoken.AsReadOnly();
    }

    // The text of the first line with the key, or null when there is none.
    private static string? TextOf(IReadOnlyList<FenixCabinLine> lines, string key)
    {
        foreach (FenixCabinLine line in lines)
        {
            if (string.Equals(line.Key, key, StringComparison.Ordinal)) return line.Text;
        }
        return null;
    }
}

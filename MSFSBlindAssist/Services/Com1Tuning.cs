using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Tuning COM 1 from the surroundings window's Frequencies list — Enter for standby, Shift+Enter for
/// active: the events, the timing, and what is said once the radio has been read back. Pure;
/// MainForm.TuneCom1FromSurroundings sends the events and reads COM 1.
///
/// <para>The read-back is the pilot's only confirmation: a list gives no feedback of its own when
/// Enter is pressed on it, and an aircraft whose radio stack ignores the stock events would
/// otherwise fail in silence. Confirming a number the pilot entered is one of the announcements the
/// app's screen-reader rules allow.</para>
/// </summary>
internal static class Com1Tuning
{
    /// <summary>The same pair the app's generic COM "set active" fields send (and the MD-11's radios):
    /// COM 1's standby-set event is the un-numbered one.</summary>
    internal const string StandbySetEvent = "COM_STBY_RADIO_SET_HZ", SwapEvent = "COM1_RADIO_SWAP";

    /// <summary>Between the standby set and the swap, so the swap moves the NEW standby — the gap the
    /// generic "set active" field leaves.</summary>
    internal const int SwapGapMs = 100;

    /// <summary>COM 1 is read this long after the last event, up to <see cref="ReadAttempts"/> times,
    /// until it holds the frequency: the stock radio applies within a frame, an add-on's own radio
    /// stack may take a moment longer.</summary>
    internal const int SettleMs = 300;
    internal const int ReadAttempts = 4;
    internal static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>How close a read must be to what was sent to count as holding it: float rounding
    /// only, never a neighbouring channel (an 8.33 kHz channel is 5 kHz from its 25 kHz neighbour).</summary>
    internal const double HoldsToleranceHz = 500;

    internal static bool Holds(double? readHz, int sentHz)
        => readHz is double r && double.IsFinite(r) && Math.Abs(r - sentHz) <= HoldsToleranceHz;

    /// <summary>
    /// What is said once COM 1 has been read back: its new frequency, or that the aircraft did not
    /// take the one sent (and what it holds instead), or that it never answered.
    /// </summary>
    internal static string Describe(bool active, int sentHz, double? readHz)
    {
        string slot = active ? "active" : "standby";
        string sent = AirportFacilities.FormatMhz(sentHz);
        if (readHz is not double r || !double.IsFinite(r)) return $"COM 1 did not report back after tuning {sent}.";
        if (Holds(r, sentHz)) return $"COM 1 {slot} {sent}";
        return $"Could not tune COM 1 {slot} to {sent}. It reads {AirportFacilities.FormatMhz((int)Math.Round(r))}.";
    }
}

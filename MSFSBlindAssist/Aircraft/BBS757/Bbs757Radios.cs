using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The 757's COM radios. BlueBird's VHF L, C and R are the simulator's own COM 1, 2 and 3 (measured):
/// their frequencies live in <c>COM ACTIVE/STANDBY FREQUENCY:1..3</c>, a standby typed in goes out as
/// the stock <c>K:COMn_STBY_RADIO_SET_HZ</c> and a transfer as the stock swap, with the tuning panel's
/// transfer button as the fallback (plan 04, rulings R53 and R54). Pure.
///
/// Radio numbers are 1 to 3; any member taking one throws <see cref="ArgumentOutOfRangeException"/> for
/// another. Frequencies are spoken and sent with a dot and three decimals whatever the PC's culture.
/// </summary>
public static class Bbs757Radios
{
    public const string EmptyMessage = "Type a frequency first.";
    public const string InvalidMessage = "Frequency must be 118.000 to 136.990, on a 25 or 8.33 kilohertz channel.";

    // The COM band in kHz.
    private const int MinKhz = 118000;
    private const int MaxKhz = 136990;

    private static readonly char[] DecimalMarks = { '.', ',' };

    /// <summary>
    /// The three radio tuning panels' "selected radio" lights, in panel order (panel 1, 2, 3), with the
    /// names the variables are shown under. Measured: panel 1 is <c>L:BBS_RTP_L_LIGHTS</c>, panel 2
    /// <c>L:BBS_RTP_C_LIGHTS</c>, panel 3 <c>L:BBS_RTP_R_LIGHTS</c>. The one place that mapping lives; the
    /// transfer's fallback reads them in this order and hands them to <see cref="TransferPanelFor"/>.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string LVar, string Label)> RtpLights = new[]
    {
        ("BBS757_RTP_1_LIGHTS", "BBS_RTP_L_LIGHTS", "Radio Tuning Panel 1 Lights"),
        ("BBS757_RTP_2_LIGHTS", "BBS_RTP_C_LIGHTS", "Radio Tuning Panel 2 Lights"),
        ("BBS757_RTP_3_LIGHTS", "BBS_RTP_R_LIGHTS", "Radio Tuning Panel 3 Lights"),
    };

    /// <summary>The keys of <see cref="RtpLights"/>, panel 1, 2, 3.</summary>
    public static readonly IReadOnlyList<string> RtpLightsKeys = RtpLights.Select(l => l.Key).ToArray();

    public static string ActiveKey(int com) => $"BBS757_COM{Radio(com)}_ACTIVE";

    public static string StandbyKey(int com) => $"BBS757_COM{Radio(com)}_STANDBY";

    /// <summary>The panel's typed field: a key containing "_SET" is what MainForm renders as a box that Enter applies.</summary>
    public static string SetKey(int com) => $"BBS757_COM{Radio(com)}_SET";

    /// <summary>The panel's transfer button.</summary>
    public static string TransferKey(int com) => $"BBS757_COM{Radio(com)}_TRANSFER";

    /// <summary>
    /// What the pilot typed into the standby box, as megahertz. Digits and one "." or "," only. Two
    /// decimals ending 2 or 7 get a trailing 5, the 8.33 kHz channel names ("121.57" is 121.575, "121.52"
    /// is 121.525); anything else is rounded to 1 kHz as typed ("121.5" is 121.500, "121.520" stays
    /// 121.520). Then the band, 118.000 to 136.990, and the channel rule: the kHz part (0 to 999) mod 25
    /// is 0, 5, 10 or 15. An empty box says to type a frequency first; everything else refused gets
    /// <see cref="InvalidMessage"/>.
    /// </summary>
    public static bool TryParseCom(string? text, out double mhz, out string error)
    {
        mhz = 0;
        error = "";
        string typed = (text ?? "").Trim();
        if (typed.Length == 0) { error = EmptyMessage; return false; }
        if (!typed.All(c => char.IsAsciiDigit(c) || c is '.' or ',')
            || !NumberParsing.TryParseTyped(typed, out double value))
        {
            error = InvalidMessage;
            return false;
        }

        double khz = Math.Round(value * 1000.0, MidpointRounding.AwayFromZero);
        int mark = typed.IndexOfAny(DecimalMarks);
        if (mark >= 0 && typed.Length - mark - 1 == 2 && typed[^1] is '2' or '7') khz += 5;

        if (khz < MinKhz || khz > MaxKhz || (int)khz % 1000 % 25 is not (0 or 5 or 10 or 15))
        {
            error = InvalidMessage;
            return false;
        }
        mhz = khz / 1000.0;
        return true;
    }

    /// <summary>
    /// The calculator string that sets the standby: "121500000 (>K:COM_STBY_RADIO_SET_HZ)". COM 1's
    /// events carry no digit; COM 2 and 3 are <c>COM2_…</c> and <c>COM3_…</c>. The Hz is an integer
    /// computed from the frequency rounded to 1 kHz, so 121.575 is 121575000 with no floating-point noise.
    /// </summary>
    public static string StandbySetCode(int com, double mhz)
    {
        long hz = (long)Math.Round(mhz * 1000.0, MidpointRounding.AwayFromZero) * 1000L;
        return $"{hz.ToString(CultureInfo.InvariantCulture)} (>K:{StandbySetEvent(com)})";
    }

    /// <summary>
    /// The stock event that sets the standby, <c>COM_STBY_RADIO_SET_HZ</c> for COM 1, <c>COM2_STBY_RADIO_SET_HZ</c>,
    /// <c>COM3_STBY_RADIO_SET_HZ</c>. Stated here once: <see cref="StandbySetCode"/> sends it and the
    /// standby field's registration names it, so the two can never differ.
    /// </summary>
    public static string StandbySetEvent(int com) => $"{EventPrefix(com)}_STBY_RADIO_SET_HZ";

    /// <summary>
    /// The stock transfer event, <c>COM_STBY_RADIO_SWAP</c> for COM 1, <c>COM2_RADIO_SWAP</c>,
    /// <c>COM3_RADIO_SWAP</c> (all three work on BlueBird, measured). <see cref="SwapCode"/> sends it and
    /// the transfer button's registration names it.
    /// </summary>
    public static string SwapEvent(int com) => Radio(com) == 1 ? "COM_STBY_RADIO_SWAP" : $"COM{com}_RADIO_SWAP";

    /// <summary>The stock transfer: <c>(&gt;K:COM_STBY_RADIO_SWAP)</c> for COM 1, <c>COM2_RADIO_SWAP</c>, <c>COM3_RADIO_SWAP</c>.</summary>
    public static string SwapCode(int com) => $"(>K:{SwapEvent(com)})";

    /// <summary>
    /// The tuning panel's transfer button pressed (1): the vendor's own click code, the one the control map
    /// replays. Hold <see cref="Bbs757Write.ButtonHoldMs"/> (200 ms), then <see cref="PanelReleaseCode"/>.
    /// A panel's button swaps the radio its lights select (measured).
    /// </summary>
    public static string PanelPressCode(int panel) => $"1 (>L:{PanelButton(panel)},bool)";

    /// <summary>The tuning panel's transfer button let go (0).</summary>
    public static string PanelReleaseCode(int panel) => $"0 (>L:{PanelButton(panel)},bool)";

    /// <summary>
    /// Whether a transfer swapped the radio: after it the active equals the standby from before AND the
    /// standby equals the active from before, compared at 1 kHz (the sim reports 121.9 as
    /// 121.89999999999999). Equal active and standby count as swapped: nothing can show the difference.
    /// A row that is not a number is never a swap.
    /// </summary>
    public static bool Swapped(double activeBefore, double standbyBefore, double activeAfter, double standbyAfter)
        => double.IsFinite(activeBefore) && double.IsFinite(standbyBefore)
           && double.IsFinite(activeAfter) && double.IsFinite(standbyAfter)
           && Khz(activeAfter) == Khz(standbyBefore) && Khz(standbyAfter) == Khz(activeBefore);

    /// <summary>
    /// The tuning panel (1, 2 or 3) whose lights show this radio selected: the radio's bit (COM1 2, COM2
    /// 4, COM3 8) set in the panel's lights value, tried in panel order, the first match winning. A panel
    /// that was not read (null), or whose value is not a count of lights, is skipped; null when no panel
    /// selects the radio.
    /// </summary>
    public static int? TransferPanelFor(int com, double? rtpPanel1, double? rtpPanel2, double? rtpPanel3)
    {
        int bit = 1 << Radio(com);   // BBS_RTP_*_LIGHTS (measured): 2 VHF L (COM 1), 4 VHF C (COM 2), 8 VHF R (COM 3)
        var panels = new[] { rtpPanel1, rtpPanel2, rtpPanel3 };
        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] is double lights && lights >= 0 && lights <= int.MaxValue
                && ((int)Math.Round(lights) & bit) != 0)
                return i + 1;
        }
        return null;
    }

    /// <summary>A frequency as spoken and shown: three decimals, a dot ("124.850").</summary>
    public static string Frequency(double mhz) => mhz.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>"COM1 active 124.850".</summary>
    public static string ActivePhrase(int com, double mhz) => $"COM{Radio(com)} active {Frequency(mhz)}";

    /// <summary>"COM1 standby 121.500".</summary>
    public static string StandbyPhrase(int com, double mhz) => $"COM{Radio(com)} standby {Frequency(mhz)}";

    /// <summary>"COM1 active 121.500, standby 124.850": a transfer's read-back, both rows in one sentence.</summary>
    public static string TransferPhrase(int com, double activeMhz, double standbyMhz)
        => $"{ActivePhrase(com, activeMhz)}, standby {Frequency(standbyMhz)}";

    /// <summary>The fallback found no tuning panel whose lights select the radio, so nothing was pressed.</summary>
    public static string NoPanelPhrase(int com) => $"No tuning panel selects COM{Radio(com)}";

    /// <summary>The radio reported back and neither the stock swap nor the panel's button moved it.</summary>
    public static string NotTransferredPhrase(int com) => $"COM{Radio(com)} did not transfer";

    /// <summary>A second press of a radio's transfer while its first is still running (each swap toggles, so two could undo each other).</summary>
    public static string TransferInProgressPhrase(int com) => $"COM{Radio(com)} transfer in progress";

    /// <summary>The transfer was sent but a row could not be read back, so it is not claimed.</summary>
    public static string TransferNoReportPhrase(int com) => $"COM{Radio(com)} transfer sent, the radio did not report back";

    private static long Khz(double mhz) => (long)Math.Round(mhz * 1000.0, MidpointRounding.AwayFromZero);

    private static string PanelButton(int panel)
        => panel is >= 1 and <= 3
            ? $"B757_PED_COMHEAD_{panel}_SWAP_BUTTON"
            : throw new ArgumentOutOfRangeException(nameof(panel), panel, "The 757 has radio tuning panels 1 to 3.");

    private static int Radio(int com)
        => com is >= 1 and <= 3
            ? com
            : throw new ArgumentOutOfRangeException(nameof(com), com, "The 757 has COM radios 1 to 3.");

    // COM 1's stock events have no digit ("COM_STBY_RADIO_SET_HZ"); COM 2 and 3 do ("COM2_…").
    private static string EventPrefix(int com) => Radio(com) == 1 ? "COM" : $"COM{com}";
}

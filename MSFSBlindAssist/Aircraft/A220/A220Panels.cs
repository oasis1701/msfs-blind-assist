using System.Globalization;
using System.Text.Json;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// Pure helpers behind the A220 panels (docs/a220.md "Panels and hotkeys"): the speedbrake
/// lever's detents and the write its cockpit lever makes ([A220-40]) and the spoken
/// confirmations of panel presses (CORE-7). Characterization-pinned by A220PanelsTests.
/// </summary>
public static class A220Panels
{
    // ---- Speedbrake lever -------------------------------------------------------------

    /// <summary>
    /// Lever travel of each detent as the cockpit lever reads it from L:A22X Spoiler Lever
    /// (0-1): RET, 1/4, 1/2, 3/4, FULL, MAX — the ECL's sensed SPOILER_LEVER_0..5. Confirmed live
    /// 2026-10-16 by a 0.1-step sweep against those sensed bits: each travel lights its own bit,
    /// no odd tenth lights any, and the read-back equals the write. A finer sweep found each bit lit
    /// within about ±0.04 of its detent (edges refined for RET, 1/4, FULL and MAX), and
    /// SPOILER_LEVER_5 only at 0.965-1.0: MAX travel, never FULL. Change them only from such a sweep.
    /// </summary>
    public static readonly IReadOnlyList<double> SpeedbrakeDetentTravel = new[] { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 };

    /// <summary>The speedbrake combo's labels, index = detent.</summary>
    public static readonly IReadOnlyList<string> SpeedbrakeDetentLabels =
        new[] { "Retracted", "Quarter", "Half", "Three quarters", "Full", "Max (emergency)" };

    /// <summary>
    /// The calculator string (without MSFSBA's seq prefix) that moves the lever to
    /// <paramref name="detent"/>: FCTL_SPD_BRK_LEVER's own VAR_WRITE,
    /// <c>&lt;travel&gt; 0.5 - 32768 * (&gt;K:SPOILERS_SET)</c> (Pedestal/FlightControls.xml).
    /// Clamped to RET..MAX; the travel is invariant fixed-point ([VAR-5]).
    /// </summary>
    public static string SpeedbrakeCalc(int detent)
    {
        int d = Math.Clamp(detent, 0, SpeedbrakeDetentTravel.Count - 1);
        string travel = SpeedbrakeDetentTravel[d].ToString("0.###", CultureInfo.InvariantCulture);
        return travel + " 0.5 - 32768 * (>K:SPOILERS_SET)";
    }

    /// <summary>
    /// The detent nearest a read-back travel — the speedbrake combo's ValueToDescriptionKey. A
    /// lever left between detents (a hardware lever, the mouse wheel's 0.1 steps) shows the
    /// nearest one; a travel outside 0-1, or NaN, clamps instead of leaving the combo blank
    /// or showing the last pick.
    /// </summary>
    public static double SpeedbrakeDetentForTravel(double travel)
    {
        if (double.IsNaN(travel)) return 0;
        int best = 0;
        for (int i = 1; i < SpeedbrakeDetentTravel.Count; i++)
            if (Math.Abs(SpeedbrakeDetentTravel[i] - travel) < Math.Abs(SpeedbrakeDetentTravel[best] - travel))
                best = i;
        return best;
    }

    // ---- Press confirmations (CORE-7) ---------------------------------------------------

    /// <summary>
    /// Panel presses with no state MSFSBA reads and no lamp the batch monitor would speak: they
    /// confirm "&lt;name&gt; pressed" once. Never when the ECL actuates them (the checklist
    /// window says what it is doing). Probe heat is absent on purpose — its lamp speaks — and so
    /// is the standby STD push, which reads the stock altimeter 3 back instead
    /// (<see cref="IsiStdReadBackText"/>).
    /// </summary>
    public static readonly IReadOnlySet<string> PressConfirmKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "A22X_L_ENG_FIRE", "A22X_R_ENG_FIRE", "A22X_CVR_TEST", "A22X_CVR_ERASE", "A22X_MECH_CALL",
    };

    /// <summary>"Left Engine Fire (guarded)" → "Left Engine Fire pressed".</summary>
    public static string PressedText(string displayName)
        => displayName.Replace("(guarded)", "", StringComparison.Ordinal).Trim() + " pressed";

    /// <summary>
    /// The selected transmitter, spoken after a transmit-select press. L:A22X Tx Channel:
    /// 1-3 = VHF 1-3 (the ACP keys' own lamp code tests exactly those), 0 = none; a negative
    /// value is MSFSBA's "no read-back" sentinel.
    /// </summary>
    public static string TxChannelText(double value)
    {
        if (double.IsNaN(value) || value < 0) return "Transmit radio not available";
        int channel = (int)Math.Round(value);
        return channel switch
        {
            0 => "No transmit radio selected",
            1 or 2 or 3 => "Transmit VHF " + channel.ToString(CultureInfo.InvariantCulture),
            _ => "Transmit channel " + channel.ToString(CultureInfo.InvariantCulture)
        };
    }

    // ---- Audio control panel: VHF receive and volume ([A220-44]) ----------------------

    /// <summary>
    /// The ACP volume knob's scale: <c>A220_KnobPotentiometer</c> has 29 detents and the
    /// multiplier 1/28 (Lib/Knob.xml), so <c>L:A22X ACP VHFn Volume</c> holds detent/28, 0 to 1.
    /// The aircraft's systems WASM turns it into COMn_VOLUME_SET: measured live 2026-10-16, 14/28
    /// gave COM VOLUME:2 50 % and 28/28 100 %, and the L:var kept the written level.
    /// </summary>
    public const int AcpVolumeSteps = 28;

    /// <summary>The volume combo's labels, index = detent: the level in whole percent.</summary>
    public static readonly IReadOnlyList<string> AcpVolumeLabels = Enumerable.Range(0, AcpVolumeSteps + 1)
        .Select(step => Math.Round(step * 100.0 / AcpVolumeSteps, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture) + "%")
        .ToArray();

    /// <summary>
    /// The detent nearest a read-back level — the volume combo's ValueToDescriptionKey. A level
    /// outside 0-1 clamps; NaN reads as 0 (an L:var always reads a number; NaN is defensive).
    /// </summary>
    public static double AcpVolumeStepFor(double raw)
        => double.IsNaN(raw) ? 0 : Math.Clamp(Math.Round(raw * AcpVolumeSteps, MidpointRounding.AwayFromZero), 0, AcpVolumeSteps);

    /// <summary>
    /// The calculator string (without MSFSBA's seq prefix) that sets VHF <paramref name="vhf"/>'s
    /// volume to <paramref name="step"/> on the knob's own scale, <c>step 28 /</c>: the sim
    /// computes the exact level, so the cockpit knob lands on its detent (WriteLVar's four
    /// decimals would leave it a hair off). Never the combo's step index itself: the L:var is
    /// 0 to 1, and 14 would be off its scale. Integers only, so no culture can reach it.
    /// </summary>
    public static string AcpVolumeCalc(int vhf, int step)
    {
        int s = Math.Clamp(step, 0, AcpVolumeSteps);
        return FormattableString.Invariant($"{s} {AcpVolumeSteps} / (>L:A22X ACP VHF{vhf} Volume)");
    }

    /// <summary>
    /// A receiver write's read-back (CORE-7): null when the aircraft took it (the screen reader
    /// already read the pick), else the state the aircraft kept, or "not confirmed" when nothing
    /// came back — never a claimed state. 1 = receiving (the knob pulled out): measured live
    /// 2026-10-16, COM RECEIVE:2 read 1 with the L:var at 1 and 0 with it at 0.
    /// </summary>
    public static string? AcpReceiverReadBack(int vhf, bool wantOn, double? now)
    {
        if (now is not { } v || double.IsNaN(v))
            return FormattableString.Invariant($"VHF {vhf} receiver not confirmed: the A220 did not report it.");
        bool on = v >= 0.5;
        return on == wantOn ? null : FormattableString.Invariant($"VHF {vhf} receiver did not change — still {(on ? "on" : "off")}.");
    }

    /// <summary>A volume write's read-back, the same way: null when the level landed.</summary>
    public static string? AcpVolumeReadBack(int vhf, int wantStep, double? nowRaw)
    {
        if (nowRaw is not { } v || double.IsNaN(v))
            return FormattableString.Invariant($"VHF {vhf} volume not confirmed: the A220 did not report it.");
        int now = (int)AcpVolumeStepFor(v);
        return now == Math.Clamp(wantStep, 0, AcpVolumeSteps)
            ? null
            : FormattableString.Invariant($"VHF {vhf} volume did not change — reads {AcpVolumeLabels[now]}.");
    }

    // ---- Standby altimeter (ISI, [A220-43]) ----------------------------------------------

    public const double HpaPerInHg = 33.8639;

    /// <summary>Spoken when a standby-baro entry is out of range or not a number.</summary>
    public const string IsiRangeText = "Enter 940 to 1090 hectopascals, or 27.00 to 32.00 inches.";

    /// <summary>Whether a typed standby-baro entry is in inches of mercury (27.00-32.00).</summary>
    public static bool IsInHgEntry(double entry) => entry >= 27.0 && entry <= 32.0;

    /// <summary>
    /// The hPa target for a typed standby-baro entry: 940-1090 is hectopascals, 27.00-32.00 is
    /// inches (converted, rounded to 0.01 hPa). Anything else — "2992" typed without its point,
    /// 0 from a blank box, NaN from an unparseable one — is null, so the walk refuses instead of
    /// driving the knob towards its stop.
    /// </summary>
    public static double? IsiTargetHpa(double entry)
    {
        if (double.IsNaN(entry)) return null;
        if (entry >= 940 && entry <= 1090) return entry;
        if (IsInHgEntry(entry)) return Math.Round(entry * HpaPerInHg, 2);
        return null;
    }

    /// <summary>Whether an isi() read carries a QNH, and if not, why. <see cref="NoReading"/> is
    /// the zero value on purpose: a <c>default(IsiRead)</c> must read as "no reading", never as an
    /// Ok with a null QNH. Nothing depends on the numeric values. The walk asks the store only
    /// whether the ISI is powered: its value comes from the stock altimeter 3 ([A220-43]).</summary>
    public enum IsiReadStatus
    {
        /// <summary>No link, no store, or no value in it.</summary>
        NoReading,
        /// <summary>The ISI's QNH is in <see cref="IsiRead.QnhHpa"/>.</summary>
        Ok,
        /// <summary>The ISI's own store says <c>powered:false</c> (cold and dark).</summary>
        Unpowered
    }

    /// <summary>One isi() read: the QNH in hPa when <see cref="Status"/> is Ok, else null.</summary>
    public readonly record struct IsiRead(IsiReadStatus Status, double? QnhHpa);

    private static readonly IsiRead NoIsiReading = new(IsiReadStatus.NoReading, null);

    /// <summary>
    /// The ISI's QNH in hPa from the displays agent's isi() JSON —
    /// {"ok":…,"isi":{"powered":…,"qnh_hpa":…},"diag":{…}} — or why there is none: Unpowered
    /// when the store says <c>powered:false</c>, NoReading when the link, the store or the field
    /// is missing. The walk uses it only as its powered check: live 2026-10-16 <c>qnh_hpa</c> is a
    /// whole number (the ISI prints it with no decimals), so it cannot see one 0.01-inch click.
    /// </summary>
    public static IsiRead ParseIsiRead(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return NoIsiReading;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("isi", out JsonElement isi) || isi.ValueKind != JsonValueKind.Object)
                return NoIsiReading;
            if (isi.TryGetProperty("powered", out JsonElement powered) && powered.ValueKind == JsonValueKind.False)
                return new IsiRead(IsiReadStatus.Unpowered, null);
            if (!isi.TryGetProperty("qnh_hpa", out JsonElement qnh) || qnh.ValueKind != JsonValueKind.Number)
                return NoIsiReading;
            return new IsiRead(IsiReadStatus.Ok, qnh.GetDouble());
        }
        catch (JsonException) { return NoIsiReading; }
    }

    /// <summary>Spoken when the stock altimeter 3 gives no reading (a read that timed out, or no
    /// SimConnect link): never a guessed setting.</summary>
    public const string IsiNoReadingText = "Standby altimeter reading not available";

    /// <summary>Spoken when the walk cannot start: the ISI's own store says it is unpowered, or the
    /// stock altimeter 3 gives no reading.</summary>
    public static string IsiUnavailableText(IsiReadStatus status) => status == IsiReadStatus.Unpowered
        ? "Standby altimeter not powered."
        : IsiNoReadingText + ".";

    /// <summary>
    /// The standby STD push's one read-back (CORE-7), from the stock altimeter 3 the aircraft keeps
    /// for the ISI ([A220-43]). Live 2026-10-16: <c>KOHLSMAN SETTING STD:3</c> reads 1 after an STD
    /// press (MB:3 then 1013.25) and 0 after the next knob click, and a second press leaves it 1 —
    /// STD is a one-shot set to 29.92 inches, not a toggle. So: STD:3 set, "Standby altimeter STD";
    /// clear (the press did not land), the setting from MB:3 in hectopascals, as the ISI shows it;
    /// a read missing, <see cref="IsiNoReadingText"/> — never a claimed state.
    /// </summary>
    public static string IsiStdReadBackText(double? std, double? mbHpa)
    {
        if (std is not { } s || double.IsNaN(s)) return IsiNoReadingText;
        if (s >= 0.5) return "Standby altimeter STD";
        if (mbHpa is not { } mb || double.IsNaN(mb)) return IsiNoReadingText;
        return FormatIsiBaro(mb, inHg: false);
    }

    /// <summary>Spoken when the ISI stops giving a reading part-way through a walk.</summary>
    public static string IsiReadLostText(IsiReadStatus status) => status == IsiReadStatus.Unpowered
        ? "Standby altimeter not powered."
        : "Standby altimeter reading lost — check the ISI.";

    /// <summary>"1013 hectopascals", or for an inches entry "29.92 inches". Culture-invariant:
    /// CI runs en-US, pilots may not.</summary>
    public static string IsiBaroValue(double hpa, bool inHg) => inHg
        ? (hpa / HpaPerInHg).ToString("0.00", CultureInfo.InvariantCulture) + " inches"
        : hpa.ToString("0", CultureInfo.InvariantCulture) + " hectopascals";

    /// <summary>"Standby altimeter 1013 hectopascals", or for an inches entry
    /// "Standby altimeter 29.92 inches".</summary>
    public static string FormatIsiBaro(double hpa, bool inHg) => "Standby altimeter " + IsiBaroValue(hpa, inHg);

    /// <summary>"Standby altimeter 1014 hectopascals — nearest selectable".</summary>
    public static string IsiNearestText(double hpa, bool inHg) => FormatIsiBaro(hpa, inHg) + " — nearest selectable";

    /// <summary>
    /// Spoken when the walk runs out of rounds: what it could not reach and where the ISI is now,
    /// both in the unit the pilot typed. Never "nearest selectable": the walk judges its last landing
    /// first (<see cref="A220Afdx.RunKnobWalkAsync"/>), so a landing on the target or on the nearest
    /// value is said as such, and the limit is reached only when a nearer value was still to go.
    /// </summary>
    public static string IsiRoundLimitText(double targetHpa, double currentHpa, bool inHg)
        => "Standby altimeter could not reach " + IsiBaroValue(targetHpa, inHg)
            + ", now " + IsiBaroValue(currentHpa, inHg) + ".";

    // ---- Standby baro walk: the per-round decision ([A220-43]) ----------------------------

    /// <summary>
    /// Most clicks one write of <c>L:A22X ISI Altimeter Set</c> carries. The knob var takes any
    /// whole-number delta in ONE write and applies it exactly (live 2026-10-16: 3 moved 29.92 to
    /// 29.95 inches, -10 moved 29.95 to 29.85, 7 moved 29.85 back to 29.92, each read back on the
    /// next sample) — unlike the FCP's K-events, where a batch of n is one click ([A220-10]). A
    /// larger gap takes another round of the walk.
    /// </summary>
    public const int IsiMaxClicksPerWrite = 100;

    /// <summary>The signed delta ONE knob write carries for a move of <paramref name="clicks"/>
    /// clicks in the direction of <paramref name="sign"/>: at least one click, at most
    /// <see cref="IsiMaxClicksPerWrite"/>.</summary>
    public static int IsiKnobWriteDelta(int clicks, int sign)
        => (sign < 0 ? -1 : 1) * Math.Clamp(clicks, 1, IsiMaxClicksPerWrite);

    /// <summary>One click of the ISI knob: its 0.01-inch step, in hPa (live 2026-10-16).</summary>
    public const double IsiClickHpa = 0.01 * HpaPerInHg;

    /// <summary>The grid altimeter 3 reports the setting on: 1/16 hPa (live 2026-10-16).</summary>
    public const double IsiReadGridHpa = 1.0 / 16;

    /// <summary>
    /// The walk's tolerance, hPa: a gap this small is on target, before and after the step is
    /// measured. Half the smallest gap between two of altimeter 3's readings one click apart: a click
    /// (<see cref="IsiClickHpa"/>, 0.3386 hPa) read on the <see cref="IsiReadGridHpa"/> grid shows as
    /// 5/16 or 6/16 hPa, so a reading within 5/32 hPa of the target has no nearer neighbour (29.92
    /// inches is 1013.21 hPa; altimeter 3 reads 1013.25 on it). The walk used to take half its
    /// measured step after the probe — 3/16 when the probe read 6/16 — and 0.2 before it, which came
    /// from the ISI's whole-hPa store: both stopped a few simulated walks on a reading with a nearer
    /// neighbour (the 0.2 one, 18 of 113,448 in the review of Task 7b2). <c>A220PanelsTests</c>
    /// derives it from the model of the grid and walks every entry from 40 starts on it.
    /// </summary>
    public const double IsiToleranceHpa = 5.0 / 32;

    /// <summary>
    /// The standby baro walk on the shared knob walk (<see cref="A220Afdx.RunKnobWalkAsync"/>, ruling
    /// R41's decision, [A220-43]): 8 moves; each move ONE write of up to <see cref="IsiMaxClicksPerWrite"/>
    /// clicks, which lands whole (live 2026-10-16), so every move re-measures the step — one click is
    /// 0.3125 or 0.375 hPa on altimeter 3's grid, a long burst measures it finer; a sign change after a
    /// burst aims back. On target within <see cref="IsiToleranceHpa"/>; the readings are uneven, so a
    /// value beyond it is called the nearest only after a look one click across. A move under 0.01 hPa
    /// did not move it, and a probe that did not is sent once more (the ISI drops its first clicks
    /// after power-up).
    /// </summary>
    internal static readonly A220Afdx.KnobWalkPolicy IsiWalkPolicy = new()
    {
        Tolerance = _ => IsiToleranceHpa,
        MaxRounds = 8,
        MaxClicksPerMove = IsiMaxClicksPerWrite,
        BurstsLandWhole = true,
        NotMovedBelow = 0.01,
        DeadProbeRetries = 1,
        EvenSteps = false,
    };

    /// <summary>The standby baro walk's one spoken line for each way it ends, in the unit the pilot
    /// typed: the setting, "— nearest selectable", "could not reach", "not responding", or "reading
    /// lost" (never a guessed setting). The nearest reading is said plainly when it prints as what
    /// was typed in that unit (1010 typed, 1010.1875 read: the ISI shows 1010), since there is then
    /// nothing to explain (11 of the 151 whole-hPa entries on the model of altimeter 3).</summary>
    internal static string IsiWalkResultText(A220Afdx.KnobWalkResult r, double targetHpa, bool inHg) => r.End switch
    {
        A220Afdx.KnobWalkEnd.OnTarget => FormatIsiBaro(r.Value, inHg),
        A220Afdx.KnobWalkEnd.NearestSelectable => IsiBaroValue(r.Value, inHg) == IsiBaroValue(targetHpa, inHg)
            ? FormatIsiBaro(r.Value, inHg)
            : IsiNearestText(r.Value, inHg),
        A220Afdx.KnobWalkEnd.RoundLimit => IsiRoundLimitText(targetHpa, r.Value, inHg),
        A220Afdx.KnobWalkEnd.NotResponding => FormatIsiBaro(r.Value, inHg) + " — the standby altimeter knob is not responding.",
        _ => IsiReadLostText(IsiReadStatus.NoReading)
    };
}

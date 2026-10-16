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
    /// Spoken when the walk runs out of rounds. "Nearest selectable" only when the ISI is within
    /// one measured step of the target (no selectable value lies between them); otherwise it says
    /// what it could not reach and where the ISI is now, both in the unit the pilot typed.
    /// </summary>
    public static string IsiRoundLimitText(double targetHpa, double currentHpa, double? step, bool inHg)
        => step is { } s && Math.Abs(targetHpa - currentHpa) <= s + IsiToleranceEpsilon
            ? IsiNearestText(currentHpa, inHg)
            : "Standby altimeter could not reach " + IsiBaroValue(targetHpa, inHg)
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

    /// <summary>The blind stage's tolerance, hPa: before a step is measured, a gap this small is
    /// already on target (29.92 inches is 1013.21 hPa; altimeter 3 reads 1013.25 on it).</summary>
    public const double IsiBlindToleranceHpa = 0.2;

    /// <summary>Slack on every tolerance compare: 1013.2 - 1013.0 is 0.20000000000004547 in
    /// floating point, a hair over 0.2, and must still count as on target.</summary>
    private const double IsiToleranceEpsilon = 1e-9;

    /// <summary>What the baro walk does next, from the remaining gap.</summary>
    public enum IsiWalkAction
    {
        /// <summary>On target: announce the value.</summary>
        Done,
        /// <summary>Stop on the current value and announce it as the nearest selectable one.</summary>
        NearestHere,
        /// <summary>The last burst overshot and the value before it was nearer: one click back.</summary>
        StepBack,
        /// <summary>Fire <see cref="IsiWalkStep.Clicks"/> clicks towards the target.</summary>
        Click
    }

    /// <summary>The walk's next move; <see cref="Clicks"/> is set only for <see cref="IsiWalkAction.Click"/>.</summary>
    public readonly record struct IsiWalkStep(IsiWalkAction Action, int Clicks = 0);

    /// <summary>Whether a remaining gap (hPa) is on target: half a measured step, or the blind
    /// tolerance before a step is measured.</summary>
    public static bool IsiWithinTolerance(double delta, double? step)
        => Math.Abs(delta) <= (step is { } s ? s / 2 : IsiBlindToleranceHpa) + IsiToleranceEpsilon;

    /// <summary>
    /// One round of the standby baro walk, pure so the loop only does I/O. <paramref name="delta"/>
    /// is target - current (hPa), <paramref name="prevDelta"/> the same before the last move
    /// (NaN on the first round), <paramref name="step"/> the hPa per click measured on THIS walk
    /// (null before calibration), <paramref name="lastClicks"/> the clicks the last move fired.
    /// Within tolerance is Done. Before a step is known the answer is one probe click. After it, a
    /// sign change in the gap after ONE click is a straddle: the target sits between that click's
    /// two values and the walk ends on the NEARER one. Once a step is known a straddle is only
    /// reached with |delta| over half a step, so the value before the click is nearer exactly when
    /// |prevDelta| &lt; |delta|, and then it takes one click back. A sign change after a burst of
    /// several clicks is an overshoot, never a straddle: the value before the burst is several
    /// clicks away, and a step measured on one click is not exact (live 2026-10-16, one click is the
    /// ISI's 0.01-inch step, about 0.34 hPa, but altimeter 3 reports it on a 1/16-hPa grid, so
    /// successive clicks read 0.3125 or 0.375 hPa apart), so a burst aimed with it can land clicks
    /// past the target. It aims back. Otherwise round(|delta| / step) clicks; a step that cannot
    /// aim stops where it is.
    /// </summary>
    public static IsiWalkStep IsiWalkDecision(double delta, double prevDelta, double? step, int lastClicks)
    {
        if (IsiWithinTolerance(delta, step)) return new IsiWalkStep(IsiWalkAction.Done);
        if (step is not { } known) return new IsiWalkStep(IsiWalkAction.Click, 1);
        if (lastClicks <= 1 && A220Afdx.WalkStraddled(delta, prevDelta))
            return new IsiWalkStep(Math.Abs(prevDelta) < Math.Abs(delta) ? IsiWalkAction.StepBack : IsiWalkAction.NearestHere);
        int clicks = A220Afdx.AimClicks(delta, known);
        return clicks == 0
            ? new IsiWalkStep(IsiWalkAction.NearestHere)
            : new IsiWalkStep(IsiWalkAction.Click, clicks);
    }
}

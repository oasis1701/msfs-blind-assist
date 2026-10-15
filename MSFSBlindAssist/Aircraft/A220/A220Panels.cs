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
    /// (0-1): RET, 1/4, 1/2, 3/4, FULL, MAX — the ECL's sensed SPOILER_LEVER_0..5. Confirmed
    /// live against those sensed bits (docs/a220.md, Panels); change only from a new measurement.
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
    /// Panel presses with no readable state and no lamp the batch monitor would speak: they
    /// confirm "&lt;name&gt; pressed" once. Never when the ECL actuates them (the checklist
    /// window says what it is doing). Probe heat is absent on purpose — its lamp speaks.
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
    /// The aircraft's systems WASM turns it into COMn_VOLUME_SET.
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
    /// came back — never a claimed state. 1 = receiving (the knob pulled out; Task 6 measures it
    /// against COM RECEIVE:n).
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

    /// <summary>
    /// The ISI's QNH in hPa from the displays agent's isi() JSON —
    /// {"ok":…,"isi":{"powered":…,"qnh_hpa":…},"diag":{…}}. Null when the link, the store,
    /// the field or the instrument's power is missing: the walk refuses, never guesses.
    /// </summary>
    public static double? ParseIsiQnhHpa(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("isi", out JsonElement isi) || isi.ValueKind != JsonValueKind.Object)
                return null;
            if (isi.TryGetProperty("powered", out JsonElement powered) && powered.ValueKind == JsonValueKind.False)
                return null;
            if (!isi.TryGetProperty("qnh_hpa", out JsonElement qnh) || qnh.ValueKind != JsonValueKind.Number)
                return null;
            return qnh.GetDouble();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>"Standby altimeter 1013 hectopascals", or for an inches entry
    /// "Standby altimeter 29.92 inches". Culture-invariant: CI runs en-US, pilots may not.</summary>
    public static string FormatIsiBaro(double hpa, bool inHg) => inHg
        ? "Standby altimeter " + (hpa / HpaPerInHg).ToString("0.00", CultureInfo.InvariantCulture) + " inches"
        : "Standby altimeter " + hpa.ToString("0", CultureInfo.InvariantCulture) + " hectopascals";

    /// <summary>The read-back spoken after a standby STD press.</summary>
    public static string IsiReadbackText(double? hpa)
        => hpa is { } q ? FormatIsiBaro(q, inHg: false) : "Standby altimeter reading not available";
}

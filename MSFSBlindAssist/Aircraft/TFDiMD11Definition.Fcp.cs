using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The four FCP type-in dialogs (input mode: H heading, S speed, A altitude, V vertical speed).
///
/// These exist because <c>MD11_EXTCTL_FCP_*</c> takes a direct value — see <see cref="Md11Fcp"/>
/// for the live-probe evidence. The aircraft is otherwise entirely relative/event-driven, so this
/// family is the one place a value can be typed rather than walked.
///
/// In the heading, speed and altitude dialogs the buttons after the value box are in the order
/// a pilot reaches for them (owner's ruling, 2026-09-08): the knob's Push and Pull first —
/// pulling to take a selected value is what the window is mostly opened for — then the mode
/// button (NAV, FMS Speed), and the window's unit toggle LAST, because switching a window between
/// heading and track or IAS and Mach is rare. The altitude dialog puts PROF first, ahead of its
/// knob, and carries no unit toggle at all: the typed value is always feet (the unit is written
/// with it), so Feet/Metres is only ever wanted for the window's own display, and that lives on
/// the Flight Control Panel in the Glareshield section like the rest of the rarely-touched
/// panel. The vertical-speed dialog is untouched: VS/FPA first, then the wheel — it has no knob.
/// </summary>
public partial class TFDiMD11Definition
{
    // ---------------------------------------------------------------------------------
    // Heading
    // ---------------------------------------------------------------------------------

    private void ShowHeadingDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            // The knob itself pushes and pulls, and both are real actions on the aircraft with
            // their own events — so they belong wherever the pilot is working this window, not
            // only in the full panel, and first in the tab order (see the class summary). One-shot
            // actions carry no state. Same for speed and altitude.
            new("P&ush knob", () => "", () => PressKnobAction(Md11Fcp.HeadingKnob, "PUSH_DOWN", "PUSH_UP",
                Md11Fcp.PushAction(Md11Fcp.HeadingKnobName), announcer)),
            new("Pu&ll knob", () => "", () => PressKnobAction(Md11Fcp.HeadingKnob, "PULL_DOWN", "PULL_UP",
                Md11Fcp.PullAction(Md11Fcp.HeadingKnobName), announcer)),
            // Engaged or not, from the FCP's own dashed heading window (Md11AutoflightState).
            new("&NAV", () => Md11AutoflightState.Engaged(Md11AutoflightState.NavEngaged(Val(sim, Md11Fcp.ReadHeading))),
                () => PressButtonAction("MD11_CGS_NAV_BT", "NAV", announcer)),
            new("&Track / Heading", () => Mode(sim, Md11Fcp.ModeHeadingIsTrack) ? "Track" : "Heading",
                () => PressButtonAction("MD11_CGS_HDGTRK_BT", "Track / Heading", announcer)),
        };

        var dialog = new ValueInputForm(
            "FCP Heading", "heading", "0-359", announcer,
            input => int.TryParse(input, out var v) && v >= 0 && v <= 359
                ? (true, "")
                : (false, "Enter a heading between 0 and 359"),
            Suppressing(toggles),
            input =>
            {
                if (!int.TryParse(input, out var hdg)) return;
                // A write that never left says so: the FCP windows are unreadable to a blind pilot,
                // so a typed heading that vanished looks exactly like one the FCC took.
                if (!SetFcpValue(Md11Fcp.WriteHeading, Md11Fcp.NormaliseHeading(hdg), sim))
                    announcer.Announce(Md11Fcp.Unavailable(Md11Fcp.HeadingKnobName));
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // ---------------------------------------------------------------------------------
    // Speed
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Accepts EITHER a knots value or a Mach value and picks the unit from the number's shape:
    /// anything below 10 is a Mach number (the FCP's Mach range is ~0.10-0.95 and its IAS range
    /// starts around 100 kt, so the two cannot overlap). That means "0.82" and "250" both just
    /// work, and the pilot never has to set the unit as a separate step — which matters here
    /// because the unit write and the value write are two different variables.
    /// </summary>
    private void ShowSpeedDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("P&ush knob", () => "", () => PressKnobAction(Md11Fcp.SpeedKnob, "PUSH_DOWN", "PUSH_UP",
                Md11Fcp.PushAction(Md11Fcp.SpeedKnobName), announcer)),
            new("Pu&ll knob", () => "", () => PressKnobAction(Md11Fcp.SpeedKnob, "PULL_DOWN", "PULL_UP",
                Md11Fcp.PullAction(Md11Fcp.SpeedKnobName), announcer)),
            // Engaged or not, from the FCP's own dashed speed window (Md11AutoflightState).
            new("&FMS Speed", () => Md11AutoflightState.Engaged(Md11AutoflightState.FmsSpeedEngaged(Val(sim, Md11Fcp.ReadSpeed))),
                () => PressButtonAction("MD11_CGS_FMSSPD_BT", "FMS Speed", announcer)),
            // Last: the typed value picks its own unit by shape, so this is only for the window's
            // display and is seldom touched.
            new("&IAS / Mach", () => Mode(sim, Md11Fcp.ModeSpeedIsMach) ? "Mach" : "IAS",
                () => PressButtonAction("MD11_CGS_IASMACH_BT", "IAS / Mach", announcer)),
        };

        var dialog = new ValueInputForm(
            "FCP Speed", "speed", "100-365 knots, or a Mach number such as 0.82", announcer,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return (false, "Enter a speed in knots, or a Mach number such as 0.82");

                return LooksLikeMach(v)
                    ? v is >= Md11Fcp.MinMach and <= Md11Fcp.MaxMach
                        ? (true, "")
                        : (false, $"Enter a Mach between {Md11Fcp.MinMach:0.00} and {Md11Fcp.MaxMach:0.00}")
                    : v >= Md11Fcp.MinSpeedKnots && v <= Md11Fcp.MaxSpeedKnots
                        ? (true, "")
                        : (false, $"Enter a speed between {Md11Fcp.MinSpeedKnots} and {Md11Fcp.MaxSpeedKnots} knots, or a Mach such as 0.82");
            },
            Suppressing(toggles),
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;

                var mach = LooksLikeMach(v);
                if (!SetFcpValue(Md11Fcp.WriteSpeed, mach ? v : Math.Round(v), sim,
                        Md11Fcp.WriteSpeedUnit, mach ? 1 : 0))
                    announcer.Announce(Md11Fcp.Unavailable(Md11Fcp.SpeedKnobName));
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// The FCP's Mach band (0.10-0.95) and its IAS band (from ~100 kt) do not overlap, so the
    /// magnitude alone identifies the unit unambiguously. 10 is the split point: comfortably above
    /// any Mach the FCP takes, comfortably below any airspeed it takes.
    /// </summary>
    private static bool LooksLikeMach(double v) => v < 10;

    // ---------------------------------------------------------------------------------
    // Altitude
    // ---------------------------------------------------------------------------------

    private void ShowAltitudeDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            // PROF first, ahead of the knob: it is the altitude window's engage. Its engagement
            // lives only on the FMA, which is not exported — no state to show. No Feet/Metres
            // here — the typed value is always feet and the unit is written with it (below); the
            // window's own unit is the "Altitude Unit Select" row of the Flight Control Panel.
            new("&PROF", () => "", () => PressButtonAction("MD11_CGS_PROF_BT", "PROF", announcer)),
            new("P&ush knob", () => "", () => PressKnobAction(Md11Fcp.AltitudeKnob, "PUSH_DOWN", "PUSH_UP",
                Md11Fcp.PushAction(Md11Fcp.AltitudeKnobName), announcer)),
            new("Pu&ll knob", () => "", () => PressKnobAction(Md11Fcp.AltitudeKnob, "PULL_DOWN", "PULL_UP",
                Md11Fcp.PullAction(Md11Fcp.AltitudeKnobName), announcer)),
        };

        var dialog = new ValueInputForm(
            "FCP Altitude", "altitude", $"{Md11Fcp.MinAltitudeFt}-{Md11Fcp.MaxAltitudeFt} feet", announcer,
            input => int.TryParse(input, out var v) && v >= Md11Fcp.MinAltitudeFt && v <= Md11Fcp.MaxAltitudeFt
                ? (true, "")
                : (false, $"Enter an altitude between {Md11Fcp.MinAltitudeFt} and {Md11Fcp.MaxAltitudeFt} feet"),
            Suppressing(toggles),
            input =>
            {
                if (!int.TryParse(input, out var alt)) return;
                // The typed number is feet, so the unit is written alongside it — otherwise a
                // window left in metres would read the value as metres and climb to the wrong level.
                if (!SetFcpValue(Md11Fcp.WriteAltitude, alt, sim, Md11Fcp.WriteAltitudeUnit, 0))
                    announcer.Announce(Md11Fcp.Unavailable(Md11Fcp.AltitudeKnobName));
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // ---------------------------------------------------------------------------------
    // Vertical speed / FPA
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Same shape-based unit pick as speed — a V/S is hundreds of feet per minute, an FPA is a
    /// single-digit angle, so "-1500" and "-3" cannot be confused for one another — with the one
    /// value both windows accept, 0, settled by the CURRENT mode (<see cref="Md11Fcp.ResolveVerticalUnit"/>):
    /// typing 0 to level off in V/S mode must not silently switch the window to FPA.
    /// </summary>
    private void ShowVSDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        // The window's current mode off the 1 Hz batch — the tie-breaker for 0. Validation and
        // commit run back-to-back in ValueInputForm.SetValue, so both see the same reading.
        bool CurrentIsFpa() => Mode(sim, Md11Fcp.ModeVerticalIsFpa);

        var toggles = new List<ToggleButtonDef>
        {
            new("&VS / FPA", () => CurrentIsFpa() ? "FPA" : "V/S",
                () => PressButtonAction("MD11_CGS_VS_FPA_BT", "VS / FPA", announcer)),
            // The MD-11 has no engage-V/S button — turning the V/S / FPA wheel is what engages the
            // pitch mode. Exposed here so the pilot can engage and fine-tune it by hand; submitting
            // a typed value engages it too (see SetVerticalSpeedEngaged). One click per press.
            new("Wheel &up", () => "", () => FireWheelAction(Md11Fcp.VerticalSpeedKnob, "WHEEL_UP",
                Md11Fcp.WheelAction(Md11Fcp.VerticalSpeedName, up: true), announcer)),
            new("Wheel &down", () => "", () => FireWheelAction(Md11Fcp.VerticalSpeedKnob, "WHEEL_DOWN",
                Md11Fcp.WheelAction(Md11Fcp.VerticalSpeedName, up: false), announcer)),
        };

        var dialog = new ValueInputForm(
            "FCP Vertical Speed", "vertical speed",
            $"plus or minus up to {Md11Fcp.MaxVerticalSpeedFpm} feet per minute, or an FPA such as -3; 0 levels off in the current mode", announcer,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var v))
                    return (false, "Enter a vertical speed in feet per minute, or an FPA such as -3");

                if (Md11Fcp.ResolveVerticalUnit(v, CurrentIsFpa()) != null) return (true, "");
                return Math.Abs(v) > Md11Fcp.MaxVerticalSpeedFpm
                    ? (false, $"Enter a vertical speed within {Md11Fcp.MaxVerticalSpeedFpm} feet per minute")
                    : (false, $"Enter a vertical speed of at least {Md11Fcp.MinVerticalSpeedFpm} feet per minute, or an FPA between -{Md11Fcp.MaxFpaDegrees} and {Md11Fcp.MaxFpaDegrees} degrees");
            },
            Suppressing(toggles),
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var v)) return;
                if (Md11Fcp.ResolveVerticalUnit(v, CurrentIsFpa()) is not Md11VerticalUnit unit) return;

                bool fpa = unit == Md11VerticalUnit.Fpa;
                // Engage the pitch mode (nudge the wheel) AND set the value — a plain value-set
                // leaves it in a window the FCC is not flying. See SetVerticalSpeedEngaged. The
                // enum's number IS the VR_U inbox value (0 = V/S, 1 = FPA).
                SetVerticalSpeedEngaged(fpa ? v : Math.Round(v), (double)unit, sim, announcer);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // ---------------------------------------------------------------------------------
    // Altimeter (Ctrl+B)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Ctrl+B — one typed value sets the captain's, the first officer's AND the standby altimeter
    /// (the A320's "enter QNH once, both sides" behaviour; all three inboxes proven live, see
    /// <see cref="Md11Fcp.Altimeters"/>). Each is converted to ITS OWN display's unit. "Standard"
    /// writes standard pressure as a value to all three — deterministic, where the knob-push STD
    /// toggle it replaces could flip an already-STD side back to QNH. The captain's setting is
    /// confirmed by the settle announcement (Md11AltimeterAnnouncer), so the dialog adds no
    /// second sentence; a side that did NOT take its value is reported by the read-back
    /// (<see cref="VerifyAltimetersAsync"/>), and only that.
    /// </summary>
    private void ShowBaroDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("&Standard, all three", () => "", () => SetAllAltimeters(sim, announcer, typed: null)),
        };

        var dialog = new ValueInputForm(
            "Altimeters", "altimeter", "hPa (900-1100) or inHg such as 29.92; sets captain, first officer and standby", announcer,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return (false, "Enter hPa (900-1100) or inHg such as 29.92");

                return Md11Fcp.LooksLikeHpa(v)
                    ? v is >= Md11Fcp.MinHpa and <= Md11Fcp.MaxHpa
                        ? (true, "")
                        : (false, $"Enter hPa between {Md11Fcp.MinHpa} and {Md11Fcp.MaxHpa}, or inHg such as 29.92")
                    : v is >= Md11Fcp.MinInHg and <= Md11Fcp.MaxInHg
                        ? (true, "")
                        : (false, $"Enter inHg between {Md11Fcp.MinInHg:0.00} and {Md11Fcp.MaxInHg:0.00}, or hPa such as 1013");
            },
            Suppressing(toggles),
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
                SetAllAltimeters(sim, announcer, v);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// Writes <paramref name="typed"/> (or standard pressure when null) to every altimeter, each in
    /// the unit its own display currently shows. A display whose reading is not cached yet, or
    /// reads 0 (cold and dark, or an export not yet populated — the read-back treats 0 the same
    /// way), is assumed to be in the typed value's unit (or inHg for Standard) — the inbox is
    /// one-shot, so a wrong-unit write would simply be corrected by the next entry. All three
    /// settings are then read back (<see cref="VerifyAltimetersAsync"/>).
    /// </summary>
    private void SetAllAltimeters(SimConnectManager sim, ScreenReaderAnnouncer announcer, double? typed)
    {
        var written = new List<(string Side, string Read, double Value)>();
        bool allWritten = true;
        foreach (var (side, read, write) in Md11Fcp.Altimeters)
        {
            var display = sim.GetCachedVariableValue(read) is double d and > 0 ? d : (typed ?? Md11Fcp.StandardInHg);
            var value = typed is double t ? Md11Fcp.BaroToDisplayUnit(t, display) : Md11Fcp.StandardFor(display);
            allWritten &= SetFcpValue(write, value, sim);
            written.Add((side, read, value));
        }
        int entry = ++_altimeterEntrySeq;   // UI thread: the dialog's callbacks and the tail below
        // Through RefuseToggleIf like the other fourteen, so Ctrl+B's "Standard, all three" records
        // the outcome too: reached from that toggle, its refusal was otherwise judged by a STALE
        // flag — cut off by the dialog's own 1.2 s label announce, or (after some earlier refusal)
        // suppressing the label of a Standard set that WORKED. Nothing reached the aircraft, so
        // there is nothing to read back, and the read-back is the only thing that would have spoken.
        RefuseToggleIf(!allWritten, Md11Fcp.AltimetersName, announcer);
        if (!allWritten) return;
        _ = VerifyAltimetersAsync(sim, announcer, written, entry);
    }

    /// <summary>
    /// Latest entry wins for the altimeter read-back. The dialog stays open for more input, so a
    /// Standard followed by the ATIS QNH within the read-back window — or a typo corrected — would
    /// otherwise have the FIRST entry's tail compare its values against the SECOND entry's, already
    /// in the cache, and speak a false "not set" citing the pilot's own newest value.
    /// </summary>
    private int _altimeterEntrySeq;

    /// <summary>
    /// The three inboxes are proven, but a write that silently did not take would leave a pilot
    /// flying an altimeter they believe they set. The captain's settle announcement does not
    /// cover that case either: it speaks only on a CHANGE, and it has a Ctrl+M row of its own. So
    /// once the exports have had time to ride the 1 Hz batch, every side's reading is compared
    /// with what was written for it (its own unit, its own value) and ONLY a disagreement is
    /// spoken, as one utterance: "Standby altimeter not set, reads 1012, 29.88". Agreement stays
    /// silent, so a successful set is never spoken twice — the screen reader announced the entry
    /// and the captain's sentence confirms the set. Not gated on Ctrl+M: this is the read-back of
    /// the pilot's own entry, an error condition, not a background change. Same shape as the
    /// minimums read-back and the altimeter settle: the UI-thread tail carries its own catch.
    /// </summary>
    private Task VerifyAltimetersAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        List<(string Side, string Read, double Value)> written, int entry)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        return DeferToUiThreadAsync(Md11Fcp.VerifyAfterMs, async () =>
        {
            // Every export is batch-covered: each fresh read completes on its next 1 Hz delivery.
            // The old fixed sleep read the cache and had to out-wait two deliveries to be sure of
            // seeing the write; an export not delivered stays null, which DescribeAltimeterShortfall
            // treats as no evidence.
            var reads = await Task.WhenAll(written.Select(w => sim.ReadFreshAsync(w.Read, BatchReadBackTimeoutMs))).ConfigureAwait(false);
            Log.Debug("MD11", $"Altimeter read-back: {reads.Count(r => r != null)} of {written.Count} exports delivered after {sw.ElapsedMilliseconds} ms.");
            return () =>
            {
                if (entry != _altimeterEntrySeq) return;         // a newer entry owns the read-back
                var shortfalls = new List<string>();
                for (int i = 0; i < written.Count; i++)
                {
                    var text = Md11Fcp.DescribeAltimeterShortfall(written[i].Side, written[i].Value, reads[i]);
                    if (text != null) shortfalls.Add(text);
                }
                if (shortfalls.Count > 0) announcer.Announce(string.Join(". ", shortfalls));
            };
        }, "Altimeter read-back threw", "Altimeter read-back (UI-thread tail) threw", guardGeneration: true);
    }

    /// <summary>
    /// Latest press wins each STD row's read-back: a quick double press (on, then off) would
    /// otherwise speak the final setting twice. The <see cref="_altimeterEntrySeq"/> precedent; UI
    /// thread only — the press and the tail.
    /// </summary>
    private readonly Dictionary<string, int> _stdReadBackSeq = new(StringComparer.Ordinal);

    /// <summary>
    /// An STD row (<see cref="Md11StdToggles"/>): presses its altimeter knob's PUSH — the aircraft's
    /// STD toggle — as the knob's own CEVENT pair, never a write. The STD state itself is unreadable,
    /// so the press is confirmed by the SETTING it changes: the captain's by the settle announcement
    /// that already speaks every change of that export (<see cref="Md11AltimeterAnnouncer"/> — nothing
    /// extra here), the first officer's and the standby's by <see cref="VerifyStdToggleAsync"/>. A
    /// press that cannot be sent says so — on this aircraft a dropped press sounds exactly like a
    /// taken one.
    /// </summary>
    private void PressStdToggle(Md11StdTarget std, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        int backlogMs = _bus?.BacklogMs ?? 0;   // sampled before the push joins the queue
        if (!PressControlEvents(std.Knob, "LEFT_BUTTON_DOWN", "LEFT_BUTTON_UP"))
        {
            var label = GetVariables().TryGetValue(std.Key, out var row) ? row.DisplayName : std.Key;
            announcer.Announce(Md11Fcp.Unavailable(label));
            return;
        }
        if (!std.ReadsBack) return;
        int seq = _stdReadBackSeq.TryGetValue(std.Key, out var last) ? last + 1 : 1;
        _stdReadBackSeq[std.Key] = seq;
        _ = VerifyStdToggleAsync(std, seq, backlogMs, sim, announcer);
    }

    /// <summary>
    /// The first officer's and the standby STD read-back: the EXTCTL-apply allowance every altimeter
    /// read-back shares (<see cref="Md11Fcp.VerifyAfterMs"/>) measured from the push's WRITE (the bus
    /// backlog ahead of it), then the altimeter is read on its next DELIVERY and spoken in the B
    /// key's words with the side in front ("First Officer altimeter standard",
    /// <see cref="Md11StdToggles.ReadBackSentence"/>). Nothing delivered — or a 0, which a missing
    /// export reads — is no evidence: a log line, no speech. Dropped by a context reset or an aircraft
    /// switch, and by a newer press of the same row; honours a Ctrl+M mute of the altimeter should it
    /// ever get a row (both are silent exports without one today).
    /// </summary>
    private Task VerifyStdToggleAsync(Md11StdTarget std, int seq, int backlogMs, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        return DeferToUiThreadAsync(Md11EventBus.ReadBackDelayMs(Md11Fcp.VerifyAfterMs, backlogMs), async () =>
        {
            var read = await sim.ReadFreshAsync(std.AltimeterVar, BatchReadBackTimeoutMs).ConfigureAwait(false);
            Log.Debug("MD11", $"STD read-back: {std.AltimeterVar}={read?.ToString("0.##", CultureInfo.InvariantCulture) ?? "null"} " +
                $"after {sw.ElapsedMilliseconds} ms (backlog {backlogMs} ms).");
            var sentence = Md11StdToggles.ReadBackSentence(std.Side, read);
            if (sentence == null) return null;
            return () =>
            {
                if (_stdReadBackSeq.TryGetValue(std.Key, out var latest) && latest != seq) return;   // a newer press owns the read-back
                if (Settings.SettingsManager.Current.Md11DisabledMonitorVariablesSet.Contains(std.AltimeterVar)) return;
                announcer.Announce(sentence);
            };
        }, "STD read-back threw", "STD read-back (UI-thread tail) threw", guardGeneration: true);
    }

    // ---------------------------------------------------------------------------------
    // Read-outs (output mode: Shift+H / Shift+S / Shift+A / Shift+V)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The FCP windows on Shift+H / S / A / V, through <see cref="Md11AutoflightState"/> so the
    /// window, the dialogs and these read-outs never disagree.
    ///
    /// The mode is NOT decoration: the same window shows a HEADING or a TRACK, a speed or a Mach,
    /// and a blind pilot cannot glance at the window to see which. And a DASHED window is itself
    /// information — TFDi document it as the FCP's own engagement cue (heading dashed = NAV is
    /// flying, speed dashed = FMS speed), so the read-out says so. What it still cannot say is
    /// PROF or APPR/LAND: those live only on the FMA (docs/md11.md §2c).
    /// </summary>
    private string DescribeHeading(SimConnectManager sim)
    {
        bool track = Mode(sim, Md11Fcp.ModeHeadingIsTrack);
        return Md11AutoflightState.Selected(Md11AutoflightState.HeadingNoun(track),
            Md11AutoflightState.HeadingValue(Val(sim, Md11Fcp.ReadHeading)));
    }

    private string DescribeSpeed(SimConnectManager sim)
        => Md11AutoflightState.Selected(Md11AutoflightState.Speed,
            Md11AutoflightState.SpeedValue(Val(sim, Md11Fcp.ReadSpeed), Mode(sim, Md11Fcp.ModeSpeedIsMach)));

    private string DescribeAltitude(SimConnectManager sim)
        => Md11AutoflightState.Selected(Md11AutoflightState.Altitude,
            Md11AutoflightState.AltitudeValue(Val(sim, Md11Fcp.ReadAltitude), Mode(sim, Md11Fcp.ModeAltitudeIsMetres)));

    private string DescribeVertical(SimConnectManager sim)
    {
        bool fpa = Mode(sim, Md11Fcp.ModeVerticalIsFpa);
        return Md11AutoflightState.Selected(Md11AutoflightState.VerticalNoun(fpa),
            Md11AutoflightState.VerticalValue(Val(sim, Md11Fcp.ReadVerticalSpeed), fpa));
    }

    // ---------------------------------------------------------------------------------
    // Shared
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The cached value, or null while it has not been delivered (after a SimConnect drop the cache
    /// is empty) — the composers then say "not available", never read a stand-in 0 as a setting.
    /// </summary>
    private static double? Val(SimConnectManager sim, string varKey)
        => sim.GetCachedVariableValue(varKey);

    private static bool Mode(SimConnectManager sim, string varKey)
        => (sim.GetCachedVariableValue(varKey) ?? 0) > 0.5;

    /// <summary>
    /// A knob push or pull from a HOTKEY or a dialog toggle, refused ALOUD when it cannot be
    /// delivered — the Ctrl+P window's <c>PressEvents</c> one layer in, so all three surfaces say
    /// the same sentence (<see cref="Md11Fcp.Unavailable"/>).
    ///
    /// Silent on success: the screen reader has already read the button or the hotkey's own
    /// feedback, and this aircraft's FCP window cannot be read, so only a press that did NOT
    /// happen is news. <see cref="Connected"/> does not cover it: that gates a dialog at OPEN time
    /// and only on <c>IsConnected</c>, so a drop AFTER it opened still reaches these toggles. The
    /// no-module configuration reaches them too, and is caught only once the calc-path probe has
    /// concluded unverified (<see cref="SimConnectManager.CalcWriteCanLand"/>) — never while it is
    /// still pending, so nothing is refused that would have worked.
    /// </summary>
    private void PressKnobAction(string node, string downEvent, string upEvent, string name,
        ScreenReaderAnnouncer announcer)
    {
        RefuseToggleIf(!PressControlEvents(node, downEvent, upEvent), name, announcer);
    }

    /// <summary>
    /// Whether the LAST toggle press this dialog made was refused. <see cref="ValueInputForm"/>
    /// asks it 1.2 s after the press and skips its own state announce when it is true: that
    /// announce is an interrupting <c>AnnounceImmediate</c> and was truncating the refusal these
    /// helpers had queued. Set on every toggle press, so a refusal can never suppress the next
    /// press's announce. UI thread only — every toggle press and the form's announce run there.
    /// </summary>
    private bool _lastToggleRefused;

    /// <summary>Records whether this toggle press was refused, and speaks the one refusal if it was.</summary>
    private void RefuseToggleIf(bool refused, string name, ScreenReaderAnnouncer announcer)
    {
        _lastToggleRefused = refused;
        if (refused) announcer.Announce(Md11Fcp.Unavailable(name));
    }

    /// <summary>Attaches the refusal suppressor to every toggle in a dialog — see <see cref="_lastToggleRefused"/>.</summary>
    private List<ToggleButtonDef> Suppressing(List<ToggleButtonDef> toggles) =>
        toggles.Select(t => t with { SuppressStateAnnounce = () => _lastToggleRefused }).ToList();

    /// <summary>
    /// One V/S / FPA wheel step, refused aloud when it cannot be delivered. The wheel is what
    /// ENGAGES the pitch mode on this aircraft, so a dropped step is a pilot believing they are
    /// flying a mode they are not.
    /// </summary>
    private void FireWheelAction(string node, string eventName, string name, ScreenReaderAnnouncer announcer)
    {
        RefuseToggleIf(!FireControlEvent(node, eventName), name, announcer);
    }

    /// <summary>
    /// A plain BUTTON press from a dialog toggle (NAV, PROF, FMS Speed, the two mode switches),
    /// refused aloud when it cannot be delivered — the Ctrl+P window's <c>Press</c> one layer in.
    /// These six discarded <see cref="PressControl"/>'s bool, so a mode button pressed during an
    /// outage did nothing and said nothing, while the dialog's own caption still read the old mode.
    /// <paramref name="label"/> is the toggle's caption, mnemonic stripped.
    /// </summary>
    private void PressButtonAction(string node, string label, ScreenReaderAnnouncer announcer)
    {
        RefuseToggleIf(!PressControl(node), label.Replace("&", ""), announcer);
    }

    private static bool Connected(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (sim.IsConnected) return true;
        announcer.AnnounceImmediate("Not connected to simulator.");
        return false;
    }
}

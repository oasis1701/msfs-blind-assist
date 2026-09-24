using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// FCP set dialogs (Ctrl+S/H/A/V/B, Ctrl+P, Ctrl+N). No direct-set FCP surface exists —
/// re-proven 2026-08-10 against the live WASM and again 2026-09-21 (12000 written to
/// AP_ALT_VAR_SET_ENGLISH, selector did not move): HEADING_BUG_SET / AP_SPD_VAR_SET /
/// AP_ALT_VAR_SET_ENGLISH / AP_VS_VAR_SET_ENGLISH all leave the FCP untouched, the
/// inc/dec events ignore their parameter, and the aircraft's whole JS→WASM CommBus call
/// surface (grep `.call("A22X.` across html_ui) has no FCP channel. So every value entry
/// drives a knob walk with read-back verification. Two walk flavors: the ALTITUDE walk is
/// GRID-based (the selector snaps to round thousands/hundreds per ring — proven live),
/// everything else is the SELF-CALIBRATING measured walk (fire inc/dec, read the value
/// back, adapt the step, stop on reach/overshoot).
///
/// The READ-BACK prefers the 30 Hz "A22X.FCP Data" CommBus block and falls back to the
/// 1 Hz SimConnect cache. Both are real sources: measured 2026-09-21, the stock
/// AUTOPILOT ALTITUDE LOCK VAR / HEADING LOCK DIR / AIRSPEED HOLD VAR each track their
/// knob click for click, so a session with no Coherent display link still gets the same
/// walks, just with a slower settle. Do not re-introduce the belief that the stock vars
/// are not published — that is what used to strand link-less sessions on the degraded
/// fallback walk.
///
/// Dialog completeness rule (user ruling 2026-07-27): each dialog carries buttons for
/// ALL functions of its physical control, and every mode press announces the RESULT.
/// Actual state CHANGES are spoken by the FG monitor; the dialogs' deferred check adds
/// the honest "no change" call so a press is never followed by silence.
/// </summary>
public partial class SynapticA220Definition
{
    private Forms.A220.A220AutopilotWindow? _autopilotWindow;

    private void ShowAutopilotWindow(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (_autopilotWindow == null || _autopilotWindow.IsDisposed)
            _autopilotWindow = new Forms.A220.A220AutopilotWindow(this, simConnect, announcer);
        _autopilotWindow.ShowForm();
    }

    /// <summary>Pulse an L:var press flag from a dialog toggle (VNAV, XFR, baro STD…).</summary>
    internal void PulsePanelFlag(SimConnectManager simConnect, string bareName) => PulseLVar(simConnect, bareName);

    internal void FireFcpEvent(SimConnectManager simConnect, string eventName) => FireKeyEvent(simConnect, eventName);

    internal void AnnounceModeResultPublic(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string stateKey, string engagedText, string offText)
        => AnnounceModeResult(simConnect, announcer, stateKey, engagedText, offText);

    internal double CachedPublic(SimConnectManager simConnect, string key, double fallback = 0)
        => Cached(simConnect, key, fallback);

    /// <summary>
    /// FD buttons are MOMENTARY (A220_ButtonMomentary in Interior/Glareshield/
    /// Autopilot.xml): the L:var is the press pulse, not the FD's state. So this presses
    /// (1 then 0) — it must NOT read the L:var back and write the opposite level, which
    /// is what it used to do and why the button state never matched the real FD.
    /// </summary>
    internal void ToggleFlightDirector(SimConnectManager simConnect, bool left)
        => PulseLVar(simConnect, left ? "A22X L Flight Director" : "A22X R Flight Director");

    /// <summary>Real FD state (FG CommBus), for the autopilot window's button labels.</summary>
    internal string FlightDirectorStateText(bool left)
        => A220.A220Afdx.FdStateText(left ? LatestAutoflight?.l_fd : LatestAutoflight?.r_fd);

    /// <summary>Normalize a heading delta to [-180, 180] so 340→020 walks +40, never -320.</summary>
    internal static double WrapHeadingDelta(double delta)
    {
        while (delta > 180) delta -= 360;
        while (delta < -180) delta += 360;
        return delta;
    }

    // ---- generic self-calibrating FCP knob walk ------------------------------

    /// <summary>
    /// Per-knob click size, remembered for the session and refined after every burst
    /// (step = distance actually moved / clicks actually sent). Two reasons this is not
    /// a constant: the altitude knob's fine/coarse ring and feet/metres mode change it
    /// live, and the day-one measurement was a non-obvious 492 ft per click. Keeping it
    /// across walks is what removes the old dedicated calibration round.
    /// </summary>
    private readonly Dictionary<string, double> _fcpStepCache = new();

    /// <summary>How long a burst needs to settle before the read-back is trustworthy,
    /// per SOURCE. The CommBus blocks publish at 20-30 Hz; the SimConnect cache only
    /// refreshes at 1 Hz, so a read taken on the live budget but served from the cache
    /// can predate — or fall inside — the burst it is supposed to be measuring. Any
    /// walk that downgrades live-to-cache mid-read must top its wait up to the cache
    /// figure before reading; see ReadAltAsync.</summary>
    private const int LiveSettleMs = 300;
    private const int FcpLiveSettleMs = 200;
    private const int CacheSettleMs = 1300;

    /// <summary>
    /// Self-calibrating FCP knob walk. No direct-set FCP events exist on this aircraft
    /// (inputs.mdx documents inc/dec only; HEADING_BUG_SET is sync-to-current-heading),
    /// so the value must be walked and verified — but the READ-BACK now comes from the
    /// aircraft's own "A22X.FCP Data" CommBus block at 30 Hz instead of the 1 Hz
    /// SimConnect batch. That is the fix for both complaints about this dialog:
    ///   * SLOW — each round used to spend 1.3 s waiting for a 1 Hz sample to refresh,
    ///     plus a whole extra round just to calibrate the step. Now a round settles in
    ///     ~0.2 s and the step is already known from last time.
    ///   * IMPRECISE — a round used to converge on whatever the 1 Hz batch happened to
    ///     hold, so a sample that had not refreshed yet was read as "the knob did not
    ///     move" and the step was re-measured from it.
    /// The SimConnect cache remains the fallback for a session with no Coherent link, and
    /// it is a SOUND one: measured live 2026-09-21, the stock AUTOPILOT ALTITUDE LOCK VAR,
    /// HEADING LOCK DIR and AIRSPEED HOLD VAR each track their FCP knob exactly, click for
    /// click. (An earlier note here claimed they were "NOT what the A220's FCP publishes";
    /// that was wrong and it mattered — it is why a session with no display link used to
    /// be pushed onto the inferior fallback walk.) Only the TIMING degrades there: the
    /// cache refreshes at 1 Hz, so each round keeps the old 1.3 s settle rather than
    /// converging on stale data. VERTICAL SPEED is the real exception — its knob is dead
    /// until VS/FPA mode is engaged, so its walk engages the mode first.
    /// </summary>
    /// <param name="liveRead">Reads this knob's value out of a fresh AFDX block.</param>
    /// <param name="note">Optional extra clause appended to the final announcement, given
    /// the landed value, whether it hit the target exactly, and the MEASURED step. The
    /// step is passed out because when a knob cannot reach a round number, its step size
    /// is the whole explanation — and a readout that says only "nearest selectable" leaves
    /// a blind pilot (and the next person debugging this) with nothing to go on.</param>
    private void StartFcpWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string readKey, string incEvent, string decEvent, double target, double tolerance,
        double? assumedStep, string valueName, Func<double, string> fmt,
        Func<A220.A220Afdx.LiveBlocks, double?>? liveRead = null, bool headingWrap = false,
        Func<double, bool, double, string>? note = null,
        Func<double, double>? cacheScale = null, string? cacheNote = null)
    {
        _walkCancel?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _walkCancel = cts;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                bool live = liveRead != null;

                // One read of the knob's value. Falls back to the SimConnect cache the
                // moment the live path misses, and latches OFF live for the rest of the
                // walk so the settle delay matches the source actually being read.
                async Task<double> ReadAsync()
                {
                    if (live && liveRead != null)
                    {
                        var blocks = await ReadAfdxLiveAsync();
                        if (blocks != null && liveRead(blocks) is { } v) return v;
                        live = false;
                        // Same top-up as the altitude walk: the short live settle has
                        // already elapsed, but the cache we are about to read only
                        // refreshes at 1 Hz, so reading now can sample mid-burst and
                        // feed the step estimator a value that is far too small.
                        await System.Threading.Tasks.Task.Delay(CacheSettleMs - FcpLiveSettleMs, cts.Token);
                    }
                    // The cache may hold the value in a DIFFERENT unit from the target
                    // (the stabilizer carrier is a 0-1 animation ratio against a target in
                    // EICAS units) — scale it, or the walk compares 0.25 to 4.3 and drives
                    // the control to its stop. Identity for every knob that needs none.
                    double raw = Cached(simConnect, readKey);
                    return cacheScale != null ? cacheScale(raw) : raw;
                }

                Task SettleAsync() =>
                    System.Threading.Tasks.Task.Delay(live ? FcpLiveSettleMs : CacheSettleMs, cts.Token);

                double lastStep = 0;
                void Finish(double value, bool exact)
                {
                    string text = $"{valueName} {fmt(value)}";
                    if (!exact) text += $" — nearest selectable to {fmt(target)}";
                    if (note != null) text += note(value, exact, lastStep);
                    // Say when the number came from the derived fallback rather than the
                    // aircraft's own bus: the pilot is entitled to know the authoritative
                    // readout is down, even though the walk still closed the loop.
                    if (!live && cacheNote != null) text += cacheNote;
                    announcer.AnnounceImmediate(text);
                }

                _fcpStepCache.TryGetValue(readKey, out double cachedStep);
                double? step = cachedStep > 0 ? cachedStep : assumedStep;

                // Which event moves the value UP is CALIBRATED, not assumed (the MD-11
                // selector lesson): if the first move goes the wrong way the two events
                // swap for the rest of the walk. Without this, an inverted control — the
                // stabilizer trim is the one that would really hurt — would be driven
                // further and further the wrong way, one confident burst after another.
                string upEvent = incEvent, downEvent = decEvent;

                // "This knob cannot get closer" is only believable once we have measured
                // the step ON THIS WALK. A step inherited from _fcpStepCache (or a bad
                // measurement from a previous burst) that is larger than the remaining
                // delta would otherwise make round 0 compute ZERO clicks and announce
                // "nearest selectable" WITHOUT EVER MOVING THE KNOB — which is exactly the
                // "I set 270 and it stopped at 239" report. Never quit before trying.
                bool measured = false;
                double prevDelta = double.NaN;
                bool calcFallback = false;

                double cur = await ReadAsync();
                for (int round = 0; round < 10 && !cts.IsCancellationRequested; round++)
                {
                    double delta = target - cur;
                    if (headingWrap) delta = WrapHeadingDelta(delta);
                    if (Math.Abs(delta) <= tolerance) { Finish(cur, exact: true); return; }

                    // Endgame guard: the knob STRADDLED the target — the last burst carried
                    // it past, so the target is between two selectable values and the one we
                    // are on is the closest. A SIGN CHANGE is what says that, not "this round
                    // got no closer", which is what this used to test.
                    //
                    // The difference is the whole "it often stops short" report. A round that
                    // simply UNDER-delivers (some clicks lost, or a step estimate that was too
                    // large) leaves the delta the same sign and merely smaller — or briefly
                    // not smaller at all — and the old test read that as "cannot get closer"
                    // and announced a value tens of degrees out as "nearest selectable to
                    // 137". Under-delivery now just costs another round, and there are ten.
                    // A genuine straddle still stops immediately: the metres-mode altitude
                    // selector steps ~492 ft, so 5085 → 6000 lands on 6069 and the sign flips.
                    if (measured && A220.A220Afdx.WalkStraddled(delta, prevDelta))
                    {
                        Finish(cur, exact: false);
                        return;
                    }
                    prevDelta = delta;

                    // AIM, never hunt. The knob only stops on multiples of its step from
                    // wherever it is, so the best reachable value is `round(delta/step)`
                    // clicks away — fire exactly that many and we land ON it. The old code
                    // used floor() and then crept the last bit one click at a time, which
                    // on a coarse step (the altitude selector in METRES steps ~492 ft)
                    // straddles the target forever: neither bracketing value is inside the
                    // tolerance, so it oscillated and reported a value hundreds of feet out.
                    // With no step estimate yet, the first move IS the calibration.
                    int clicks = step == null ? 1 : A220.A220Afdx.AimClicks(delta, step.Value);
                    if (clicks == 0)
                    {
                        // Nothing measured yet — the step is a guess or an inherited value,
                        // so try one click and find out rather than giving up untested.
                        if (!measured) clicks = 1;
                        else
                        {
                            // Already on the closest value this knob can select. Say so,
                            // with what was asked for, instead of pretending it landed.
                            Finish(cur, exact: false);
                            return;
                        }
                    }
                    await FireKnobBurstAsync(simConnect, delta > 0 ? upEvent : downEvent, clicks,
                                             cts.Token, calcFallback);
                    await SettleAsync();

                    double after = await ReadAsync();
                    double signed = after - cur;
                    if (headingWrap) signed = WrapHeadingDelta(signed);
                    double moved = Math.Abs(signed);
                    if (moved < 1e-6)
                    {
                        // Nothing moved. If that was the FIRST burst and it went out by
                        // TransmitClientEvent, the direct path may not reach this aircraft on
                        // this setup — drop to the calculator path (the transport every other
                        // A220 control uses) and try the same burst again before giving up.
                        if (!calcFallback && !measured)
                        {
                            calcFallback = true;
                            continue;
                        }
                        announcer.AnnounceImmediate($"{valueName} knob is not responding — value unchanged at {fmt(cur)}.");
                        return;
                    }
                    // Wrong way: swap the events and don't trust this round's step yet.
                    if (Math.Sign(signed) != Math.Sign(delta))
                        (upEvent, downEvent) = (downEvent, upEvent);
                    // Refine from what the clicks ACTUALLY did, so a changed ring/unit
                    // mode — or a sim that swallows part of a burst — self-corrects
                    // instead of walking on a stale step forever.
                    step = moved / clicks;
                    lastStep = step.Value;
                    _fcpStepCache[readKey] = step.Value;
                    measured = true;
                    cur = after;
                }
                if (!cts.IsCancellationRequested) Finish(cur, exact: false);
            }
            catch (OperationCanceledException) { }
            catch { }
        });
    }

    /// <summary>
    /// Walk the stabilizer trim to a target in the units the EICAS prints — the same
    /// number the EFB's takeoff-performance page gives you (its own code renders that
    /// figure as <c>abs(trimUnitsNoseUp).toFixed(1) + "UP"</c> from a weight/CG table).
    /// There is no set event and no trim L:var: `ELEV_TRIM_UP`/`_DN` are RATE inputs
    /// (documented as "drives the trim rate input this tick"), so this is the same
    /// calibrated walk as the FCP knobs, reading `efcs.pitch_trim` back off the CommBus.
    /// Direction is calibrated on the first move, never assumed — driving a stabilizer
    /// confidently the wrong way is the one failure here that actually matters.
    /// </summary>
    internal void StartStabTrimWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, double target)
    {
        // This used to REFUSE outright whenever the CommBus block was missing, on the
        // grounds that a stabilizer must never be driven open-loop. The premise was
        // right; the conclusion was wrong, and it is the FCP lesson over again (see the
        // note above about the cache being a SOUND fallback). There IS a second
        // read-back that needs no bus: "L:A22X Horizontal Stabilizer" is the trim
        // animation ratio over the SAME 0-17 unit travel the EICAS draws, so
        // ratio * 17 is the number the EICAS prints. Measured live 2026-09-22 against
        // the aircraft's own trim gauge, whose pointer is drawn with
        // `translate(0 -pitch_trim * (108/17))` up a 108 px scale from the ND end: at
        // rest the ratio read 0.2941176 = EXACTLY 5/17, and it tracked every burst of
        // ELEV_TRIM_DN monotonically down to 0.25343 = 4.31/17.
        //
        // It is a DERIVED value, not the bus's own, so it is announced as such — and it
        // is only ever the fallback: StartFcpWalk prefers `liveRead` whenever the bus is
        // up. The takeoff-range clause below degrades by itself (pitch_trim_up/_dn exist
        // only on the bus), so a fallback walk simply omits it rather than guessing it.
        StartFcpWalk(simConnect, announcer, "A22X_STAB_TRIM", "ELEV_TRIM_UP", "ELEV_TRIM_DN",
            target, 0.05, null, "Stabilizer trim", d => $"{d:0.0} units",
            liveRead: b => b.fc?.pitch_trim,
            cacheScale: r => r * A220.A220Afdx.StabTrimFullScaleUnits,
            cacheNote: " — read from the stabilizer position, because the data bus is not reporting the trim",
            note: (v, _, _) =>
            {
                var fc = LatestFlightControl;
                if (fc?.pitch_trim_dn is not { } dn || fc.pitch_trim_up is not { } up || Math.Abs(up - dn) <= 0.01)
                    return "";
                return v >= dn && v <= up
                    ? ", in takeoff range"
                    : $", still OUTSIDE takeoff range {dn:0.0} to {up:0.0}";
            });
    }

    /// <summary>
    /// Fire <paramref name="clicks"/> knob events, ONE PER CALL, paced.
    ///
    /// Sent by TransmitClientEvent (<see cref="SimConnectManager.SendEvent"/>), NOT the
    /// calculator path the rest of this definition uses, and measured on the live aircraft
    /// on 2026-09-21 before the change: three HEADING_BUG_INC fired back to back moved the
    /// bug 246 → 249, and six HEADING_BUG_DEC moved it 249 → 243. Every event landed. The
    /// knob events are plain stock K: events, so they need no MobiFlight module, and the
    /// direct path is better on both counts the walks care about:
    ///
    ///   * RELIABILITY. The calc path writes each click as a command string into
    ///     MobiFlight's single command area; a click written before the module has read the
    ///     previous one is simply lost, which is a walk that "often stops before reaching"
    ///     (2026-07-31). TransmitClientEvent has no such slot — SimConnect queues the
    ///     events and the aircraft's handler runs once per event.
    ///   * SPEED. No calc string to format, no seq prefix, no module round trip, so the
    ///     pacing comes down from 25 ms to 8 ms: a 108° heading change goes from ~2.7 s of
    ///     audible ticking to ~0.9 s.
    ///
    /// STILL ONE EVENT PER SEND. Do NOT batch several "(&gt;K:EVENT)" statements into one
    /// calc string: measured the same day, four <c>(&gt;K:AP_ALT_VAR_INC)</c> in ONE string
    /// moved the selector by exactly ONE click (2100 → 2200), because a single RPN
    /// evaluation latches the event once. That is a property of the STRING, not of the
    /// rate — separate events fired simultaneously all land, as the bursts above show.
    ///
    /// The calc path stays as the FALLBACK, chosen per walk: if the first burst moved
    /// nothing at all, every later burst in that walk goes back through
    /// <see cref="FireKeyEvent"/>. A setup where TransmitClientEvent does not reach this
    /// aircraft therefore degrades to exactly the old behaviour rather than to a dead knob.
    /// </summary>
    private async Task FireKnobBurstAsync(SimConnectManager simConnect, string eventName,
        int clicks, System.Threading.CancellationToken token, bool useCalcPath = false)
    {
        bool direct = !useCalcPath && simConnect.CanSendEvent;
        for (int sent = 0; sent < clicks && !token.IsCancellationRequested; sent++)
        {
            if (direct) simConnect.SendEvent(eventName);
            else FireKeyEvent(simConnect, eventName);
            await System.Threading.Tasks.Task.Delay(direct ? 8 : 25, token);
        }
    }

    // ---- Speed (Ctrl+S) ------------------------------------------------------

    private void ShowSpeedDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            // Outer ring: FMS SPD <-> MAN SPD. No documented input event exists (recon
            // R8) — best effort writes the annunciation enum; the 1.2 s label re-read
            // speaks the TRUTH, so a reverted write is heard as "still FMS/Manual".
            new("&FMS / Manual speed", () => Cached(simConnect, "A22X_FG_SPEED_MODE") > 0.5 ? "FMS" : "Manual",
                () => WriteLVar(simConnect, "A22X FG Speed Mode",
                        Cached(simConnect, "A22X_FG_SPEED_MODE") > 0.5 ? 0 : 1)),
            new("&Knots / Mach", () => Cached(simConnect, "A22X_AP_MACH_MODE") > 0.5 ? "Mach" : "Knots",
                () => FireKeyEvent(simConnect, "AP_MANAGED_SPEED_IN_MACH_TOGGLE")),
        };

        bool machNow = Cached(simConnect, "A22X_AP_MACH_MODE") > 0.5;
        string current = machNow
            ? $"Mach {Cached(simConnect, "A22X_AP_MACH"):0.00}"
            : $"{(int)Math.Round(Cached(simConnect, "A22X_AP_SPD"))} knots";
        var dialog = new ValueInputForm(
            "FCP Speed", $"speed (now {current})", "100-350 knots, or Mach 0.10-0.99 (e.g. .78)",
            announcer,
            input =>
            {
                if (!TryParseSpeed(input, out bool isMach, out double val))
                    return (false, "Enter knots 100-350 or Mach like .78");
                return (true, "");
            },
            toggles,
            input =>
            {
                if (!TryParseSpeed(input, out bool isMach, out double val)) return;
                if (isMach)
                    StartFcpWalk(simConnect, announcer, "A22X_AP_MACH", "AP_SPD_VAR_INC", "AP_SPD_VAR_DEC",
                        val, 0.005, null, "Speed", d => $"Mach {d:0.00}",
                        liveRead: b => b.fcp?.spd_sel_mach);
                else
                    StartFcpWalk(simConnect, announcer, "A22X_AP_SPD", "AP_SPD_VAR_INC", "AP_SPD_VAR_DEC",
                        val, 0.5, 1.0, "Speed", d => $"{d:F0} knots",
                        liveRead: b => b.fcp?.spd_sel_ias);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private static bool TryParseSpeed(string input, out bool isMach, out double value)
    {
        isMach = false;
        value = 0;
        input = input.Trim().ToUpperInvariant().TrimStart('M');
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
        if (value < 1.0)
        {
            isMach = true;
            return value >= 0.10 && value <= 0.99;
        }
        return value >= 100 && value <= 350;
    }

    // ---- Heading (Ctrl+H) ----------------------------------------------------

    private void ShowHeadingDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            // Full lateral-mode cluster (dialog completeness rule): the same
            // events/state keys as the Ctrl+P FCP window's HDG/NAV/APPR buttons.
            new("&HDG select mode", () => Cached(simConnect, "A22X_FG_HEADING") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_HDG_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_HEADING", "HDG mode", "HDG mode off"); }),
            new("&NAV mode", () => Cached(simConnect, "A22X_FG_LNAV") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_NAV1_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_LNAV", "NAV mode", "NAV mode off"); }),
            new("&Approach mode", () => Cached(simConnect, "A22X_FG_APPROACH") > 0.5 ? "Armed" : "Off",
                () => { FireKeyEvent(simConnect, "AP_APR_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_APPROACH", "Approach mode armed", "Approach mode off"); }),
            // Knob push = sync to current heading (HEADING_BUG_SET is documented as
            // sync, it takes no target).
            new("&Sync to current heading", () => "",
                () => FireKeyEvent(simConnect, "HEADING_BUG_SET")),
            new("Half &bank", () => Cached(simConnect, "A22X_FG_HALF_BANK") > 0.5 ? "On" : "Off",
                () => FireKeyEvent(simConnect, "AP_MAX_BANK_ANGLE_SET")),
        };

        int now = (int)Math.Round(Cached(simConnect, "A22X_AP_HDG"));
        var dialog = new ValueInputForm(
            "FCP Heading", $"heading (now {now:000})", "1-360 degrees", announcer,
            input => int.TryParse(input, out int h) && h >= 1 && h <= 360
                ? (true, "") : (false, "Enter a heading between 1 and 360"),
            toggles,
            input =>
            {
                if (!int.TryParse(input, out int hdg)) return;
                StartFcpWalk(simConnect, announcer, "A22X_AP_HDG", "HEADING_BUG_INC", "HEADING_BUG_DEC",
                    hdg % 360, 0.5, 1.0, "Heading", d => $"{(int)Math.Round(d == 0 ? 360 : d):000}",
                    liveRead: b => b.fcp?.hdg_sel, headingWrap: true);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // ---- Altitude (Ctrl+A) ---------------------------------------------------

    private void ShowAltitudeDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            // The book's default for altitude changes.
            new("F&LC", () => Cached(simConnect, "A22X_FG_FLC") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "FLIGHT_LEVEL_CHANGE");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_FLC", "FLC engaged", "FLC off"); }),
            new("&VNAV", () => Cached(simConnect, "A22X_FG_VNAV") > 0.5 ? "On" : "Off",
                () => { PulseLVar(simConnect, "A22X FG VNAV Toggle");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_VNAV", "VNAV on", "VNAV off"); }),
            new("Altitude &hold", () => Cached(simConnect, "A22X_FG_ALT") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_ALT_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_ALT", "Altitude hold engaged", "Altitude hold off"); }),
            // Label from the aircraft's OWN alt_in_m when we have it. The official docs
            // give no values for "L:A22X FG Altitude Unit" ("enum: feet or meters"), and a
            // label read off an assumed polarity is worse than none: it reported "Feet"
            // while the selector was measurably stepping 150 m / 50 m (live 2026-07-31).
            new("&Unit feet / meters",
                () => LatestFcp?.alt_in_m is { } m
                    ? (m ? "Meters" : "Feet")
                    : (Cached(simConnect, "A22X_FG_ALT_UNIT") > 0.5 ? "Meters (assumed)" : "Feet (assumed)"),
                () => WriteLVar(simConnect, "A22X FG Altitude Unit",
                        Cached(simConnect, "A22X_FG_ALT_UNIT") > 0.5 ? 0 : 1)),
            // The ALT knob's PUSH is the fine/coarse ring (Autopilot.xml: FCU_ALT_PUSH's
            // BTN_VAR is "L:A22X FG Altitude Fine" with BTN_TOGGLE). On coarse the selector
            // steps ~492 ft, so most round altitudes are simply not selectable — this is
            // the control that makes them reachable, and the walk selects it automatically.
            new("&Fine / coarse steps", () => Cached(simConnect, "A22X_FG_ALT_FINE") > 0.5 ? "Fine" : "Coarse",
                () => WriteLVar(simConnect, "A22X FG Altitude Fine",
                        Cached(simConnect, "A22X_FG_ALT_FINE") > 0.5 ? 0 : 1)),
        };

        int now = (int)Math.Round(Cached(simConnect, "A22X_AP_ALT"));
        // Name the metres mode UP FRONT. In metres the selector snaps to metric steps, so
        // a round number of feet usually isn't selectable and the result looks broken
        // rather than explained (live: asked 6000, got 5085 = 1550 m). The "Unit feet /
        // meters" button below is the same latched ring the real FCP has.
        string unitNote = LatestFcp?.alt_in_m == true
            ? " — FCP is in METRES; use the Unit button below for feet"
            : "";
        var dialog = new ValueInputForm(
            "FCP Altitude", $"altitude (now {now} feet){unitNote}", "0-41000 feet", announcer,
            input => int.TryParse(input, out int a) && a >= 0 && a <= 41000
                ? (true, "") : (false, "Enter an altitude between 0 and 41000 feet"),
            toggles,
            input =>
            {
                if (!int.TryParse(input, out int alt)) return;
                StartAltitudeSetAsync(simConnect, announcer, alt);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// Set the ALT selector: feet first, then a GRID walk — coarse ring for the
    /// thousands, fine ring for the hundreds, exactly like a pilot's hand.
    ///
    /// The selector SNAPS TO ITS GRID on every click (proven live 2026-08-10: 2625 ft +
    /// one coarse inc lands on 3000, not 3625; re-proven 2026-09-21: 2300 + one coarse
    /// inc lands on 3000), so in feet mode every multiple of 100 ft is exactly reachable:
    /// coarse clicks land on round thousands, fine clicks on round hundreds. Two
    /// consequences the pre-2026-08 measured walk got wrong:
    ///   * SPEED — it selected FINE up front and crept the WHOLE distance 100 ft at a
    ///     time (2000→35000 = 330 clicks ≈ 8+ s of "turning the wheel one by one").
    ///     Coarse covers the same distance in 33 clicks.
    ///   * ACCURACY — its cur+n×step model can't represent snapping, so any off-grid
    ///     start (metres-mode leftovers) "converged" to a value that was never on the
    ///     100 ft grid: the "always something close but not quite right" report.
    ///
    /// THREE THINGS WERE MEASURED LIVE ON 2026-09-21 (Synaptic A220-300, EGNT, engines
    /// running) and each one fixes a way this walk could report failure on a perfectly
    /// reachable altitude:
    ///
    ///   1. The stock AUTOPILOT ALTITUDE LOCK VAR DOES track the FCP selected altitude —
    ///      0 → 1000 → 2000 → 2100 → 2200 → 2300 → 3000 across single clicks, matching the
    ///      knob exactly. The old comment here ("the stock AUTOPILOT * VAR SimVars are NOT
    ///      what the A220's FCP publishes") was wrong for altitude, and it cost every
    ///      session with no Coherent display link the whole grid walk: ReadAfdxLiveAsync()
    ///      returning null dropped straight to the inferior measured fallback. The
    ///      read-back now PREFERS the 30 Hz CommBus and falls back to the 1 Hz SimConnect
    ///      cache, so the grid walk is the path EVERYONE gets — the display agent only
    ///      makes it faster. (Heading and speed track their stock vars too: 245→246 and
    ///      80→81 per click. AP_ALT_VAR_SET_ENGLISH is still inert — 12000 written, the
    ///      selector did not move — so the walk itself is still required.)
    ///
    ///   2. L:A22X FG Altitude Fine is a LEVEL, not a pulse: writing 1 gives 100 ft
    ///      clicks, writing 0 gives 1000 ft clicks, and the value persists across clicks
    ///      (the WASM resets it to 0 at power-up, not per click). So the ring writes below
    ///      are sound — but they were never VERIFIED, and a ring that did not flip makes
    ///      every later click a tenth, or ten times, the assumed size. The walk now
    ///      measures what a burst actually moved and re-selects the ring once if the step
    ///      is off by more than 2x, instead of grinding against the wrong grid until it
    ///      runs out of rounds.
    ///
    ///   3. The coarse stage must not stop "close enough". Its exit test was
    ///      |delta| &lt; step/2 — 500 ft on the coarse ring — while the final verdict
    ///      demands 50 ft. So an off-grid start within 500 ft of a round thousand fired
    ///      ZERO clicks and then reported "Altitude did not reach 5000 — selector reads
    ///      5085 feet", for a target one single click away. Every stage now exits only
    ///      when it is genuinely ON its value (5% of a step).
    ///
    /// FEET is ensured first by TRUTH where truth exists (write, re-read the aircraft's
    /// own alt_in_m, try the other polarity if it didn't flip): in metres the grid is
    /// metric and round feet genuinely don't exist. With no display link there is no truth
    /// to check, and feet is the overwhelmingly common case, so the walk proceeds and
    /// simply says nothing about units rather than refusing. The ring is restored to its
    /// prior state afterwards — the walk's ring-flipping is an implementation detail, not
    /// a cockpit selection the pilot made, so it must not leak.
    /// </summary>
    private void StartAltitudeSetAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, int alt)
    {
        _walkCancel?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _walkCancel = cts;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                string prep = "";
                try { prep = await EnsureAltitudeUnitFeetAsync(simConnect); }
                catch { /* best-effort; the walk still reports honestly */ }

                var blocks = await ReadAfdxLiveAsync();
                bool live = blocks?.fcp?.alt_sel_ft != null;

                // METRES is the one state that genuinely cannot do this, and only the
                // aircraft's own alt_in_m can say so. A link that is simply DOWN says
                // nothing either way — it must never be read as "in metres".
                if (blocks?.fcp?.alt_in_m == true)
                {
                    if (!cts.IsCancellationRequested)
                        StartAltitudeFallbackWalk(simConnect, announcer, alt, prep);
                    return;
                }
                if (cts.IsCancellationRequested) return;

                int target = (int)Math.Round(alt / 100.0) * 100;
                if (target != alt)
                    prep += $" — {alt} is between selector steps, aiming for {target}";

                // Read-back: the 30 Hz CommBus when the display agent is up, else the
                // 1 Hz SimConnect cache of AUTOPILOT ALTITUDE LOCK VAR, which tracks the
                // selector exactly (measured 2026-09-21). Latches OFF live on the first
                // miss so the settle delay always matches the source being read.
                async Task<double> ReadAltAsync()
                {
                    if (live)
                    {
                        var b = await ReadAfdxLiveAsync();
                        if (b?.fcp?.alt_sel_ft is { } v) return v;
                        live = false;
                        // The caller budgeted the SHORT (live) settle before this read,
                        // but we are now reading a 1 Hz cache — top the wait up to the
                        // cache settle. Without this, the first read after a downgrade
                        // lands only ~300 ms after a burst that itself takes ~180 ms, so
                        // the 1 Hz sample can be from MID-BURST: a 28000 -> 6000 walk
                        // read "12000", measured a bogus 727 ft per click from it and
                        // reported having stopped there (user report 2026-09-22).
                        await System.Threading.Tasks.Task.Delay(CacheSettleMs - LiveSettleMs, cts.Token);
                    }
                    return Cached(simConnect, "A22X_AP_ALT");
                }
                Task SettleAsync() =>
                    System.Threading.Tasks.Task.Delay(live ? LiveSettleMs : CacheSettleMs, cts.Token);

                // Ring bookkeeping: coarse for the thousands, fine for the hundreds, the
                // pilot's own ring selection put back afterwards.
                bool ringWasFine = Cached(simConnect, "A22X_FG_ALT_FINE") > 0.5;
                bool ringNowFine = ringWasFine;
                async Task SelectRingAsync(bool fine)
                {
                    WriteLVar(simConnect, "A22X FG Altitude Fine", fine ? 1 : 0, quiet: true);
                    ringNowFine = fine;
                    await System.Threading.Tasks.Task.Delay(250, cts.Token);
                }

                double cur = blocks?.fcp?.alt_sel_ft ?? Cached(simConnect, "A22X_AP_ALT");
                int t1000 = (int)Math.Round(target / 1000.0, MidpointRounding.AwayFromZero) * 1000;

                // Walk one grid stage. `step` is what a click is EXPECTED to move; if a
                // burst says otherwise the ring did not take, so re-select it once and
                // carry on rather than grinding against the wrong grid (finding 2).
                async Task<bool> WalkToAsync(int walkTarget, double step, bool wantFine)
                {
                    bool ringRetried = false;
                    // 5% of a step, not half of one: "close enough" on the coarse ring
                    // left the value up to 499 ft out and then failed the 50 ft verdict
                    // (finding 3).
                    double reached = step * 0.05;
                    for (int round = 0; round < 6 && !cts.IsCancellationRequested; round++)
                    {
                        if (Math.Abs(walkTarget - cur) <= reached) return true;
                        int clicks = A220.A220Afdx.GridClicks(cur, walkTarget, step);
                        if (clicks == 0) return Math.Abs(walkTarget - cur) <= reached;
                        bool up = walkTarget > cur;
                        await FireKnobBurstAsync(simConnect,
                            up ? "AP_ALT_VAR_INC" : "AP_ALT_VAR_DEC", clicks, cts.Token);
                        await SettleAsync();

                        double after = await ReadAltAsync();
                        double moved = Math.Abs(after - cur);
                        cur = after;
                        if (moved < 1e-6) return false;   // knob not responding

                        double perClick = moved / clicks;
                        if (!ringRetried && A220.A220Afdx.StepIsWrongRing(perClick, step))
                        {
                            ringRetried = true;
                            await SelectRingAsync(wantFine);
                        }
                    }
                    return Math.Abs(walkTarget - cur) <= reached;
                }

                await SelectRingAsync(fine: false);
                bool ok = await WalkToAsync(t1000, 1000, wantFine: false);
                if (ok && target != t1000)
                {
                    await SelectRingAsync(fine: true);
                    ok = await WalkToAsync(target, 100, wantFine: true);
                }
                if (ringNowFine != ringWasFine)
                    WriteLVar(simConnect, "A22X FG Altitude Fine", ringWasFine ? 1 : 0, quiet: true);

                if (cts.IsCancellationRequested) return;
                int landed = (int)Math.Round(cur);
                announcer.AnnounceImmediate(ok && Math.Abs(landed - target) < 50
                    ? $"Altitude {landed} feet{prep}"
                    : $"Altitude did not reach {target} — selector reads {landed} feet{prep}");
            }
            catch (OperationCanceledException) { }
            catch { }
        });
    }

    /// <summary>
    /// The pre-2026-08 measured walk, now reached in exactly ONE case: the selector is
    /// in METRES and would not switch to feet. There the grid is metric, so no round
    /// number of feet is on it and the grid walk has nothing to aim at — this selects
    /// FINE (so at least some round values are reachable), converges on whatever the
    /// knob will give, and names the step and the metres mode in the result instead of
    /// pretending. A session with merely no Coherent link no longer comes here: the
    /// stock SimVar is a real read-back (measured 2026-09-21), so it gets the grid walk
    /// like everyone else, just with a slower settle.
    /// </summary>
    private void StartAltitudeFallbackWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        int alt, string prep)
    {
        if (Cached(simConnect, "A22X_FG_ALT_FINE") < 0.5)
        {
            WriteLVar(simConnect, "A22X FG Altitude Fine", 1);
            prep += ", fine steps selected";
        }
        string note = prep;
        StartFcpWalk(simConnect, announcer, "A22X_AP_ALT", "AP_ALT_VAR_INC", "AP_ALT_VAR_DEC",
            alt, 55, null, "Altitude", d => $"{(int)Math.Round(d)} feet",
            liveRead: b => b.fcp?.alt_sel_ft,
            note: (v, exact, stepFt) =>
            {
                string s = note;
                var fcp = LatestFcp;
                if (fcp?.alt_in_m == true)
                    s += $". FCP altitude is in metres — {(int)Math.Round(fcp.alt_sel_m ?? v * 0.3048)} metres selected";
                // When the knob CANNOT reach the requested altitude, its step size is
                // the entire explanation — and both the pilot and the next person
                // debugging this need it stated, not inferred from two attempts.
                if (!exact && stepFt > 0)
                {
                    s += $". Selector step {stepFt:0} feet";
                    if (fcp?.alt_sel_m is { } m) s += $", selector reads {m:0} metres";
                    if (fcp?.alt_in_m == false) s += ", units are feet";
                }
                return s;
            });
    }

    /// <summary>
    /// Put the ALT selector in FEET, verifying against the aircraft's own
    /// <c>alt_in_m</c> rather than trusting a polarity for
    /// <c>L:A22X FG Altitude Unit</c> that the official docs never state ("enum: feet or
    /// meters", no values). Writes one value, checks, and writes the other if that was the
    /// wrong way round. Returns the clause to append to the result — including an honest
    /// "could not switch" when the write does not take, and nothing at all when the
    /// display link is down and there is no truth to check against.
    /// </summary>
    private async Task<string> EnsureAltitudeUnitFeetAsync(SimConnectManager simConnect)
    {
        var blocks = await ReadAfdxLiveAsync();
        if (blocks?.fcp?.alt_in_m is not { } inMetres) return "";   // no truth available — say nothing
        if (!inMetres) return "";

        foreach (double candidate in new[] { 0.0, 1.0 })
        {
            WriteLVar(simConnect, "A22X FG Altitude Unit", candidate);
            await System.Threading.Tasks.Task.Delay(400);
            var after = await ReadAfdxLiveAsync();
            if (after?.fcp?.alt_in_m == false) return ", altitude units switched to feet";
        }
        return ", but the selector is in METRES and would not switch — a round number of feet is not selectable in that mode";
    }

    // ---- VS / FPA (Ctrl+V) ---------------------------------------------------

    private void ShowVerticalDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            new("&VS mode", () => Cached(simConnect, "A22X_FG_VS") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_VS_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_VS", "Vertical speed mode engaged", "Vertical speed mode off"); }),
            new("&FPA mode", () => Cached(simConnect, "A22X_FG_FPA") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_ATT_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_FPA", "Flight path angle mode engaged", "Flight path angle mode off"); }),
        };

        int vsNow = (int)Math.Round(Cached(simConnect, "A22X_AP_VS"));
        double fpaNow = Cached(simConnect, "A22X_SELECTED_FPA");
        var dialog = new ValueInputForm(
            "FCP Vertical", $"target (VS now {vsNow} fpm, FPA {fpaNow:0.0}°)",
            "VS -8000 to 8000 fpm, or FPA -9.9 to 9.9 (decimal = FPA)", announcer,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double t))
                    return (false, "Enter a vertical speed or a flight path angle");
                if (Math.Abs(t) <= 9.9) return (true, "");
                return Math.Abs(t) <= 8000 && Math.Abs(t) >= 100
                    ? (true, "") : (false, "VS must be between -8000 and 8000");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double t)) return;
                StartVerticalTargetSetAsync(simConnect, announcer, t, isFpa: Math.Abs(t) <= 9.9);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// ENGAGE the vertical mode first, THEN walk the wheel — the A220's VS thumbwheel is
    /// DEAD while neither VS nor FPA mode is engaged (proven live 2026-08-10: with no
    /// vertical mode, AP_VS_VAR_INC changes nothing and the FCP publishes vs_sel = null;
    /// after AP_VS_HOLD the very next click moves it). The old walk fired clicks at the
    /// dead wheel, read null back, and fell through to the stock SimVar — which this FCP
    /// never writes for VS — so "setting a VS" quietly did nothing. Same lesson as the
    /// LVFR A330's engage-before-write rule. The engagement is spoken in the result and
    /// the FG monitor announces the mode transition itself.
    /// </summary>
    private void StartVerticalTargetSetAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        double target, bool isFpa)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                string engagedNote = "";
                string modeKey = isFpa ? "A22X_FG_FPA" : "A22X_FG_VS";
                if (Cached(simConnect, modeKey) < 0.5)
                {
                    FireFcpEvent(simConnect, isFpa ? "AP_ATT_HOLD" : "AP_VS_HOLD");
                    engagedNote = isFpa ? ", FPA mode selected" : ", vertical speed mode selected";
                    await System.Threading.Tasks.Task.Delay(900);
                }

                // If the aircraft's own FCP block says the wheel STILL has no target, the
                // mode did not engage (FG refused it) — walking now would converge on a
                // stale fallback value, so say what actually happened instead.
                var blocks = await ReadAfdxLiveAsync();
                if (!isFpa && blocks?.fcp != null && blocks.fcp.vs_sel == null)
                {
                    announcer.AnnounceImmediate(
                        "The vertical speed wheel has no target — VS mode did not engage. " +
                        "Check flight director or autopilot state, then try again.");
                    return;
                }

                string note = engagedNote;
                if (isFpa)
                {
                    // FPA target — the thumbwheel drives FPA while FPA mode is engaged.
                    // (vs_sel's scale in FPA mode is unverified, so the read-back stays on
                    // the L:var until a live flight proves the CommBus value is degrees.)
                    StartFcpWalk(simConnect, announcer, "A22X_SELECTED_FPA", "AP_VS_VAR_INC", "AP_VS_VAR_DEC",
                        target, 0.06, 0.1, "Flight path angle", d => $"{d:0.0} degrees",
                        note: (_, _, _) => note);
                }
                else
                {
                    StartFcpWalk(simConnect, announcer, "A22X_AP_VS", "AP_VS_VAR_INC", "AP_VS_VAR_DEC",
                        target, 55, 100, "Vertical speed", d => $"{(int)Math.Round(d)} feet per minute",
                        liveRead: b => b.fcp?.vs_mode == 0 ? b.fcp?.vs_sel : null,
                        note: (_, _, _) => note);
                }
            }
            catch { }
        });
    }

    // ---- Baro (Ctrl+B) -------------------------------------------------------

    private void ShowBaroDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            new("&Standard", () =>
                Math.Abs(Cached(simConnect, "A22X_KOHLSMAN", 29.92) - 29.92) < 0.005 ? "STD" : "QNH",
                () => PulseLVar(simConnect, "A22X L Altimeter STD")),
            // READ-ONLY. This used to WRITE "A22X L Altimeter HPA" to flip the unit, and
            // that was wrong twice over (measured live 2026-09-22):
            //   1. It does nothing. The var is the WASM's OUTPUT mirror — Synaptic's own
            //      simvars.mdx says "READS whether the CTP's ... setting is displayed in
            //      hPa", while every neighbour (Altimeter Set/STD, Nav Source) says
            //      "Read/write". Nothing consumes it: the displays take the unit from the
            //      CTP CommBus store, and the aircraft exposes no JS->WASM baro channel
            //      (the whole .call("A22X.…") surface is Display Tune / Refuel / Resync /
            //      SSPC). B:CTP_BARO_MODE_1_Set and _Toggle were tried live and do nothing.
            //   2. It CORRUPTED OUR OWN READOUT. The write sticks (the var held a written
            //      1 for 13 s, unopposed — the WASM republishes only when ITS unit
            //      changes), and HotkeyAction.ReadAltimeter picks its unit from this very
            //      var. So one press made the altimeter read-out announce "hectopascals"
            //      while the aircraft was displaying inches — a wrong unit on an altimeter.
            // Never write it again without a PROVEN input. Left as a status line so the
            // pilot can still hear which unit the CTP is in.
            new("&Units (read-only)", () => Cached(simConnect, "A22X_L_BARO_HPA") > 0.5 ? "Hectopascals" : "Inches",
                () => announcer.AnnounceImmediate(
                    (Cached(simConnect, "A22X_L_BARO_HPA") > 0.5 ? "Hectopascals" : "Inches")
                    + ". The A220 exposes no way to change the altimeter unit from outside the cockpit;"
                    + " it is set in the aircraft's own options, not here.")),
            new("First Officer S&TD", () => "",
                () => PulseLVar(simConnect, "A22X R Altimeter STD")),
        };

        double mbNow = Cached(simConnect, "A22X_KOHLSMAN_MB", 1013);
        var dialog = new ValueInputForm(
            "Altimeter Setting", $"QNH (now {mbNow:F0} hPa)", "940-1090 hPa or 27.00-32.00 inches",
            announcer,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double q))
                    return (false, "Enter a QNH in hPa or inches");
                bool ok = (q >= 940 && q <= 1090) || (q >= 27 && q <= 32);
                return ok ? (true, "") : (false, "QNH must be 940-1090 hPa or 27.00-32.00 inches");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double q)) return;
                double mb = q >= 100 ? q : q * 33.8639;
                SetBaroWithVerification(simConnect, announcer, mb);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// KOHLSMAN_SET (mb × 16) first; if the custom CTP swallowed it, fall back to
    /// walking the CTP's own knob-delta L:var, one seq-prefixed click per write.
    /// The read-back announces the value that actually landed.
    /// </summary>
    private void SetBaroWithVerification(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, double targetMb)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                FireKeyEvent(simConnect, "KOHLSMAN_SET", (long)Math.Round(targetMb * 16));
                await System.Threading.Tasks.Task.Delay(1300);
                double mb = Cached(simConnect, "A22X_KOHLSMAN_MB", 0);
                if (Math.Abs(mb - targetMb) <= 0.6)
                {
                    announcer.AnnounceImmediate($"Altimeter {mb:F0} hectopascals, {Cached(simConnect, "A22X_KOHLSMAN", 29.92):F2} inches");
                    return;
                }
                // Fallback: CTP knob deltas (read/write increment applied per tick).
                for (int round = 0; round < 30; round++)
                {
                    mb = Cached(simConnect, "A22X_KOHLSMAN_MB", 0);
                    double delta = targetMb - mb;
                    if (Math.Abs(delta) <= 0.6) break;
                    WriteLVar(simConnect, "A22X L Altimeter Set", Math.Sign(delta), quiet: true);
                    await System.Threading.Tasks.Task.Delay(150);
                }
                await System.Threading.Tasks.Task.Delay(1100);
                mb = Cached(simConnect, "A22X_KOHLSMAN_MB", 0);
                announcer.AnnounceImmediate(Math.Abs(mb - targetMb) <= 0.6
                    ? $"Altimeter {mb:F0} hectopascals"
                    : $"Altimeter did not take the setting — now {mb:F0} hectopascals. Use the Standard button or the cockpit knob.");
            }
            catch { }
        });
    }

    // ---- NAV radios (Ctrl+N) -------------------------------------------------

    private Forms.A220.A220RadiosForm? _radiosForm;

    /// <summary>
    /// Ctrl+N: the A220 radios window — captain course, nav source, NAV 1/2 preset
    /// frequencies and AUTO/MAN tuning, all through the aircraft's own CommBus calls
    /// (A220RadiosForm). Replaced the stock NAV*_STBY_SET dialog, which had no course,
    /// nav-source or AUTO/MAN control.
    /// </summary>
    private void ShowNavRadiosDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        _sim = simConnect;
        if (_radiosForm != null && !_radiosForm.IsDisposed)
        {
            _radiosForm.Activate();
            return;
        }
        _radiosForm = new Forms.A220.A220RadiosForm(
            DisplaysAgentCallAsync,
            () => PulseLVar(simConnect, "A22X L Nav Source"),
            announcer);
        _ = _radiosForm.OpenAsync();
    }

    private void DisposeRadiosForm()
    {
        try { _radiosForm?.Dispose(); } catch { }
        _radiosForm = null;
    }
}

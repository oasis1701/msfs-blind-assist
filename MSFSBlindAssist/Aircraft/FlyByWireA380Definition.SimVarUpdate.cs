using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class FlyByWireA380Definition
{
    private void MaybeAnnounceTcasRaGuidance(ScreenReaderAnnouncer announcer)
    {
        string? text = _tcasRa.ComposeIfChanged();
        if (text == null) return;
        // Mute rides the TCAS_STATE monitor entry — one Ctrl+M checkbox governs
        // both the state announce and the composed guidance.
        if (!Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains("A32NX_TCAS_STATE"))
            announcer.AnnounceImmediate(text);
    }

    // FBW TLA detents: IDLE 0, CLB 25, FLX/MCT 35, TOGA 45, reverse negative.
    private static string? TlaDetent(double v) =>
        Math.Abs(v) < 1.5 ? "Idle" :
        Math.Abs(v - 25) < 2.5 ? "Climb" :
        Math.Abs(v - 35) < 2.5 ? "Flex M C T" :
        Math.Abs(v - 45) < 2.5 ? "TOGA" :
        v <= -15 ? "Maximum reverse" :
        v < -2 ? "Reverse idle" : null;

    private static string UnpackSixBitIdent(double w0, double w1)
    {
        double[] words = { w0, w1 };
        string s = "";
        for (int i = 0; i < words.Length * 8; i++)
        {
            int code = (int)(words[i / 8] / Math.Pow(2, (i % 8) * 6)) & 0x3F;
            if (code > 0) s += (char)(code + 31);
        }
        return s.Trim();
    }

    private static string DecodeArmedModes(int v, (int bit, string name)[] bits)
    {
        var names = new List<string>();
        foreach (var b in bits) if ((v & b.bit) != 0) names.Add(b.name);
        return string.Join(", ", names);
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Cache the ND TO-waypoint packed-word halves for the ND status box decode
        // (no announcement; fall through to normal processing).
        if (varName == "A32NX_EFIS_L_TO_WPT_IDENT_0") _ndIdent0 = value;
        else if (varName == "A32NX_EFIS_L_TO_WPT_IDENT_1") _ndIdent1 = value;
        else if (varName == "A32NX_BETA_TARGET_ACTIVE") _betaTargetActive = value > 0.5;

        // Passengers on board: each station L:var is an integer seat-bitmask (set bit =
        // filled seat). Popcount it, cache per station, and keep the running total.
        // Value < 2^53 (≤ 50 seats/station), so (long)value is exact. Never announced.
        if (varName.StartsWith("A32NX_PAX_", StringComparison.Ordinal) && varName.IndexOf("_DESIRED", StringComparison.Ordinal) < 0)
        {
            _paxFilledByStation[varName] = System.Numerics.BitOperations.PopCount((ulong)(long)Math.Round(value));
            int total = 0;
            foreach (var c in _paxFilledByStation.Values) total += c;
            _paxOnBoard = total;
            return true;
        }

        // Icing conditions — A32NX_ICING_STATE_ICING_STICK_INDICATOR is the cockpit
        // ice-accretion "stick" (the visual ice-evidence probe): a CONTINUOUS 0..1
        // ratio (ice builds over ~120 s in icing conditions, melts over ~200 s), NOT a
        // 0/1 flag. The old Mon {0:None,1:Icing} mapping never matched the fractional
        // value, so the generic monitor spoke the raw number on EVERY change — the
        // "Icing Conditions 0.1, 0.2, …" spam. Convert to a debounced discrete state and
        // announce ONLY the transition. Honours the Ctrl+M "Icing Conditions" mute; the
        // panel readout shows the live percentage via TryGetDisplayOverride. Returning
        // true suppresses the generic raw-value announce.
        if (varName == "A32NX_ICING_STATE_ICING_STICK_INDICATOR")
        {
            bool nowIcing = _icingActive ? value > ICING_CLEAR_RATIO : value >= ICING_DETECT_RATIO;
            if (!_icingBaselineDone) { _icingActive = nowIcing; _icingBaselineDone = true; return true; }
            if (nowIcing != _icingActive)
            {
                _icingActive = nowIcing;
                if (!Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                    announcer.Announce(nowIcing ? "Icing conditions" : "Icing conditions cleared");
            }
            return true;
        }

        // RMP active VHF frequency — auto-announce on change so a blind pilot hears the new
        // ACTIVE frequency after a transfer/swap (the standby stays an on-request panel readout,
        // so this is the single non-duplicate call-out). The register is raw Hz; range-gate to a
        // valid VHF band (118.000–136.975 MHz) so the UNINITIALISED value the FBW RMP holds while
        // unpowered (reads ~19 MHz) is cached silently and never spoken. Honours the Ctrl+M mute.
        // RELIABLE VHF freq auto-announce off the stock COM simvars (the FBW_RMP_FREQUENCY L:vars
        // read garbage). value is MHz; gate to the VHF band so an uninitialised 0 stays silent.
        // Active change = "VHF n active 121.900" (after a swap); standby change = "VHF n standby …"
        // (the autocomplete/load read-back). prev>0 skips the first-seen baseline. Honours Ctrl+M.
        if (varName.StartsWith("COM_ACTIVE_", StringComparison.Ordinal))
        {
            string ch = varName.Substring("COM_ACTIVE_".Length);
            bool plausible = value >= 118.0 && value <= 137.0;
            bool changed = !_comActiveFreq.TryGetValue(ch, out var prev) || Math.Abs(prev - value) > 0.0004;
            _comActiveFreq[ch] = value;
            if (plausible && changed && prev > 0
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                announcer.Announce($"VHF {ch} active {value:0.000}");
            return true;
        }
        if (varName.StartsWith("COM_STANDBY_", StringComparison.Ordinal))
        {
            string ch = varName.Substring("COM_STANDBY_".Length);
            bool plausible = value >= 118.0 && value <= 137.0;
            bool changed = !_comStandbyFreq.TryGetValue(ch, out var prev) || Math.Abs(prev - value) > 0.0004;
            _comStandbyFreq[ch] = value;
            if (plausible && changed && prev > 0
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                announcer.Announce($"VHF {ch} standby {value:0.000}");
            return true;
        }
        // Transponder squawk auto-announce: TRANSPONDER CODE:1 reads a BCD16 word; decode (same as
        // the display) and speak "Squawk 1234" whenever it changes. _lastSquawkBcd<0 skips the first.
        if (varName == "XPNDR_CODE")
        {
            int bcd = (int)Math.Round(value);
            if (bcd != _lastSquawkBcd)
            {
                bool first = _lastSquawkBcd < 0;
                bool formSet = bcd == _formSetSquawkBcd;   // the RMP window set this code and already spoke it
                _lastSquawkBcd = bcd;
                if (formSet) _formSetSquawkBcd = -1;        // consume
                if (!first && !formSet && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                    announcer.Announce($"Squawk {(bcd >> 12) & 0xF}{(bcd >> 8) & 0xF}{(bcd >> 4) & 0xF}{bcd & 0xF}");
            }
            return true;
        }
        if (varName.StartsWith("FBW_RMP_FREQUENCY_ACTIVE_", StringComparison.Ordinal))
        {
            string ch = varName.Substring("FBW_RMP_FREQUENCY_ACTIVE_".Length);
            double mhz = value / 1_000_000.0;
            bool plausible = mhz >= 118.0 && mhz <= 137.0;
            bool changed = !_rmpActiveFreq.TryGetValue(ch, out var prev) || Math.Abs(prev - value) > 0.5;
            _rmpActiveFreq[ch] = value;
            if (plausible && changed && prev != 0
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                announcer.Announce($"VHF {ch} active {mhz:0.000}");
            return true;
        }

        // Doors — read-only auto-announce. Passenger doors are INTERACTIVE POINT OPEN, a 0..1
        // animation fraction (open > 0.05); cargo doors are the inverted LOCKED L:var (1 = locked
        // = closed). Announce Open/Closed once per transition (honours the Ctrl+M mute).
        if (varName.StartsWith("A380X_MSFSBA_DOOR_", StringComparison.Ordinal)
            || varName.StartsWith("A380X_MSFSBA_CARGO_", StringComparison.Ordinal))
        {
            foreach (var dd in _doorDefs)
            {
                if (dd.Key != varName) continue;
                bool open = dd.CargoLocked ? value < 0.5 : value > 0.05;
                bool? prev = _doorOpen.TryGetValue(varName, out var pv) ? pv : null;
                _doorOpen[varName] = open;
                if (prev.HasValue && prev.Value != open
                    && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                {
                    // The fuel-truck service point (interactive point 18) reads more naturally as
                    // connected/disconnected than open/closed; doors + the cargo hatch keep open/closed.
                    string verb = varName == "A380X_MSFSBA_DOOR_18"
                        ? (open ? "connected" : "disconnected")
                        : (open ? "open" : "closed");
                    announcer.Announce($"{dd.Name} {verb}");
                }
                break;
            }
            return true;
        }

        // Aircraft-preset load progress (flyPad loads the preset; MSFSBA narrates it). The L:var
        // runs 0..1 while loading then resets to 0. Announce each 10% milestone once, "complete"
        // at 100%, and stay silent at idle (0). Honours the Ctrl+M mute.
        if (varName == "A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS")
        {
            int pct = value <= 1.0 ? (int)Math.Round(value * 100) : (int)Math.Round(value);
            pct = Math.Max(0, Math.Min(100, pct));
            if (pct <= 0) { _presetBucket = -1; return true; }   // idle / reset — silent
            if (!Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
            {
                if (pct >= 100)
                {
                    if (_presetBucket < 100) { _presetBucket = 100; announcer.Announce("Aircraft preset loading complete"); }
                }
                else
                {
                    int bucket = (pct / 10) * 10;
                    if (bucket > _presetBucket) { _presetBucket = bucket; announcer.Announce($"Aircraft preset loading {bucket} percent"); }
                }
            }
            return true;
        }

        // Speed-brake handle: a 0..1 fraction. Announce by 10% band (with Retracted/Full
        // at the ends) so a steady lever doesn't spam, but movement is spoken. Silent
        // baseline on the first sample. (Speculative A380 addition.)
        if (varName == "A32NX_SPOILERS_HANDLE_POSITION")
        {
            int band = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, value)) * 10.0);
            if (_lastSpoilerBand < 0) { _lastSpoilerBand = band; return true; }
            if (band != _lastSpoilerBand)
            {
                _lastSpoilerBand = band;
                string phrase = band == 0 ? "Speed brake retracted"
                              : band == 10 ? "Speed brake full"
                              : $"Speed brake {band * 10} percent";
                announcer.Announce(phrase);
            }
            return true;
        }

        // FMA armed modes — decode the legacy bitmask and announce NEWLY-armed modes
        // on change (so arming ALT/NAV speaks "Altitude armed"/"NAV armed"). Parity
        // with the A32NX, which the A380 previously lacked (it was read-only).
        if (varName == "A32NX_FMA_VERTICAL_ARMED" || varName == "A32NX_FMA_LATERAL_ARMED")
        {
            bool vert = varName == "A32NX_FMA_VERTICAL_ARMED";
            int iv = (int)Math.Round(value);
            int prev = vert ? _prevVertArmed : _prevLatArmed;
            if (vert) _prevVertArmed = iv; else _prevLatArmed = iv;
            if (prev >= 0 && (iv & ~prev) != 0
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
            {
                string nm = DecodeArmedModes(iv & ~prev, vert ? _vertArmedBits : _latArmedBits);
                if (!string.IsNullOrEmpty(nm))
                    foreach (var one in nm.Split(new[] { ", " }, StringSplitOptions.None))
                        announcer.Announce($"{one} armed");
            }
            return true;
        }
        // Flight phase — match the A32NX "Entering X phase" wording (was the generic
        // "Flight Phase: X" via the monitor).
        if (varName == "A32NX_FMGC_FLIGHT_PHASE")
        {
            _fmgcPhaseA380 = (int)Math.Round(value);
            if (_varCache != null && _varCache.TryGetValue(varName, out var fpDef)
                && fpDef.ValueDescriptions != null && fpDef.ValueDescriptions.TryGetValue(value, out var phase)
                && _lastFlightPhaseA380 != phase)
            {
                _lastFlightPhaseA380 = phase;
                if (!Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                    announcer.Announce($"Entering {phase} phase");
            }
            return true;
        }
        // ARINC429 enum guard. Several FBW discretes (e.g. APU_LOW_FUEL_PRESSURE_FAULT,
        // written `write_arinc429`) come through as a huge SSM-encoded word (e.g.
        // 12884901888 = 0x3_00000000 = SSM NormalOp, payload 0) that matches no entry
        // in the var's 0/1 ValueDescriptions, so the generic announce would say
        // "<label>: 12884901888". Decode any ANNOUNCED enum var whose raw value is
        // ARINC-large (>= 2^32 -> an SSM is present) to its payload (0/1) and announce
        // the mapped state ON CHANGE only (default prev = 0 so the initial no-fault is
        // silent). Honours the Ctrl+M mute; returns true to suppress the raw announce.
        if (value >= 4294967296.0 && _varCache != null
            && _varCache.TryGetValue(varName, out var arDef)
            && arDef.IsAnnounced
            && arDef.ValueDescriptions is { Count: > 0 } arDesc)
        {
            int st = (int)Math.Round(new SimConnect.Arinc429Word(value).ValueOr(0f));
            int prevSt = _arincEnumState.TryGetValue(varName, out var ps) ? ps : 0;
            _arincEnumState[varName] = st;
            if (st != prevSt
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName)
                && arDesc.TryGetValue(st, out var sdesc))
                announcer.Announce($"{arDef.DisplayName}: {sdesc}");
            return true;
        }
        // Keep the live current state of the FCU engage/mode toggles so their
        // combos can decide whether a "set" needs to fire the toggle event. Fall
        // through so the base still auto-announces the state change.
        if (_fcuToggleEvents.ContainsKey(varName)) _fcuStateCache[varName] = value;
        // Thrust lever angle -> announce the DETENT when it changes (not the raw
        // angle, which would spam). FBW TLA detents: IDLE 0, CLB 25, FLX/MCT 35,
        // TOGA 45, reverse negative. Only speak when the lever is AT a detent so
        // mid-travel doesn't false-announce. Returns true to suppress the raw value.
        if (varName.StartsWith("A32NX_AUTOTHRUST_TLA:", StringComparison.Ordinal))
        {
            int eng = varName[varName.Length - 1] - '0';
            if (eng >= 1 && eng <= 4)
            {
                _tla[eng - 1] = value;
                string? det = TlaDetent(value);
                // Establish the baseline SILENTLY on first start: until all four
                // levers have reported once, just record their detents and don't
                // announce — otherwise MSFSBA reads out "Thrust lever 1 Idle …
                // Thrust lever 4 Idle, All thrust levers Idle" every startup. Only
                // real detent CHANGES after the baseline are announced.
                if (!_tlaBaselineDone)
                {
                    if (det != null) _lastEngDetent[eng - 1] = det;
                    bool allSeen = true;
                    for (int i = 0; i < 4; i++) if (double.IsNaN(_tla[i])) { allSeen = false; break; }
                    if (allSeen)
                    {
                        _tlaBaselineDone = true;
                        string? d0 = TlaDetent(_tla[0]);
                        bool same = d0 != null;
                        for (int i = 1; i < 4 && same; i++) if (TlaDetent(_tla[i]) != d0) same = false;
                        _lastAllDetent = same ? d0 : null;
                    }
                    return true;
                }
                if (det != null)
                {
                    // When all four levers sit at the same detent (the usual case)
                    // announce once for "all"; when split, announce the engine that
                    // moved. Mid-travel (det == null) is silent.
                    bool allSame = true;
                    for (int i = 0; i < 4; i++)
                        if (double.IsNaN(_tla[i]) || TlaDetent(_tla[i]) != det) { allSame = false; break; }
                    if (allSame)
                    {
                        if (det != _lastAllDetent)
                        {
                            _lastAllDetent = det;
                            for (int i = 0; i < 4; i++) _lastEngDetent[i] = det;
                            announcer.Announce($"All thrust levers {det}");
                        }
                    }
                    else if (det != _lastEngDetent[eng - 1])
                    {
                        _lastEngDetent[eng - 1] = det;
                        _lastAllDetent = null;
                        announcer.Announce($"Thrust lever {eng} {det}");
                    }
                }
            }
            return true;
        }

        // Autoland capability (FCDC FG discrete word 4, bits 23/24/25). Announce
        // decoded transitions only; suppress the raw ARINC word from the generic path.
        // GATED on the in-flight FMGC phases (Climb..Go-around) — on the ground the
        // word flickers none↔capability during taxi and spammed callouts (the same
        // user-reported bug as the A32NX, fixed in lockstep). Hotkey readout
        // unaffected.
        if (varName == "PFD_AUTOLAND")
        {
            var w = new SimConnect.Arinc429Word(value);
            string cap = (!w.IsNormalOperation && !w.IsFunctionalTest) ? "none"
                : w.BitValueOr(25, false) ? "LAND 3 dual"
                : w.BitValueOr(24, false) ? "LAND 3 single"
                : w.BitValueOr(23, false) ? "LAND 2" : "none";
            bool inFlightPhase = _fmgcPhaseA380 >= 2 && _fmgcPhaseA380 <= 6; // Climb..Go-around
            if (inFlightPhase && _lastAutolandCap != null && _lastAutolandCap != cap && cap != "none")
                announcer.Announce($"Approach capability {cap}");
            _lastAutolandCap = cap;
            return true;
        }

        // ---- TCAS resolution-advisory guidance (cache + composed announce) ----
        // The detail vars cache silently; during an RA each update recomposes the
        // spoken guidance and announces only when the sentence changes. The state
        // var itself returns FALSE so the generic Mon announce ("TCAS advisory:
        // resolution advisory") still fires (queued; a detail-driven
        // AnnounceImmediate may land first).
        if (_tcasRa.TryHandleDetailVar(varName, value))
        {
            MaybeAnnounceTcasRaGuidance(announcer);
            return true;
        }
        if (varName == "A32NX_TCAS_STATE")
        {
            _tcasRa.AdvisoryState = (int)value;
            if (_tcasRa.AdvisoryState != 2)
            {
                _tcasRa.ResetSpoken();
                _tcasRaComposeTimer?.Stop();
            }
            else
            {
                // Do NOT compose synchronously here: FBW resets corrective + the
                // V/S bands only in TCAS STBY (NOT on clear-of-conflict), so the
                // cache can still hold the PREVIOUS RA's values and a new
                // opposite-sense RA would briefly speak "Climb" for a Descend.
                // Defer ~800 ms: detail vars that CHANGED for this RA announce
                // fresh from their own handlers; if nothing changed, the cached
                // guidance is identical to the previous RA's and still correct —
                // the timer speaks it.
                _tcasRa.ResetSpoken();
                _tcasRaAnnouncer = announcer;
                if (_tcasRaComposeTimer == null)
                {
                    _tcasRaComposeTimer = new System.Windows.Forms.Timer { Interval = 800 };
                    _tcasRaComposeTimer.Tick += (_, _) =>
                    {
                        _tcasRaComposeTimer!.Stop();
                        if (_tcasRaAnnouncer != null) MaybeAnnounceTcasRaGuidance(_tcasRaAnnouncer);
                    };
                }
                _tcasRaComposeTimer.Stop();
                _tcasRaComposeTimer.Start();
            }
            return false; // generic Mon announce still speaks the state itself
        }

        // Runway Overrun Warning / Protection (ROW/ROP) + OANS RWY AHEAD — decode
        // the ARINC429 discrete word and announce each safety call-out on its rising
        // edge (0->1). On the ground pre-flight every bit is 0, so this is silent at
        // baseline and only speaks the real landing/taxi warnings ("Runway too
        // short", "Max braking", "Runway ahead"). Honours the Ctrl+M mute.
        if (varName is "A32NX_ROW_ROP_WORD_1" or "A32NX_OANS_WORD_1")
        {
            var word = new SimConnect.Arinc429Word(value);
            // Bit map from the FBW writer (a380_systems hydraulic/autobrakes.rs):
            // 11 = ROW/ROP operative (status, not spoken), 12 = ROP actively
            // braking UNDER AUTOBRAKE, 13 = ROP manual-braking warning (throttles
            // idle, no autobrake), 14/15 = in-flight ROW wet/dry too short.
            // Bit 12 was missing from this decode — an autobrake landing where
            // ROP commanded max braking announced nothing.
            (int bit, string phrase)[] bits = varName == "A32NX_ROW_ROP_WORD_1"
                ? new[] { (12, "Maximum braking"), (13, "Max braking"), (14, "If wet, runway too short"), (15, "Runway too short") }
                : new[] { (11, "Runway ahead") };
            bool muted = Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName);
            foreach (var (bit, phrase) in bits)
            {
                bool active = word.BitValueOr(bit, false);
                string k = varName + ":" + bit;
                if (active && _rowRopActive.Add(k)) { if (!muted) announcer.Announce(phrase); }
                else if (!active) _rowRopActive.Remove(k);
            }
            return true;
        }

        // Track the BTV state (gates the rollout distance call-outs below). Captured
        // here but NOT consumed — fall through so the Mon registration still speaks
        // the state transition. Leaving the rollout (state < 2) resets the spoken
        // thresholds so the next landing starts fresh.
        if (varName == "A32NX_BTV_STATE")
        {
            _btvState = (int)value;
            if (_btvState < 2) { _btvExitSpoken.Clear(); _btvRwyEndSpoken.Clear(); }
        }

        // BTV rollout distances: distance to the selected exit, and runway remaining.
        // Both are ARINC429 metres, valid (SSM normal) only while BTV is computing
        // the rollout. Announce as each descends through fixed thresholds (once each),
        // gated on the BTV state (Rotation Optimised / Decel) so it only speaks during
        // the actual braking rollout. Verify the live numbers on a real landing.
        if (varName is "A32NX_OANS_BTV_REMAINING_DIST_TO_EXIT" or "A32NX_OANS_BTV_REMAINING_DIST_TO_RWY_END")
        {
            bool toExit = varName.EndsWith("EXIT");
            var spoken = toExit ? _btvExitSpoken : _btvRwyEndSpoken;
            var word = new SimConnect.Arinc429Word(value);
            bool rolling = _btvState == 2 || _btvState == 3;   // Rotation Optimised / Decel
            if (!rolling || !word.IsNormalOperation) { spoken.Clear(); return true; }
            double m = word.ValueOr(0);
            if (m <= 0 || m > 9000) return true;               // out of sensible range
            bool muted = Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName);
            // Mark EVERY band at/above the current distance as spoken in one pass —
            // so if the rollout starts already below the top band (short exit/runway),
            // the bands we skipped past don't each re-fire on later frames. Announce
            // once if we newly entered any band this update.
            bool announce = false;
            foreach (int t in (toExit ? BtvExitThresholdsM : BtvRwyEndThresholdsM))
                if (m <= t && spoken.Add(t)) announce = true;
            if (announce && !muted)
            {
                int rounded = (int)(Math.Round(m / 10.0) * 10);
                announcer.Announce(toExit ? $"{rounded} meters to exit" : $"{rounded} meters runway remaining");
            }
            return true;
        }

        // External power (GPU) available — explicit edge announce so connecting/
        // disconnecting ground power (incl. via GSX) clearly speaks, rather than
        // relying on the generic indexed-simvar path. SEED SILENTLY on the first read
        // per GPU (prev < 0): the global timed first-detect grace can expire before all
        // four AVAIL vars first arrive, which made MSFSBA call out "External Power 1..4"
        // on startup. Now only a genuine post-startup connect/disconnect speaks. Ctrl+M
        // mute honoured.
        if (varName.StartsWith("A380X_GND_GPU_AVAIL_", StringComparison.Ordinal)
            && int.TryParse(varName.AsSpan("A380X_GND_GPU_AVAIL_".Length), out int gpuN)
            && gpuN >= 1 && gpuN <= 4)
        {
            int now = value > 0.5 ? 1 : 0;
            int prev = _gpuAvail[gpuN - 1];
            _gpuAvail[gpuN - 1] = now;
            if (prev >= 0 && prev != now
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                announcer.Announce(now == 1 ? $"External Power {gpuN} available" : $"External Power {gpuN} disconnected");
            return true;
        }

        // ECAM upper (E/WD) memo/warning lines: decode the numeric code to text
        // and announce it (with its FWC colour as a priority word). Returning
        // true suppresses the generic raw-number announcement.
        if (varName.StartsWith("A32NX_EWD_LOWER_"))
        {
            long code = (long)value;
            if (!_lastEwdCode.TryGetValue(varName, out var prev) || prev != code)
            {
                _lastEwdCode[varName] = code;
                // De-dup by the MESSAGE SET across ALL E/WD lines, not per line.
                // A message that merely scrolls to a different line (because a
                // higher one cleared) stays in the set, so it is NOT re-announced;
                // only a message that's NEWLY present anywhere is spoken. This
                // kills the "same caution repeats as it shifts lines" spam.
                var current = new HashSet<long>();
                foreach (var kv in _lastEwdCode) if (kv.Value != 0) current.Add(kv.Value);
                bool muted = Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains("FBWA380_ECAM_MEMOS");
                foreach (var c in current)
                {
                    if (_announcedEwdCodes.Contains(c)) continue;   // already on screen
                    if (muted) continue;                            // honour Ctrl+M mute
                    // When the E/WD DOM-scrape monitor (CoherentEWDClient) is running
                    // it is the single source for the E/WD auto-call-outs (failures
                    // AND memos), so suppress this SimVar announce to avoid double
                    // speech. The dedup sets below are still maintained so the
                    // on-demand Alt+E E/WD window decode keeps working, and
                    // if the scrape monitor is NOT active this SimVar path still
                    // announces (safe default).
                    if (EwdScrapeHandlesAnnounce) continue;
                    string text = EWDMessageLookupA380.GetMessage(c);
                    if (!string.IsNullOrWhiteSpace(text) &&
                        !text.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                    {
                        string priority = EWDMessageLookupA380.GetMessagePriority(c);
                        announcer.Announce(string.IsNullOrEmpty(priority) ? text : $"{text}, {priority}");
                    }
                }
                // Snapshot the on-screen set so a cleared message can re-announce
                // if it genuinely returns later (but a moved one does not).
                _announcedEwdCodes.Clear();
                foreach (var c in current) _announcedEwdCodes.Add(c);
            }
            return true;
        }

        // Cache gross weight (kg, stock) + CG (%MAC, FBW L-var) silently for the
        // W / Shift+W readouts. Both monitored continuously; the hotkeys read these
        // caches and speak immediately (no async request, no flag timing).
        if (varName == "GW_KG_CACHE") { _gwKgCache = value; return true; }
        if (varName == "A32NX_AIRFRAME_GW_CG_PERCENT_MAC") { _gwCgMac = value; return true; }

        // ---- On-demand flaps / gear readouts (global hotkeys) ----
        // Only intercept while a readout is pending; otherwise fall through so the
        // normal continuous-monitor announcement (on change) still fires.
        if (_reqFlaps && varName == "A32NX_FLAPS_HANDLE_INDEX")
        {
            _reqFlaps = false;
            string[] detents = { "Up", "1", "2", "3", "Full" };
            int i = (int)Math.Round(value);
            announcer.AnnounceImmediate("Flaps " + (i >= 0 && i < detents.Length ? detents[i] : value.ToString()));
            return true;
        }
        if (_reqGear && varName == "A32NX_GEAR_HANDLE_POSITION")
        {
            _reqGear = false;
            announcer.AnnounceImmediate(value > 0.5 ? "Gear down" : "Gear up");
            return true;
        }
        if (_readoutKey != null && varName == _readoutKey)
        {
            string lbl = _readoutLabel ?? varName;
            string spoken;
            if (_readoutMap != null && _readoutMap.TryGetValue(Math.Round(value), out var dsc))
                spoken = lbl + " " + dsc;
            else if (_readoutIsWeight)
            {
                // The raw var is kilograms; speak it in the pilot's selected unit.
                var (wv, wu) = WeightUser(value);
                spoken = $"{lbl} {wv:0} {wu}";
            }
            else
                spoken = string.IsNullOrEmpty(_readoutUnit) ? $"{lbl} {value:0}" : $"{lbl} {value:0} {_readoutUnit}";
            announcer.AnnounceImmediate(spoken);
            _readoutKey = null; _readoutMap = null; _readoutIsWeight = false;
            return true;
        }
        // Live EFIS baro auto-announce — spoken as the pilot turns the knob, in
        // whichever unit the EFIS is set to. The HPA var is an ARINC429 word
        // (decode it); BaroHpa range-detects so it still works if the value ever
        // comes through in inHg. Only spoken in QNH (STD is handled below).
        if (varName == "A32NX_FCU_LEFT_EIS_BARO_HPA" || varName == "A32NX_FCU_RIGHT_EIS_BARO_HPA")
        {
            bool capt = varName.Contains("LEFT");
            if (!BaroHpa(new Arinc429Word(value).ValueOr(0), out double hpa)) return true; // STD / no data
            if (Math.Abs(hpa - (capt ? _lastBaroL : _lastBaroR)) > 0.05) // half the word's 0.1 step
            {
                // SEED SILENTLY on the first read (last == -1) so MSFSBA doesn't call out
                // "Captain/First officer altimeter ..." on startup; only a genuine later
                // knob turn speaks.
                bool first = (capt ? _lastBaroL : _lastBaroR) < 0;
                if (capt) _lastBaroL = hpa; else _lastBaroR = hpa;
                if (!first && (capt ? _baroStdL : _baroStdR) != true)
                    announcer.Announce(BaroPhrase(capt, hpa, false));
            }
            return true;
        }
        // Stock-altimeter MB mirrors: consumed by MainForm's A380 STD-flag watchdog
        // (the def's ProcessSimVarUpdate has no SimConnect manager to write with —
        // same split as the engine-mode-selector watchdog). Never announce the raw value.
        if (varName is "BARO_MB_WATCH_L" or "BARO_MB_WATCH_R") return true;
        // EFIS baro STD (PUSH) / QNH (PULL) — announce the mode change.
        if (varName == "A32NX_FCU_LEFT_EIS_BARO_IS_STD" || varName == "A32NX_FCU_RIGHT_EIS_BARO_IS_STD")
        {
            bool capt = varName.Contains("LEFT");
            bool std = value > 0.5;
            bool? prev = capt ? _baroStdL : _baroStdR;
            if (capt) _baroStdL = std; else _baroStdR = std;
            if (prev.HasValue && prev.Value != std) // skip the baseline read
            {
                double last = capt ? _lastBaroL : _lastBaroR;
                // Guard like the unit-change branch below: _lastBaroL/R start at -1 and
                // are seeded only by a valid HPA word — STD->QNH before any valid sample
                // would otherwise speak "QNH -1 hectopascals".
                announcer.Announce(std
                    ? $"{(capt ? "Captain" : "First officer")} altimeter standard"
                    : (last > 0
                        ? BaroPhrase(capt, last, true)
                        : $"{(capt ? "Captain" : "First officer")} altimeter QNH"));
            }
            return true;
        }
        // EFIS baro UNIT lives on XMLVAR_Baro_Selector_HPA_{1,2} (1=hPa, 0=inHg) —
        // NOT A32NX_FCU_EFIS_*_BARO_IS_INHG, which is stuck at 0 on the A380X
        // (verified live: F/O reads XMLVAR=0/inHg while IS_INHG stays 0/hPa, so
        // the readout always said hPa). Track the real unit here and re-announce
        // the setting in the new unit when the pilot switches it.
        if (varName == "XMLVAR_Baro_Selector_HPA_1" || varName == "XMLVAR_Baro_Selector_HPA_2")
        {
            bool capt = varName.EndsWith("_1", StringComparison.Ordinal);
            bool inHg = value < 0.5;
            bool? prev = capt ? _baroInHgL : _baroInHgR;
            if (capt) _baroInHgL = inHg; else _baroInHgR = inHg;
            if (prev.HasValue && prev.Value != inHg) // skip the baseline read
            {
                double last = capt ? _lastBaroL : _lastBaroR;
                if (last > 0 && (capt ? _baroStdL : _baroStdR) != true)
                    announcer.Announce(BaroPhrase(capt, last, false));
                else
                    announcer.Announce($"{(capt ? "Captain" : "First officer")} baro unit {(inHg ? "inches" : "hectopascals")}");
            }
            return true;
        }
        // Minimums are ARINC429 words — decode and announce when a minimum is set
        // or changed on the MCDU PERF APPR page (no announce on clear/NCD).
        if (varName == "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE" || varName == "A32NX_FM1_DECISION_HEIGHT")
        {
            bool baro = varName.EndsWith("DESCENT_ALTITUDE", StringComparison.Ordinal);
            var w = new Arinc429Word(value);
            int ft = (w.IsNormalOperation || w.IsFunctionalTest) ? (int)(Math.Round(w.Value / 10.0) * 10) : -1;
            if (ft != (baro ? _lastBaroMin : _lastDh))
            {
                if (baro) _lastBaroMin = ft; else _lastDh = ft;
                if (ft > 0) announcer.Announce($"{(baro ? "Baro minimum" : "Decision height")} {ft} feet");
            }
            return true;
        }
        if (_reqBaro && varName == "KOHLSMAN_HG")
        {
            _reqBaro = false;
            announcer.AnnounceImmediate($"Altimeter {value * 33.8639:0} hectopascals, {value:0.00} inches");
            return true;
        }
        // Weight-unit selection (kg/lb) mirror of the EFB "US Units" toggle. Seed
        // MSFSBA's read-out unit from the aircraft on first read (silent); on a
        // genuine AIRCRAFT change (someone flipped it in the flyPad EFB Settings),
        // follow it and announce. The MCDU "Units" button changes _metricWeight
        // directly without touching _aircraftMetric, so it never fights this.
        if (varName == "A32NX_EFB_USING_METRIC_UNIT")
        {
            bool m = value > 0.5;
            if (!_metricKnown) { _metricKnown = true; _aircraftMetric = m; _metricWeight = m; return true; }
            if (m != _aircraftMetric)
            {
                _aircraftMetric = m; _metricWeight = m;
                announcer.Announce($"Weight units {(m ? "kilograms" : "pounds")}");
            }
            return true;
        }
        // Metric-altitude (FCU MTRS) state — cache it so every MSFSBA altitude
        // read-out switches to metres; let the generic monitor announce On/Off.
        if (varName == "A32NX_METRIC_ALT_TOGGLE") { _metricAlt = value > 0.5; return false; }

        // Suppress the side-effect "Altitude Increment: 100" announce that a window-driven
        // SetFCUAltitudeValue fires to force 100-ft granularity (the user set an altitude, not
        // the increment). Time-boxed, so a deliberate later increment change still speaks.
        if (varName == "XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT" && DateTime.UtcNow < _altIncrAnnounceSuppressUntil)
            return true;

        // ---- FCU selected-value CHANGE announcements (hardware knob turns; 777-MCP parity) ----
        // These five vars are Continuous+IsAnnounced so an external hardware dial (MobiFlight,
        // FSUIPC, the cockpit knob) is spoken as it changes, exactly like the PMDG 777 MCP
        // ("Heading 250" / "Speed 250 knots" / "Altitude 10000" / "Vertical speed -1500").
        // This block never consumes the event — the _req* readout branches below still see the
        // same update — and the fcuValueVar return at the end of the section keeps the generic
        // monitor from also speaking the raw SI-unit value. MSFSBA's own FCU windows suppress
        // the echo via SuppressFcuValueChangeEcho() in the SetFCU*/FireFCUButton methods.
        bool fcuValueVar = varName is "A32NX_AUTOPILOT_HEADING_SELECTED" or "A32NX_AUTOPILOT_SPEED_SELECTED"
            or "FCU_ALT_VALUE" or "A32NX_AUTOPILOT_VS_SELECTED" or "A32NX_AUTOPILOT_FPA_SELECTED";
        if (fcuValueVar)
        {
            bool fcuMuted = Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName);
            switch (varName)
            {
                case "A32NX_AUTOPILOT_HEADING_SELECTED":
                    // -1 = managed (dashes) — the managed-mode monitor announces that transition.
                    // Angular L:var arrives in RADIANS (same heuristic as the readout below).
                    if (value >= 0)
                    {
                        double hdgDeg = Math.Abs(value) <= (Math.PI * 2 + 0.05) ? value * 180.0 / Math.PI : value;
                        hdgDeg = Math.Round(((hdgDeg % 360) + 360) % 360);
                        AnnounceFcuValueChanged(varName, hdgDeg, 0.5, v => $"Heading {(int)v}", announcer, fcuMuted);
                    }
                    break;
                case "A32NX_AUTOPILOT_SPEED_SELECTED":
                    // -1 = managed (dashes). The var holds the target directly: a Mach number
                    // when < 10, otherwise knots (no SI scaling — see the readout note below).
                    if (value >= 0)
                        AnnounceFcuValueChanged(varName, value, 0.005,
                            v => v < 10 ? $"Mach {v:F2}" : $"Speed {(int)v} knots", announcer, fcuMuted);
                    break;
                case "FCU_ALT_VALUE":
                    // Stock simvar, already feet; follow the metric-alt (MTRS) toggle for the unit.
                    AnnounceFcuValueChanged(varName, value, 50, v =>
                    {
                        if (!_metricAlt) return $"Altitude {(int)v}";
                        var (av, au) = AltUser(v);
                        return $"Altitude {av:0} {au}";
                    }, announcer, fcuMuted);
                    break;
                case "A32NX_AUTOPILOT_VS_SELECTED":
                    // SI read: m/s -> fpm, rounded to the FCU's 100-fpm step (same conversion
                    // as the readout below).
                    AnnounceFcuValueChanged(varName, Math.Round(value * 196.8503937 / 100.0) * 100.0, 50,
                        v => $"Vertical speed {(int)v}", announcer, fcuMuted);
                    break;
                case "A32NX_AUTOPILOT_FPA_SELECTED":
                    // Angular: radians when in the radian range (FPA maxes at ~9.9 degrees).
                    AnnounceFcuValueChanged(varName,
                        Math.Abs(value) <= 0.2 ? value * 180.0 / Math.PI : value, 0.05,
                        v => $"FPA {v:F1} degrees", announcer, fcuMuted);
                    break;
            }
        }

        // ---- FCU readouts: value + managed-indicator pairs ----
        // Each Read* hotkey requests the value var(s) and the managed indicator
        // and force-updates them; we announce once the pair (or, for VS, the
        // mode + matching value) has arrived. The _req* guards ensure these are
        // only intercepted during an active readout — otherwise they fall
        // through so the FCU panel's display readouts work normally.
        if (_reqHdg && (varName == "A32NX_AUTOPILOT_HEADING_SELECTED" || varName == "A32NX_FCU_HDG_MANAGED_DASHES"))
        {
            // SimConnect's native L-var read returns this ANGULAR var in RADIANS
            // (verified live: 250° reads 4.363, 300° reads 5.236) — non-angular FCU
            // vars like VS/speed come through unscaled. Convert to degrees only when
            // the magnitude is in the radian range (<= 2pi), so a future build/path
            // that returns degrees directly is handled correctly too.
            if (varName.EndsWith("HEADING_SELECTED"))
            {
                double hv = Math.Abs(value) <= (Math.PI * 2 + 0.05) ? value * 180.0 / Math.PI : value;
                _pHdgVal = ((hv % 360) + 360) % 360;
            }
            else _pHdgMgd = value;
            if (_pHdgVal.HasValue && _pHdgMgd.HasValue)
            {
                string st = _pHdgMgd.Value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU heading {_pHdgVal.Value:000} degrees, {st}");
                _pHdgVal = _pHdgMgd = null; _reqHdg = false;
            }
            return true;
        }
        if (_reqSpd && (varName == "A32NX_AUTOPILOT_SPEED_SELECTED" || varName == "A32NX_FCU_SPD_MANAGED_DOT"))
        {
            if (varName.EndsWith("SPEED_SELECTED")) _pSpdVal = value; else _pSpdMgd = value;
            if (_pSpdVal.HasValue && _pSpdMgd.HasValue)
            {
                // Managed speed parks SPEED_SELECTED at -1 (dashes on the FCU). Don't
                // format that as a bogus "mach -1.00" — announce the managed state.
                bool managed = _pSpdMgd.Value > 0 || _pSpdVal.Value < 0;
                string spoken;
                if (managed)
                    spoken = "FCU speed managed";
                else
                    // A32NX_AUTOPILOT_SPEED_SELECTED holds the target DIRECTLY: a mach number
                    // when < 1 (e.g. 0.82), otherwise the speed already in KNOTS (e.g. 220 = 220 kt).
                    // It is NOT an SI velocity — the earlier ×1.943844 m/s→kt conversion was wrong
                    // and reported 220 kt as "428 knots" (220 × 1.943844). Live-verified airborne:
                    // the L:var read = 220 with IAS 220. So announce knots verbatim, no scaling.
                    spoken = _pSpdVal.Value < 10
                        ? $"FCU speed mach {_pSpdVal.Value:0.00}, selected"
                        : $"FCU speed {_pSpdVal.Value:000} knots, selected";
                announcer.AnnounceImmediate(spoken);
                _pSpdVal = _pSpdMgd = null; _reqSpd = false;
            }
            return true;
        }
        if (_reqAlt && (varName == "FCU_ALT_VALUE" || varName == "A32NX_FCU_ALT_MANAGED"))
        {
            if (varName == "FCU_ALT_VALUE") _pAltVal = value; else _pAltMgd = value;
            if (_pAltVal.HasValue && _pAltMgd.HasValue)
            {
                string st = _pAltMgd.Value > 0 ? "managed" : "selected";
                var (av, au) = AltUser(_pAltVal.Value);
                announcer.AnnounceImmediate($"FCU altitude {av:0} {au}, {st}");
                _pAltVal = _pAltMgd = null; _reqAlt = false;
            }
            return true;
        }
        if (_reqVs && (varName == "A32NX_AUTOPILOT_VS_SELECTED" || varName == "A32NX_AUTOPILOT_FPA_SELECTED" ||
                       varName == "A32NX_TRK_FPA_MODE_ACTIVE"))
        {
            // V/S is a RATE, so the SimConnect L:var read returns it in m/s (SI),
            // NOT feet/min (verified live: MobiFlight=2000 fpm reads as 10.16 here).
            // Convert m/s -> fpm (x196.85) and round to the FCU's 100-fpm step.
            if (varName.EndsWith("VS_SELECTED")) _pVsVal = Math.Round(value * 196.8503937 / 100.0) * 100.0;
            // FPA is angular, so SimConnect returns it in radians (like heading);
            // convert when the magnitude is in the radian range (FPA maxes at ~9.9°).
            else if (varName.EndsWith("FPA_SELECTED")) _pFpaVal = Math.Abs(value) <= 0.2 ? value * 180.0 / Math.PI : value;
            else _pVsMode = value;
            if (_pVsMode.HasValue && ((_pVsMode.Value > 0 && _pFpaVal.HasValue) || (_pVsMode.Value <= 0 && _pVsVal.HasValue)))
            {
                string spoken = _pVsMode.Value > 0
                    ? $"FCU flight path angle {_pFpaVal!.Value:0.0} degrees"
                    : $"FCU vertical speed {_pVsVal!.Value:0} feet per minute";
                announcer.AnnounceImmediate(spoken);
                _pVsVal = _pFpaVal = _pVsMode = null; _reqVs = false;
            }
            return true;
        }

        // FCU value vars are def-handled (the change block above + the readout branches) —
        // never let the generic monitor speak their raw SI-unit values.
        if (fcuValueVar) return true;

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    // Spoken CG suffix for the gross-weight readouts. Empty (suppressed) when the CG
    // isn't available/sane, so the gross-weight readout never breaks or says "CG 0".
    private string CgMacPhrase() => (_gwCgMac > 5 && _gwCgMac < 60) ? $", center of gravity {_gwCgMac:0.0} percent MAC" : "";
}

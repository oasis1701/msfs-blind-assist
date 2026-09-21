using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Self-announced monitors: the FG-annunciator stopgap mode monitor (the real FMA
/// vocabulary needs the P3 PFD scrape), flight stage, APU milestones, master
/// warning/caution, and the "Reverse green" rollout callout. All announcements use
/// announcer.Announce (suppressible) so the global UI-echo wrap keeps combo sets
/// from double-speaking. Every self-handled key is baseline-first: the first sample
/// after connect/switch is recorded silently.
/// </summary>
public partial class SynapticA220Definition
{
    private readonly Dictionary<string, double> _edgePrev = new();
    private int _lastFlightStage = -1;
    private bool _apuAvailableAnnounced;
    private bool _reverseAnnounced;
    private double _rev1, _rev2;

    /// <summary>Engage/disengage phrases for the FG stopgap monitor. Null off-text = engage-only
    /// (lateral/vertical modes hand off to each other — announcing every off is chatter).</summary>
    private static readonly Dictionary<string, (string on, string? off)> FgPhrases = new()
    {
        ["A22X_AP_MASTER"] = ("Autopilot engaged", "Autopilot disengaged"),
        ["A22X_AT_MASTER"] = ("Autothrottle active", "Autothrottle off"),
        ["A22X_FG_HEADING"] = ("HDG mode", null),
        ["A22X_FG_LNAV"] = ("NAV mode", null),
        ["A22X_FG_APPROACH"] = ("Approach mode armed", "Approach mode cancelled"),
        ["A22X_FG_FLC"] = ("FLC mode", null),
        ["A22X_FG_ALT"] = ("Altitude hold", null),
        ["A22X_FG_VNAV"] = ("VNAV on", "VNAV off"),
        ["A22X_FG_VS"] = ("Vertical speed mode", null),
        ["A22X_FG_FPA"] = ("Flight path angle mode", null),
        ["A22X_FG_HALF_BANK"] = ("Half bank on", "Half bank off"),
        ["A22X_FG_EDM"] = ("Emergency descent mode active", "Emergency descent mode off"),
    };

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Display pump hooks (Displays partial): stash the announcer, lazily start the
        // Coherent poll, and peek IAS / on-ground for the Rotate callout — must run
        // BEFORE the base call and the silent-key swallow below.
        ObserveForDisplays(varName, value, announcer);

        // Final-approach PM callouts (ApproachCallouts partial) — also fed by
        // silent-swallowed keys, so it too must run before the swallow.
        ObserveApproachCallouts(varName, value, announcer);

        if (base.ProcessSimVarUpdate(varName, value, announcer))
            return true;

        // Batch-riding silent readout vars (radios, FCP targets, baro…): cache-only.
        if (_silentReadoutKeys.Contains(varName))
            return true;

        switch (varName)
        {
            case "A22X_FLIGHT_STAGE":
            {
                int stage = (int)Math.Round(value);
                bool first = _lastFlightStage < 0;
                if (!first && stage != _lastFlightStage)
                {
                    _lastFlightStage = stage;
                    string? phase = CurrentFlightPhase;
                    if (phase != null && !Muted(varName)) announcer.Announce($"Flight stage {phase}");
                }
                else
                {
                    _lastFlightStage = stage;
                }
                return true;
            }

            case "A22X_APU_RPM":
            {
                if (value >= 99 && !_apuAvailableAnnounced)
                {
                    _apuAvailableAnnounced = true;
                    if (!Muted(varName)) announcer.Announce("APU available");
                }
                else if (value < 10 && _apuAvailableAnnounced)
                {
                    _apuAvailableAnnounced = false;
                    if (!Muted(varName)) announcer.Announce("APU shut down");
                }
                return true;
            }

            // The EXEC prompt (the MKP EXEC key's own lamp var). RISING edge only:
            // the falling edge means the pending mod was just executed or cancelled,
            // which is nearly always the user's own keypress — and announcing a
            // direct user action is exactly what the screen-reader rules forbid.
            // Edge() is baseline-first, so loading an aircraft that already has a
            // pending modification never speaks on the first read.
            case "A22X_FPLN_MODIFIED":
                if (Edge(varName, value, out bool fplnModified) && fplnModified && !Muted(varName))
                    announcer.Announce("Flight plan modified. Press EXEC to activate.");
                return true;

            case "A22X_CAUTION_PBA":
                if (Edge(varName, value, out bool cautionOn) && !Muted(varName))
                    announcer.Announce(cautionOn ? "Master Caution" : "Master Caution clear");
                return true;

            case "A22X_WARNING_PBA":
                if (Edge(varName, value, out bool warningOn) && !Muted(varName))
                    announcer.Announce(warningOn ? "Master Warning" : "Master Warning clear");
                return true;

            case "A22X_ENG1_REVERSER":
            case "A22X_ENG2_REVERSER":
            {
                if (varName == "A22X_ENG1_REVERSER") _rev1 = value; else _rev2 = value;
                if (!_reverseAnnounced && _rev1 > 0.5 && _rev2 > 0.5)
                {
                    _reverseAnnounced = true;
                    if (!Muted(varName)) announcer.Announce("Reverse green");
                }
                else if (_reverseAnnounced && _rev1 < 0.1 && _rev2 < 0.1)
                {
                    _reverseAnnounced = false;
                    if (!Muted(varName)) announcer.Announce("Reversers stowed");
                }
                return true;
            }
        }

        if (FgPhrases.TryGetValue(varName, out var phrase))
        {
            if (Edge(varName, value, out bool on) && !Muted(varName))
            {
                if (on) announcer.Announce(phrase.on);
                else if (phrase.off != null) announcer.Announce(phrase.off);
            }
            return true;
        }

        return false; // lamps + everything else: generic path (labels/ValueDescriptions)
    }

    /// <summary>Ctrl+M mute for the self-announced vars — they never reach MainForm's
    /// generic disabled-set gate (ProcessSimVarUpdate returns true first), so the def
    /// honours the set itself (the iFly light-edge precedent). State still updates.</summary>
    private static bool Muted(string varName)
        => Settings.SettingsManager.Current.A220DisabledMonitorVariablesSet.Contains(varName);

    /// <summary>Baseline-first boolean edge: false (no announce) on the very first sample.</summary>
    private bool Edge(string key, double value, out bool on)
    {
        on = value > 0.5;
        if (_edgePrev.TryGetValue(key, out double prev))
        {
            _edgePrev[key] = value;
            return (prev > 0.5) != on;
        }
        _edgePrev[key] = value;
        return false;
    }
}

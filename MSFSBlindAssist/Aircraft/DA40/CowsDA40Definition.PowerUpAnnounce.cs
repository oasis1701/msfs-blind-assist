using System;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// WHAT THE BUSES DID WHEN THE MASTER MOVED.
///
/// Turning the electric master on announced "Electric Master: On" and nothing else, because
/// every bus voltage is a NUMBER and numbers are silent by the house rule. A sighted pilot
/// does not read voltages either - they watch the panel come alive - so the switch position
/// was the whole of what a blind pilot got, and it is the one thing that says nothing about
/// whether power actually reached anything.
///
/// It matters on this aeroplane in particular: the master can be on over a dead bus (a pulled
/// breaker, a flat battery, the essential-bus switch isolating the main bus), and this project
/// has already lost an afternoon to exactly that state.
///
/// ⚠️ IT REPORTS, IT DOES NOT COACH. One line naming what the buses came up at, and never a
/// word about what a low bus means or what to do about it - the pilot's ruling on the trim
/// circuit applies here too, and this is the announcement most tempting to editorialise on.
///
/// ⚠️ AND IT IS ONE UTTERANCE, NOT A FLOOD. Every consequence of the master is a separate
/// variable, so announcing them individually would speak a dozen lines over a single switch
/// throw - which is the wall the scan exists to remove, not a feature. The buses are read
/// together, after a settle, and spoken as one sentence.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>
    /// How long to let the buses settle before reading them.
    ///
    /// The master is a switch and the buses are a consequence, so reading them in the same
    /// breath reads the state BEFORE the switch took effect - the announcement would report
    /// the buses the pilot just left rather than the ones they just made.
    ///
    /// ⚠️ It must also EXCEED ONE BATCH PERIOD (1 Hz) - at 900 ms it did not, so a bus still
    /// rising when the timer expired was read mid-climb and the settled voltage never spoken.
    /// Same rule as every other settle here; see RadioSettleMs for the full reasoning.
    /// </summary>
    private const int PowerSettleMs = 1200;

    private System.Windows.Forms.Timer? _powerSettleTimer;
    private ScreenReaderAnnouncer? _powerAnnouncer;
    private string _powerPendingLabel = "";
    private bool _powerPendingOn;

    /// <summary>The two switches whose consequence is worth reading back.</summary>
    private bool NotePowerSwitchChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        // ⚠️ THE ELECTRIC MASTER ONLY. The avionics master used to arm this too, and it
        // re-read the SAME THREE VOLTAGES a second time - main, essential and battery are
        // on the battery side and do not move when the avionics bus comes up (this
        // aeroplane's own systems.cfg: bus.1 = BUS_BAT carries the displays and the first
        // radios, bus.2 = BUS_AVN only the second set). So the pilot heard an identical
        // sentence twice a session, the second time INTERRUPTING whatever was being said,
        // for no new information at all.
        //
        // The avionics master keeps its own switch announcement, which is the whole of the
        // news: there is no avionics-bus voltage to report, so a read-back could only
        // repeat the battery side. Same reasoning as the switched-OFF case below - when the
        // switch has already said it, saying it again is noise.
        string label;
        switch (varKey)
        {
            case "DA40_ELEC_MASTER_BATTERY": label = IsNG ? "Electric master" : "Battery master"; break;
            case "DA40_ELEC_ALT_MASTER": label = "Alternator master"; break;
            default: return false;
        }

        // ⚠️ Returns FALSE, never true. This is an ADDITION to the switch's own announcement,
        // not a replacement for it - the generic monitor still says "Electric Master: On" the
        // moment the switch moves, and this follows a second later with what happened. A pilot
        // who flicks a switch should hear the switch immediately, not after a settle.
        _powerAnnouncer = announcer;
        _powerPendingLabel = label;
        _powerPendingOn = value > 0.5;

        if (_powerSettleTimer == null)
        {
            _powerSettleTimer = new System.Windows.Forms.Timer { Interval = PowerSettleMs };
            _powerSettleTimer.Tick += (_, _) => FlushPowerSettle();
        }

        _powerSettleTimer.Stop();
        _powerSettleTimer.Start();
        return false;
    }

    private void FlushPowerSettle()
    {
        _powerSettleTimer?.Stop();
        if (_powerAnnouncer == null || _powerPendingLabel.Length == 0) return;

        // The Ctrl+M mute is checked HERE, not left to the caller - this speaks from a TIMER,
        // and the generic monitor gate only wraps ProcessSimVarUpdate. Same rule the altimeter
        // settle, the radio settle and the A32NX armed-altitude flush all follow. Muting the
        // master's own row mutes its consequence too, which is what a pilot un-ticking that row
        // is asking for.
        var muted = Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet;
        if (muted.Contains("DA40_ELEC_MASTER_BATTERY")) { _powerPendingLabel = ""; return; }

        string text = ComposeBusState(_powerPendingOn, _lastMainBusVolts, _lastEssBusVolts,
                                      _lastBattBusVolts);
        _powerPendingLabel = "";
        if (text.Length > 0) _powerAnnouncer.AnnounceImmediate(text);
    }

    // The three buses are cached as they arrive on the batch - see the note on their
    // definitions. They are silent readouts; this is the one thing that speaks them.
    private double? _lastMainBusVolts;
    private double? _lastEssBusVolts;
    private double? _lastBattBusVolts;

    private void NoteBusVoltage(string varKey, double value)
    {
        switch (varKey)
        {
            case "DA40_ELEC_BUS_MAIN_VOLT": _lastMainBusVolts = value; break;
            case "DA40_ELEC_BUS_ESS_VOLT": _lastEssBusVolts = value; break;
            case "DA40_ELEC_BUS_BATT_VOLT": _lastBattBusVolts = value; break;
        }
    }

    /// <summary>
    /// The sentence. Pure so the suite can pin it.
    ///
    /// ⚠️ A bus reading ZERO is named as zero, never softened and never explained. That is the
    /// state a pilot most needs to hear and the one an "everything normal" summary would hide.
    /// </summary>
    internal static string ComposeBusState(bool on, double? main, double? ess, double? batt)
    {
        // Switched OFF, the buses going dead is the expected consequence and saying so adds
        // nothing - the switch already said it. Only the powered case carries news.
        if (!on) return "";

        var parts = new System.Collections.Generic.List<string>();
        if (main is not null) parts.Add($"main bus {main.Value:0.0} volts");
        if (ess is not null) parts.Add($"essential {ess.Value:0.0}");
        if (batt is not null) parts.Add($"battery {batt.Value:0.0}");

        // Nothing cached yet - the first master-on of a session can land before the batch has
        // delivered a single voltage. Silence beats inventing a reading.
        if (parts.Count == 0) return "";

        return string.Join(", ", parts) + ".";
    }

    /// <summary>Stops the settle timer. Called when the aircraft is switched away.</summary>
    private void StopPowerAnnounce()
    {
        try { _powerSettleTimer?.Stop(); _powerSettleTimer?.Dispose(); } catch { }
        _powerSettleTimer = null;
        _powerPendingLabel = "";
    }
}

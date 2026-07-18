using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// A320 Autopilot panel: AP1/AP2, A/THR engage + disconnect, AP disconnect,
// APPR/LOC/EXPED, and a read-only Flight Director status. State labels refresh
// from the live cache. Mirrors the A380 panel (shared A32NX FCU events/vars).
public class FBWA320AutopilotWindow : FBWA320FCUWindowBase
{
    private readonly Button ap1, ap2, appr, loc, exped;
    private readonly Label fdLabel;
    private readonly System.Windows.Forms.Timer refreshTimer;

    public FBWA320AutopilotWindow(FlyByWireA320Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "A320 Autopilot";
        Size = new Size(420, 360);

        ap1 = MakeToggle("AP 1", 20, 20, "A32NX.FCU_AP_1_PUSH", 0);
        ap2 = MakeToggle("AP 2", 210, 20, "A32NX.FCU_AP_2_PUSH", 1);
        loc = MakeToggle("LOC", 20, 65, "A32NX.FCU_LOC_PUSH", 2);
        appr = MakeToggle("APPR", 210, 65, "A32NX.FCU_APPR_PUSH", 3);
        exped = MakeToggle("EXPED", 20, 110, "A32NX.FCU_EXPED_PUSH", 4);

        var athr = new Button { Text = "A/THR engage", Location = new Point(210, 110), Size = new Size(180, 35), TabIndex = 5, AccessibleName = "Autothrust engage" };
        athr.Click += (s, e) => { simConnect.SendEvent("AUTO_THROTTLE_ARM"); RefreshStates(); };
        var apDisc = new Button { Text = "AP disconnect", Location = new Point(20, 155), Size = new Size(180, 35), TabIndex = 6, AccessibleName = "Autopilot disconnect" };
        apDisc.Click += (s, e) => { simConnect.SendEvent("A32NX.FCU_AP_DISCONNECT_PUSH"); RefreshStates(); };
        var athrDisc = new Button { Text = "A/THR disconnect", Location = new Point(210, 155), Size = new Size(180, 35), TabIndex = 7, AccessibleName = "Autothrust disconnect" };
        athrDisc.Click += (s, e) => { simConnect.SendEvent("A32NX.FCU_ATHR_DISCONNECT_PUSH"); RefreshStates(); };

        fdLabel = new Label { Location = new Point(20, 205), Size = new Size(370, 20), AccessibleName = "Flight Director status", Text = "Flight Director: ..." };

        var closeButton = new Button { Text = "Close", Location = new Point(140, 250), Size = new Size(140, 35), TabIndex = 8, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { ap1, ap2, loc, appr, exped, athr, apDisc, athrDisc, fdLabel, closeButton });
        CancelButton = closeButton;

        // Continuous refresh so every button label tracks the LIVE state — including EXPED,
        // which only engages with an altitude gap and so often flips a moment AFTER the press
        // (the old one-shot 250 ms timer stopped itself and missed that late change).
        refreshTimer = new System.Windows.Forms.Timer { Interval = 400 };
        refreshTimer.Tick += (s, e) => { aircraft.RequestAutopilotStates(simConnect); UpdateLabels(); };
    }

    private Button MakeToggle(string name, int x, int y, string evt, int tab)
    {
        var b = new Button { Text = name + " ...", Location = new Point(x, y), Size = new Size(180, 35), TabIndex = tab, AccessibleName = name, Tag = name };
        b.Click += (s, e) => { simConnect.SendEvent(evt); RefreshStates(); };
        return b;
    }

    protected override void SpeakInitialReadout() { RefreshStates(); refreshTimer.Start(); }

    // Request a fresh read + repaint the labels right now (on open and after a press, for
    // immediate feedback); the continuous timer keeps them current thereafter.
    private void RefreshStates() { aircraft.RequestAutopilotStates(simConnect); UpdateLabels(); }

    private void UpdateLabels()
    {
        SetState(ap1, "AP 1", "A32NX_AUTOPILOT_1_ACTIVE");
        SetState(ap2, "AP 2", "A32NX_AUTOPILOT_2_ACTIVE");
        // LOC/APPR/FD read the registered FCU button-light L:vars (the real FBW vars,
        // analogous to A32NX_FCU_APPR_LIGHT_ON used by the cockpit). The previous
        // A32NX_FCU_{LOC,APPR}_MODE_ACTIVE / EFIS_*_FD_ACTIVE names DON'T EXIST in FBW
        // and weren't registered, so RequestVariable no-op'd and the labels were stuck
        // "off" / never refreshed.
        SetState(loc, "LOC", aircraft.LocLightVar);
        SetState(appr, "APPR", aircraft.ApprLightVar);
        SetState(exped, "EXPED", "A32NX_FMA_EXPEDITE_MODE");
        bool fdL = (simConnect.GetCachedVariableValue(aircraft.FdLeftLightVar) ?? 0) > 0.5;
        bool fdR = (simConnect.GetCachedVariableValue(aircraft.FdRightLightVar) ?? 0) > 0.5;
        fdLabel.Text = $"Flight Director: Captain {(fdL ? "on" : "off")}, First Officer {(fdR ? "on" : "off")}";
    }

    private void SetState(Button b, string name, string stateVar)
    {
        bool on = (simConnect.GetCachedVariableValue(stateVar) ?? 0) > 0.5;
        b.Text = $"{name} ({(on ? "on" : "off")})";
        b.AccessibleName = $"{name} {(on ? "on" : "off")}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { refreshTimer.Stop(); refreshTimer.Dispose(); base.OnFormClosing(e); }
}

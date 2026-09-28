using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

// A380 Autopilot panel: AP1/AP2, the flight-director pushbutton, A/THR engage +
// disconnect, AP disconnect, APPR/LOC. State labels refresh from the live cache.
public class FBWA380AutopilotWindow : FBWA380FCUWindowBase
{
    private readonly Button ap1, ap2, appr, loc, fd;
    private readonly System.Windows.Forms.Timer refreshTimer;

    public FBWA380AutopilotWindow(FlyByWireA380Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "A380 Autopilot";
        Size = new Size(420, 360);

        ap1 = MakeToggle("AP 1", 20, 20, "A32NX.FCU_AP_1_PUSH", 0);
        ap2 = MakeToggle("AP 2", 210, 20, "A32NX.FCU_AP_2_PUSH", 1);
        loc = MakeToggle("LOC", 20, 65, "A32NX.FCU_LOC_PUSH", 2);
        appr = MakeToggle("APPR", 210, 65, "A32NX.FCU_APPR_PUSH", 3);
        // (No EXPED button — the A380 FCU has none; FBW #10855 removed the backing var
        //  and A32NX.FCU_EXPED_PUSH does not exist on this airframe.)
        // The FD pushbutton — ONE button for both flight directors since FBW #10855, so one
        // toggle here, labelled from its light (see A380FlightDirector). It takes the tab slot
        // and position EXPED left free. Pressed through the definition, which records the state
        // it commands: a Flight Directors combo pick straight after is then judged against the
        // new state, not the cache the once-a-second batch has not yet updated, and is no
        // second press.
        fd = MakeToggle("FD", 20, 110, A380FlightDirector.PushEvent, 4,
            press: () => aircraft.ToggleFlightDirectors(simConnect));

        var athr = new Button { Text = "A/THR engage", Location = new Point(210, 110), Size = new Size(180, 35), TabIndex = 5, AccessibleName = "Autothrust engage" };
        athr.Click += (s, e) => { simConnect.SendEvent("AUTO_THROTTLE_ARM"); RefreshStates(); };
        var apDisc = new Button { Text = "AP disconnect", Location = new Point(20, 155), Size = new Size(180, 35), TabIndex = 6, AccessibleName = "Autopilot disconnect" };
        // Same A380-new-FCU K-event family as the mode buttons (the dotted H-event is inert).
        // NOTE: not fire-tested live (would disconnect the AP in flight) — verify on the ground.
        apDisc.Click += (s, e) => { simConnect.SendEvent("A32NX.FCU_AP_DISCONNECT_PUSH"); RefreshStates(); };
        var athrDisc = new Button { Text = "A/THR disconnect", Location = new Point(210, 155), Size = new Size(180, 35), TabIndex = 7, AccessibleName = "Autothrust disconnect" };
        athrDisc.Click += (s, e) => { simConnect.SendEvent("A32NX.FCU_ATHR_DISCONNECT_PUSH"); RefreshStates(); };

        var closeButton = new Button { Text = "Close", Location = new Point(140, 250), Size = new Size(140, 35), TabIndex = 8, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { ap1, ap2, loc, appr, fd, athr, apDisc, athrDisc, closeButton });
        CancelButton = closeButton;

        // Continuous refresh so every button label tracks the LIVE state. Modes can engage a
        // moment AFTER the press (LOC/APPR only arm when the aircraft accepts them), which the
        // old one-shot 250 ms timer stopped itself too early to catch.
        refreshTimer = new System.Windows.Forms.Timer { Interval = 400 };
        refreshTimer.Tick += (s, e) => { aircraft.RequestAutopilotStates(simConnect); UpdateLabels(); };
    }

    private Button MakeToggle(string name, int x, int y, string evt, int tab, Action? press = null)
    {
        var b = new Button { Text = name + " ...", Location = new Point(x, y), Size = new Size(180, 35), TabIndex = tab, AccessibleName = name, Tag = name };
        // CRITICAL: the A380's NEW FCU (FCU/Managers/AutopilotManager.ts) consumes these as
        // K-EVENTS (K:A32NX.FCU_AP_1_PUSH / AP_2 / LOC / APPR) — NOT the dotted H-event the
        // A320 used. Firing the H-event does NOTHING (live-verified: H:A32NX.FCU_AP_2_PUSH left
        // A32NX_AUTOPILOT_2_ACTIVE at 0; the K-event flipped it to 1). So fire via the calc K path.
        b.Click += (s, e) => { if (press != null) press(); else simConnect.SendEvent(evt); RefreshStates(); };
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
        // LOC/APPR state = the FCU button lights since FBW #10855.
        SetState(loc, "LOC", "A32NX_FCU_LOC_LIGHT_ON");
        SetState(appr, "APPR", "A32NX_FCU_APPR_LIGHT_ON");
        SetState(fd, "FD", A380FlightDirector.StateKey);
    }

    private void SetState(Button b, string name, string stateVar)
    {
        bool on = (simConnect.GetCachedVariableValue(stateVar) ?? 0) > 0.5;
        b.Text = $"{name} ({(on ? "on" : "off")})";
        b.AccessibleName = $"{name} {(on ? "on" : "off")}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { refreshTimer.Stop(); refreshTimer.Dispose(); base.OnFormClosing(e); }
}

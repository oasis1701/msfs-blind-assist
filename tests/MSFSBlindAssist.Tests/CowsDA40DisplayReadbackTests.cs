using System.Collections.Generic;
using MSFSBlindAssist.Forms.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What the G1000 display window says after a softkey press, and when its list redraws.
/// The rows are the agent's own, captured live on the NG at LOWG (2026-10-12).
/// </summary>
public class CowsDA40DisplayReadbackTests
{
    private static List<string> Pfd(string time, string transponder, params string[] softkeys)
    {
        var rows = new List<string>
        {
            "CAS messages: 1",
            "  Caution: PITOT HT OFF",
            "Autopilot: off",
            "Navigation source: GPS, DPRT",
            "Radio: NAV 1, active 116.20, standby 113.90, TUNING",
            "Information bar: OAT 10°C, ISA -3°C, TAS 0 KT, GS blank KT",
            "Time: " + time,
            "Altimeter setting: 30.06 IN",
            "Transponder: " + transponder
        };
        for (int i = 0; i < softkeys.Length; i++) rows.Add($"Softkey {i + 1}: {softkeys[i]}");
        return rows;
    }

    private static readonly string[] Keypad = { "0", "1", "2", "3", "4", "5", "6", "7", "Ident, dimmed", "BKSP", "Back", "Alerts, dimmed" };

    [Fact]
    public void ASquawkDigitIsAnsweredWithTheCodeNotTheClock()
    {
        // The "before" is the last poll, up to 1.2 s old, so the clock has moved too - and it
        // comes first on the PFD. It used to be what the pilot heard.
        var before = Pfd("TMR 0:00:00 UTC 03:04:12", "code 1 0 0 0, on", Keypad);
        var after = Pfd("TMR 0:00:00 UTC 03:04:13", "code 0 0 0 7, on", Keypad);
        Assert.Equal("Transponder: code 0 0 0 7, on", CowsDA40DisplayForm.FirstChangedRow(before, after));
    }

    [Fact]
    public void AnEngineGaugeOrACautionIsNeverTheAnswerToAKey()
    {
        var before = new List<string> { "Page: EIS - Engine", "Engine strip:", "  RPM: 710", "CAS messages: 1", "  Caution: PITOT HT OFF", "Fields (1), cursor on:", "  Failures Mode: OFF" };
        var after = new List<string> { "Page: EIS - Engine", "Engine strip:", "  RPM: 720", "CAS messages: 1", "  Caution: FUEL LOW", "Fields (1), cursor on:", "  Failures Mode: Normal" };
        Assert.Equal("Failures Mode: Normal", CowsDA40DisplayForm.FirstChangedRow(before, after));
    }

    [Fact]
    public void OnlyTheClockMovingIsNoChange()
    {
        var before = Pfd("TMR 0:00:00 UTC 03:04:12", "code 7 7 0 0, on", Keypad);
        var after = Pfd("TMR 0:00:00 UTC 03:04:14", "code 7 7 0 0, on", Keypad);
        Assert.Equal("", CowsDA40DisplayForm.FirstChangedRow(before, after));
    }

    [Fact]
    public void NewSoftkeysRedrawAtOnceWhileThePilotIsReading()
    {
        // The fourth digit puts the transponder keys back. A list still showing the keypad
        // would make Enter on "Softkey 8: 7" press what key 8 has become.
        var keypad = Pfd("TMR 0:00:00 UTC 03:04:12", "code 0 7 7 0, on", Keypad);
        var menu = Pfd("TMR 0:00:00 UTC 03:04:13", "code 7 7 0 0, on",
            "blank", "blank", "Standby", "On", "Alt", "blank", "VFR", "Code", "Ident", "blank", "Back", "Alerts, dimmed");
        Assert.True(CowsDA40DisplayForm.MustRedrawNow(keypad, menu));
    }

    [Fact]
    public void AnotherPageRedrawsAtOnce()
    {
        var vor = new List<string> { "Page: NRST - Nearest VOR", "Page content:", "  Nearest VOR:", "    GRZ, 160°, 2.4 NM" };
        var apt = new List<string> { "Page: NRST - Nearest Airports", "Page content:", "  Nearest Airports:", "    LOWG, 110°, 0.2 NM" };
        Assert.True(CowsDA40DisplayForm.MustRedrawNow(vor, apt));
    }

    [Fact]
    public void AWindowOpeningRedrawsAtOnce()
    {
        var plain = Pfd("TMR 0:00:00 UTC 03:04:12", "code 1 0 0 0, on", "blank");
        var alerts = new List<string>(plain);
        alerts.Insert(alerts.Count - 1, "Alerts:");
        alerts.Insert(alerts.Count - 1, "  PITOT HT OFF - Pitot heat is off.");
        Assert.True(CowsDA40DisplayForm.MustRedrawNow(plain, alerts));
    }

    [Fact]
    public void AValueChangingOnTheSameScreenWaitsForThePilot()
    {
        var before = Pfd("TMR 0:00:00 UTC 03:04:12", "code 1 0 0 0, on", Keypad);
        var after = Pfd("TMR 0:00:00 UTC 03:04:13", "code 0 0 0 7, on", Keypad);
        Assert.False(CowsDA40DisplayForm.MustRedrawNow(before, after));
    }
}

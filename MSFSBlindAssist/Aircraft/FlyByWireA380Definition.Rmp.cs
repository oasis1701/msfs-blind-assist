using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class FlyByWireA380Definition
{
    /// <summary>Fire a single RMP keypad key (press + release) on RMP <paramref name="rmp"/>.
    /// Used by the RMP window for the page selectors / line keys / swap / clear / digit entry.</summary>
    public void SendRmpKey(int rmp, string key, SimConnectManager s)
    {
        if (s == null || !s.IsConnected) return;
        if (rmp < 1 || rmp > 3) rmp = 1;   // 1=Captain, 2=First Officer, 3=Overhead (RMP 3)
        // CRITICAL #1: fire PRESS and RELEASE in ONE calculator call. MobiFlight's command channel
        // is a single shared buffer it reads once per frame — two back-to-back ExecuteCalculatorCode
        // calls (separate SetClientData writes) land in the same frame, so the RELEASE overwrites the
        // PRESS before the WASM module processes it. The key then never registers as pressed and the
        // page switch / digit / swap silently does nothing. One call = one buffer write = both events
        // run together (live-verified: page switch + digit entry only work this way through the app).
        // CRITICAL #2: the SAME channel also COALESCES two CONSECUTIVE IDENTICAL command strings — so
        // typing a repeated key (the "22" in 122.800, or the select-then-load double-press of LSK_1 /
        // swap double-press of ADK_1) silently dropped the second press (122.800 -> 128.000; load/swap
        // no-op). The leading "{seq} 0 *" makes every call's string textually unique (computes a
        // discarded 0) so it's never deduped — same anti-dedup idiom as FireDcduEvent / the seat-motor
        // ticks. (Earlier "verified" repeats like 118500/11850 hid this because their auto-complete
        // padded the dropped-duplicate result back to the right frequency; 122.800 does not.)
        s.ExecuteCalculatorCode($"{++_rmpKeySeq} 0 * (>H:RMP_{rmp}_{key}_PRESSED) (>H:RMP_{rmp}_{key}_RELEASED)");
    }

    /// <summary>Set the transponder squawk straight from the RMP window via the stock <c>XPNDR_SET</c>
    /// event (BCD16) — INDEPENDENT of the RMP SQWK page / keypad chain, which proved unreliable to drive
    /// externally. Live-verified: <c>0x{code} (&gt;K:XPNDR_SET)</c> changes TRANSPONDER CODE:1 regardless
    /// of which RMP page the cockpit shows. Speaks the code once and primes the XPNDR_CODE monitor to skip
    /// its duplicate announce. <paramref name="fourOctalDigits"/> must be 4 chars, each 0–7.</summary>
    public void SetSquawkFromForm(string fourOctalDigits, SimConnectManager s, ScreenReaderAnnouncer? ann)
    {
        if (s == null || !s.IsConnected || string.IsNullOrEmpty(fourOctalDigits) || fourOctalDigits.Length != 4) return;
        foreach (char c in fourOctalDigits) if (c < '0' || c > '7') return;   // squawk is octal
        _formSetSquawkBcd = Convert.ToInt32(fourOctalDigits, 16);   // "2222" -> 0x2222 (each nibble = a digit)
        s.ExecuteCalculatorCode($"0x{fourOctalDigits} (>K:XPNDR_SET)");
        ann?.AnnounceImmediate($"Squawk {fourOctalDigits}");
    }

    /// <summary>Fire an IDENT pulse via the stock <c>XPNDR_IDENT_ON</c> event (the same one the FBW
    /// TransponderController uses) — RMP-page-independent. Announced by the caller.</summary>
    public void SendTransponderIdent(SimConnectManager s)
    {
        if (s == null || !s.IsConnected) return;
        s.ExecuteCalculatorCode("(>K:XPNDR_IDENT_ON)");
    }

    /// <summary>Press a single RMP keypad key WITHOUT releasing it. Pair with
    /// <see cref="SendRmpKeyRelease"/> — used by the RMP window to HOLD the Clear key: held for
    /// &gt;1 s the FBW RMP does a FULL scratchpad clear (vs a single-digit backspace on a tap).
    /// A full clear is REQUIRED before typing a new frequency, because an invalid scratchpad entry
    /// blocks all further digits (<c>VhfComController.onDigitEntered</c> early-returns when invalid).</summary>
    public void SendRmpKeyPress(int rmp, string key, SimConnectManager s)
    {
        if (s == null || !s.IsConnected) return;
        if (rmp < 1 || rmp > 3) rmp = 1;   // 1=Captain, 2=First Officer, 3=Overhead (RMP 3)
        // Same anti-dedup idiom as SendRmpKey — the leading "{seq} 0 *" makes this call's string
        // textually unique so MobiFlight's command channel never coalesces it with the last one
        // (e.g. a held-then-tapped repeat of the same key). Shares SendRmpKey's counter so a
        // press/release pair issued back-to-back from separate calls still can't collide.
        s.ExecuteCalculatorCode($"{++_rmpKeySeq} 0 * (>H:RMP_{rmp}_{key}_PRESSED)");
    }

    /// <summary>Release a single RMP keypad key (the up half of <see cref="SendRmpKeyPress"/>).</summary>
    public void SendRmpKeyRelease(int rmp, string key, SimConnectManager s)
    {
        if (s == null || !s.IsConnected) return;
        if (rmp < 1 || rmp > 3) rmp = 1;   // 1=Captain, 2=First Officer, 3=Overhead (RMP 3)
        s.ExecuteCalculatorCode($"{++_rmpKeySeq} 0 * (>H:RMP_{rmp}_{key}_RELEASED)");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Shared output-mode "TO waypoint" readout (Ctrl+W) for the FlyByWire jets.
/// Speaks the ND active/TO waypoint ident, live distance, and bearing using the
/// SAME A32NX_EFIS_L_TO_WPT_* SimVars the A380 ND read-form already uses — so it
/// is a pure SimConnect (SimVar) path with NO Coherent debugger connection, and
/// therefore cannot conflict with viewing the ND via the live ND panel. The same
/// vars exist + are registered (OnRequest) on both the A380X and the A32NX, so one
/// implementation serves both. Bearing follows the ND reference: TRUE when TRUE REF
/// is selected, else magnetic. FBW writes both bearings in "Degrees", which the sim
/// stores as radians, and an on-request L:var is read raw, so both arrive in radians
/// (read live 2026-11-04 on the A380: true 5.1505 raw = 295.1 degrees).
/// </summary>
public static class NdWaypointReadout
{
    // TRUE-REF source differs per jet: the A380 registers A32NX_PUSH_TRUE_REF (the
    // real pilot input there; FMGC_TRUE_REF has no A380 writer), the A32NX registers
    // A32NX_FMGC_TRUE_REF. Both are listed; the unregistered one is a harmless no-op
    // request and reads 0 from the cache, so OR-ing the two reads is correct.
    private static readonly List<string> Vars = new()
    {
        "A32NX_EFIS_L_TO_WPT_IDENT_0", "A32NX_EFIS_L_TO_WPT_IDENT_1",
        "A32NX_EFIS_L_TO_WPT_DISTANCE", "A32NX_EFIS_L_TO_WPT_BEARING",
        "A32NX_EFIS_L_TO_WPT_TRUE_BEARING", "A32NX_FMGC_TRUE_REF", "A32NX_PUSH_TRUE_REF",
    };

    /// <summary>
    /// Force-reads the TO-waypoint SimVars and announces them. Fire-and-forget:
    /// the read is async (one SimConnect round-trip) so the call returns immediately.
    /// Non-handler async void (called directly from HandleHotkeyAction, not subscribed
    /// to an event) — wrapped end-to-end so a fault can't escape as an unobserved
    /// async-void exception.
    /// </summary>
    public static async void Announce(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (sim == null || announcer == null) return;
        try
        {
            if (!sim.IsConnected) { announcer.AnnounceImmediate("Not connected"); return; }

            // Seed from cache, then force a fresh read of each var (all OnRequest, so
            // RequestVariable(forceUpdate) delivers them; not batch-covered).
            var raw = new Dictionary<string, double>(sim.GetCachedVariableSnapshot(Vars));
            foreach (var v in Vars) sim.RequestVariable(v, forceUpdate: true);
            await Task.Delay(400);
            foreach (var kvp in sim.GetCachedVariableSnapshot(Vars)) raw[kvp.Key] = kvp.Value;

            double R(string v) => raw.TryGetValue(v, out double d) ? d : 0;

            string ident = UnpackSixBit(R("A32NX_EFIS_L_TO_WPT_IDENT_0"), R("A32NX_EFIS_L_TO_WPT_IDENT_1"));
            if (string.IsNullOrWhiteSpace(ident))
            {
                announcer.AnnounceImmediate("No active waypoint");
                return;
            }

            bool trueRef = R("A32NX_FMGC_TRUE_REF") > 0.5 || R("A32NX_PUSH_TRUE_REF") > 0.5;
            var (brg, brgRef) = Bearing(trueRef,
                R("A32NX_EFIS_L_TO_WPT_TRUE_BEARING"), R("A32NX_EFIS_L_TO_WPT_BEARING"));

            double dist = R("A32NX_EFIS_L_TO_WPT_DISTANCE");
            announcer.AnnounceImmediate(
                $"{ident}, distance {dist:0.0} nautical miles, bearing {brg:000} degrees {brgRef}");
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"Announce error: {ex.Message}");
        }
    }

    /// <summary>
    /// The bearing to speak, in degrees, from the two stored (radian) bearings: TRUE when
    /// TRUE REF is selected and the true bearing is valid, else magnetic.
    /// </summary>
    internal static (double degrees, string reference) Bearing(bool trueRef, double storedTrue, double storedMagnetic)
    {
        bool useTrue = trueRef && storedTrue >= 0;
        double deg = (useTrue ? storedTrue : storedMagnetic) * 180.0 / Math.PI;
        if (deg < 0) deg += 360;
        return (deg, useTrue ? "true" : "magnetic");
    }

    /// <summary>
    /// The one decoder for FBW's 6-bit text (ND TO-waypoint idents, the A32NX approach
    /// message): 8 chars per double, low bits first, char = code + 31. A word holding
    /// 6+ chars passes 2^31, so each word is read as a long: an int cast saturates
    /// there and the first two chars read "^^" (61N020W read "^^1W020").
    /// </summary>
    internal static string UnpackSixBit(double word0, double word1)
    {
        var sb = new System.Text.StringBuilder();
        foreach (double word in new[] { word0, word1 })
        {
            long bits = (long)word;
            for (int charPos = 0; charPos < 8; charPos++)
            {
                int code = (int)((bits >> (charPos * 6)) & 0x3F);
                if (code > 0) sb.Append((char)(code + 31));
            }
        }
        return sb.ToString().Trim();
    }
}

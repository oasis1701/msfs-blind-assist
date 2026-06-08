using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Shared output-mode "TO waypoint" readout (Ctrl+W) for the FlyByWire jets.
/// Speaks the ND active/TO waypoint ident, live distance, and bearing using the
/// SAME A32NX_EFIS_L_TO_WPT_* SimVars the A380 ND read-form already uses — so it
/// is a pure SimConnect (SimVar) path with NO Coherent debugger connection, and
/// therefore cannot conflict with viewing the ND via the live ND panel. The same
/// vars exist + are registered (OnRequest) on both the A380X and the A32NX, so one
/// implementation serves both. Bearing follows the ND reference: TRUE when TRUE REF
/// is selected, else magnetic (the FBW var is radians for magnetic, degrees for true).
/// </summary>
public static class NdWaypointReadout
{
    private static readonly List<string> Vars = new()
    {
        "A32NX_EFIS_L_TO_WPT_IDENT_0", "A32NX_EFIS_L_TO_WPT_IDENT_1",
        "A32NX_EFIS_L_TO_WPT_DISTANCE", "A32NX_EFIS_L_TO_WPT_BEARING",
        "A32NX_EFIS_L_TO_WPT_TRUE_BEARING", "A32NX_FMGC_TRUE_REF",
    };

    /// <summary>
    /// Force-reads the TO-waypoint SimVars and announces them. Fire-and-forget:
    /// the read is async (one SimConnect round-trip) so the call returns immediately.
    /// </summary>
    public static async void Announce(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (sim == null || announcer == null) return;
        if (!sim.IsConnected) { announcer.AnnounceImmediate("Not connected"); return; }

        // Seed from cache, then force a fresh read of each var (all OnRequest, so
        // RequestVariable(forceUpdate) delivers them; not batch-covered).
        var raw = new Dictionary<string, double>(sim.GetCachedVariableSnapshot(Vars));
        foreach (var v in Vars) sim.RequestVariable(v, forceUpdate: true);
        await Task.Delay(400);
        foreach (var kvp in sim.GetCachedVariableSnapshot(Vars)) raw[kvp.Key] = kvp.Value;

        double R(string v) => raw.TryGetValue(v, out double d) ? d : 0;

        string ident = UnpackIdent(R("A32NX_EFIS_L_TO_WPT_IDENT_0"), R("A32NX_EFIS_L_TO_WPT_IDENT_1"));
        if (string.IsNullOrWhiteSpace(ident))
        {
            announcer.AnnounceImmediate("No active waypoint");
            return;
        }

        bool trueRef = R("A32NX_FMGC_TRUE_REF") > 0.5;
        double trueBrg = R("A32NX_EFIS_L_TO_WPT_TRUE_BEARING");
        double brg; string brgRef;
        if (trueRef && trueBrg >= 0) { brg = trueBrg; brgRef = "true"; }
        else { brg = R("A32NX_EFIS_L_TO_WPT_BEARING") * 180.0 / Math.PI; brgRef = "magnetic"; }
        if (brg < 0) brg += 360;

        double dist = R("A32NX_EFIS_L_TO_WPT_DISTANCE");
        announcer.AnnounceImmediate(
            $"{ident}, distance {dist:0.0} nautical miles, bearing {brg:000} degrees {brgRef}");
    }

    // 6-bit-per-char packed ident (two doubles, 8 chars each) — same encoding the
    // ND form and approach-capability message use.
    private static string UnpackIdent(double ident0, double ident1)
    {
        double[] values = { ident0, ident1 };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length * 8; i++)
        {
            int word = i / 8, charPos = i % 8;
            int code = (int)(values[word] / Math.Pow(2, charPos * 6)) & 0x3F;
            if (code > 0) sb.Append((char)(code + 31));
        }
        return sb.ToString().Trim();
    }
}

namespace MSFSBlindAssist.Aircraft;

/// <summary>One FCU input event to fire, with its parameter (0 for a plain push).</summary>
public readonly record struct A380EfisCommand(string EventName, uint Parameter = 0);

/// <summary>
/// EFIS-CP and FCU controls whose backing L:var is an FCU-SHIM OUTPUT, so a direct L:var
/// write is DEAD.
///
/// ⚠️ `FlyByWireA380Definition.HandleUIVariableSet` routes every unclaimed `A32NX_EFIS_` /
/// `A380X_EFIS_` key — and, further down, every unclaimed `A32NX_` key — to a direct
/// calculator write, on the comment's claim that *"EFIS Control Panel controls are ALL
/// direct L:var writes on the A380X (no events)"*. That is true for some of them and FALSE
/// for every key in this class: `fbw.wasm`'s `FlyByWireInterface::updateFcu` decodes the
/// FCU's ARINC output words and rewrites each of these vars EVERY FRAME (the `idFcuShim…`
/// family), so the write is overwritten within one frame and the control never moves. The
/// panel combo then snaps back to the aircraft's value and the pilot has a dead control
/// with no error — nothing announces, nothing logs.
///
/// Live-measured on a380x 2026-09-03 (each write reverted within one read):
/// <list type="bullet">
/// <item>`A32NX_EFIS_R_NAVAID_1_MODE` ← 2 read back 0</item>
/// <item>`A32NX_PUSH_TRUE_REF` ← 1 read back 0 — which RETIRES the 2026-06 comment on its
/// registration claiming "writing PUSH_TRUE_REF=1 latched and is what the displays read".
/// That was true when measured; FBW #10855's new FCU took ownership of the var afterwards.
/// A var's WRITER can change under a name that survives — re-measure, don't trust the note.</item>
/// </list>
///
/// Every actuator below was read out of the shipped `fbw.wasm`. None of these keys is
/// touched by the First Officer — this is a panel-only fault.
///
/// ⚠️ EVERY control here is ONE absolute event. That is what lets the caller be a single
/// `SendEvent` with no spacing, no serialization and no async: keep it that way. The first
/// version drove the navaid selectors by CYCLING `…_NAVAID_{n}_PUSH` 0-2 times, which needed
/// all three (the FCU samples panel inputs once per frame and a push is a bool, not a
/// counter, so two presses in one frame register as ONE — measured). FBW #10914 added the
/// absolute `…_NAVAID_{n}_SET` on 2026-09-01 and the cycle went away with it. If a future
/// control here can only be reached by stepping, do NOT re-introduce spacing inline —
/// restore the serialized async-local walk from this file's history (never `Task.Run`).
/// </summary>
public static class A380EfisCpControls
{
    /// <summary>ND overlay: 0 Off / 1 Weather / 2 Terrain (`getNdOverlay`).</summary>
    public const int OverlayOff = 0, OverlayWeather = 1, OverlayTerrain = 2;

    /// <summary>Navaid selector: 0 Off / 1 ADF / 2 VOR. The published `getNavaidMode` values
    /// and the `a380_efis_navaid_selection` enum the SET event takes are the SAME
    /// (NONE = 0, ADF, VOR), so the value goes on the wire unchanged.</summary>
    public const int MaxNavaid = 2;

    /// <summary>Highest OANS zoom index. `getOansRange` publishes 0..4 for the five zoom
    /// levels and 5 for "not zoomed"; 0..4 are exactly `a380_efis_range_selection`'s
    /// RANGE_ZOOM_POINT_2..RANGE_ZOOM_5, so the RANGE_SET parameter is the value itself.
    /// Live-verified: RANGE_SET 2 drove `A32NX_EFIS_R_OANS_RANGE` to 2. 5 is not settable —
    /// leaving the zoom means choosing a range in NM on the ND RANGE knob instead.</summary>
    public const int MaxOansZoom = 4;

    /// <summary>
    /// True when this class owns <paramref name="varKey"/>, whether or not a given set has
    /// anything to send.
    ///
    /// ⚠️ The caller MUST gate on this rather than on <see cref="Command"/> returning null.
    /// Command answers null for two different questions — "not my key" and "already there,
    /// send nothing" — and conflating them drops a no-op set (a toggle picked at its current
    /// position, an overlay re-picked) through to the direct-L:var catch-all, which is the
    /// dead write this whole class exists to bypass. Harmless today because the write lands
    /// on a var the FCU immediately overwrites, but it is the exact trap that would bite the
    /// moment one of these keys stops being shim-owned.
    /// </summary>
    public static bool Handles(string varKey) =>
        varKey == "A32NX_PUSH_TRUE_REF"
        || TrySplit(varKey, "A32NX_EFIS_", "_NAVAID_1_MODE", out _)
        || TrySplit(varKey, "A32NX_EFIS_", "_NAVAID_2_MODE", out _)
        || TrySplit(varKey, "A32NX_EFIS_", "_OANS_RANGE", out _)
        || TrySplit(varKey, "A380X_EFIS_", "_LS_BUTTON_IS_ON", out _)
        || TrySplit(varKey, "A380X_EFIS_", "_TRAF_BUTTON_IS_ON", out _)
        || TrySplit(varKey, "A380X_EFIS_", "_ACTIVE_OVERLAY", out _);

    /// <summary>
    /// The FCU input event that moves <paramref name="varKey"/> to <paramref name="desired"/>,
    /// given the live <paramref name="current"/> value, or null when the key is not one of
    /// these shim-output controls (every other key must keep its existing routing) or when
    /// there is nothing to send.
    ///
    /// A null <paramref name="current"/> (cold cache) is read as 0, matching the
    /// `_fcuToggleEvents` precedent. It only affects the RELATIVE controls — the two absolute
    /// SET controls ignore it entirely and therefore work from a cold cache, which the old
    /// cycling navaid path could not.
    /// </summary>
    public static A380EfisCommand? Command(string varKey, double desired, double? current)
    {
        int want = (int)Math.Round(desired);
        int have = (int)Math.Round(current ?? 0);

        // ---- Absolute setters: no current state needed, one event, no ordering ----
        if (TrySplit(varKey, "A32NX_EFIS_", "_NAVAID_1_MODE", out string? side))
            return NavaidSet(side!, 1, want);
        if (TrySplit(varKey, "A32NX_EFIS_", "_NAVAID_2_MODE", out side))
            return NavaidSet(side!, 2, want);
        if (TrySplit(varKey, "A32NX_EFIS_", "_OANS_RANGE", out side))
            return want is >= 0 and <= MaxOansZoom
                ? new A380EfisCommand($"A32NX.FCU_EFIS_{side}_RANGE_SET", (uint)want)
                : null;

        // ---- LS and TRAF: plain toggles, live-verified 0 -> 1 -> 0 ----
        if (TrySplit(varKey, "A380X_EFIS_", "_LS_BUTTON_IS_ON", out side))
            return Toggle($"A32NX.FCU_EFIS_{side}_LS_PUSH", want > 0, have > 0);
        if (TrySplit(varKey, "A380X_EFIS_", "_TRAF_BUTTON_IS_ON", out side))
            return Toggle($"A32NX.FCU_EFIS_{side}_TRAF_PUSH", want > 0, have > 0);

        // ---- ND overlay: WX and TERR are two buttons over one three-state selection ----
        // Same shape as NdFilterSelection — press the button you WANT, or, to clear, press
        // whichever is currently shown. Unlike the ND filter, this one really does clear:
        // live-verified all four legs (Off->TERR->Off, Off->WX->TERR->Off), so it needs no
        // "cannot clear" announcement.
        if (TrySplit(varKey, "A380X_EFIS_", "_ACTIVE_OVERLAY", out side))
        {
            if (want == have) return null;
            string? name = (want == OverlayOff ? have : want) switch
            {
                OverlayWeather => "WX",
                OverlayTerrain => "TERR",
                _ => null
            };
            return name == null ? null : new A380EfisCommand($"A32NX.FCU_EFIS_{side}_{name}_PUSH");
        }

        // ---- TRUE/MAG heading reference: one toggle pushbutton on the FCU ----
        // Actuator read from fcu.xml (the PUSH_FCU_TRUEMAG button's LEFT_SINGLE_CODE) and
        // confirmed present in fbw.wasm. Source-verified only — the dead write it replaces
        // IS live-measured, but toggling the crew's heading reference in flight was not a
        // probe worth running on a live aircraft.
        if (varKey == "A32NX_PUSH_TRUE_REF")
            return Toggle("A32NX.FCU_TRUE_TOGGLE_PUSH", want > 0, have > 0);

        return null;
    }

    private static A380EfisCommand? NavaidSet(string side, int knob, int want) =>
        want is >= 0 and <= MaxNavaid
            ? new A380EfisCommand($"A32NX.FCU_EFIS_{side}_NAVAID_{knob}_SET", (uint)want)
            : null;

    private static A380EfisCommand? Toggle(string evt, bool want, bool have) =>
        want == have ? null : new A380EfisCommand(evt);

    /// <summary>Match `{prefix}{L|R}{suffix}` and hand back the side.</summary>
    private static bool TrySplit(string varKey, string prefix, string suffix, out string? side)
    {
        side = null;
        if (!varKey.StartsWith(prefix, StringComparison.Ordinal)
            || !varKey.EndsWith(suffix, StringComparison.Ordinal)) return false;
        string middle = varKey[prefix.Length..^suffix.Length];
        if (middle is not ("L" or "R")) return false;
        side = middle;
        return true;
    }
}

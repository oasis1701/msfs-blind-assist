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
/// touched by any auto-flow or checklist code — this is a panel-only fault.
///
/// ⚠️ EVERY control here is ONE event. That is what lets the caller be a single `SendEvent`
/// with no spacing, no serialization and no async: keep it that way. (ONE event, not
/// necessarily an ABSOLUTE one — the navaid and OANS setters are absolute and ignore the
/// live value, but LS, TRAF, the overlay and `A32NX_PUSH_TRUE_REF` are TOGGLES that must
/// read it; see <see cref="Command"/>. Dropping <paramref name="current"/> as vestigial
/// would drive each of those four to the opposite of what the pilot picked.) The first
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
    /// Live-verified: RANGE_SET 2 drove `A32NX_EFIS_R_OANS_RANGE` to 2.</summary>
    public const int MaxOansZoom = 4;

    /// <summary>`getOansRange`'s "not zoomed" readback — the value the var holds for most of a
    /// flight, i.e. whenever the ND RANGE knob is on a range in NM. It is a READBACK, not a
    /// selection: leaving the zoom means picking an NM range on the ND RANGE knob, a different
    /// control. The position must still appear in the combo's value list — without it the combo
    /// opens with NOTHING selected (MainForm only defaults to item 0 when the key has no value
    /// at all), and one arrow key then commits an airport zoom the pilot never asked for — so
    /// like the ND RANGE knob's "Zoom" it is refused OUT LOUD; see
    /// <see cref="NotZoomedUnsupportedMessage"/>.</summary>
    public const int OansNotZoomed = 5;

    /// <summary>Which control a var key is. ONE table: <see cref="Handles"/> and
    /// <see cref="Command"/> both classify through <see cref="Classify"/>, so the ownership
    /// list and the actuator list cannot drift apart. They used to be two hand-maintained
    /// copies, where adding a key to one and not the other silently produced either a dead
    /// write (the catch-all claims it) or a dead control (swallowed, nothing sent).</summary>
    private enum Kind { None, Navaid1, Navaid2, OansRange, Ls, Traf, Overlay, TrueRef }

    private static Kind Classify(string varKey, out string side)
    {
        side = string.Empty;
        if (varKey == "A32NX_PUSH_TRUE_REF") return Kind.TrueRef;
        if (TrySplit(varKey, "A32NX_EFIS_", "_NAVAID_1_MODE", out side)) return Kind.Navaid1;
        if (TrySplit(varKey, "A32NX_EFIS_", "_NAVAID_2_MODE", out side)) return Kind.Navaid2;
        if (TrySplit(varKey, "A32NX_EFIS_", "_OANS_RANGE", out side)) return Kind.OansRange;
        if (TrySplit(varKey, "A380X_EFIS_", "_LS_BUTTON_IS_ON", out side)) return Kind.Ls;
        if (TrySplit(varKey, "A380X_EFIS_", "_TRAF_BUTTON_IS_ON", out side)) return Kind.Traf;
        if (TrySplit(varKey, "A380X_EFIS_", "_ACTIVE_OVERLAY", out side)) return Kind.Overlay;
        return Kind.None;
    }

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
    public static bool Handles(string varKey) => Classify(varKey, out _) != Kind.None;

    /// <summary>
    /// The FCU input event that moves <paramref name="varKey"/> to <paramref name="desired"/>,
    /// given the live <paramref name="current"/> value, or null when the key is not one of
    /// these shim-output controls (every other key must keep its existing routing) or when
    /// there is nothing to send.
    ///
    /// ⚠️ <paramref name="current"/> must be the caller's BEST view of the live value, not a
    /// bare `GetCachedVariableValue` — that cache is fed only by the 1 Hz continuous batch and
    /// is never written on a UI set, so two combo commits inside one batch period both see the
    /// pre-first-commit value. `FlyByWireA380Definition.EfisCpCurrentValue` layers what it
    /// just commanded over the cache for exactly this reason.
    ///
    /// A null <paramref name="current"/> means the live value is UNKNOWN (cold cache, before
    /// the first batch after a connect or an aircraft switch). The absolute setters ignore it
    /// and work regardless, which the old cycling navaid path could not. The TOGGLES fire
    /// anyway — matching the `?? (desiredOn ? 0.0 : 1.0)` convention every other
    /// toggle-if-differs branch in `HandleUIVariableSet` uses (FD_1_CTL, ELEC_ENG_GEN,
    /// SEATBELT_SIGN, the fuel-pump circuits) — because reading unknown as 0 makes "Off"
    /// permanently unsendable while "On" toggles an already-on control back OFF. The overlay
    /// is the one exception: CLEARING it means re-pressing whichever button is shown, so with
    /// an unknown state there is no button to name and it sends nothing.
    /// </summary>
    public static A380EfisCommand? Command(string varKey, double desired, double? current)
    {
        Kind kind = Classify(varKey, out string side);
        int want = (int)Math.Round(desired);

        switch (kind)
        {
            // ---- Absolute setters: no current state needed, one event, no ordering ----
            case Kind.Navaid1: return NavaidSet(side, 1, want);
            case Kind.Navaid2: return NavaidSet(side, 2, want);
            case Kind.OansRange:
                // OansNotZoomed is deliberately NOT settable — it is refused out loud by the
                // caller via IsNotZoomedAttempt, not silently dropped here.
                return want is >= 0 and <= MaxOansZoom
                    ? new A380EfisCommand($"A32NX.FCU_EFIS_{side}_RANGE_SET", (uint)want)
                    : null;

            // ---- LS and TRAF: plain toggles, live-verified 0 -> 1 -> 0 ----
            case Kind.Ls: return Toggle($"A32NX.FCU_EFIS_{side}_LS_PUSH", want > 0, AsBool(current));
            case Kind.Traf: return Toggle($"A32NX.FCU_EFIS_{side}_TRAF_PUSH", want > 0, AsBool(current));

            // ---- TRUE/MAG heading reference: one toggle pushbutton on the FCU ----
            // Actuator read from fcu.xml (the PUSH_FCU_TRUEMAG button's LEFT_SINGLE_CODE) and
            // confirmed present in fbw.wasm. Source-verified only — the dead write it replaces
            // IS live-measured, but toggling the crew's heading reference in flight was not a
            // probe worth running on a live aircraft.
            case Kind.TrueRef: return Toggle("A32NX.FCU_TRUE_TOGGLE_PUSH", want > 0, AsBool(current));

            // ---- ND overlay: WX and TERR are two buttons over one three-state selection ----
            // Same shape as NdFilterSelection — press the button you WANT, or, to clear, press
            // whichever is currently shown. Unlike the ND filter, this one really does clear:
            // live-verified all four legs (Off->TERR->Off, Off->WX->TERR->Off), so it needs no
            // "cannot clear" announcement.
            //
            // ⚠️ Only the CLEAR leg reads `current`, and it is the leg that goes wrong when
            // the value is stale: a press of the NON-active button REPLACES the selection
            // rather than clearing it, so naming the button from a stale value switches the
            // overlay ON when the pilot asked for Off. With the value unknown there is no
            // button to name, so nothing is sent — never guess one.
            case Kind.Overlay:
            {
                int? have = current is { } c ? (int)Math.Round(c) : null;
                if (have == want) return null;
                int button = want == OverlayOff ? (have ?? -1) : want;
                string? name = button switch
                {
                    OverlayWeather => "WX",
                    OverlayTerrain => "TERR",
                    _ => null
                };
                return name == null ? null : new A380EfisCommand($"A32NX.FCU_EFIS_{side}_{name}_PUSH");
            }

            default: return null;
        }
    }

    /// <summary>True when the pilot picked the "Not zoomed" position on an OANS Range combo.
    /// See <see cref="OansNotZoomed"/> for why the position exists but cannot be commanded.</summary>
    public static bool IsNotZoomedAttempt(string varKey, double value) =>
        Classify(varKey, out _) == Kind.OansRange && (int)Math.Round(value) == OansNotZoomed;

    /// <summary>Spoken when "Not zoomed" is picked on an OANS Range combo. It names the control
    /// that DOES leave the zoom — a refusal that only says no teaches the pilot nothing.</summary>
    public static string NotZoomedUnsupportedMessage =>
        "Not zoomed is what this reads when the airport map is not zoomed in. "
        + "To leave the zoom, choose a range in nautical miles on the ND Range control.";

    private static bool? AsBool(double? value) => value is { } v ? v > 0.5 : null;

    private static A380EfisCommand? NavaidSet(string side, int knob, int want) =>
        want is >= 0 and <= MaxNavaid
            ? new A380EfisCommand($"A32NX.FCU_EFIS_{side}_NAVAID_{knob}_SET", (uint)want)
            : null;

    /// <summary>A push only when the pick differs from the live state. A null
    /// <paramref name="have"/> is UNKNOWN, which never equals <paramref name="want"/>, so the
    /// push fires — see <see cref="Command"/> for why that direction is the safe one.</summary>
    private static A380EfisCommand? Toggle(string evt, bool want, bool? have) =>
        have == want ? null : new A380EfisCommand(evt);

    /// <summary>Match `{prefix}{L|R}{suffix}` and hand back the side.</summary>
    private static bool TrySplit(string varKey, string prefix, string suffix, out string side)
    {
        side = string.Empty;
        // ⚠️ Length FIRST. Every prefix here ends with '_' and every suffix begins with one, so
        // a key ONE character shorter than the two combined satisfies StartsWith AND EndsWith
        // by overlapping on that underscore — "A32NX_EFIS_NAVAID_1_MODE" passes both — and the
        // range slice below would then be [11..10] and throw ArgumentOutOfRangeException
        // ("length ('-1')") out of a WinForms SelectionChangeCommitted handler, before the
        // `middle is not ("L" or "R")` guard written to reject exactly those keys ever runs.
        if (varKey.Length != prefix.Length + suffix.Length + 1
            || !varKey.StartsWith(prefix, StringComparison.Ordinal)
            || !varKey.EndsWith(suffix, StringComparison.Ordinal)) return false;
        string middle = varKey[prefix.Length..^suffix.Length];
        if (middle is not ("L" or "R")) return false;
        side = middle;
        return true;
    }
}

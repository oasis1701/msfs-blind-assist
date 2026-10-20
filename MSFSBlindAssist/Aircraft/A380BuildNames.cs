using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The FlyByWire A380X variables and events the FIRST OFFICER reads or writes that differ between
/// the two installed builds (<see cref="FbwSimBuild"/>). ONE table, read by the First Officer's
/// state evaluator (reads) and executor (writes), so the two can never disagree about which name a
/// build uses. Every getter takes a value source that answers NaN for an unread variable, and every
/// answer is NaN (or null) when the build is unknown: nothing build-dependent is guessed.
///
/// The A380 DEFINITION (panels, hotkeys, announcements) is on the FS2024 (post-#10855) names; this
/// table does not change it. The FS2020 names below are registered by the definition for the First
/// Officer only (<see cref="Fs2020FoRegistrations"/>, OnRequest, never announced), and this file is
/// the ONE place A380 code may spell them (A380Fbw10855DeadNameTests exempts it for exactly that).
///
/// Verified against the installed packages' own files (2026-10-20; FS2020 = flybywire-aircraft-
/// a380-842 2020.16.0, FS2024 = 2024.2.0):
/// <list type="bullet">
/// <item>FLIGHT DIRECTORS — FS2020: per side, the stock AUTOPILOT FLIGHT DIRECTOR ACTIVE:n (the
/// fcu.xml tooltip and FCU/fcu.js read it), toggled by K:TOGGLE_FLIGHT_DIRECTOR with the side as the
/// parameter (fcu.xml BUTTON_CODE fires "1 (&gt;K:TOGGLE_FLIGHT_DIRECTOR)" and "2 …"; the FS2020
/// fbw.wasm does not name the event, so nothing masks it). FS2024: ONE FCU pushbutton —
/// <c>A32NX.FCU_FD_PUSH</c> with light <c>L:A32NX_FCU_FD_LIGHT_ON</c> (fcu.xml + fbw.wasm; neither
/// name exists in the FS2020 package); see <see cref="A380FlightDirector"/>. That light is FD 1 OR
/// FD 2, so "both on" is read from the PRIM's FG discrete word 1 (bits 13 AND 14, the definition's
/// FD_1 key; the FS2024 PFD/EWD/MFD read the word, the FS2020 package has no FG word at all), the
/// light only when the word has no data.</item>
/// <item>BARO UNIT — FS2020: <c>L:XMLVAR_Baro_Selector_HPA_1</c> ONLY, 1 = hPa: both cockpit
/// selectors drive it (efis-cp.xml uses FBW_AIRLINER_Switch_Baro_Selector_Template with its default
/// ID 1, legacy/AirlinerCommon.xml) and FCU/fcu.js's BaroManager reads HPA_1 for both sides. FS2024:
/// <c>L:A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG</c>, 1 = inHg (efis-cp.xml + fcu.js + fbw.wasm; the FS2020
/// package has no _BARO_IS_INHG at all).</item>
/// <item>BARO STD — FS2020: the stock KOHLSMAN SETTING STD:n (fcu.js MsfsBaroManager writes 1 in STD,
/// 0 in QNH), set by <c>H:A380X_EFIS_CP_BARO_{PUSH,PULL}_n</c> (legacy/generated/A32NX_Interior_FCU.xml
/// fires them; fcu.js H_EVENT_REGEX consumes them; PUSH = STD, PULL = QNH, both directional; absent
/// from FS2024). FS2024: the FCU's <c>L:A32NX_FCU_EFIS_{L,R}_DISPLAY_BARO_IS_STD</c> (fcu.js +
/// fbw.wasm; the definition's A32NX_FCU_{LEFT,RIGHT}_EIS_BARO_IS_STD keys), set by
/// <c>A32NX.FCU_EFIS_{L,R}_BARO_{PUSH,PULL}</c> through the definition's verified baro branch.</item>
/// <item>LANDING CONF 3 — FS2020: <c>L:A32NX_SPEEDS_LANDING_CONF3</c> (MFD/mfd.js writes it,
/// SystemsHost reads it). FS2024: <c>L:A380X_FM_LANDING_CONF3</c> (mfd.js, SystemsHost, fbw.wasm).
/// Each name is absent from the other build.</item>
/// <item>ND MODE / RANGE — FS2020: the EFIS-CP knobs write <c>L:A32NX_EFIS_{L,R}_ND_{MODE,RANGE}</c>
/// directly (efis-cp.xml KNOB_POSITION_VAR), so the First Officer writes the L:var. FS2024: those are
/// FCU OUTPUTS, set through the definition's A380NdKnobSelection (A32NX.FCU_EFIS_*_{MODE,RANGE}_SET,
/// fbw.wasm only; no _RANGE_SET in the FS2020 package). Same published enum on both builds.</item>
/// <item>The FCU SPD / HDG / ALT pushes and AP 1 (<c>A32NX.FCU_{SPD,HDG,ALT,AP_1}_PUSH</c>) exist in
/// both builds' fbw.wasm, so they are not per build.</item>
/// </list>
/// </summary>
public static class A380BuildNames
{
    // ---- FS2020 names (the First Officer's own keys; registered by Fs2020FoRegistrations) ----
    public const string Fs2020FdCaptKey = "FO_A380_FS2020_FD_1";
    public const string Fs2020FdFoKey = "FO_A380_FS2020_FD_2";
    public const string Fs2020FdToggleEvent = "TOGGLE_FLIGHT_DIRECTOR";
    /// <summary>FS2020's ONE baro-unit selector variable (both sides read it); key = its own name.</summary>
    public const string Fs2020BaroUnitKey = "XMLVAR_Baro_Selector_HPA_1";
    public const string Fs2020BaroStdCaptKey = "FO_A380_FS2020_BARO_STD_1";   // KOHLSMAN SETTING STD:1
    public const string Fs2020BaroStdFoKey = "FO_A380_FS2020_BARO_STD_2";     // KOHLSMAN SETTING STD:2
    public const string Fs2020LandingConf3Key = "A32NX_SPEEDS_LANDING_CONF3";

    // ---- FS2024 names (the definition's own keys) ----
    public const string Fs2024FdLightKey = A380FlightDirector.StateKey;
    /// <summary>The definition's key for PRIM FG discrete word 1 (its Name is
    /// <see cref="A380FlightDirector.PrimFgWord1"/>).</summary>
    public const string Fs2024PrimFgWord1Key = "FD_1";
    public const string Fs2024FdPushEvent = A380FlightDirector.PushEvent;
    public const string Fs2024BaroUnitCaptKey = "A32NX_FCU_EFIS_L_BARO_IS_INHG";
    public const string Fs2024BaroUnitFoKey = "A32NX_FCU_EFIS_R_BARO_IS_INHG";
    /// <summary>The definition's STD keys (Name L:A32NX_FCU_EFIS_{L,R}_DISPLAY_BARO_IS_STD); its baro
    /// branch fires the FS2024 PUSH/PULL for them.</summary>
    public const string Fs2024BaroStdCaptKey = "A32NX_FCU_LEFT_EIS_BARO_IS_STD";
    public const string Fs2024BaroStdFoKey = "A32NX_FCU_RIGHT_EIS_BARO_IS_STD";
    public const string Fs2024LandingConf3Key = "A380X_FM_LANDING_CONF3";

    /// <summary>Every variable the per-build resolution READS, both builds together — the First
    /// Officer polls them all, so whichever build is flying has its own values on the cache (the
    /// other build's names just read 0 there, unused).</summary>
    public static readonly string[] AllStateKeys =
    {
        Fs2020FdCaptKey, Fs2020FdFoKey, Fs2024FdLightKey, Fs2024PrimFgWord1Key,
        Fs2020BaroUnitKey, Fs2024BaroUnitCaptKey, Fs2024BaroUnitFoKey,
        Fs2020BaroStdCaptKey, Fs2020BaroStdFoKey, Fs2024BaroStdCaptKey, Fs2024BaroStdFoKey,
        Fs2020LandingConf3Key, Fs2024LandingConf3Key,
    };

    /// <summary>
    /// The FS2020 variables the definition registers for the First Officer: OnRequest (delivered by
    /// the First Officer window's poll of the evaluator's fields), never announced, out of the Ctrl+M
    /// list. Both builds' names are always registered, so nothing depends on when the simulator is
    /// detected; each read and write picks its build's name at the time it runs.
    /// </summary>
    public static IEnumerable<(string Key, SimVarDefinition Definition)> Fs2020FoRegistrations()
    {
        SimVarDefinition Def(string name, string display, SimVarType type, string units) => new()
        {
            Name = name, DisplayName = display, Type = type, Units = units,
            UpdateFrequency = UpdateFrequency.OnRequest, ExcludeFromMonitorManager = true,
        };
        yield return (Fs2020FdCaptKey, Def("AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", "Flight director 1 (MSFS 2020 build)", SimVarType.SimVar, "bool"));
        yield return (Fs2020FdFoKey, Def("AUTOPILOT FLIGHT DIRECTOR ACTIVE:2", "Flight director 2 (MSFS 2020 build)", SimVarType.SimVar, "bool"));
        yield return (Fs2020BaroUnitKey, Def(Fs2020BaroUnitKey, "Baro unit hectopascals (MSFS 2020 build)", SimVarType.LVar, "number"));
        yield return (Fs2020BaroStdCaptKey, Def("KOHLSMAN SETTING STD:1", "Captain altimeter STD (MSFS 2020 build)", SimVarType.SimVar, "bool"));
        yield return (Fs2020BaroStdFoKey, Def("KOHLSMAN SETTING STD:2", "First officer altimeter STD (MSFS 2020 build)", SimVarType.SimVar, "bool"));
        yield return (Fs2020LandingConf3Key, Def(Fs2020LandingConf3Key, "Landing CONF 3 selected (MSFS 2020 build)", SimVarType.LVar, "number"));
    }

    // ===================================================== flight directors

    /// <summary>1 when every flight director is on, 0 when any is definitely off, NaN when unknown
    /// (build unknown, or an unread side with none read off). FS2024: both engaged bits of the PRIM
    /// FG word (13 AND 14); only when that word is unread or carries no data, the FD light.</summary>
    public static double FlightDirectorsOn(FbwSimBuild build, Func<string, double> get)
    {
        switch (build)
        {
            case FbwSimBuild.Fs2024:
            {
                double raw = get(Fs2024PrimFgWord1Key);
                if (!double.IsNaN(raw) && new Arinc429Word(raw).HasData)
                    return A380FlightDirector.DescribeSide(raw, 1) == "on"
                        && A380FlightDirector.DescribeSide(raw, 2) == "on" ? 1.0 : 0.0;
                double light = get(Fs2024FdLightKey);
                return double.IsNaN(light) ? double.NaN : light > 0.5 ? 1.0 : 0.0;
            }
            case FbwSimBuild.Fs2020:
            {
                double c = get(Fs2020FdCaptKey), f = get(Fs2020FdFoKey);
                if ((!double.IsNaN(c) && c < 0.5) || (!double.IsNaN(f) && f < 0.5)) return 0.0;
                return double.IsNaN(c) || double.IsNaN(f) ? double.NaN : 1.0;
            }
            default:
                return double.NaN;
        }
    }

    /// <summary>One press that moves a flight-director control: a stock event with a parameter
    /// (FS2020 TOGGLE_FLIGHT_DIRECTOR, side 1/2) or the dotted FCU event (FS2024 FD push).</summary>
    public readonly record struct FdPress(string EventName, uint Parameter);

    /// <summary>
    /// The presses that bring the flight directors to <paramref name="on"/>, or NULL when ANY state
    /// they depend on is unknown (or the build is) — nothing may be pressed blind: both are toggles,
    /// and a press on a wrong guess turns a flight director OFF. Empty when already there.
    /// </summary>
    public static IReadOnlyList<FdPress>? FlightDirectorPresses(FbwSimBuild build, bool on, Func<string, double> get)
    {
        switch (build)
        {
            case FbwSimBuild.Fs2020:
            {
                double c = get(Fs2020FdCaptKey), f = get(Fs2020FdFoKey);
                if (double.IsNaN(c) || double.IsNaN(f)) return null;
                var presses = new List<FdPress>();
                if ((c > 0.5) != on) presses.Add(new FdPress(Fs2020FdToggleEvent, 1));
                if ((f > 0.5) != on) presses.Add(new FdPress(Fs2020FdToggleEvent, 2));
                return presses;
            }
            case FbwSimBuild.Fs2024:
            {
                double light = get(Fs2024FdLightKey);
                if (double.IsNaN(light)) return null;
                // The light is FD 1 OR FD 2. Where the PRIM word says the two sides disagree, one
                // push of the single button cannot bring both to the target: refuse and leave it to
                // the Captain, never report "already set" over one FD off.
                double raw = get(Fs2024PrimFgWord1Key);
                if (!double.IsNaN(raw) && new Arinc429Word(raw).HasData)
                {
                    bool fd1 = A380FlightDirector.DescribeSide(raw, 1) == "on";
                    bool fd2 = A380FlightDirector.DescribeSide(raw, 2) == "on";
                    if (fd1 != fd2) return null;
                }
                return (light > 0.5) == on ? Array.Empty<FdPress>() : new[] { new FdPress(Fs2024FdPushEvent, 0) };
            }
            default:
                return null;
        }
    }

    /// <summary>The calculator code for one press: FS2020 the stock K-event with the side as its
    /// parameter, FS2024 the FCU event as the definition's FCU-button transport sends it.</summary>
    public static string PressCalculatorCode(FdPress press) => $"{press.Parameter} (>K:{press.EventName})";

    // ===================================================== baro unit

    /// <summary>The baro-unit selector variables for this build, captain's first: FS2020 has ONE
    /// (HPA_1, read by both sides), FS2024 one per side. Empty when the build is unknown.</summary>
    public static string[] BaroUnitKeys(FbwSimBuild build) => build switch
    {
        FbwSimBuild.Fs2020 => new[] { Fs2020BaroUnitKey },
        FbwSimBuild.Fs2024 => new[] { Fs2024BaroUnitCaptKey, Fs2024BaroUnitFoKey },
        _ => Array.Empty<string>(),
    };

    /// <summary>The value a baro-unit selector holds for hectopascals: FS2020's XMLVAR is "IS_HPA"
    /// (1), FS2024's L:var is "IS_INHG" (0) — opposite polarity.</summary>
    public static int HectopascalsValue(FbwSimBuild build) => build == FbwSimBuild.Fs2024 ? 0 : 1;

    /// <summary>1 when the selector value means hPa, 0 inHg, NaN unknown.</summary>
    public static double IsHectopascals(FbwSimBuild build, double raw)
    {
        if (build == FbwSimBuild.Unknown || double.IsNaN(raw)) return double.NaN;
        bool high = raw > 0.5;
        return build == FbwSimBuild.Fs2024 ? (high ? 0.0 : 1.0) : (high ? 1.0 : 0.0);
    }

    /// <summary>1 when every selector reads hPa, 0 when any reads inHg, NaN when unknown.</summary>
    public static double BaroUnitsHectopascals(FbwSimBuild build, Func<string, double> get)
    {
        var keys = BaroUnitKeys(build);
        if (keys.Length == 0) return double.NaN;
        var v = keys.Select(k => IsHectopascals(build, get(k))).ToArray();
        if (v.Any(x => x == 0.0)) return 0.0;
        return v.Any(double.IsNaN) ? double.NaN : 1.0;
    }

    /// <summary>The captain side's unit as an "is inches" flag (1 inHg, 0 hPa, NaN unknown) — what the
    /// read-back formatter takes.</summary>
    public static double CaptainBaroIsInHg(FbwSimBuild build, Func<string, double> get)
    {
        var keys = BaroUnitKeys(build);
        if (keys.Length == 0) return double.NaN;
        double hpa = IsHectopascals(build, get(keys[0]));
        return double.IsNaN(hpa) ? double.NaN : 1.0 - hpa;
    }

    // ===================================================== baro STD

    /// <summary>The variable that reads whether one side's baro is in STD (side 1 = captain), or
    /// null when the build is unknown.</summary>
    public static string? BaroStdKey(FbwSimBuild build, int side) => build switch
    {
        FbwSimBuild.Fs2020 => side == 1 ? Fs2020BaroStdCaptKey : Fs2020BaroStdFoKey,
        FbwSimBuild.Fs2024 => side == 1 ? Fs2024BaroStdCaptKey : Fs2024BaroStdFoKey,
        _ => null,
    };

    /// <summary>FS2020 only: the H-event that sets one side to STD (push) or QNH (pull). Both are
    /// directional sets, never toggles (fcu.js MsfsBaroManager).</summary>
    public static string Fs2020BaroModeEvent(int side, bool standard) =>
        $"H:A380X_EFIS_CP_BARO_{(standard ? "PUSH" : "PULL")}_{side}";

    // ===================================================== CONF 3

    /// <summary>The MFD PERF APPR "CONF 3" landing selection for this build, or null when unknown.</summary>
    public static string? LandingConf3Key(FbwSimBuild build) => build switch
    {
        FbwSimBuild.Fs2020 => Fs2020LandingConf3Key,
        FbwSimBuild.Fs2024 => Fs2024LandingConf3Key,
        _ => null,
    };

    // ===================================================== ND knobs

    /// <summary>True for the four ND MODE / RANGE knob keys.</summary>
    public static bool IsNdKnobKey(string varKey) =>
        varKey is "A32NX_EFIS_L_ND_MODE" or "A32NX_EFIS_L_ND_RANGE" or "A32NX_EFIS_R_ND_MODE" or "A32NX_EFIS_R_ND_RANGE";

    /// <summary>FS2020: the calculator code that sets an ND knob, as the cockpit knob does (it
    /// writes the L:var itself; the published enum is the knob's).</summary>
    public static string Fs2020NdKnobCalculatorCode(string varKey, int value) => $"{value} (>L:{varKey})";
}

namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>How the First Officer actuates and reads back one MD-11 control (TFDi's handler semantics).</summary>
public enum Md11FoKind
{
    /// <summary>DOWN toggles the button's own var (the var IS the state). Press only when it differs.</summary>
    Latch,
    /// <summary>A single-event switch: each click TOGGLES the var. Click once only when it differs.</summary>
    Toggle,
    /// <summary>A multi-position switch/knob stepped one position per event, clamped.</summary>
    Stepped,
    /// <summary>A momentary button whose system handler toggles a hidden state; a lamp is the read-back.</summary>
    LampToggle,
    /// <summary>A test that runs only while held: DOWN, hold, UP.</summary>
    HoldTest,
    /// <summary>A one-shot press (a timed test, a reset, a page select).</summary>
    PressOnce,
}

/// <param name="Key">The control's node id (its app variable key).</param>
/// <param name="Down">LEFT_BUTTON_DOWN id (Latch/Toggle/LampToggle/HoldTest/PressOnce).</param>
/// <param name="Up">LEFT_BUTTON_UP id, or 0 where the map has none.</param>
/// <param name="Raise">Stepped: the event TFDi's handler INCREASES the value on.</param>
/// <param name="Lower">Stepped: the event TFDi's handler DECREASES the value on.</param>
/// <param name="Min">Stepped: lowest position.</param>
/// <param name="Max">Stepped: highest position.</param>
/// <param name="ReadKey">The cache key read back, or null for tests and one-shot presses.</param>
/// <param name="LitMeans">LampToggle only: the logical state a LIT read-back lamp means (1 on, 0 off).</param>
public sealed record Md11FoControl(
    string Key, Md11FoKind Kind, int Down, int Up, int Raise, int Lower,
    int Min, int Max, string? ReadKey, double LitMeans);

/// <summary>
/// The First Officer's own verified MD-11 control table. Every id is pinned to the generated
/// control map by Md11FoControlsTests, and every step direction to TFDi's own event handlers
/// (decoded from md11host.wasm: RIGHT_BUTTON_DOWN raises on every overhead stepped control).
/// The FO does NOT use the panel walker's learned polarity, which guesses the opposite and would
/// take a wrong first step (EVAC Armed→Off through ON) on a fresh install.
///
/// The flap handle, Dial-A-Flap wheel, speedbrake and gear lever are deliberately absent: they
/// have dedicated pseudo-key handlers in the executor with their own safety rules.
/// </summary>
public static class Md11FoControls
{
    private static Md11FoControl Latch(string key, int down, int up)
        => new(key, Md11FoKind.Latch, down, up, 0, 0, 0, 1, key, 1);

    private static Md11FoControl Toggle(string key, int click)
        => new(key, Md11FoKind.Toggle, click, 0, 0, 0, 0, 1, key, 1);

    private static Md11FoControl Stepped(string key, int raise, int lower, int min, int max)
        => new(key, Md11FoKind.Stepped, 0, 0, raise, lower, min, max, key, 1);

    private static Md11FoControl LampToggle(string key, int down, int up, string lamp, double litMeans)
        => new(key, Md11FoKind.LampToggle, down, up, 0, 0, 0, 1, lamp, litMeans);

    private static Md11FoControl Hold(string key, int down, int up)
        => new(key, Md11FoKind.HoldTest, down, up, 0, 0, 0, 1, null, 1);

    private static Md11FoControl Once(string key, int down, int up)
        => new(key, Md11FoKind.PressOnce, down, up, 0, 0, 0, 1, null, 1);

    private static readonly Md11FoControl[] Table =
    {
        // ---- Latching push-buttons (the button's own var is the state; OnRequest reads) ----
        Latch("MD11_OVHD_ELEC_BATT_BT", 90150, 90151),          // guarded: CEVENT works cover-closed; TFDi recloses it
        Latch("MD11_OVHD_ELEC_SYSTEM_SEL_BT", 90157, 90158),    // 0 Auto / 1 Manual
        Latch("MD11_OVHD_FUEL_SYSTEM_SEL_BT", 90212, 90213),
        Latch("MD11_OVHD_HYD_SYSTEM_SEL_BT", 90177, 90178),
        Latch("MD11_OVHD_PNEU_SYSTEM_SEL_BT", 90295, 90296),    // 2nd press = the OTHER auto channel
        Latch("MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_BT", 90328, 90329),
        Latch("MD11_OVHD_PNEU_APU_BLEED_BT", 90313, 90314),     // 1 On
        Latch("MD11_OVHD_WNDSHLD_AICE_L_BT", 90424, 90425),     // 1 On
        Latch("MD11_OVHD_WNDSHLD_AICE_R_BT", 90428, 90429),
        Latch("MD11_OVHD_WNDSHLD_AICE_BT", 90426, 90427),       // 1 High / 0 Norm
        Latch("MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", 90430, 90431), // INVERTED: 1 = defog OFF
        Latch("MD11_OVHD_ANNUNLT_BRTDIM_BT", 90406, 0),         // 1 Dim / 0 Bright (no UP id)

        // ---- Single-event toggle switches (each click toggles; OnRequest reads) ----
        Toggle("MD11_OVHD_IRS_1_KB", 90112),                    // 1 Nav
        Toggle("MD11_OVHD_IRS_2_KB", 90114),
        Toggle("MD11_OVHD_IRS_3_KB", 90116),                    // auxiliary IRS
        Toggle("MD11_THR_L_FUEL_SW", 77834),                    // engine 1, 1 On
        Toggle("MD11_THR_C_FUEL_SW", 77835),                    // engine 2 (tail)
        Toggle("MD11_THR_R_FUEL_SW", 77836),                    // engine 3
        Toggle("MD11_THR_PARK_LVR", 77848),                     // 1 Engaged
        Toggle("MD11_PED_XPNDR_ALT_RPTG_KB", 69854),            // 1 Enabled

        // ---- Stepped switches/knobs (RIGHT raises, per TFDi's handlers; OnRequest reads) ----
        Stepped("MD11_OVHD_ELEC_EMER_PWR_KB", 90160, 90159, 0, 2),   // 0 Off 1 Armed 2 On
        Stepped("MD11_OVHD_LTS_EMER_SW", 90243, 90242, 0, 2),        // 0 Off 1 Armed 2 On
        Stepped("MD11_OVHD_LTS_NO_SMOKE_SW", 90247, 90246, 0, 2),    // 0 Off 1 Auto 2 On
        Stepped("MD11_OVHD_LTS_SEAT_BELTS_SW", 90249, 90248, 0, 2),  // 0 Off 1 Auto 2 On
        Stepped("MD11_OVHD_LTS_LDG_L_SW", 90258, 90257, 0, 2),       // 0 Retracted 1 Extended 2 On
        Stepped("MD11_OVHD_LTS_LDG_R_SW", 90260, 90259, 0, 2),
        Stepped("MD11_OVHD_LTS_NOSE_SW", 90262, 90261, 0, 2),        // 0 Off 1 Taxi 2 Landing
        Stepped("MD11_OVHD_PNEU_FWD_CARGO_TEMP", 90276, 90275, 0, 2),// 0 OFF
        Stepped("MD11_OVHD_PNEU_AFT_CARGO_TEMP", 90278, 90277, 0, 6),// 0 OFF
        Stepped("MD11_AOVHD_EVAC_SW", 73774, 73773, 0, 2),           // 0 Off 1 Armed 2 On; INC gated by the CLOSED cover above 1
        Stepped("MD11_AOVHD_GPWS_SW", 73770, 73769, 0, 2),           // 0 Test 1 Normal 2 Flap Override; INC gated above 1
        Stepped("MD11_CTR_AUTOBRAKE_SW", 82212, 82211, 0, 4),        // 0 T.O. 1 Off 2 Min 3 Med 4 Max
        Stepped("MD11_PED_XPNDR_MODE_KB", 69877, 69876, 0, 3),       // 0 Stby 1 XPDR 2 TA 3 TA/RA

        // ---- Momentary buttons whose system toggles a hidden state; a LAMP is the read-back ----
        LampToggle("MD11_OVHD_ELEC_EXT_PWR_BT", 90142, 90143, "MD11_OVHD_ELEC_EXT_PWR_ON_LT", 1),
        LampToggle("MD11_OVHD_ELEC_APU_PWR_BT", 90144, 90145, "MD11_OVHD_ELEC_APU_PWR_ON_LT", 1),
        LampToggle("MD11_OVHD_HYD_AUX_PUMP_1_BT", 90173, 90174, "MD11_OVHD_HYD_AUX_PUMP_1_ON_LT", 1),
        LampToggle("MD11_OVHD_ENG_A_BT", 90350, 90351, "MD11_OVHD_ENG_A_LT", 1),
        LampToggle("MD11_OVHD_ENG_B_BT", 90352, 90353, "MD11_OVHD_ENG_B_LT", 1),
        LampToggle("MD11_OVHD_ENG_IGN_OVRD_BT", 90354, 90355, "MD11_OVHD_ENG_IGN_OVRD_LT", 1),
        LampToggle("MD11_OVHD_PNEU_PACK_1_BT", 90289, 90290, "MD11_OVHD_PNEU_PACK_1_OFF_LT", 0),
        LampToggle("MD11_OVHD_PNEU_PACK_2_BT", 90291, 90292, "MD11_OVHD_PNEU_PACK_2_OFF_LT", 0),
        LampToggle("MD11_OVHD_PNEU_PACK_3_BT", 90293, 90294, "MD11_OVHD_PNEU_PACK_3_OFF_LT", 0),
        LampToggle("MD11_OVHD_PNEU_ECON_BT", 90297, 90298, "MD11_OVHD_PNEU_ECON_OFF_LT", 0),
        LampToggle("MD11_OVHD_LTS_NAV_BT", 90267, 90268, "MD11_OVHD_LTS_NAV_LT", 0),       // OFF legend
        LampToggle("MD11_OVHD_LTS_BCN_BT", 90271, 90272, "MD11_OVHD_LTS_BCN_LT", 0),       // OFF legend
        LampToggle("MD11_OVHD_LTS_HI_INT_BT", 90273, 90274, "MD11_OVHD_LTS_HI_INT_LT", 0), // OFF legend (strobes)
        LampToggle("MD11_OVHD_LTS_LOGO_BT", 90269, 90270, "MD11_OVHD_LTS_LOGO_ON_LT", 1),
        LampToggle("MD11_OVHD_LTS_RWY_TURNOFF_L_BT", 90263, 90264, "MD11_OVHD_LTS_RWY_TURNOFF_L_LT", 1),
        LampToggle("MD11_OVHD_LTS_RWY_TURNOFF_R_BT", 90265, 90266, "MD11_OVHD_LTS_RWY_TURNOFF_R_LT", 1),
        LampToggle("MD11_OVHD_LTS_DOME_BT", 90236, 90237, "MD11_LTS_DOME", 1),
        LampToggle("MD11_OVHD_AICE_ENG1_BT", 90414, 90415, "MD11_OVHD_AICE_ENG1_ON_LT", 1),
        LampToggle("MD11_OVHD_AICE_ENG2_BT", 90416, 90417, "MD11_OVHD_AICE_ENG2_ON_LT", 1),
        LampToggle("MD11_OVHD_AICE_ENG3_BT", 90418, 90419, "MD11_OVHD_AICE_ENG3_ON_LT", 1),
        LampToggle("MD11_OVHD_AICE_WING_BT", 90420, 90421, "MD11_OVHD_AICE_WING_ON_LT", 1),
        LampToggle("MD11_OVHD_AICE_TAIL_BT", 90422, 90423, "MD11_OVHD_AICE_TAIL_ON_LT", 1),

        // ---- Hold-to-test (the test runs only while held) ----
        Hold("MD11_AOVHD_FIRETEST_BT", 73748, 73749),           // engine + APU fire loops
        Hold("MD11_AOVHD_CRGSMK_TEST_BT", 73757, 73758),        // cargo fire/smoke manual test
        Hold("MD11_OVHD_CVR_TEST_BT", 90358, 90359),
        Hold("MD11_OVHD_FUEL_QTY_TEST_BT", 90234, 90235),
        Hold("MD11_OVHD_LTS_EMER_TEST_BT", 90244, 90245),
        Hold("MD11_LSIDE_OXY_TEST_BT", 94235, 94236),
        Hold("MD11_RSIDE_OXY_TEST_BT", 95515, 95516),
        Hold("MD11_PED_XPNDR_TEST_BT", 69878, 69879),           // TCAS/transponder test
        Hold("MD11_MIP_ISFD_TEST_BT", 94987, 94988),            // standby display (when fitted)
        Hold("MD11_OVHD_ANNUNLT_TEST_BT", 90408, 90409),        // only via FO_ANNUN_TEST (lamp mute)

        // ---- One-shot presses ----
        Once("MD11_OVHD_HYD_HYD_TEST_BT", 90191, 90192),        // ~100 s timed test; guarded, cover not needed
        Once("MD11_OVHD_CRG_DOOR_TEST_BT", 90356, 90357),       // ~5 s
        Once("MD11_OVHD_FUELUSEDRESET_BT", 90410, 90411),
        Once("MD11_GSL_MST_WRN_BT", 86108, 86109),              // clears both sides
        Once("MD11_PED_SD_CONFIG_BT", 69844, 69845),            // NOT idempotent: press once only
        Once("MD11_PED_WXR_TEST_BT", 69885, 69886),
        Once("MD11_PED_WXR_OFF_BT", 69883, 69884),
        Once("MD11_CGS_NAV_BT", 86090, 86091),                  // arms NAV; no armed state is exported; a 2nd press leaves it armed
        Once("MD11_CGS_PROF_BT", 86096, 86097),                 // arms PROF; same
        Once("MD11_CGS_AUTOFLIGHT_BT", 86094, 86095),           // only via FO_AUTO_FLIGHT (read first: a press with an AP on swaps AP 1/2)
    };

    public static IReadOnlyDictionary<string, Md11FoControl> All { get; } =
        Table.ToDictionary(c => c.Key, StringComparer.Ordinal);

    public static bool TryGet(string key, out Md11FoControl control)
        => ((Dictionary<string, Md11FoControl>)All).TryGetValue(key, out control!);
}

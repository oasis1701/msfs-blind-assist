namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>How a preflight test is pressed.</summary>
public enum Bbs757FoTestKind
{
    /// <summary>A held button of the control map, pressed and let go (<c>BBS757Definition.FoPressAsync</c>).</summary>
    HeldButton,

    /// <summary>A P61 test button, its spring switch held 3 s and let go (<c>BBS757Definition.FoSpringTestAsync</c>).</summary>
    P61Switch,
}

/// <summary>One of BlueBird's preflight tests, run by the First Officer as a step of the Preflight flow and a line of its checklist.</summary>
/// <param name="Key">The First Officer's own key for the test (<c>FO_TEST_*</c>), the step's and the line's action.</param>
/// <param name="Label">The step's and the line's label, spoken as the step's progress (FO-26): the test, and what to listen for.</param>
/// <param name="Target">A map node (<see cref="Bbs757FoTestKind.HeldButton"/>) or a P61 test button key (<see cref="Bbs757FoTestKind.P61Switch"/>).</param>
/// <param name="Kind">How it is pressed.</param>
/// <param name="HoldMs">
/// How long a held button is held, replacing the map's; null for the control's own hold (the map's for a held button,
/// 3 s; a P61 test switch's own <c>Bbs757SpringControls.TestHoldMs</c>, 3 s).
/// </param>
/// <param name="AfterMs">
/// How long the First Officer waits after the release before the next step: the aircraft's own sound and the cockpit
/// watcher's announcements of the lights the test raised (they keep speaking during the tests; owner, 2026-11-05).
/// First values, to be measured in the sim.
/// </param>
public sealed record Bbs757FoTest(string Key, string Label, string Target, Bbs757FoTestKind Kind, int? HoldMs, int AfterMs);

/// <summary>
/// BlueBird's preflight tests (User Procedures v1.01, simplified procedures pp. 36-42), shortest first, so the long ones
/// come last (owner, 2026-11-08; each one's time is its hold plus its After wait). Left out: the battery test (owner,
/// plan 19), the squib test, heard doing nothing, and the landing config test, not needed (owner, plan 29); the pilot can
/// still press both on their panels. The Preflight flow runs them where the Captain's "System tests" reminder stood, and
/// each has its own line in the PREFLIGHT checklist group. Pure.
/// </summary>
public static class Bbs757FoTests
{
    private const Bbs757FoTestKind Held = Bbs757FoTestKind.HeldButton;
    private const Bbs757FoTestKind P61 = Bbs757FoTestKind.P61Switch;

    public static IReadOnlyList<Bbs757FoTest> Preflight { get; } = new Bbs757FoTest[]
    {
        new("FO_TEST_STALL_L", "Left stall warning test — listen for the stick shaker", "BBS757_P61_LEFT_STALL_TEST", P61, null, 1000),
        new("FO_TEST_STALL_R", "Right stall warning test — listen for the stick shaker", "BBS757_P61_RIGHT_STALL_TEST", P61, null, 1000),
        new("FO_TEST_CVR", "CVR test", "B757_OVHD_SW_CVR_TEST", Held, null, 1000),
        new("FO_TEST_CONFIG_TO", "Takeoff config test — listen for the configuration warning", "BBS757_P61_TAKEOFF_CONFIG_TEST", P61, null, 2000),
        new("FO_TEST_FIRE_WHLWELL", "Wheel well fire test — listen for the fire bell", "B757_PED_FIRE_WHLWELL_BUTTON", Held, null, 3000),
        new("FO_TEST_FIRE_ENG", "Engine fire test — listen for the fire bell", "B757_PED_FIRE_ENG_BUTTON", Held, null, 3000),
        new("FO_TEST_FIRE_CARGO", "Engine, APU and cargo fire test — listen for the fire bell", "B757_PED_FIRE_CARGO_BUTTON", Held, null, 3000),
        new("FO_TEST_OXY_CAPT", "Captain oxygen test — listen for the oxygen flow", "OXY_CAPT_TEST_SWITCH001", Held, 10000, 1000),
        new("FO_TEST_OXY_FO", "First officer oxygen test — listen for the oxygen flow", "OXY_FO_TEST_SWITCH1", Held, 10000, 1000),
        new("FO_TEST_TCAS", "TCAS test — listen for TCAS test pass", "B757_PED_TCAS_MODE_KNOB_TEST_CLICK", Held, null, 8000),
        new("FO_TEST_GND_PROX", "Ground proximity test — listen for the callouts", "BBS757_P61_GROUND_PROX_TEST", P61, null, 15000),
    };

    // Declared after Preflight: static fields are initialized in the order they are written.
    private static readonly Dictionary<string, Bbs757FoTest> ByKey = Preflight.ToDictionary(t => t.Key, StringComparer.Ordinal);

    /// <summary>The test whose key is <paramref name="key"/>, or null for any other key.</summary>
    public static Bbs757FoTest? For(string key) => ByKey.TryGetValue(key, out var t) ? t : null;

    /// <summary>The test's Preflight step and line id: <c>PF_</c> and the key's tail (<c>FO_TEST_SQUIB</c> is <c>PF_TEST_SQUIB</c>).</summary>
    public static string PreflightId(Bbs757FoTest test) => "PF_" + test.Key["FO_".Length..];
}

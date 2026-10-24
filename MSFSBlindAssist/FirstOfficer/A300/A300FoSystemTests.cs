using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>One press of a test row and the lights that prove it. <paramref name="AnyLight"/>: any one of them is
/// enough (the cargo smoke lights differ between the freighter and the passenger variant). <paramref name="WatchMs"/>:
/// how long its lights are watched from the press, for a test whose first light comes later than the default
/// watch (the hold plus a second, at least four seconds).</summary>
public sealed record A300FoTestPress(string Row, IReadOnlyList<string> Lights, bool AnyLight = false, int? WatchMs = null);

/// <summary>A system test: its presses in order. <paramref name="ExpectDark"/>: passing means the lights stay dark
/// (the takeoff config test). <paramref name="ResetsMasterWarning"/>: a master warning left lit afterwards is reset.</summary>
public sealed record A300FoSystemTest(string Key, string Name, IReadOnlyList<A300FoTestPress> Presses,
    bool ExpectDark = false, bool ResetsMasterWarning = false);

/// <summary>
/// The A300 system tests and how each proves itself (design decision: every test reads back pass or fail).
/// The rows are held as long as the aircraft's own checklist holds them (A300WritePlan.TestHoldMs). A light counts
/// only if it was dark before its press: a light already lit proves nothing.
/// </summary>
public static class A300FoSystemTests
{
    public static readonly A300FoSystemTest ApuFire = new("FO_TEST_APU_FIRE", "APU fire test", new[]
    {
        new A300FoTestPress(A300FoKeys.ApuLoopTest, new[] { A300FoKeys.FireHandleApuLight }),
        new A300FoTestPress(A300FoKeys.ApuSquibTest, new[] { A300FoKeys.ApuSquibLight }),
    });

    public static readonly A300FoSystemTest Engine1Fire = new("FO_TEST_ENG1_FIRE", "Engine 1 fire test", new[]
    {
        new A300FoTestPress(A300FoKeys.Eng1LoopTest, new[] { A300FoKeys.FireHandleEng1Light }),
        new A300FoTestPress(A300FoKeys.Eng1SquibTest, new[] { A300FoKeys.Eng1Squib1Light, A300FoKeys.Eng1Squib2Light }),
    });

    public static readonly A300FoSystemTest Engine2Fire = new("FO_TEST_ENG2_FIRE", "Engine 2 fire test", new[]
    {
        new A300FoTestPress(A300FoKeys.Eng2LoopTest, new[] { A300FoKeys.FireHandleEng2Light }),
        new A300FoTestPress(A300FoKeys.Eng2SquibTest, new[] { A300FoKeys.Eng2Squib1Light, A300FoKeys.Eng2Squib2Light }),
    });

    public static readonly A300FoSystemTest CargoSmoke = new("FO_TEST_CARGO_SMOKE", "Cargo smoke test", new[]
    {
        new A300FoTestPress(A300FoKeys.SmokeTest, A300FoKeys.CargoSmokeLights.Append(A300FoKeys.MasterWarning).ToArray(), AnyLight: true),
    }, ResetsMasterWarning: true);

    /// <summary>The GPWS self-test lights the below glide slope light about 7 s after the press and the GPWS warning
    /// light about 25 s after (measured 2026-10-21, twice), so either proves it, watched 12 s.</summary>
    public static readonly A300FoSystemTest Gpws = new("FO_TEST_GPWS", "GPWS test", new[]
    {
        new A300FoTestPress(A300FoKeys.GpwsTest, new[] { A300FoKeys.GlideslopeLight, A300FoKeys.GpwsLight },
            AnyLight: true, WatchMs: 12_000),
    });

    public static readonly A300FoSystemTest GearWarning = new("FO_TEST_GEAR_WARNING", "Landing gear warning test", new[]
    {
        new A300FoTestPress(A300FoKeys.GearWarningTest, new[] { A300FoKeys.MasterWarning }),
    }, ResetsMasterWarning: true);

    public static readonly A300FoSystemTest TakeoffWarning = new("FO_TEST_TAKEOFF_WARNING", "Takeoff warning test", new[]
    {
        new A300FoTestPress(A300FoKeys.TakeoffConfigTest, new[] { A300FoKeys.MasterWarning }),
    }, ResetsMasterWarning: true);

    public static readonly A300FoSystemTest TakeoffConfig = new("FO_TEST_TAKEOFF_CONFIG", "Takeoff config test", new[]
    {
        new A300FoTestPress(A300FoKeys.TakeoffConfigTest, new[] { A300FoKeys.MasterWarning }),
    }, ExpectDark: true, ResetsMasterWarning: true);

    public static readonly IReadOnlyDictionary<string, A300FoSystemTest> ByKey = new[]
    {
        ApuFire, Engine1Fire, Engine2Fire, CargoSmoke, Gpws, GearWarning, TakeoffWarning, TakeoffConfig,
    }.ToDictionary(t => t.Key, StringComparer.Ordinal);

    /// <summary>A light's spoken name: the board light's own name, or "Master warning".</summary>
    public static string LightName(string id) =>
        id == A300FoKeys.MasterWarning ? "Master warning"
        : A300LampBoard.ById.TryGetValue(id, out var lamp) ? lamp.Name : id;

    /// <summary>The one sentence a test ends with. <paramref name="failed"/> names the lights that did not light
    /// (or, for a dark test, that came on).</summary>
    public static string Sentence(A300FoSystemTest test, IReadOnlyList<string> failed)
    {
        if (failed.Count == 0)
            return $"{test.Name}: pass.";
        string names = string.Join(" and ", failed.Select(LightName));
        return test.ExpectDark ? $"{test.Name}: fail. {names} came on." : $"{test.Name}: fail. {names} did not light.";
    }
}

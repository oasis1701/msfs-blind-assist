using System.Linq;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// "Passengers on Board" (Status panel) sums the per-station A32NX_PAX_{st}_DESIRED
/// seat bitmasks. The A339X has TEN passenger stations (A..J); the inherited A320
/// definition registered only FOUR (A..D), so six stations were never registered,
/// never delivered, and never counted — a figure spoken to a blind pilot that
/// substantially undercounts.
///
/// Measured live on the A339X in cruise, 2026-08-31:
///     A32NX_PAX_A_DESIRED = 135291469824       -> popcount  6
///     A32NX_PAX_E_DESIRED = 70366596694016     -> popcount 15   (was ignored)
///     A32NX_PAX_J_DESIRED = 9007197107257344   -> popcount 22   (was ignored)
/// Stations F..I were likewise ignored — 37 passengers missing from the two sampled
/// stations alone.
/// </summary>
public class HwA330PaxStationTests
{
    private static string[] Stations(params char[] letters) =>
        letters.Select(c => $"A32NX_PAX_{c}_DESIRED").ToArray();

    private static readonly string[] A320Stations = Stations('A', 'B', 'C', 'D');
    private static readonly string[] A330Stations =
        Stations('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J');

    // Live A339X capture (see class doc) — real masks, so this test carries the evidence.
    private const double PaxA = 135291469824d;      // popcount 6
    private const double PaxE = 70366596694016d;    // popcount 15
    private const double PaxJ = 9007197107257344d;  // popcount 22

    [Fact]
    public void A330_registers_all_ten_passenger_stations()
    {
        var vars = new HeadwindA330Definition().GetVariables();
        foreach (var key in A330Stations)
            Assert.True(vars.ContainsKey(key),
                $"{key} is unregistered, so its seats never reach Passengers on Board.");
    }

    [Fact]
    public void A320_still_registers_exactly_its_own_four_stations()
    {
        var vars = new FlyByWireA320Definition().GetVariables();
        var registered = vars.Keys
            .Where(k => k.StartsWith("A32NX_PAX_") && k.EndsWith("_DESIRED"))
            .OrderBy(k => k)
            .ToArray();
        // The A320 has four zones — over-applying the A330's ten would register six
        // L:vars the A32NX never publishes.
        Assert.Equal(A320Stations, registered);
    }

    [Fact]
    public void A330_station_A_remains_the_passengers_on_board_display_row()
    {
        var def = new HeadwindA330Definition();
        Assert.Equal("Passengers on Board", def.GetVariables()["A32NX_PAX_A_DESIRED"].DisplayName);
        Assert.Contains("A32NX_PAX_A_DESIRED", def.GetPanelDisplayVariables()["Status"]);
    }

    /// <summary>
    /// The summing consumer in ProcessSimVarUpdate is station-agnostic (it matches any
    /// A32NX_PAX_*_DESIRED), so a newly REGISTERED station must contribute to the total
    /// with no consumer change. Drive it with the live station-E and station-J masks and
    /// read the total back off the station-A display row.
    /// </summary>
    [Fact]
    public void A330_total_counts_a_station_beyond_D()
    {
        var def = new HeadwindA330Definition();
        // The pax branch never touches the announcer (it caches, sums and returns true
        // without speaking), so a null keeps this headless — no Tolk/SAPI/WinForms timer.
        MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer announcer = null!;

        def.ProcessSimVarUpdate("A32NX_PAX_A_DESIRED", PaxA, announcer);
        Assert.True(def.TryGetDisplayOverride("A32NX_PAX_A_DESIRED", PaxA, out var total));
        Assert.Equal("6", total);

        def.ProcessSimVarUpdate("A32NX_PAX_E_DESIRED", PaxE, announcer);
        Assert.True(def.TryGetDisplayOverride("A32NX_PAX_A_DESIRED", PaxA, out total));
        Assert.Equal("21", total);   // 6 + 15

        def.ProcessSimVarUpdate("A32NX_PAX_J_DESIRED", PaxJ, announcer);
        Assert.True(def.TryGetDisplayOverride("A32NX_PAX_A_DESIRED", PaxA, out total));
        Assert.Equal("43", total);   // 6 + 15 + 22
    }

    /// <summary>
    /// End to end: only a REGISTERED station is ever delivered, so feed the live masks
    /// through the registered set exactly as SimConnect would. Before the fix stations E
    /// and J are unregistered, never delivered, and the total reads 6 instead of 43 — the
    /// undercount as the pilot hears it.
    /// </summary>
    [Fact]
    public void A330_total_includes_stations_only_the_A330_registers()
    {
        var def = new HeadwindA330Definition();
        var registered = def.GetVariables().Keys
            .Where(k => k.StartsWith("A32NX_PAX_") && k.EndsWith("_DESIRED"))
            .ToHashSet();
        MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer announcer = null!;

        foreach (var (key, mask) in new[]
                 {
                     ("A32NX_PAX_A_DESIRED", PaxA),
                     ("A32NX_PAX_E_DESIRED", PaxE),
                     ("A32NX_PAX_J_DESIRED", PaxJ)
                 })
        {
            if (registered.Contains(key))
                def.ProcessSimVarUpdate(key, mask, announcer);
        }

        Assert.True(def.TryGetDisplayOverride("A32NX_PAX_A_DESIRED", PaxA, out var total));
        Assert.Equal("43", total);   // 6 (A) + 15 (E) + 22 (J)
    }

    /// <summary>
    /// Every station mask must stay inside float64's exact-integer range or the popcount
    /// silently reads a rounded value. The A330's station J is the tight case — a full
    /// 53-bit mask, 2^31 below the limit — which is what the header comment on
    /// FlyByWireA320Definition.PaxStationVars now records.
    /// </summary>
    [Fact]
    public void The_live_masks_are_exact_as_float64()
    {
        const double ExactIntegerLimit = 9007199254740992d;   // 2^53
        foreach (var mask in new[] { PaxA, PaxE, PaxJ })
            Assert.True(mask < ExactIntegerLimit, $"{mask} is not exact as a double.");

        // The A330's station J leaves only 2^31 of headroom — name it so a future station
        // mask cannot quietly cross the limit.
        Assert.Equal(2147483648d, ExactIntegerLimit - PaxJ);

        // And the masks really do popcount to the seat counts this file claims.
        Assert.Equal(6, System.Numerics.BitOperations.PopCount((ulong)PaxA));
        Assert.Equal(15, System.Numerics.BitOperations.PopCount((ulong)PaxE));
        Assert.Equal(22, System.Numerics.BitOperations.PopCount((ulong)PaxJ));
    }
}

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The ground service tiles (plan 11 Tasks 2 and 5): their variables and words, which tile a read names, the announced
/// rows and the phrases. A press on a tile is the tablet press pipeline's (plan 18): nothing here refuses it.</summary>
public class Bbs757GroundServicesTests
{
    private static Bbs757GroundService Svc(string key) => Bbs757GroundServices.All.Single(s => s.Key == key);

    [Fact]
    public void Every_tile_names_a_state_variable_and_two_words()
    {
        Assert.All(Bbs757GroundServices.All, s =>
        {
            Assert.Matches("^[A-Za-z0-9_]+$", s.StateVar);
            Assert.NotEmpty(s.OnWord);
            Assert.NotEmpty(s.OffWord);
        });
    }

    [Fact]
    public void The_announced_services_are_the_four_with_a_measured_variable_and_never_the_chocks()
    {
        // The chocks' variable is already the map's Status row: a second announced key on one L:var would break the batch ([VAR-7]).
        Assert.Equal(new[] { "AFT STAIRS", "FUEL TRUCK", "CATERING", "CARGO SVC" }, Bbs757GroundServices.Announced.Select(s => s.Key));
    }

    [Fact]
    public void The_definition_has_one_announced_row_for_each_announced_service_and_for_no_other_tile()
    {
        var vars = new BBS757Definition { NestRunning = () => true }.GetVariables();

        var rows = vars.Keys.Where(k => k.StartsWith("BBS757_SVC_", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(Bbs757GroundServices.Announced.Select(BBS757Definition.ServiceKey).OrderBy(k => k, StringComparer.Ordinal), rows);
        Assert.All(Bbs757GroundServices.Announced, s =>
        {
            var row = vars[BBS757Definition.ServiceKey(s)];
            Assert.Equal(s.StateVar, row.Name);
            Assert.True(row.IsAnnounced);
        });
    }

    [Theory]
    [InlineData("FUEL TRUCK OFF", "FUEL TRUCK")]
    [InlineData("FUEL TRUCK", "FUEL TRUCK")]
    [InlineData("AFT STAIRS ON", "AFT STAIRS")]
    [InlineData("CHOCKS", "CHOCKS")]
    public void A_tile_is_found_by_its_words(string words, string key) =>
        Assert.Equal(key, Bbs757GroundServices.ForTile(words)!.Key);

    [Theory]
    [InlineData(100, 100, "Door L1 open")]
    [InlineData(0, 100, "Door L1 closed")]
    [InlineData(50, 100, null)]
    [InlineData(200, 200, "Door L1 open")]
    public void Door_phrases(double pos, double max, string? expected) =>
        Assert.Equal(expected, Bbs757GroundServices.DoorPhrase("L1", pos, max));

    [Fact]
    public void Change_phrases() =>
        Assert.Equal("Aft stairs attached", Bbs757GroundServices.ChangePhrase(Svc("AFT STAIRS"), 1));
}

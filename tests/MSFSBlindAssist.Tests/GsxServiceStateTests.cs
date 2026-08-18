using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxServiceStateTests
{
    private static JsonElement Fixture()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-services.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void Parses_the_live_services_array()
    {
        var list = GsxServiceState.ParseList(Fixture());
        Assert.NotEmpty(list);
        Assert.Contains(list, s => s.Id == "Deboarding");
        Assert.Contains(list, s => s.Id == "Refueling");
    }

    [Fact]
    public void Parses_deboarding_detail_and_progress()
    {
        var d = GsxServiceState.ParseList(Fixture()).First(s => s.Id == "Deboarding");
        Assert.Equal("performing", d.State);
        Assert.Equal(5, d.StateRaw);
        Assert.Equal("Deboard", d.DisplayName);
        Assert.Equal("OneJet", d.Operator);
        Assert.False(d.CanTrigger);
        Assert.Equal("in position", d.BusPhase);
        Assert.Equal(181, d.PaxDone);
        Assert.Equal(186, d.PaxTotal);
        Assert.Equal(100, d.BagsPercent);
        Assert.Equal("pax", d.ProgressUnit);
    }

    [Fact]
    public void Service_without_detail_leaves_optional_fields_null()
    {
        var c = GsxServiceState.ParseList(Fixture()).First(s => s.Id == "Catering");
        Assert.Equal("available", c.State);
        Assert.True(c.CanTrigger);
        Assert.Null(c.BusPhase);
        Assert.Null(c.PaxDone);
        Assert.Null(c.BagsPercent);
    }

    [Fact]
    public void Non_array_and_garbage_return_empty_not_throw()
    {
        Assert.Empty(GsxServiceState.ParseList(JsonDocument.Parse("{}").RootElement));
        Assert.Empty(GsxServiceState.ParseList(JsonDocument.Parse("[{}]").RootElement)
                                    .Where(s => !string.IsNullOrEmpty(s.Id)));
    }

    // The three PublishesTypedProgress tests that stood here went with the property. They
    // pinned a row-level answer to "is the message slot a ticker right now?", which is not
    // answerable per row: one service's figures and its crew prose share the slot. The
    // question is now asked per phrase — see GsxSlotRotationTrackerTests. The parse-level
    // coverage those tests leaned on (detail.pax / bagsPercent / fuel / the progress pair all
    // reaching their typed fields) is kept below, since that is a property of the PARSER and
    // outlived the predicate.

    [Theory]
    [InlineData("""{"id":"Boarding","state":"performing","detail":{"pax":{"done":10,"total":155}}}""", 10)]
    [InlineData("""{"id":"Boarding","state":"performing","detail":{"pax":{"done":0,"total":155}}}""", 0)]
    public void A_pax_detail_reaches_the_typed_field(string rowJson, int expectedDone)
    {
        var row = Assert.Single(GsxServiceState.ParseList(JsonDocument.Parse($"[{rowJson}]").RootElement));
        Assert.Equal(expectedDone, row.PaxDone);
        Assert.Equal(155, row.PaxTotal);
    }

    [Fact]
    public void A_bags_percent_reaches_the_typed_field()
    {
        var row = Assert.Single(GsxServiceState.ParseList(JsonDocument.Parse(
            """[{"id":"Boarding","state":"performing","detail":{"bagsPercent":40}}]""").RootElement));
        Assert.Equal(40, row.BagsPercent);
    }

    [Fact]
    public void A_fuel_detail_reaches_the_typed_field()
    {
        var row = Assert.Single(GsxServiceState.ParseList(JsonDocument.Parse(
            """[{"id":"Refueling","state":"performing","detail":{"fuel":{"current":2221,"unit":"kg"}}}]""").RootElement));
        Assert.Equal(2221, row.FuelCurrent);
    }

    [Fact]
    public void A_generic_progress_pair_reaches_the_typed_fields()
    {
        var row = Assert.Single(GsxServiceState.ParseList(JsonDocument.Parse(
            """[{"id":"Catering","state":"performing","progress":{"current":3,"total":9}}]""").RootElement));
        Assert.Equal(3, row.ProgressCurrent);
        Assert.Equal(9, row.ProgressTotal);
    }

    [Theory]
    // Pushback and de-icing carry no quantity at all — the shape that made a row-level gate
    // look workable, and the one whose prose the slot is the only channel for.
    [InlineData("""{"id":"Departure","state":"performing","detail":{"phase":"connecting"}}""")]
    [InlineData("""{"id":"DeIce","state":"performing"}""")]
    public void A_service_carrying_no_quantity_leaves_every_typed_field_unset(string rowJson)
    {
        var row = Assert.Single(GsxServiceState.ParseList(JsonDocument.Parse($"[{rowJson}]").RootElement));
        Assert.Null(row.PaxDone);
        Assert.Null(row.BagsPercent);
        Assert.Null(row.FuelCurrent);
        Assert.Null(row.ProgressCurrent);
    }
}

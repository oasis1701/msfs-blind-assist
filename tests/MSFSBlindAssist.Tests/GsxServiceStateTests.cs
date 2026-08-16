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

    /// <summary>
    /// PublishesTypedProgress decides whether GSX's message slot is this service's rotating
    /// progress TICKER (stay silent — the typed announcers already speak those figures) or is
    /// carrying prose the pilot has to act on. Getting it wrong in the "silent" direction is how
    /// the pushback parking-brake prompts went unspoken: a blanket "any service is performing"
    /// gate silenced the slot for services that publish no figures at all.
    /// </summary>
    [Fact]
    public void A_service_carrying_a_quantity_publishes_typed_progress()
    {
        var list = GsxServiceState.ParseList(Fixture());

        // Deboarding carries detail.pax in the live capture.
        var deboarding = Assert.Single(list.Where(s => s.Id == "Deboarding"));
        Assert.True(deboarding.PublishesTypedProgress);
    }

    [Theory]
    // Pushback and de-icing: no pax, no bags, no fuel, no progress pair. The message slot is the
    // ONLY channel carrying their prompts, so it must not be gated shut for them.
    [InlineData("""{"id":"Departure","state":"performing","detail":{"phase":"connecting"}}""")]
    [InlineData("""{"id":"DeIce","state":"performing"}""")]
    [InlineData("""{"id":"Departure","state":"performing","detail":{}}""")]
    public void A_service_carrying_no_quantity_does_not(string rowJson)
    {
        var row = Assert.Single(GsxServiceState.ParseList(JsonDocument.Parse($"[{rowJson}]").RootElement));
        Assert.False(row.PublishesTypedProgress);
    }

    [Theory]
    [InlineData("""{"id":"Boarding","state":"performing","detail":{"pax":{"done":10,"total":155}}}""")]
    [InlineData("""{"id":"Boarding","state":"performing","detail":{"bagsPercent":40}}""")]
    [InlineData("""{"id":"Refueling","state":"performing","detail":{"fuel":{"current":2221,"unit":"kg"}}}""")]
    [InlineData("""{"id":"Catering","state":"performing","progress":{"current":3,"total":9}}""")]
    public void Every_quantity_shape_counts_as_typed_progress(string rowJson)
    {
        var row = Assert.Single(GsxServiceState.ParseList(JsonDocument.Parse($"[{rowJson}]").RootElement));
        Assert.True(row.PublishesTypedProgress);
    }
}

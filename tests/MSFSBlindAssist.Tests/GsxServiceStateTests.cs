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
}

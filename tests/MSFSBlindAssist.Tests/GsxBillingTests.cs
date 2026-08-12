using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxBillingTests
{
    private static JsonElement Fixture(string name)
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void Parses_the_live_billing_timers()
    {
        var b = GsxBilling.Parse(Fixture("gsx-billing.json"));
        Assert.NotEmpty(b.Timers);
        var jetway = b.Timers.First();
        Assert.Equal("Jetway", jetway.SubService);
        Assert.False(string.IsNullOrEmpty(jetway.Friendly));
        Assert.True(jetway.Running);
        Assert.True(b.AnyRunning);
    }

    [Fact]
    public void No_timers_means_nothing_running()
    {
        var b = GsxBilling.Parse(JsonDocument.Parse("""{"timers":[]}""").RootElement);
        Assert.Empty(b.Timers);
        Assert.False(b.AnyRunning);
    }

    [Fact]
    public void Malformed_billing_does_not_throw()
    {
        Assert.Empty(GsxBilling.Parse(JsonDocument.Parse("{}").RootElement).Timers);
        Assert.Empty(GsxBilling.Parse(JsonDocument.Parse("[]").RootElement).Timers);
    }

    [Fact]
    public void Null_receipt_parses_to_null()
    {
        Assert.Null(GsxReceipt.Parse(JsonDocument.Parse("null").RootElement));
        Assert.Null(GsxReceipt.Parse(default));
    }

    [Fact]
    public void Receipt_reads_operator_and_ignores_the_logo_blob()
    {
        var r = GsxReceipt.Parse(JsonDocument.Parse("""{"operator":"OneJet","logo":"data:image/png;base64,AAAA","total":116.75,"lines":[{"label":"Jetway operations","amount":116.75}]}""").RootElement);
        Assert.NotNull(r);
        Assert.Equal("OneJet", r!.Operator);
        Assert.Equal(116.75, r.Total);
        Assert.Single(r.Lines);
        Assert.Equal("Jetway operations", r.Lines[0].Label);
    }
}

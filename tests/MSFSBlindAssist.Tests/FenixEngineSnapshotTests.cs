// Characterization tests for the Fenix engine data names and the snapshot the data-service
// callback writes into (pool thread) and the Engines window reads (UI timer).
using MSFSBlindAssist.Services;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixEngineSnapshotTests
{
    private static FenixDataValue V(string json) => FenixDataValue.FromJson(JToken.Parse(json));

    [Fact]
    public void Names_are_the_eighteen_published_values()
    {
        string[] engine(int n) => new[] { "running", "n1", "egt", "n2", "ff.kg", "oil.quantity", "oil.pressure", "oil.temperature" }
            .Select(f => $"aircraft.engines.{n}.{f}").ToArray();
        Assert.Equal(engine(1).Concat(engine(2)).Append("aircraft.temperature.tat").Append("aircraft.temperature.oat"), FenixEngineNames.All);
        Assert.DoesNotContain(FenixEngineNames.All, n => n.Contains("vibration"));
    }

    [Fact]
    public void Polled_is_every_value_but_the_running_flags()
    {
        // The window polls these once a second (the subscription sends N2 only once). The running flag is
        // left out: the subscription's flag is newer, and a stale polled one would speak a false "stopped".
        Assert.Equal(16, FenixEngineNames.Polled.Count);
        Assert.DoesNotContain(FenixEngineNames.Polled, n => n.EndsWith(".running", StringComparison.Ordinal));
        Assert.Equal(FenixEngineNames.All.Where(n => !n.EndsWith(".running", StringComparison.Ordinal)), FenixEngineNames.Polled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void For_rejects_an_engine_the_A320_does_not_have(int engine)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FenixEngineNames.For(engine, FenixEngineField.N1));

    [Fact]
    public void Apply_reads_numbers_and_the_running_flag()
    {
        var s = new FenixEngineSnapshot(() => 1000);
        s.Apply("aircraft.engines.2.n2", V("62.391407703690426"));
        s.Apply("aircraft.engines.1.running", V("true"));
        s.Apply("aircraft.engines.2.ff.kg", V("440.70427092395045"));
        s.Apply("aircraft.temperature.oat", V("17.0685977935791"));
        var r = s.Read();
        Assert.Equal(62.391407703690426, r.Engine2.N2);
        Assert.True(r.Engine1.Running);
        Assert.Equal(440.70427092395045, r.Engine2.FuelFlowKgPerHour);
        Assert.Equal(17.0685977935791, r.OatC);
        Assert.Null(r.Engine1.N1);
        Assert.Equal(1000, r.LastUpdateTick);
    }

    [Fact]
    public void Unknown_values_read_as_null_never_zero()
    {
        var s = new FenixEngineSnapshot(() => 1);
        s.Apply("aircraft.engines.1.n1", V("22.4"));
        s.Apply("aircraft.engines.1.n1", V("\"\""));              // v3: unknown name or no value
        s.Apply("aircraft.engines.1.egt", V("null"));
        s.Apply("aircraft.engines.1.n2", V("true"));              // wrong kind
        s.Apply("aircraft.engines.1.running", V("1"));            // wrong kind: only a JSON bool counts
        var e1 = s.Read().Engine1;
        Assert.Null(e1.N1); Assert.Null(e1.Egt); Assert.Null(e1.N2); Assert.Null(e1.Running);
    }

    [Fact]
    public void Other_names_are_ignored()
    {
        var s = new FenixEngineSnapshot(() => 5);
        s.Apply("aircraft.engines.1.vibration.n1", V("3"));
        s.Apply("aircraft.mcdu1.display", V("\"<root/>\""));
        Assert.Equal(FenixEngineReading.None, s.Read());
    }

    // Only a known value is news: the gateway answering "" for every name (Fenix not loaded) must leave
    // the status "waiting", not "values update every second" over a list of dashes.
    [Fact]
    public void Last_update_follows_known_values_only()
    {
        long now = 1000;
        var s = new FenixEngineSnapshot(() => now);
        Assert.Null(s.Read().LastUpdateTick);
        s.Apply("aircraft.engines.1.egt", V("\"\""));
        Assert.Null(s.Read().LastUpdateTick);
        s.Apply("aircraft.engines.1.n1", V("22.4"));
        now = 5000;
        s.Apply("aircraft.engines.1.n1", V("\"\""));
        Assert.Equal(1000, s.Read().LastUpdateTick);
        now = 6000;
        s.Apply("aircraft.engines.1.n1", V("22.5"));
        Assert.Equal(6000, s.Read().LastUpdateTick);
    }

    // The window's one-shot read (open, F5) is sampled before the subscription's newer values can
    // arrive: applying it late would turn "Engine 2 running" back into a false "stopped".
    [Fact]
    public void A_read_older_than_a_subscription_value_is_not_applied()
    {
        var s = new FenixEngineSnapshot(() => 1);
        long before = s.Version;
        s.Apply("aircraft.engines.2.running", V("true"));
        var read = new[] { new KeyValuePair<string, FenixDataValue>("aircraft.engines.2.running", V("false")) };
        Assert.False(s.ApplyAllIfUnchangedSince(read, before));
        Assert.True(s.Read().Engine2.Running);
    }

    [Fact]
    public void A_read_with_nothing_newer_is_applied()
    {
        var s = new FenixEngineSnapshot(() => 1);
        long before = s.Version;
        var read = new[]
        {
            new KeyValuePair<string, FenixDataValue>("aircraft.engines.2.running", V("false")),
            new KeyValuePair<string, FenixDataValue>("aircraft.engines.2.n2", V("20.5")),
        };
        Assert.True(s.ApplyAllIfUnchangedSince(read, before));
        Assert.False(s.Read().Engine2.Running);
        Assert.Equal(20.5, s.Read().Engine2.N2);
    }
}

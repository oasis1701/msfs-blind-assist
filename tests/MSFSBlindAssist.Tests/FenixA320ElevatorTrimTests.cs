using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Fenix's pitch trim is its own <c>A_FC_ELEVATOR_TRIM</c>, thousandths of a degree with
/// positive = UP (2026-11-04, A321 at CYYZ: 1698 = UP 1.7 while the stock ELEVATOR TRIM POSITION read
/// 0.29°). The call-out speaks it in the MCDU's degrees, and the panel takes a typed trim in degrees
/// rather than a dropdown of whole-degree raw steps. Run under de-DE: a comma-decimal PC must still
/// hear "1.7" and write 1700.
/// </summary>
public sealed class FenixA320ElevatorTrimTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public FenixA320ElevatorTrimTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static List<string> Feed(FenixA320Definition def, params double[] raws)
    {
        var speech = new SpeechCapture();
        foreach (double raw in raws) def.ProcessSimVarUpdate("MON_ElevatorTrim", raw, speech);
        return speech.All;
    }

    [Fact]
    public void The_call_out_reads_the_fenix_ths_not_the_stock_trim()
    {
        var vars = new FenixA320Definition().GetVariables();

        Assert.Equal(FenixA320ElevatorTrim.LVar, vars["MON_ElevatorTrim"].Name);
        Assert.Equal(SimConnect.UpdateFrequency.Continuous, vars["MON_ElevatorTrim"].UpdateFrequency);
    }

    [Fact]
    public void The_measured_value_is_spoken_as_up_1_7()
    {
        // The first sample is the silent baseline; 1698 is the CYYZ reading.
        Assert.Equal(new[] { "Trim up 1.7" }, Feed(new FenixA320Definition(), 300, 1698));
    }

    [Fact]
    public void Nose_down_trim_is_spoken_as_down()
    {
        Assert.Equal(new[] { "Trim down 0.8" }, Feed(new FenixA320Definition(), 0, -800));
    }

    [Fact]
    public void Jitter_in_thousandths_is_silent()
    {
        // ±10 raw is 0.01°: the hysteresis works in degrees after the conversion, so it still holds.
        Assert.Empty(Feed(new FenixA320Definition(), 1450, 1460, 1440, 1455, 1445));
    }

    [Theory]
    [InlineData(0.3, 300)]
    [InlineData(-0.8, -800)]
    [InlineData(1.66, 1700)]
    [InlineData(13.5, 13500)]
    [InlineData(-4.0, -4000)]
    public void A_typed_trim_in_degrees_writes_thousandths_to_the_tenth(double degrees, double expectedRaw)
    {
        Assert.True(FenixA320ElevatorTrim.TryToRaw(degrees, out double raw, out string? error));
        Assert.Equal(expectedRaw, raw);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(13.6)]
    [InlineData(-4.1)]
    [InlineData(300)]
    public void A_trim_past_the_ths_travel_is_refused_aloud(double degrees)
    {
        Assert.False(FenixA320ElevatorTrim.TryToRaw(degrees, out _, out string? error));
        Assert.Equal("Pitch trim out of range. Range: down 4.0 to up 13.5", error);
    }

    [Fact]
    public void An_unparseable_entry_is_refused()
    {
        Assert.False(FenixA320ElevatorTrim.TryToRaw(double.NaN, out _, out string? error));
        Assert.Equal("Invalid number format", error);
    }

    [Theory]
    [InlineData(0.3, "up 0.3")]
    [InlineData(-0.8, "down 0.8")]
    [InlineData(0.0, "up 0.0")]
    public void The_confirmation_reads_like_the_mcdu(double degrees, string expected)
    {
        Assert.Equal(expected, FenixA320ElevatorTrim.Describe(degrees));
    }

    [Fact]
    public void The_panel_entry_is_a_typed_field_on_the_same_lvar_read_on_request()
    {
        var def = new FenixA320Definition();
        var entry = def.GetVariables()[FenixA320ElevatorTrim.SetKey];

        Assert.Equal(FenixA320ElevatorTrim.LVar, entry.Name);
        Assert.Equal(SimConnect.UpdateFrequency.OnRequest, entry.UpdateFrequency);
        Assert.Empty(entry.ValueDescriptions);
        Assert.True(entry.UnparseableTextAsNaN);
        Assert.False(def.GetVariables().ContainsKey(FenixA320ElevatorTrim.LVar));
    }
}

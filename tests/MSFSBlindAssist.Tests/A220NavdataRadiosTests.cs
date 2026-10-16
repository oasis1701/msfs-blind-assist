using MSFSBlindAssist.Aircraft.A220;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the A220 FMS navdata-load and radios read models — the agent JSON the
/// captain MFW's Data Load page and the AFDX nav/radio stores produce.
/// </summary>
public class A220NavdataRadiosTests
{
    [Fact]
    public void Navdata_ParsesPackageListAndProgress()
    {
        var st = A220NavdataState.Parse(
            "{\"ok\":true,\"fmt\":8,\"auth\":null,\"page\":\"databases\"," +
            "\"rows\":[{\"name\":\"Navigraph_v2_2609_03SEP26\",\"selected\":true,\"disabled\":false,\"status\":\"\"}]," +
            "\"canStart\":true,\"progress\":46,\"complete\":null}");
        Assert.NotNull(st);
        Assert.Equal(8, st!.Format);
        Assert.False(st.AuthRequired);
        Assert.Single(st.Rows);
        Assert.True(st.Rows[0].Selected);
        Assert.True(st.CanStart);
        Assert.Equal(46, st.Progress);
        Assert.Null(st.Complete);
    }

    [Fact]
    public void Navdata_ReadsSignInCode()
    {
        var st = A220NavdataState.Parse("{\"ok\":true,\"fmt\":8,\"auth\":{\"code\":\"ASSPWXLZ\",\"url\":\"https://navigraph.com/code\"},\"page\":\"none\",\"rows\":[]}");
        Assert.True(st!.AuthRequired);
        Assert.Equal("ASSPWXLZ", st.AuthCode);
    }

    [Fact]
    public void Navdata_NotOk_IsNull()
    {
        Assert.Null(A220NavdataState.Parse("{\"ok\":false}"));
        Assert.Null(A220NavdataState.Parse(""));
        Assert.Null(A220NavdataState.Parse("not json"));
    }

    [Theory]
    [InlineData("Navigraph_v2_2609_03SEP26", "Navigraph cycle 2609, effective 03SEP26")]
    [InlineData("Something else", "Something else")]
    public void Navdata_DescribesPackage(string name, string expected)
        => Assert.Equal(expected, A220NavdataState.DescribePackage(name));

    [Theory]
    [InlineData(1, "Navigraph")]
    [InlineData(0, "MSFS native")]
    [InlineData(-1, "unknown")]
    public void Navdata_DescribesSource(double source, string startsWith)
        => Assert.StartsWith(startsWith, A220NavdataState.DescribeSource(source));

    [Fact]
    public void Radios_ParsesStoresInHz()
    {
        var st = A220RadioState.Parse(
            "{\"ok\":true,\"lnav\":{\"source\":2,\"frequency\":110300000,\"course\":110,\"to\":true}," +
            "\"lctp\":{\"nav_source\":1,\"course\":110}," +
            "\"radio\":{\"vhf\":{\"nav_1\":{\"active\":110300000,\"preset\":108000000,\"autotune\":false},\"nav_2\":108000000}}}");
        Assert.NotNull(st);
        Assert.Equal(2, st!.NavSource);
        Assert.Equal("VOR 1", A220RadioState.SourceName(st.NavSource));
        Assert.True(A220RadioState.IsRadioSource(st.NavSource));
        Assert.Equal(110.3, st.SourceFrequencyMhz!.Value, 3);
        Assert.Equal(110.3, st.Nav1!.ActiveMhz!.Value, 3);
        Assert.Equal(108.0, st.Nav1.PresetMhz!.Value, 3);
        Assert.False(st.Nav1.AutoTune);
        Assert.Equal(108.0, st.Nav2!.ActiveMhz!.Value, 3);   // bare number = active
        Assert.Contains("Captain nav source: VOR 1", st.Describe());
    }

    [Fact]
    public void Radios_NoStores_SaysWaiting()
    {
        var st = A220RadioState.Parse("{\"ok\":true,\"lnav\":null,\"lctp\":null,\"radio\":null}");
        Assert.False(st!.LinkUp);
        Assert.StartsWith("Waiting", st.Describe()[0]);
    }

    [Theory]
    [InlineData("110", true, 110)]
    [InlineData("0", true, 360)]
    [InlineData("360", true, 360)]
    [InlineData("361", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("abc", false, 0)]
    public void Radios_ParsesCourse(string text, bool ok, int expected)
    {
        Assert.Equal(ok, A220RadioState.TryParseCourse(text, out int c));
        if (ok) Assert.Equal(expected, c);
    }

    [Theory]
    [InlineData("110.3", true, 110.30)]
    [InlineData("110,30", true, 110.30)]
    [InlineData("109.52", true, 109.50)]   // snapped to the 50 kHz grid
    [InlineData("107.95", false, 0)]
    [InlineData("118.00", false, 0)]
    public void Radios_ParsesNavFrequency(string text, bool ok, double expected)
    {
        Assert.Equal(ok, A220RadioState.TryParseNavMhz(text, out double f));
        if (ok) Assert.Equal(expected, f, 3);
    }

    [Theory]
    [InlineData(0.0, "360")]
    [InlineData(5.0, "005")]
    [InlineData(110.4, "110")]
    public void Radios_FormatsDegrees(double v, string expected)
        => Assert.Equal(expected, A220RadioState.Degrees(v));
}

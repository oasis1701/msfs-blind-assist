using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// Each IRS's alignment on the IRS panel's status box (measured 2026-10-10): a selector in NAV sets
/// <c>INI_IRSn_IS_ALIGNING</c> with <c>INI_IRSn_TIME_REMAIN</c> at 0 until the MCDU's ALIGN IRS (INIT A, once all
/// three are in NAV and the route has a FROM/TO); then the time counts down from about 180 s, and at 0 the IRS
/// reads <c>INI_IRSn_ALIGNED</c> 1 and aligning 0.
/// </summary>
public class A300IrsAlignmentTests
{
    [Theory]
    [InlineData(147.5, 1, 0, "aligning, 2 minutes 28 seconds left")]
    [InlineData(59, 1, 0, "aligning, 59 seconds left")]
    [InlineData(0, 1, 0, "waiting for ALIGN IRS on the MCDU")]
    [InlineData(-0.01, 0, 1, "aligned")]
    [InlineData(0, 0, 0, "off")]
    public void An_irs_reads_its_alignment(double remain, double aligning, double aligned, string text) =>
        Assert.Equal(text, A300IrsAlignment.Status(remain, aligning, aligned));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void It_reads_the_same_in_any_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("aligning, 1 minute 5 seconds left", A300IrsAlignment.Status(64.5, 1, 0));   // counts up to the next second
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_irs_panels_status_box_has_a_line_for_each_irs()
    {
        var lines = A300Readouts.All.Where(r => r.Panel == "IRS").Select(r => (r.Name, r.Var)).ToArray();
        Assert.Equal(new[]
        {
            ("IRS 1 alignment", "INI_IRS1_TIME_REMAIN"),
            ("IRS 2 alignment", "INI_IRS2_TIME_REMAIN"),
            ("IRS 3 alignment", "INI_IRS3_TIME_REMAIN"),
        }, lines);
    }

    [Fact]
    public void The_line_is_composed_from_the_irs_flags()
    {
        var irs = A300IrsAlignment.All[1];
        var cache = new Dictionary<string, double>();
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        // The aligning flag is the variable of the IRS align light, whose own key streams it: the line reads that key's cache.
        string aligningStream = def.StreamingKey(irs.AligningKey);
        Assert.Equal("A300_LT_IRS_3_012_LIGHT", aligningStream);
        cache[aligningStream] = 1;
        cache[irs.AlignedKey] = 0;
        Assert.True(def.TryGetDisplayOverride(irs.LineKey, 0, out var waiting));
        Assert.Equal("waiting for ALIGN IRS on the MCDU", waiting);
        cache[aligningStream] = 0;
        Assert.True(def.TryGetDisplayOverride(irs.LineKey, 0, out var off));
        Assert.Equal("off", off);
        var vars = def.GetVariables();
        Assert.Equal(irs.AligningVar, vars[aligningStream].Name);
        Assert.DoesNotContain(irs.AligningKey, vars.Keys);
        Assert.True(vars[irs.AlignedKey].ExcludeFromBatch && vars[irs.AlignedKey].ExcludeFromMonitorManager);
        Assert.True(def.ProcessSimVarUpdate(irs.AlignedKey, 1, new SpeechCapture()));
    }
}

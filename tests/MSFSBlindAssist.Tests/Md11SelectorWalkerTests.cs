using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the pure position-resolution logic behind the closed-loop walker.
///
/// The walk itself needs a live sim, but the arithmetic that decides WHICH WAY to step does not —
/// and that arithmetic is where a silent, invisible bug lives. If PositionIndex misreads where a
/// control currently is, the walker confidently steps the wrong way to an end stop, on an
/// aircraft whose panels the pilot cannot see.
/// </summary>
public class Md11SelectorWalkerTests
{
    private static Md11Control FlapLever()
        => Md11ControlMap.Load().Controls.First(c => c.NodeId == Md11FlapSystem.LeverKey);

    /// <summary>A plain point-detent control: no curated detents, values straight off the tooltip.</summary>
    private static Md11Control PointControl() => new()
    {
        NodeId = "TEST_KNOB",
        Kind = Md11Kinds.Knob,
        ValueMap = new Dictionary<string, string> { ["0"] = "A", ["1"] = "B", ["2"] = "C" },
    };

    // ---------------------------------------------------------------------------------
    // OrderedValues
    // ---------------------------------------------------------------------------------

    [Fact]
    public void OrderedValues_FromValueMap_AreAscending()
    {
        Assert.Equal(new List<double> { 0, 1, 2 }, Md11SelectorWalker.OrderedValues(PointControl()));
    }

    /// <summary>
    /// Curated detents win over the tooltip's value map. The flap lever is exactly why: its
    /// value map has five entries, its detents have six — the tooltip's %{case} block cannot see
    /// the Dial-A-Flap range test, so ordering off the value map loses the take-off detent and
    /// every index downstream shifts.
    /// </summary>
    [Fact]
    public void OrderedValues_PreferCuratedDetentsOverValueMap()
    {
        var lever = FlapLever();

        Assert.Equal(new List<double> { 0, 20, 50, 70, 82, 100 }, Md11SelectorWalker.OrderedValues(lever));
        Assert.Equal(5, lever.ValueMap.Count);   // the tooltip map really is one short
    }

    /// <summary>Decimal values parse invariantly — "17.5" must not become 175 under a comma locale.</summary>
    [Fact]
    public void OrderedValues_ParseDecimalsInvariantly()
    {
        var spoiler = new Md11Control
        {
            NodeId = "TEST_SPOILER",
            ValueMap = new Dictionary<string, string>
            {
                ["0"] = "Retracted", ["17.5"] = "1/3", ["25"] = "2/3", ["32.5"] = "3/3",
            },
        };

        Assert.Equal(new List<double> { 0, 17.5, 25, 32.5 }, Md11SelectorWalker.OrderedValues(spoiler));
    }

    // ---------------------------------------------------------------------------------
    // PositionIndex — the range-detent trap
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 1)]
    [InlineData(70, 3)]
    [InlineData(82, 4)]
    [InlineData(100, 5)]
    public void PositionIndex_ResolvesPointDetents(double rng, int expectedIdx)
    {
        var lever = FlapLever();
        var ordered = Md11SelectorWalker.OrderedValues(lever);

        Assert.Equal(expectedIdx, Md11SelectorWalker.PositionIndex(lever, ordered, rng));
    }

    /// <summary>
    /// THE regression this guards. FLAP_RNG 60–65 is inside the Dial-A-Flap band [38,65], but it
    /// is nearer to the NEXT detent's value (70) than to Dial-A-Flap's representative value (50).
    /// Nearest-value matching therefore reports index 3 ("Flap 28") for a handle that is really in
    /// the take-off detent — so a walk from there would step the wrong way, and the read-out would
    /// name the wrong detent. The range test is what fixes it.
    /// </summary>
    [Theory]
    [InlineData(38)]
    [InlineData(50)]
    [InlineData(59)]
    [InlineData(60)]   // exact midpoint of 50 and 70 — nearest-value is ambiguous here
    [InlineData(63)]
    [InlineData(65)]   // top of the band, 5 units from 70 but still Dial-A-Flap
    public void PositionIndex_WholeDialBandResolvesToDialDetent(double rng)
    {
        var lever = FlapLever();
        var ordered = Md11SelectorWalker.OrderedValues(lever);

        Assert.Equal(2, Md11SelectorWalker.PositionIndex(lever, ordered, rng));
    }

    /// <summary>Proves the above is a real divergence, not a test that would pass either way.</summary>
    [Fact]
    public void PositionIndex_DivergesFromNearestValue_InsideTheDialBand()
    {
        var lever = FlapLever();
        var ordered = Md11SelectorWalker.OrderedValues(lever);

        // Nearest-value would call 63 "Flap 28" (index 3); the range test correctly says
        // Dial-A-Flap (index 2).
        Assert.Equal(3, Md11SelectorWalker.NearestIndex(ordered, 63));
        Assert.Equal(2, Md11SelectorWalker.PositionIndex(lever, ordered, 63));
    }

    /// <summary>A lever caught mid-travel has no matching detent, so it falls back to nearest.</summary>
    [Fact]
    public void PositionIndex_BetweenDetents_FallsBackToNearest()
    {
        var lever = FlapLever();
        var ordered = Md11SelectorWalker.OrderedValues(lever);

        // 22 matches no detent band; nearest is 20 (index 1).
        Assert.Equal(1, Md11SelectorWalker.PositionIndex(lever, ordered, 22));
    }

    [Fact]
    public void PositionIndex_ControlWithoutDetents_UsesNearest()
    {
        var knob = PointControl();
        var ordered = Md11SelectorWalker.OrderedValues(knob);

        Assert.Equal(0, Md11SelectorWalker.PositionIndex(knob, ordered, 0.2));
        Assert.Equal(2, Md11SelectorWalker.PositionIndex(knob, ordered, 1.9));
    }

    // ---------------------------------------------------------------------------------
    // Detent matching
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Detent_RangeMatch_IsInclusiveOfBothEnds()
    {
        var d = new Md11Detent { Value = 50, Range = new List<double> { 38, 65 }, Name = "Dial-A-Flap" };

        Assert.True(d.Matches(38));
        Assert.True(d.Matches(65));
        Assert.False(d.Matches(37.9));
        Assert.False(d.Matches(65.1));
    }

    [Fact]
    public void Detent_PointMatch_ToleratesFloatNoise()
    {
        var d = new Md11Detent { Value = 70, Name = "Flap 28" };

        Assert.True(d.Matches(70));
        Assert.True(d.Matches(70.2));    // sim floats are never exact
        Assert.False(d.Matches(71));
    }
}

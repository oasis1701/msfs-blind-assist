namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.SimConnect.IFly;

public class IFlySdkFieldsInvariantTests
{
    private static int KindSize(char k) => k switch { 'I' => 4, 'D' => 8, _ => 1 };

    [Fact]
    public void AllFields_WithinStruct_AndNonOverlapping()
    {
        var ranges = new List<(int Start, int End, string Name)>();
        foreach (var f in IFlySdkFields.All)
            for (int i = 0; i < f.Count; i++)
            {
                int start = f.Offset + i * f.Stride;
                int end = start + KindSize(f.Kind);
                Assert.True(end <= IFlySdkOffsets.StructSize, $"{f.Name}[{i}] ends at {end} > StructSize");
                ranges.Add((start, end, f.Name));
            }
        ranges.Sort((a, b2) => a.Start.CompareTo(b2.Start));
        for (int i = 1; i < ranges.Count; i++)
            Assert.True(ranges[i].Start >= ranges[i - 1].End,
                $"{ranges[i].Name}@{ranges[i].Start} overlaps {ranges[i - 1].Name} ending {ranges[i - 1].End}");
    }
}

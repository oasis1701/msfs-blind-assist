using MSFSBlindAssist.Utils.Logging;
using Xunit;

public class LogRotatorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(5*1024*1024 - 1, false)]
    [InlineData(5*1024*1024, true)]
    [InlineData(6*1024*1024, true)]
    public void ShouldRotate_at_cap(long size, bool expected)
        => Assert.Equal(expected, LogRotator.ShouldRotate(size, 5*1024*1024));

    [Theory]
    [InlineData(@"C:\logs\taxi_guidance.log", 1, @"C:\logs\taxi_guidance.1.log")]
    [InlineData(@"C:\logs\taxi_guidance.log", 3, @"C:\logs\taxi_guidance.3.log")]
    [InlineData(@"C:\logs\input_events.txt", 2, @"C:\logs\input_events.2.txt")]
    public void RotatedName_inserts_index_before_extension(string basePath, int i, string expected)
        => Assert.Equal(expected, LogRotator.RotatedName(basePath, i));

    [Fact]
    public void Plan_deletes_oldest_then_shifts_down()
    {
        var plan = LogRotator.Plan(@"C:\logs\d.log", 3);
        Assert.Equal(@"C:\logs\d.3.log", plan.DeleteFirst);
        Assert.Equal(new[]{ (@"C:\logs\d.2.log", @"C:\logs\d.3.log"),
                            (@"C:\logs\d.1.log", @"C:\logs\d.2.log"),
                            (@"C:\logs\d.log",   @"C:\logs\d.1.log") },
                     plan.Moves);
    }
}

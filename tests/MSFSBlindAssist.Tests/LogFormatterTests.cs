using System;
using MSFSBlindAssist.Utils.Logging;
using Xunit;

public class LogFormatterTests
{
    static readonly DateTime T = new(2026, 7, 7, 14, 3, 12, 417, DateTimeKind.Local);

    [Theory]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Info,  "INFO ")]
    [InlineData(LogLevel.Warn,  "WARN ")]
    [InlineData(LogLevel.Error, "ERROR")]
    public void LevelTag_is_padded_to_five(LogLevel level, string expected)
        => Assert.Equal(expected, LogFormatter.LevelTag(level));

    [Fact]
    public void Format_produces_exact_line()
        => Assert.Equal("2026-07-07 14:03:12.417 [INFO ] [Docking] docked",
                        LogFormatter.Format(T, LogLevel.Info, "Docking", "docked"));

    [Fact]
    public void Empty_category_renders_as_dash()
        => Assert.Equal("2026-07-07 14:03:12.417 [DEBUG] [-] hi",
                        LogFormatter.Format(T, LogLevel.Debug, "", "hi"));

    [Fact]
    public void Exception_is_appended_indented()
    {
        var ex = new InvalidOperationException("boom");
        string line = LogFormatter.Format(T, LogLevel.Error, "X", "failed", ex);
        Assert.StartsWith("2026-07-07 14:03:12.417 [ERROR] [X] failed", line);
        Assert.Contains("InvalidOperationException", line);
        Assert.Contains(Environment.NewLine + "    ", line); // indented continuation
    }
}

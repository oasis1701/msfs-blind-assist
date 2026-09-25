// tests/MSFSBlindAssist.Tests/GeminiResponseTests.cs
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

// ParseResponse is the tail of SendRequestAsync, made internal so the finishReason handling is
// pinned: a MAX_TOKENS response gets the same "may be incomplete" note ClaudeService already adds.
public class GeminiResponseTests
{
    [Fact]
    public void Text_parts_are_joined()
        => Assert.Equal("Hello world", GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"Hello "},{"text":"world"}]},"finishReason":"STOP"}]}"""));

    [Fact]
    public void Max_tokens_appends_the_incomplete_note()
    {
        string text = GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"Cut off"}]},"finishReason":"MAX_TOKENS"}]}""");
        Assert.Equal("Cut off" + GeminiService.IncompleteNote, text);
    }

    [Fact]
    public void Stop_does_not_append_the_note()
        => Assert.Equal("Done", GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"Done"}]},"finishReason":"STOP"}]}"""));

    [Fact]
    public void Missing_finish_reason_is_treated_as_complete()
        => Assert.Equal("Done", GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"Done"}]}}]}"""));

    [Fact]
    public void Blank_text_reads_as_no_description()
        => Assert.Equal("No description available.", GeminiService.ParseResponse(
            """{"candidates":[{"content":{"parts":[{"text":"  "}]},"finishReason":"STOP"}]}"""));

    [Fact]
    public void No_candidates_throws()
        => Assert.Throws<InvalidOperationException>(() => GeminiService.ParseResponse("""{"candidates":[]}"""));
}

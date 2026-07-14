using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using MSFSBlindAssist.Services;
using Xunit;

// Characterization tests for ClaudeService's pure logic (accessed via InternalsVisibleTo;
// see Properties/InternalsVisibleTo.cs). ParseResponse pins the web-search narration
// stripping (only text AFTER the last tool block is the answer) and the stop_reason
// incompleteness surfacing; GetRetryDelay pins Retry-After header handling.
public class ClaudeServiceTests
{
    // ── ParseResponse: narration stripping (current behavior) ──────────────

    [Fact]
    public void Plain_text_blocks_are_joined()
        => Assert.Equal("Hello world",
            ClaudeService.ParseResponse(
                """{"content":[{"type":"text","text":"Hello "},{"type":"text","text":"world"}]}"""));

    [Fact]
    public void Narration_before_tool_blocks_is_dropped()
        => Assert.Equal("The briefing.",
            ClaudeService.ParseResponse(
                """
                {"content":[
                  {"type":"text","text":"Let me search for NOTAMs."},
                  {"type":"server_tool_use"},
                  {"type":"web_search_tool_result"},
                  {"type":"text","text":"The briefing."}
                ]}
                """));

    [Fact]
    public void Narration_between_tool_blocks_is_dropped()
        => Assert.Equal("Final answer.",
            ClaudeService.ParseResponse(
                """
                {"content":[
                  {"type":"text","text":"Searching."},
                  {"type":"server_tool_use"},
                  {"type":"text","text":"Now composing."},
                  {"type":"web_search_tool_result"},
                  {"type":"text","text":"Final answer."}
                ]}
                """));

    [Fact]
    public void Empty_content_throws()
        => Assert.Throws<InvalidOperationException>(
            () => ClaudeService.ParseResponse("""{"content":[]}"""));

    [Fact]
    public void Missing_content_throws()
        => Assert.Throws<InvalidOperationException>(
            () => ClaudeService.ParseResponse("""{"stop_reason":"end_turn"}"""));

    [Fact]
    public void Whitespace_only_text_yields_placeholder()
        => Assert.Equal("No description available.",
            ClaudeService.ParseResponse("""{"content":[{"type":"text","text":"  "}]}"""));

    // ── ParseResponse: stop_reason surfacing (new behavior) ────────────────

    [Fact]
    public void End_turn_stop_appends_nothing()
        => Assert.Equal("Done.",
            ClaudeService.ParseResponse(
                """{"content":[{"type":"text","text":"Done."}],"stop_reason":"end_turn"}"""));

    [Fact]
    public void Max_tokens_stop_appends_incomplete_note()
        => Assert.Equal(
            "Partial briefing\n\n(Response may be incomplete — Claude stopped before finishing.)",
            ClaudeService.ParseResponse(
                """{"content":[{"type":"text","text":"Partial briefing"}],"stop_reason":"max_tokens"}"""));

    [Fact]
    public void Pause_turn_stop_appends_incomplete_note()
        => Assert.Equal(
            "Partial briefing\n\n(Response may be incomplete — Claude stopped before finishing.)",
            ClaudeService.ParseResponse(
                """{"content":[{"type":"text","text":"Partial briefing"}],"stop_reason":"pause_turn"}"""));

    [Fact]
    public void Incomplete_stop_with_only_tool_blocks_reports_interruption()
        => Assert.Equal("Claude stopped before completing a response. Please try again.",
            ClaudeService.ParseResponse(
                """{"content":[{"type":"server_tool_use"}],"stop_reason":"pause_turn"}"""));

    // ── ComputeScaledSize: vision downscale sizing ──────────────────────────
    // Anthropic's per-image cap is 5 MB and current models accept at most 2576 px on the
    // long edge (anything larger is downscaled server-side anyway), so scaling client-side
    // loses no fidelity while keeping 4K screenshots under the byte cap.

    [Theory]
    [InlineData(1920, 1080)]  // 1080p — untouched
    [InlineData(2560, 1440)]  // 1440p — untouched
    [InlineData(2576, 1440)]  // exactly at the limit — untouched
    public void At_or_below_limit_is_unchanged(int width, int height)
        => Assert.Equal((width, height), ClaudeService.ComputeScaledSize(width, height));

    [Fact]
    public void FourK_downscales_to_2576_long_edge()
        => Assert.Equal((2576, 1449), ClaudeService.ComputeScaledSize(3840, 2160));

    [Fact]
    public void Portrait_long_edge_is_height()
        => Assert.Equal((1449, 2576), ClaudeService.ComputeScaledSize(2160, 3840));

    [Fact]
    public void Ultrawide_rounds_away_from_zero()
        => Assert.Equal((2576, 725), ClaudeService.ComputeScaledSize(5120, 1440));

    // ── GetRetryDelay: Retry-After header handling ──────────────────────────

    [Fact]
    public void Retry_after_delta_is_used()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
        Assert.Equal(10, ClaudeService.GetRetryDelay(response, 0));
    }

    [Fact]
    public void Retry_after_delta_is_capped_at_30()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        Assert.Equal(30, ClaudeService.GetRetryDelay(response, 0));
    }

    [Fact]
    public void Retry_after_far_future_date_is_capped_at_30()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter =
            new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(300));
        Assert.Equal(30, ClaudeService.GetRetryDelay(response, 0));
    }

    [Fact]
    public void Retry_after_past_date_clamps_to_1()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter =
            new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(-60));
        Assert.Equal(1, ClaudeService.GetRetryDelay(response, 0));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    public void No_header_falls_back_to_exponential(int attempt, int expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(expected, ClaudeService.GetRetryDelay(response, attempt));
    }
}

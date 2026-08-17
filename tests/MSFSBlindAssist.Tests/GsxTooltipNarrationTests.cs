// GSX's running ground-crew narration must survive a governing service.
//
// Reported live 2026-08-17 (present in origin/main, so every user has it). The pilot used to
// poll the tooltip during pushback and hear GSX's commentary — "Headset operator approaching",
// "Inserting bypass pin", "Raising nose gear", "Disconnecting tug", "Removing bypass pin",
// "Signalling clear". None of it is auto-announced, so the manual poll was the only route to it.
//
// RecomputeTooltip read GSX's message slot ONLY when no service was governing, which is the
// exact inverse of when the slot has anything in it: the narration appeared while parked and
// idle, and was replaced by a one-line service summary the moment a service started running.
// MessageText's own doc-comment recorded the behaviour as intended — "the fallback whenever no
// service is performing" — which is how it survived review.

using System.Text.Json;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxTooltipNarrationTests
{
    private static GsxServiceState Performing(string stateText) =>
        GsxServiceState.ParseList(JsonDocument.Parse(
            $$"""[{"key":"Departure","name":"Pushback","state":"performing","stateText":"{{stateText}}"}]""")
            .RootElement)[0];

    [Fact]
    public void The_live_narration_reaches_the_tooltip_while_a_service_is_governing()
    {
        // The case that broke: pushback running (so a service governs) AND GSX narrating.
        string tooltip = GsxActiveServiceResolver.ComposeTooltip(
            Performing("Pushback in progress"), "Inserting bypass pin");

        Assert.Contains("Inserting bypass pin", tooltip);
        Assert.Contains("Pushback in progress", tooltip);
    }

    [Theory]
    [InlineData("Headset operator approaching")]
    [InlineData("Raising nose gear")]
    [InlineData("Disconnecting tug")]
    [InlineData("Removing bypass pin")]
    [InlineData("Signalling clear")]
    [InlineData("Release parking brakes")]
    [InlineData("Commencing push. All engines clear. Start after 49.0 meters")]
    public void Every_step_of_the_narration_survives(string narration)
    {
        Assert.Contains(narration,
            GsxActiveServiceResolver.ComposeTooltip(Performing("Pushback in progress"), narration));
    }

    [Fact]
    public void The_service_summary_alone_is_unchanged_when_the_slot_is_empty()
    {
        // GSX hides the slot between steps ({"visible":false} reads as ""), and the tooltip must
        // not sprout stray punctuation on those frames.
        var svc = Performing("Pushback in progress");
        string bare = GsxActiveServiceResolver.ComposeTooltip(svc);

        Assert.Equal(bare, GsxActiveServiceResolver.ComposeTooltip(svc, null));
        Assert.Equal(bare, GsxActiveServiceResolver.ComposeTooltip(svc, ""));
        Assert.Equal(bare, GsxActiveServiceResolver.ComposeTooltip(svc, "   "));
        Assert.DoesNotContain("..", bare);
        Assert.False(bare.EndsWith('.'), $"trailing full stop on a bare summary: '{bare}'");
    }

    [Fact]
    public void A_slot_that_merely_restates_the_service_is_not_read_out_twice()
    {
        // GSX frequently echoes the phase into the slot. Hearing the same clause twice on every
        // poll is the fastest way to train a pilot out of polling.
        string tooltip = GsxActiveServiceResolver.ComposeTooltip(
            Performing("Pushback in progress"), "Pushback in progress");

        Assert.Equal("Pushback in progress", tooltip);
    }

    [Fact]
    public void The_duplicate_check_ignores_case()
    {
        Assert.Equal("Pushback in progress", GsxActiveServiceResolver.ComposeTooltip(
            Performing("Pushback in progress"), "pushback IN progress"));
    }

    [Fact]
    public void Narration_still_reaches_the_tooltip_with_no_governing_service_at_all()
    {
        // The pre-existing idle path — parked, in the cruise, after a service finishes — must
        // keep working exactly as it did. That one was never broken.
        Assert.Equal("Have a good trip", GsxActiveServiceResolver.MessageText(
            JsonDocument.Parse("""{"text":"Have a good trip","visible":true}""").RootElement));
    }

    [Fact]
    public void A_hidden_slot_contributes_nothing_however_much_text_it_carries()
    {
        // GSX gates rendering on `visible`; stale text behind a hidden slot is not current
        // narration and must not be appended to a live service summary.
        Assert.Equal("", GsxActiveServiceResolver.MessageText(
            JsonDocument.Parse("""{"text":"Removing bypass pin","visible":false}""").RootElement));
    }
}

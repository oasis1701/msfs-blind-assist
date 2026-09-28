using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteDescriptionSessionTests
{
    [Fact]
    public void A_new_session_holds_no_description_and_is_not_generating()
    {
        var session = new RouteDescriptionSession();

        Assert.Equal("", session.Text);
        Assert.False(session.IsGenerating);
    }

    [Fact]
    public void A_description_stored_for_the_current_generation_is_kept()
    {
        var session = new RouteDescriptionSession();
        int generation = session.BeginGenerating();

        bool stored = session.TryStore(generation, "Depart runway 18R.");

        Assert.True(stored);
        Assert.Equal("Depart runway 18R.", session.Text);
    }

    [Fact]
    public void Clear_erases_the_description_and_moves_the_generation_on()
    {
        var session = new RouteDescriptionSession();
        int before = session.BeginGenerating();
        session.TryStore(before, "Depart runway 18R.");

        session.Clear();

        Assert.Equal("", session.Text);
        Assert.NotEqual(before, session.Generation);
    }

    [Fact]
    public void A_description_finished_after_Clear_is_discarded()
    {
        // Load SimBrief pressed while the briefing ran: the briefing is for the plan just replaced.
        var session = new RouteDescriptionSession();
        int generation = session.BeginGenerating();
        session.Clear();

        bool stored = session.TryStore(generation, "Stale briefing.");

        Assert.False(stored);
        Assert.Equal("", session.Text);
    }

    [Fact]
    public void A_stale_store_leaves_an_existing_description_untouched()
    {
        var session = new RouteDescriptionSession();
        int stale = session.Generation;
        session.Clear();
        int current = session.Generation;
        session.TryStore(current, "Current briefing.");

        session.TryStore(stale, "Stale briefing.");

        Assert.Equal("Current briefing.", session.Text);
    }

    [Fact]
    public void Begin_and_End_toggle_the_generating_flag()
    {
        var session = new RouteDescriptionSession();

        session.BeginGenerating();
        Assert.True(session.IsGenerating);

        session.EndGenerating();
        Assert.False(session.IsGenerating);
    }

    [Fact]
    public void BeginGenerating_returns_the_current_generation()
    {
        var session = new RouteDescriptionSession();
        session.Clear();
        session.Clear();

        Assert.Equal(session.Generation, session.BeginGenerating());
    }

    [Fact]
    public void Changed_fires_on_begin_store_end_and_clear()
    {
        var session = new RouteDescriptionSession();
        int raised = 0;
        session.Changed += (_, _) => raised++;

        int generation = session.BeginGenerating();
        session.TryStore(generation, "Briefing.");
        session.EndGenerating();
        session.Clear();

        Assert.Equal(4, raised);
    }

    [Fact]
    public void Changed_does_not_fire_for_a_rejected_store()
    {
        var session = new RouteDescriptionSession();
        int stale = session.Generation;
        session.Clear();
        int raised = 0;
        session.Changed += (_, _) => raised++;

        session.TryStore(stale, "Stale briefing.");

        Assert.Equal(0, raised);
    }
}

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

        int generation = session.BeginGenerating();
        Assert.True(session.IsGenerating);

        session.EndGenerating(generation);
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
        session.EndGenerating(generation);
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

    [Fact]
    public void Abandon_keeps_the_description_but_discards_a_running_briefing_and_frees_the_flag()
    {
        // A database switch mid-briefing: the running briefing was computed against the old database.
        var session = new RouteDescriptionSession();
        int first = session.BeginGenerating();
        session.TryStore(first, "Kept briefing.");
        int running = session.BeginGenerating();

        session.Abandon();

        Assert.False(session.IsGenerating);
        Assert.Equal("Kept briefing.", session.Text);
        Assert.False(session.TryStore(running, "Old-database briefing."));
    }

    [Fact]
    public void A_superseded_briefing_ending_never_frees_a_newer_one()
    {
        var session = new RouteDescriptionSession();
        int stale = session.BeginGenerating();
        session.Clear();                       // Load SimBrief releases the flag and moves the generation on
        int current = session.BeginGenerating();

        session.EndGenerating(stale);

        Assert.True(session.IsGenerating);
        session.EndGenerating(current);
        Assert.False(session.IsGenerating);
    }

    [Fact]
    public void IsFor_matches_only_the_plan_the_kept_description_was_stored_for()
    {
        var session = new RouteDescriptionSession();
        Assert.False(session.IsFor("OFP-1"));

        session.TryStore(session.BeginGenerating(), "Briefing.", "OFP-1");

        Assert.True(session.IsFor("OFP-1"));
        Assert.False(session.IsFor("OFP-2"));
        Assert.False(session.IsFor(""));
        session.Clear();
        Assert.False(session.IsFor("OFP-1"));
    }
}

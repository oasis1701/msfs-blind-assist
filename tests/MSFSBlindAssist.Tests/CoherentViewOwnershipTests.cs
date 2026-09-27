// Coherent GT accepts ONE inspector socket per view. CoherentViewOwnership makes that rule
// structural: a persistent client claims its view for its whole lifetime, reconnect gaps
// included, and a one-shot eval is refused on a claimed view. PR #253 review: D / Shift+D
// took the one-shot path whenever the MCDU client was between sockets (a flight reload, the
// first connect), and that one-shot raced the client's own reconnect onto the same view.
//
// The registry is process-wide and xUnit runs test classes in parallel, so every test
// claims a view name of its own.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CoherentViewOwnershipTests
{
    private static string NewView() => "TEST_VIEW_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    [Fact]
    public void A_one_shot_enters_a_view_nobody_claims()
    {
        var view = NewView();
        using var oneShot = CoherentViewOwnership.TryEnterOneShot(view);
        Assert.NotNull(oneShot);
    }

    [Fact]
    public void A_one_shot_is_refused_on_a_claimed_view_whatever_the_case()
    {
        var view = NewView();
        using var claim = CoherentViewOwnership.Claim(view);
        Assert.Null(CoherentViewOwnership.TryEnterOneShot(view.ToLowerInvariant()));
    }

    [Fact]
    public void Releasing_the_claim_admits_one_shots_again()
    {
        var view = NewView();
        CoherentViewOwnership.Claim(view).Dispose();
        using var oneShot = CoherentViewOwnership.TryEnterOneShot(view);
        Assert.NotNull(oneShot);
    }

    [Fact]
    public void A_one_shot_is_in_flight_until_it_is_disposed()
    {
        // A persistent client must not open its socket while a one-shot still holds the view.
        var view = NewView();
        var oneShot = CoherentViewOwnership.TryEnterOneShot(view)!;
        Assert.True(CoherentViewOwnership.OneShotInFlight(view));

        oneShot.Dispose();

        Assert.False(CoherentViewOwnership.OneShotInFlight(view));
    }

    [Fact]
    public void A_view_stays_claimed_until_every_claim_is_released()
    {
        var view = NewView();
        var first = CoherentViewOwnership.Claim(view);
        var second = CoherentViewOwnership.Claim(view);

        first.Dispose();
        Assert.True(CoherentViewOwnership.IsClaimed(view));

        second.Dispose();
        Assert.False(CoherentViewOwnership.IsClaimed(view));
    }

    [Fact]
    public void The_owner_is_told_when_the_last_one_shot_on_its_view_ends()
    {
        // A one-shot that began before the owner claimed the view (D pressed just before the
        // MCDU window first opened) keeps the owner from connecting. It must hear the moment it
        // can, not on its next 2 s reconnect pass — the window says "disconnected" at 2 s.
        var view = NewView();
        var first = CoherentViewOwnership.TryEnterOneShot(view)!;
        var second = CoherentViewOwnership.TryEnterOneShot(view)!;
        int told = 0;
        using var claim = CoherentViewOwnership.Claim(view, onOneShotDone: () => told++);

        first.Dispose();
        Assert.Equal(0, told);

        second.Dispose();
        Assert.Equal(1, told);
    }

    [Fact]
    public void A_released_claim_is_not_told()
    {
        var view = NewView();
        var oneShot = CoherentViewOwnership.TryEnterOneShot(view)!;
        int told = 0;
        CoherentViewOwnership.Claim(view, onOneShotDone: () => told++).Dispose();

        oneShot.Dispose();

        Assert.Equal(0, told);
    }

    [Fact]
    public void Only_the_owner_of_that_view_is_told()
    {
        var view = NewView();
        var oneShot = CoherentViewOwnership.TryEnterOneShot(view)!;
        int told = 0;
        using var claim = CoherentViewOwnership.Claim(NewView(), onOneShotDone: () => told++);

        oneShot.Dispose();

        Assert.Equal(0, told);
    }

    [Fact]
    public void Disposing_a_claim_twice_releases_it_once()
    {
        var view = NewView();
        var first = CoherentViewOwnership.Claim(view);
        var second = CoherentViewOwnership.Claim(view);

        first.Dispose();
        first.Dispose();

        Assert.True(CoherentViewOwnership.IsClaimed(view));
        second.Dispose();
    }
}

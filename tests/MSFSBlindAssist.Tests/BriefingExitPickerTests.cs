using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

// Exit distances: ComfortableExitLeadFeet(140 kt, 90°) ≈ 4,641 ft and (140, 30°) ≈ 4,185 ft;
// (130, 90°) ≈ 4,021 ft; (115, 90°) ≈ 3,173 ft. DistanceFromTouchdownFeet = threshold − 1,000.
public class BriefingExitPickerTests
{
    private static LandingExit Exit(string name, double thresholdFt, double angle = 90, bool vacates = true, string side = "Left") => new()
    {
        TaxiwayName = name,
        DistanceFromThresholdFeet = thresholdFt,
        DistanceFromTouchdownFeet = thresholdFt - 1000.0,
        ExitAngleDegrees = angle,
        ExitType = angle <= 50 ? "High-speed" : angle > 110 ? "End" : "Normal",
        ExitSide = side,
        VacatesRunway = vacates,
    };

    [Fact]
    public void A_heavy_skips_an_exit_it_cannot_slow_down_for()
    {
        var a = Exit("A", 3500);   // 2,500 ft from touchdown — needs 4,641 at 140 kt
        var b = Exit("B", 6500);   // 5,500 ft — reachable
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.Null(choice.NextExit);
        Assert.True(choice.ComfortablyReachable);
    }

    [Fact]
    public void A_lighter_aircraft_takes_the_earlier_exit()
    {
        var a = Exit("A", 4500);   // 3,500 ft from touchdown — enough at 115 kt (3,173), not at 140 (4,641)
        var b = Exit("B", 6500);
        Assert.Same(a, BriefingExitPicker.Pick(new[] { a, b }, 115.0)!.Exit);
        Assert.Same(b, BriefingExitPicker.Pick(new[] { a, b }, 140.0)!.Exit);
    }

    [Fact]
    public void A_high_speed_exit_within_1500ft_beyond_the_first_reachable_wins()
    {
        var a = Exit("A", 6000);            // normal, reachable
        var h = Exit("H", 7200, angle: 30); // high-speed, 1,200 ft further
        var c = Exit("C", 9000);
        var choice = BriefingExitPicker.Pick(new[] { a, h, c }, 140.0)!;

        Assert.Same(h, choice.Exit);
        Assert.Same(c, choice.NextExit);
    }

    [Fact]
    public void A_high_speed_exit_further_than_1500ft_does_not_win()
    {
        var a = Exit("A", 6000);
        var h = Exit("H", 7800, angle: 30); // 1,800 ft further
        var choice = BriefingExitPicker.Pick(new[] { a, h }, 140.0)!;

        Assert.Same(a, choice.Exit);
        Assert.Same(h, choice.NextExit);
    }

    [Fact]
    public void Non_vacating_and_end_exits_are_not_candidates_when_a_usable_one_exists()
    {
        var a = Exit("A", 6000, vacates: false);
        var b = Exit("B", 7000);
        var e = Exit("E", 8000, angle: 130);
        var choice = BriefingExitPicker.Pick(new[] { a, b, e }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.Null(choice.NextExit);   // E is not a candidate, so nothing follows B
    }

    [Fact]
    public void Only_end_exits_vacating_falls_back_to_them()
    {
        var e = Exit("E", 8000, angle: 130);
        Assert.Same(e, BriefingExitPicker.Pick(new[] { e }, 140.0)!.Exit);
    }

    [Fact]
    public void Nothing_reachable_picks_the_furthest_and_flags_it()
    {
        var a = Exit("A", 2000);
        var b = Exit("B", 3000);
        var choice = BriefingExitPicker.Pick(new[] { a, b }, 140.0)!;

        Assert.Same(b, choice.Exit);
        Assert.False(choice.ComfortablyReachable);
        Assert.Null(choice.NextExit);
    }

    [Fact]
    public void No_vacating_exit_returns_null()
    {
        Assert.Null(BriefingExitPicker.Pick(Array.Empty<LandingExit>(), 140.0));
        Assert.Null(BriefingExitPicker.Pick(new[] { Exit("A", 6000, vacates: false) }, 140.0));
    }
}

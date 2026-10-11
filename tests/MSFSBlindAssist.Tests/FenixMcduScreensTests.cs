// Characterization tests for FenixMcduScreens, the per-side screen store behind the Fenix MCDU
// window. Both screens stream on one subscription; only the selected side may reach the window
// (and the announcer), and a side switch must show that side's latest screen at once, or nothing
// if none has arrived yet, never the previous side's screen.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixMcduScreensTests
{
    private static MCDUDisplayData Screen(string title) => new() { Title = title };

    [Fact]
    public void The_left_MCDU_is_selected_at_start()
        => Assert.Equal(FenixMcduSides.Left, new FenixMcduScreens().Selected);

    [Fact]
    public void A_screen_for_the_selected_side_is_shown_at_once()
    {
        var screens = new FenixMcduScreens();
        var fpln = Screen("F-PLN");
        Assert.Same(fpln, screens.Store(FenixMcduSides.Left, fpln));
    }

    [Fact]
    public void A_screen_for_the_other_side_is_kept_but_not_shown()
        => Assert.Null(new FenixMcduScreens().Store(FenixMcduSides.Right, Screen("PERF")));

    [Fact]
    public void Switching_shows_the_other_sides_latest_screen()
    {
        var screens = new FenixMcduScreens();
        screens.Store(FenixMcduSides.Right, Screen("INIT"));
        var perf = Screen("PERF");
        screens.Store(FenixMcduSides.Right, perf);

        Assert.True(screens.TrySelect(FenixMcduSides.Right, out var stored));
        Assert.Same(perf, stored);
        Assert.Equal(FenixMcduSides.Right, screens.Selected);
    }

    [Fact]
    public void Switching_before_that_sides_first_screen_shows_nothing()
    {
        var screens = new FenixMcduScreens();
        screens.Store(FenixMcduSides.Left, Screen("F-PLN"));

        Assert.True(screens.TrySelect(FenixMcduSides.Right, out var stored));
        Assert.Null(stored);
    }

    [Fact]
    public void After_a_switch_only_the_new_side_is_shown()
    {
        var screens = new FenixMcduScreens();
        screens.TrySelect(FenixMcduSides.Right, out _);

        Assert.Null(screens.Store(FenixMcduSides.Left, Screen("F-PLN")));
        var perf = Screen("PERF");
        Assert.Same(perf, screens.Store(FenixMcduSides.Right, perf));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(3)]
    public void Selecting_the_current_side_or_one_the_Fenix_lacks_does_nothing(int side)
    {
        var screens = new FenixMcduScreens();
        screens.Store(FenixMcduSides.Left, Screen("F-PLN"));

        Assert.False(screens.TrySelect(side, out var stored));
        Assert.Null(stored);
        Assert.Equal(FenixMcduSides.Left, screens.Selected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_screen_for_a_side_the_Fenix_lacks_is_dropped(int side)
        => Assert.Null(new FenixMcduScreens().Store(side, Screen("X")));

    [Theory]
    [InlineData(1, "LSK1L", "system.switches.S_CDU1_KEY_LSK1L")]
    [InlineData(2, "A", "system.switches.S_CDU2_KEY_A")]
    public void KeyRef_routes_a_key_to_the_given_side(int side, string key, string expected)
        => Assert.Equal(expected, FenixMcduSides.KeyRef(side, key));
}

using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>
/// The A300 First Officer's quiet, per control (the MD-11's model, design decision 2026-10-10): a write quiets
/// only the row it moved and that row's own lights; only a system test's run quiets every light.
/// </summary>
public class A300FoQuietTests
{
    private readonly A300FoQuiet _quiet = new();

    [Theory]
    [InlineData("A300_OUT_TK_PMP_L1", "A300_LT_OUT_TK_PMP_L1_SEQ1_LIGHT")]   // the pump's low pressure legend
    [InlineData("A300_ENG_2_START", "A300_LT_ENG_2_START_SEQ1_LIGHT")]       // the starter's start valve legend
    [InlineData("A300_EXT_PWR", "A300_LT_EXT_PWR_SEQ1_LIGHT")]               // the button's AVAIL legend
    [InlineData("A300_PACK_1_VALVE", "A300_LT_PACK_1_VALVE_IND_003_LIGHT")]  // the pack's flow bar
    [InlineData("A300_DH_CPT", "A300_LT_DH_CPT_SEQ1_LIGHT")]                // a button its light labels
    [InlineData("A300_NAVATTLEFT", "A300_LT_IRS_1_009_LIGHT")]              // IRS 1's mode panel
    [InlineData("A300_NAVATTRIGHT", "A300_LT_IRS_3_009_LIGHT")]             // IRS 2's panel is node IRS_3
    [InlineData("A300_NAVATTCTR", "A300_LT_IRS_2_009_LIGHT")]               // IRS 3's panel is node IRS_2
    public void A_row_owns_the_lights_on_its_own_button_or_panel(string row, string lamp) =>
        Assert.Contains(lamp, A300FoQuiet.LampsOf(row));

    [Fact]
    public void The_max_autobrake_button_owns_its_armed_light_and_not_its_decel_light()
    {
        var max = A300Autobrake.ByButton["A300_AUTO_BRK_MAX"];
        Assert.Contains(max.ArmedLampId, A300FoQuiet.LampsOf("A300_AUTO_BRK_MAX"));
        Assert.DoesNotContain(max.DecelKey, A300FoQuiet.LampsOf("A300_AUTO_BRK_MAX"));
    }

    [Fact]
    public void Every_owned_light_is_a_board_light()
    {
        foreach (var lamp in A300FoQuiet.AllOwnedLamps)
            Assert.True(A300LampBoard.ById.ContainsKey(lamp), lamp);
    }

    [Fact]
    public void A_write_quiets_its_row_and_its_own_lights_only_for_its_window()
    {
        _quiet.NoteRow("A300_OUT_TK_PMP_L1", 1000);
        Assert.True(_quiet.IsRowQuiet("A300_OUT_TK_PMP_L1", 1000 + A300FoQuiet.RowQuietMs));
        Assert.True(_quiet.IsLampQuiet("A300_LT_OUT_TK_PMP_L1_SEQ1_LIGHT", 1000 + A300FoQuiet.RowQuietMs));
        Assert.False(_quiet.IsLampQuiet("A300_LT_OUT_TK_PMP_L2_SEQ1_LIGHT", 1500));   // another pump's light
        Assert.False(_quiet.IsRowQuiet("A300_OUT_TK_PMP_L2", 1500));
        Assert.False(_quiet.IsLampQuiet("A300_LT_OUT_TK_PMP_L1_SEQ1_LIGHT", 1001 + A300FoQuiet.RowQuietMs));
    }

    [Fact]
    public void Only_a_test_quiets_every_light_and_the_master_lights()
    {
        _quiet.NoteRow("A300_PACK_1_VALVE", 1000);
        Assert.False(_quiet.IsMasterQuiet(1500));
        _quiet.QuietAll(1000, 6000);
        Assert.True(_quiet.IsMasterQuiet(6000));
        Assert.True(_quiet.IsLampQuiet("A300_LT_APU_AVAIL_SEQ1_LIGHT", 6000));
        Assert.False(_quiet.IsRowQuiet("A300_PARKINGBRAKE", 6000));   // a lever is not a light
        Assert.False(_quiet.IsMasterQuiet(7001));
    }

    [Fact]
    public void A_longer_window_is_never_shortened_by_a_later_shorter_one()
    {
        _quiet.QuietAll(0, 10_000);
        _quiet.QuietAll(1000, 2000);
        Assert.True(_quiet.IsMasterQuiet(9_000));
        _quiet.NoteRow("A300_BEACONSWITCH", 0, 8000);
        _quiet.NoteRow("A300_BEACONSWITCH", 1000);
        Assert.True(_quiet.IsRowQuiet("A300_BEACONSWITCH", 7_500));
    }

    [Fact]
    public void Clear_ends_every_window()
    {
        _quiet.NoteRow("A300_BEACONSWITCH", 0);
        _quiet.QuietAll(0, 5000);
        _quiet.Clear();
        Assert.False(_quiet.IsRowQuiet("A300_BEACONSWITCH", 1));
        Assert.False(_quiet.IsMasterQuiet(1));
    }
}

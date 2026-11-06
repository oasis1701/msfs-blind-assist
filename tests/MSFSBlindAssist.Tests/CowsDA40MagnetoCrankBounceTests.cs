using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The XLS ignition key bounces START → BOTH → START every second while <c>STARTER_SPAD:1</c>
/// cranks (measured live at VCBI, 2026-10-20). The definition claims that movement, so the generic
/// announcer never reads it ("Ignition Key: Start. Both. Start."); every other position is still
/// the generic announcer's to speak.
/// </summary>
public class CowsDA40MagnetoCrankBounceTests
{
    private const int Off = 0, Right = 1, Left = 2, Both = 3, Start = 4;

    private static bool Claimed(CowsDA40Definition def, int position)
        => def.ProcessSimVarUpdate("DA40_MAG_KEY", position, null!);

    [Fact]
    public void The_crank_bounce_is_claimed_and_silent()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        Claimed(def, Both);                       // the first reading is not a change

        Assert.True(Claimed(def, Start));
        Assert.True(Claimed(def, Both));
        Assert.True(Claimed(def, Start));
        Assert.True(Claimed(def, Both));          // released: back to BOTH, still the crank's
    }

    [Fact]
    public void Every_other_position_is_still_the_generic_announcers()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        Claimed(def, Off);

        Assert.False(Claimed(def, Right));
        Assert.False(Claimed(def, Left));
        Assert.False(Claimed(def, Both));         // from LEFT, not from START: spoken
        Assert.False(Claimed(def, Off));
    }
}

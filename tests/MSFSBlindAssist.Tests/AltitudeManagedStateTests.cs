// The A380 managed-vs-selected ALTITUDE state, derived from the FMA.
//
// FBW #10855 (a380x 1bbd304, "add FG part to PRIM") deleted the TypeScript FCU that used to
// publish L:A32NX_FCU_ALT_MANAGED and replaced it with a WASM shim that hardcodes the var:
//
//     const bool lvlChManaged = false;                       // FlyByWireInterface.cpp
//     idFcuShimAltManaged->set(lvlChManaged);
//
// so the L:var reads 0 forever. These cases pin the formula the DELETED FCU used
// (fbw-a380x/.../FCU/Managers/AltitudeManager.ts at 1bbd304c4^), which is the authoritative
// definition of what that var meant:
//
//     (verticalArmed & (AltCst|Clb|Des|Gs)) > 0 || MANAGED_MODES.includes(verticalMode)
//
// plus the LAND/FLARE/ROLL OUT rescue via the lateral mode — see AltitudeManagedState for why
// those three cannot arrive on the vertical mode on this build.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class AltitudeManagedStateTests
{
    private const int NoMode = 0;

    // ---- Selected: the FCU's own altitude is being flown ----

    [Theory]
    [InlineData(0)]   // NONE
    [InlineData(10)]  // ALT      — level at the FCU altitude (the live FL360 cruise capture)
    [InlineData(11)]  // ALT*
    [InlineData(12)]  // OP CLB
    [InlineData(13)]  // OP DES
    [InlineData(14)]  // V/S
    [InlineData(15)]  // FPA
    [InlineData(40)]  // SRS
    [InlineData(41)]  // SRS GA
    [InlineData(50)]  // TCAS
    public void Selected_vertical_modes_are_not_managed(int verticalMode)
    {
        Assert.False(AltitudeManagedState.IsManaged(verticalMode, 0, NoMode));
    }

    // ---- Managed: the FMS profile is being flown ----

    [Theory]
    [InlineData(20)]  // ALT CST
    [InlineData(21)]  // ALT CST*
    [InlineData(22)]  // CLB
    [InlineData(23)]  // DES
    [InlineData(24)]  // FINAL
    [InlineData(30)]  // G/S capture
    [InlineData(31)]  // G/S track
    [InlineData(32)]  // LAND
    [InlineData(33)]  // FLARE
    [InlineData(34)]  // ROLL OUT
    public void Managed_vertical_modes_are_managed(int verticalMode)
    {
        Assert.True(AltitudeManagedState.IsManaged(verticalMode, 0, NoMode));
    }

    // ---- Armed half: a managed mode ARMED counts, even from a selected mode ----

    [Theory]
    [InlineData(2)]   // ALT CST armed
    [InlineData(4)]   // CLB armed
    [InlineData(8)]   // DES armed
    [InlineData(16)]  // G/S armed
    public void Managed_armed_modes_are_managed_even_in_a_selected_vertical_mode(int verticalArmed)
    {
        // OP CLB with CLB armed is the everyday case: the pilot is climbing on the FCU
        // altitude but the FMS profile is armed to take over.
        Assert.True(AltitudeManagedState.IsManaged(12, verticalArmed, NoMode));
    }

    [Theory]
    [InlineData(1)]   // ALT armed — the FCU's OWN altitude, not the FMS profile
    [InlineData(32)]  // FINAL armed
    [InlineData(64)]  // TCAS armed
    public void Armed_modes_outside_the_managed_mask_are_not_managed(int verticalArmed)
    {
        Assert.False(AltitudeManagedState.IsManaged(12, verticalArmed, NoMode));
    }

    [Fact]
    public void Armed_bitmask_is_masked_not_compared()
    {
        // ALT armed (1) + CLB armed (4): the CLB bit alone makes it managed.
        Assert.True(AltitudeManagedState.IsManaged(12, 1 | 4, NoMode));
    }

    // ---- The LAND / FLARE / ROLL OUT rescue ----
    //
    // The #10855 shim's vertical-mode chain assigns lateralMode (not verticalMode) in its
    // 32/33/34 branches — a copy-paste of the lateral chain directly above it — so
    // A32NX_FMA_VERTICAL_MODE can NEVER report LAND/FLARE/ROLL OUT and falls to 0 there.
    // Without the rescue the readout flips to "Selected" during the flare and ANNOUNCES it.

    [Theory]
    [InlineData(32)]  // LAND
    [InlineData(33)]  // FLARE
    [InlineData(34)]  // ROLL OUT
    public void Lateral_land_flare_rollout_is_managed_when_the_vertical_mode_has_dropped(int lateralMode)
    {
        Assert.True(AltitudeManagedState.IsManaged(NoMode, 0, lateralMode));
    }

    [Theory]
    [InlineData(10)]  // HDG
    [InlineData(11)]  // TRACK
    [InlineData(20)]  // NAV
    [InlineData(30)]  // LOC capture
    [InlineData(31)]  // LOC track
    [InlineData(40)]  // RWY
    [InlineData(41)]  // RWY track
    [InlineData(50)]  // GA track
    public void Other_lateral_modes_never_make_altitude_managed(int lateralMode)
    {
        // NAV (managed LATERAL guidance) with OP CLB is the case that must stay Selected:
        // lateral and vertical management are independent.
        Assert.False(AltitudeManagedState.IsManaged(12, 0, lateralMode));
    }

    // ---- Spoken text ----

    [Fact]
    public void Text_matches_the_wording_the_dead_var_used()
    {
        Assert.Equal("Managed", AltitudeManagedState.Text(true));
        Assert.Equal("Selected", AltitudeManagedState.Text(false));
    }
}

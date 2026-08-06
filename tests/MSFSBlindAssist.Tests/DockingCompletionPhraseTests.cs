// Characterization tests for the terminal callout of a VERIFIED-good park
// (DockingGuidanceManager.CompletionPhrase) — the one cue that means "you are parked,
// guidance is finished". Two independent facts decide its wording:
//   • deice      — a deice pad is not a gate: no "GSX docking complete.", no alignment claim.
//   • hasGsxStop — the gate carries a real GSX VDGS stop position (ParkingSpot.StopLatitude);
//                  navdata-only gates target the parking-spot centre and keep a plain "Stop."
// The alignment claim is only truthful because the caller reaches this phrase solely from the
// square + on-centerline + in-stop-band branch (see DockingSquareGateTests for that gate).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class DockingCompletionPhraseTests
{
    [Fact]
    public void Gsx_gate_park_claims_the_dock_and_the_alignment()
        => Assert.Equal("GSX docking complete. Aligned with gate. Parking brake.",
                        DockingGuidanceManager.CompletionPhrase(deice: false, hasGsxStop: true));

    [Fact]
    public void Navdata_only_gate_park_is_a_plain_stop_but_still_claims_the_alignment()
        => Assert.Equal("Stop. Aligned with gate. Parking brake.",
                        DockingGuidanceManager.CompletionPhrase(deice: false, hasGsxStop: false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]  // a deice VDGS stop position must NOT promote the pad to a gate
    public void Deice_pad_gets_the_brake_cue_without_any_gate_wording(bool hasGsxStop)
        => Assert.Equal("Stop. Parking brake.",
                        DockingGuidanceManager.CompletionPhrase(deice: true, hasGsxStop));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Every_good_park_ends_on_the_parking_brake_cue(bool deice, bool hasGsxStop)
        => Assert.EndsWith("Parking brake.", DockingGuidanceManager.CompletionPhrase(deice, hasGsxStop));
}

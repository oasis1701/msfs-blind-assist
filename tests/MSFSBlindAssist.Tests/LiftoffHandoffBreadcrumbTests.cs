// Pins the spoken breadcrumb the liftoff auto-handoff plays, and the announcement mute that
// protects it.
//
// WHY THE MUTE EXISTS: nobody pressed a key, so this one phrase is the entire spoken record
// that takeoff assist stopped and hand fly took over. Hand fly's own pitch/bank/heading
// callouts pass their announce gates within a frame of activation and use AnnounceImmediate,
// which INTERRUPTS — so without a mute the breadcrumb is cut off after a syllable.
//
// WHY IT HAD TO SHRINK: the mute was a flat 3500 ms sized for the old, much longer phrase, and
// it lands exactly on rotation. Measured on a live Fenix A320 takeoff (2026-08-25): takeoff
// assist was calling pitch every ~0.5 s through a steady 1°-per-half-second rotation ramp, then
// the handoff opened a 3.504 s hole in which pitch went 11.6° → 12.6° with nothing spoken at
// all. Shortening the phrase is what buys the mute back.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class LiftoffHandoffBreadcrumbTests
{
    [Fact]
    public void ActivatingHandFlyNamesTheModeTheHandoffJustEntered()
    {
        Assert.Equal("Airborne. Hand fly.",
            LiftoffHandoffBreadcrumb.Compose(activatedHandFly: true, quickKeysRegistered: true));
    }

    [Fact]
    public void APreArmedHandFlyIsNotAnnouncedAsIfItJustStarted()
    {
        // The pilot armed hand fly manually on the ground; it never stopped talking. The only
        // news is that the aircraft is airborne and takeoff assist has stood down.
        Assert.Equal("Airborne.",
            LiftoffHandoffBreadcrumb.Compose(activatedHandFly: false, quickKeysRegistered: true));
    }

    [Fact]
    public void AFailedQuickKeyRegistrationIsFoldedIntoTheSameUtterance()
    {
        // It cannot be a separate announcement: the breadcrumb's AnnounceImmediate cancels
        // pending speech on every backend, so a standalone warning would be swallowed and the
        // pilot would never learn the quick-access keys are dead.
        string message = LiftoffHandoffBreadcrumb.Compose(
            activatedHandFly: true, quickKeysRegistered: false);

        Assert.StartsWith("Airborne. Hand fly.", message);
        Assert.Contains("Quick access keys unavailable", message);
        Assert.Contains("H, V, Q", message);
    }

    [Fact]
    public void APreArmedHandFlyNeverCarriesTheQuickKeyWarning()
    {
        // OnHandFlyModeActiveChanged did not fire during this handoff, so the flag describes an
        // earlier registration the pilot already heard about in full when they armed hand fly.
        Assert.Equal("Airborne.",
            LiftoffHandoffBreadcrumb.Compose(activatedHandFly: false, quickKeysRegistered: false));
    }

    [Fact]
    public void TheNormalHandoffMutesCalloutsForFarLessThanASecondAndAHalfOfRotation()
    {
        int grace = LiftoffHandoffBreadcrumb.GraceMsFor(
            activatedHandFly: true, quickKeysRegistered: true);

        // The measured hole was 3504 ms. Anything at or above the old flat value would leave
        // the defect in place.
        Assert.True(grace <= 1500, $"expected a short mute, got {grace} ms");
        Assert.True(grace > 0, "the breadcrumb still has to survive");
    }

    [Fact]
    public void TheWarningVariantKeepsTheLongerMuteItNeeds()
    {
        int normal = LiftoffHandoffBreadcrumb.GraceMsFor(true, quickKeysRegistered: true);
        int warned = LiftoffHandoffBreadcrumb.GraceMsFor(true, quickKeysRegistered: false);

        // Roughly four times the words, and it is a warning the pilot cannot get back. This
        // path is not made worse than it already was.
        Assert.True(warned > normal, "the longer phrase needs the longer mute");
    }

    [Fact]
    public void TheMuteAlwaysMatchesThePhraseThatIsActuallySpoken()
    {
        // A grace sized for a phrase other than the one spoken is how this drifted the first
        // time: the phrase was shortened and the constant was not.
        foreach (bool activated in new[] { true, false })
        foreach (bool keysOk in new[] { true, false })
        {
            string message = LiftoffHandoffBreadcrumb.Compose(activated, keysOk);
            int grace = LiftoffHandoffBreadcrumb.GraceMsFor(activated, keysOk);
            bool carriesWarning = message.Contains("Quick access keys unavailable");

            Assert.Equal(carriesWarning
                ? LiftoffHandoffBreadcrumb.GraceWithWarningMs
                : LiftoffHandoffBreadcrumb.GraceMs, grace);
        }
    }
}

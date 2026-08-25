// Pins the spoken cue the liftoff auto-handoff plays, and the announcement mute that protects
// it — as ONE decision, because they are one decision in the production type too.
//
// WHY THE MUTE EXISTS: nobody pressed a key, so this phrase is the only cue the handoff itself
// speaks. Hand fly's own pitch/bank/heading callouts pass their announce gates within a frame
// of activation and use AnnounceImmediate, which INTERRUPTS — so without a mute the cue is cut
// off after a syllable.
//
// WHY THE PHRASE IS ONE SENTENCE: measured by rendering through SAPI at the Rate = 0 the app
// hardcodes (~206 wpm) and segmenting on the energy envelope, "Airborne. Hand fly." segments as
// [0.11-0.67] [1.56-2.16] — SAPI's inter-sentence pause is ~0.89 s, longer than all the
// phonation in "Hand fly" — so against a 1500 ms mute the second clause was NEVER SPOKEN AT
// ALL. The comma form measures ~1.05 s end to end and fits with headroom down to ~135 wpm. The
// sentence pause, not the word count, is the dominant cost.
//
// WHY THE MUTE HAD TO SHRINK, stated correctly: the flat 3500 ms was NOT a value left behind by
// a shortened phrase — git shows it was introduced already sized for the phrase it shipped with.
// It shrank because it lands on ROTATION, where a multi-second hole in the pitch callouts is
// unaffordable: measured on a live Fenix A320 takeoff (2026-08-25), takeoff assist was calling
// pitch every ~0.5 s through a 1°-per-half-second ramp, then the handoff opened a 3.504 s hole
// in which pitch went 11.6° → 12.6° with nothing spoken. Shrinking the mute required shrinking
// the phrase, which is why the two must stay one decision.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class LiftoffHandoffBreadcrumbTests
{
    [Fact]
    public void ActivatingHandFlyNamesTheModeInASingleSentence()
    {
        var cue = LiftoffHandoffBreadcrumb.For(activatedHandFly: true, quickKeysRegistered: true);

        Assert.Equal("Airborne, hand fly.", cue.Text);
        Assert.Equal(LiftoffHandoffBreadcrumb.GraceMs, cue.GraceMs);
    }

    [Fact]
    public void TheSpokenPhraseIsOneSentenceSoTheSentencePauseCannotEatIt()
    {
        // Guards the actual defect rather than the literal: a second sentence re-introduces
        // SAPI's ~0.89 s inter-sentence pause, which alone is over half the mute.
        foreach (bool keysOk in new[] { true, false })
        {
            string text = LiftoffHandoffBreadcrumb.For(true, keysOk).Text;
            string beforeWarning = text.Replace(LiftoffHandoffBreadcrumb.QuickKeysWarning, "").Trim();

            Assert.Equal(1, beforeWarning.Count(c => c == '.'));
        }
    }

    [Fact]
    public void APreArmedHandFlyIsNotAnnouncedAsIfItJustStarted()
    {
        // The pilot armed hand fly manually on the ground; it never stopped talking. The only
        // news is that the aircraft is airborne.
        var cue = LiftoffHandoffBreadcrumb.For(activatedHandFly: false, quickKeysRegistered: true);

        Assert.Equal("Airborne.", cue.Text);
    }

    [Fact]
    public void AFailedQuickKeyRegistrationIsFoldedIntoTheSameUtterance()
    {
        // It cannot be a separate announcement: the cue's AnnounceImmediate cancels pending
        // speech on every backend, so a standalone warning would be swallowed and the pilot
        // would never learn the quick-access keys are dead.
        var cue = LiftoffHandoffBreadcrumb.For(activatedHandFly: true, quickKeysRegistered: false);

        Assert.StartsWith("Airborne, hand fly.", cue.Text);
        Assert.Contains(LiftoffHandoffBreadcrumb.QuickKeysWarning, cue.Text);
    }

    [Fact]
    public void APreArmedHandFlyNeverCarriesTheQuickKeyWarning()
    {
        // OnHandFlyModeActiveChanged did not fire during this handoff, so the flag describes an
        // earlier registration the pilot already heard about in full when they armed hand fly.
        var cue = LiftoffHandoffBreadcrumb.For(activatedHandFly: false, quickKeysRegistered: false);

        Assert.Equal("Airborne.", cue.Text);
    }

    [Fact]
    public void TheNormalHandoffMutesCalloutsForFarLessThanASecondAndAHalfOfRotation()
    {
        int grace = LiftoffHandoffBreadcrumb.For(true, quickKeysRegistered: true).GraceMs;

        // The measured hole was 3504 ms. Anything at or above the old flat value would leave
        // the defect in place.
        Assert.True(grace <= 1500, $"expected a short mute, got {grace} ms");
        Assert.True(grace > 0, "the cue still has to survive");
    }

    [Fact]
    public void TheWarningVariantKeepsTheLongerMuteItNeeds()
    {
        int normal = LiftoffHandoffBreadcrumb.For(true, quickKeysRegistered: true).GraceMs;
        int warned = LiftoffHandoffBreadcrumb.For(true, quickKeysRegistered: false).GraceMs;

        // Several times the words, and it is a warning the pilot cannot get back. This path is
        // not made worse than it already was.
        Assert.True(warned > normal, "the longer phrase needs the longer mute");
    }

    [Fact]
    public void ThePhraseAndItsMuteComeFromOneDecisionSoTheyCannotDisagree()
    {
        // The whole reason this type exists. One call returns both, so there is no second
        // predicate to leave behind — the previous shape had Compose and GraceMsFor each
        // re-deriving `activatedHandFly && !quickKeysRegistered` by hand.
        foreach (bool activated in new[] { true, false })
        foreach (bool keysOk in new[] { true, false })
        {
            var cue = LiftoffHandoffBreadcrumb.For(activated, keysOk);
            bool carriesWarning = cue.Text.Contains(LiftoffHandoffBreadcrumb.QuickKeysWarning);

            Assert.Equal(carriesWarning
                ? LiftoffHandoffBreadcrumb.GraceWithWarningMs
                : LiftoffHandoffBreadcrumb.GraceMs, cue.GraceMs);
        }
    }

    [Fact]
    public void TheStandaloneWarningAndTheCueShareOneWording()
    {
        // MainForm.Hotkeys.cs speaks this same sentence when a MANUAL arm fails to register the
        // keys. It used to hold a byte-identical copy, so rewording one would have given the
        // pilot two different sentences for one condition depending on how hand fly was armed.
        Assert.Equal("Quick access keys unavailable. Use output mode for H, V, Q.",
            LiftoffHandoffBreadcrumb.QuickKeysWarning);
    }
}

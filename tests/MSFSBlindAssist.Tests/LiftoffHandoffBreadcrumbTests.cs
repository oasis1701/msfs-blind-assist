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
    // Measured by rendering each phrase through System.Speech at Rate = 0 -- the rate
    // ScreenReaderAnnouncer hardcodes for its SAPI fallback, and so the only SAPI rate this app
    // produces -- with trailing silence trimmed off the buffer. These are EVIDENCE, not a
    // words-per-minute estimate: an estimate put "Airborne, hand fly." at ~1.05 s when it is
    // really 1.68 s, and a mute was sized against the estimate.
    private static readonly Dictionary<string, double> MeasuredSeconds = new()
    {
        ["Airborne."]                           = 0.63,
        ["Airborne, hand fly."]                 = 1.68,
        ["Airborne, hand fly, quick keys off."] = 3.00,
    };

    // The callout hole measured on the live Fenix A320 takeoff this area exists to close. No
    // mute may reach it -- that is the ceiling the phrases are written to fit, not a target.
    private const int MeasuredDefectMs = 3504;

    [Fact]
    public void EveryCueIsMutedForLongerThanItTakesToSpeak()
    {
        // THE point of this type. A mute shorter than its own phrase clips the phrase; a mute
        // longer than it needs is a pitch callout the pilot does not get at rotation. Both are
        // the same defect, and both have shipped.
        foreach (bool activated in new[] { true, false })
        foreach (bool keysOk in new[] { true, false })
        {
            var cue = LiftoffHandoffBreadcrumb.For(activated, keysOk);

            Assert.True(MeasuredSeconds.ContainsKey(cue.Text),
                $"unmeasured phrase \"{cue.Text}\" -- render it through SAPI at Rate 0 and add it, " +
                "rather than guessing a mute for it");

            int spokenMs = (int)(MeasuredSeconds[cue.Text] * 1000);
            Assert.True(cue.GraceMs >= spokenMs,
                $"\"{cue.Text}\" takes {spokenMs} ms to say but is muted for only {cue.GraceMs} ms");
        }
    }

    [Fact]
    public void NoCueIsMutedForAnythingLikeTheHoleThisAreaExistsToClose()
    {
        foreach (bool activated in new[] { true, false })
        foreach (bool keysOk in new[] { true, false })
        {
            var cue = LiftoffHandoffBreadcrumb.For(activated, keysOk);

            Assert.True(cue.GraceMs <= MeasuredDefectMs,
                $"\"{cue.Text}\" mutes callouts for {cue.GraceMs} ms, at or past the {MeasuredDefectMs} ms " +
                "hole that was reported as the defect -- shorten the phrase, do not widen the mute");
        }
    }

    [Fact]
    public void NoCueWastesMoreThanHalfAgainOfItsOwnSpeech()
    {
        // The other direction: muting far past the phrase is silence bought for nothing. The
        // pre-armed cue used to hold a 1500 ms floor for 0.63 s of speech.
        foreach (bool activated in new[] { true, false })
        foreach (bool keysOk in new[] { true, false })
        {
            var cue = LiftoffHandoffBreadcrumb.For(activated, keysOk);
            int spokenMs = (int)(MeasuredSeconds[cue.Text] * 1000);

            Assert.True(cue.GraceMs <= spokenMs * 1.5,
                $"\"{cue.Text}\" takes {spokenMs} ms but is muted for {cue.GraceMs} ms — " +
                "the excess is pitch callouts the pilot loses at rotation for no phrase");
        }
    }

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

            Assert.Equal(1, text.Count(c => c == '.'));
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

        Assert.StartsWith("Airborne, hand fly", cue.Text);
        Assert.Contains(LiftoffHandoffBreadcrumb.QuickKeysShortWarning, cue.Text);
    }

    [Fact]
    public void TheRotationWarningIsTheShortFormNotTheFullSentence()
    {
        // The full wording measures 8.15 s and could only be delivered by muting the pitch
        // callouts for more than twice the hole this area exists to close. The short form is
        // what fits the budget; the full one still reaches the pilot through the unbounded,
        // queued MainForm.Hotkeys path when they re-arm hand fly.
        var cue = LiftoffHandoffBreadcrumb.For(activatedHandFly: true, quickKeysRegistered: false);

        Assert.DoesNotContain(LiftoffHandoffBreadcrumb.QuickKeysWarning, cue.Text);
        Assert.DoesNotContain("H, V, Q", cue.Text);
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
    public void TheNormalHandoffMutesCalloutsForWellUnderTheMeasuredHole()
    {
        int grace = LiftoffHandoffBreadcrumb.For(true, quickKeysRegistered: true).GraceMs;

        // Not an arbitrary bound: the phrase measures 1.68 s, so a mute under ~1700 ms clips it
        // (1500 ms did), while 3504 ms is the hole that was reported as the defect.
        Assert.True(grace >= 1680, $"the phrase takes 1.68 s to say, mute is only {grace} ms");
        Assert.True(grace < MeasuredDefectMs, $"expected well under the measured hole, got {grace} ms");
    }

    [Fact]
    public void ThePreArmedCueIsMutedForLessThanTheOthers()
    {
        // It is one word (0.63 s) AND it is the path where hand fly was already speaking, so
        // every extra millisecond costs a callout the pilot would otherwise have had.
        int preArmed = LiftoffHandoffBreadcrumb.For(false, quickKeysRegistered: true).GraceMs;
        int normal = LiftoffHandoffBreadcrumb.For(true, quickKeysRegistered: true).GraceMs;

        Assert.True(preArmed < normal, "the shortest phrase must not hold the longest mute");
    }

    [Fact]
    public void TheWarningVariantKeepsTheLongerMuteItNeeds()
    {
        int normal = LiftoffHandoffBreadcrumb.For(true, quickKeysRegistered: true).GraceMs;
        int warned = LiftoffHandoffBreadcrumb.For(true, quickKeysRegistered: false).GraceMs;

        // More words, and a warning the pilot cannot get back at this moment. This path is not
        // made worse than it already was: the mute is unchanged and the phrase was shortened to
        // fit inside it.
        Assert.True(warned > normal, "the longer phrase needs the longer mute");
        Assert.Equal(3500, warned);
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
            bool carriesWarning = cue.Text.Contains(LiftoffHandoffBreadcrumb.QuickKeysShortWarning);

            int expected = carriesWarning ? LiftoffHandoffBreadcrumb.GraceWithWarningMs
                : activated ? LiftoffHandoffBreadcrumb.GraceMs
                : LiftoffHandoffBreadcrumb.GracePreArmedMs;

            Assert.Equal(expected, cue.GraceMs);
        }
    }

    [Fact]
    public void TheStandaloneWarningKeepsTheFullWordingItCanAfford()
    {
        // MainForm.Hotkeys.cs speaks this when a MANUAL arm fails to register the keys. That
        // path is queued and nothing is racing it, so it keeps the remedy and the key names --
        // the rotation cue cannot, and the two live together here so they cannot come to
        // describe different conditions.
        Assert.Equal("Quick access keys unavailable. Use output mode for H, V, Q.",
            LiftoffHandoffBreadcrumb.QuickKeysWarning);
    }
}

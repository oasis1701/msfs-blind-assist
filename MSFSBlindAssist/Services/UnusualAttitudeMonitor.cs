using System;

namespace MSFSBlindAssist.Services;

/// <summary>
/// THE ATTITUDE NOBODY ASKED ABOUT.
///
/// ⚠️ THIS EXISTS BECAUSE A SPIRAL DIVE KILLED AN AEROPLANE WHILE EVERY INSTRUMENT MSFSBA
/// OFFERS WAS WORKING PERFECTLY. Live, 2026-09-02: a DA40 rolled into a 65-degree bank at
/// 3,200 ft and flew into the ground in under a minute. Hand fly mode was ACTIVE, so the bank
/// tone was sounding. The pilot pressed a quick-access readout key TWELVE TIMES IN THIRTEEN
/// SECONDS — the signature of somebody who can feel that something is wrong and is hunting for
/// what — and not one thing in the app ever said the word "bank".
///
/// EVERY ATTITUDE CHANNEL HERE IS A QUERY, AND THAT IS THE DEFECT. The bank tone is a steering
/// aid for a manoeuvre you MEANT to make; the B key answers a question you already knew to ask.
/// Both are useless for the case that matters, which is the attitude you have not noticed —
/// and a steep bank is the least noticeable dangerous state in flying. Held coordinated it
/// feels like sitting upright. The only cues are the altimeter unwinding and the speed
/// building, and a blind pilot has to go and ASK for both.
///
/// A sighted pilot does not ask. A 65-degree bank seizes their attention off the attitude
/// indicator whether they were scanning or not. That is exactly the bar this codebase already
/// sets for interrupting somebody — announce what would interrupt a sighted pilot too — so this
/// interrupts.
///
/// ⚠️ IT ANNOUNCES THE RECOVERY AS WELL, which is a deliberate exception to the house rule that
/// a cleared fault stays silent. That rule is right for a lamp: the pilot flicked the switch and
/// already knows. It is wrong here, because "am I level yet?" is the entire question a pilot
/// rolling out of an unnoticed bank is asking, and it is the one they cannot answer without
/// letting go of something to press a key.
/// </summary>
public static class UnusualAttitudeMonitor
{
    /// <summary>
    /// Beyond a normal manoeuvre. A rate-one turn is 15-25 degrees and a deliberate steep turn
    /// is 45, so 45 is the boundary between "flying" and "something has got away from you".
    /// </summary>
    public const double BankAlertDeg = 45;

    /// <summary>
    /// Hysteresis, so an aeroplane sitting on the threshold does not chatter. Recovery is
    /// called at 20 degrees rather than at wings level: a pilot rolling out wants to know they
    /// are back inside normal manoeuvring, not to be held to a millimetre.
    /// </summary>
    public const double BankClearDeg = 20;

    public const double PitchUpAlertDeg = 20;
    public const double PitchDownAlertDeg = -15;
    public const double PitchClearDeg = 10;

    /// <summary>How much worse it has to get before it is worth saying again.</summary>
    public const double WorseningStepDeg = 20;

    /// <summary>What one sample means once compared with what was last said.</summary>
    public readonly struct Verdict
    {
        /// <summary>What to say, or empty for silence.</summary>
        public string Message { get; init; }
        /// <summary>The state to carry into the next sample.</summary>
        public State Next { get; init; }
    }

    /// <summary>Carried between samples. Default is "nothing said, nothing wrong".</summary>
    public readonly struct State
    {
        public bool BankAlerted { get; init; }
        public double BankSpokenDeg { get; init; }
        public bool PitchAlerted { get; init; }
        public double PitchSpokenDeg { get; init; }
    }

    /// <summary>
    /// ⚠️ BANK IS LEFT-POSITIVE out of SimConnect — <c>PLANE BANK DEGREES</c> reports a RIGHT
    /// bank as a NEGATIVE number, which is the opposite of every tone API in this codebase and
    /// has caught this project before. The caller passes the raw SimVar and the naming is done
    /// here, once, so no call site has to remember it.
    /// </summary>
    public static Verdict Evaluate(double bankDegLeftPositive, double pitchDeg,
                                   bool onGround, State state)
    {
        // On the ground an attitude reading is the scenery, not the aeroplane. A parked
        // aircraft on a slope must never be told it is in an unusual attitude.
        if (onGround) return new Verdict { Message = "", Next = default };

        double bankMagnitude = Math.Abs(bankDegLeftPositive);
        bool bankRight = bankDegLeftPositive < 0;

        // ---- bank, which is the one that kills ------------------------------------------
        if (bankMagnitude >= BankAlertDeg)
        {
            bool onset = !state.BankAlerted;
            bool worse = state.BankAlerted &&
                         bankMagnitude >= state.BankSpokenDeg + WorseningStepDeg;

            if (onset || worse)
            {
                return new Verdict
                {
                    // The DIRECTION first, because it is the half that tells the pilot which
                    // way to move the stick, and the number second.
                    Message = $"Bank {(bankRight ? "right" : "left")} {bankMagnitude:0} degrees.",
                    Next = new State
                    {
                        BankAlerted = true,
                        BankSpokenDeg = bankMagnitude,
                        PitchAlerted = state.PitchAlerted,
                        PitchSpokenDeg = state.PitchSpokenDeg
                    }
                };
            }
        }
        else if (state.BankAlerted && bankMagnitude <= BankClearDeg)
        {
            return new Verdict
            {
                Message = "Wings level.",
                Next = new State
                {
                    BankAlerted = false,
                    BankSpokenDeg = 0,
                    PitchAlerted = state.PitchAlerted,
                    PitchSpokenDeg = state.PitchSpokenDeg
                }
            };
        }

        // ---- pitch, checked second so a spiral reports the bank first --------------------
        bool pitchOut = pitchDeg >= PitchUpAlertDeg || pitchDeg <= PitchDownAlertDeg;
        if (pitchOut)
        {
            bool onset = !state.PitchAlerted;
            bool worse = state.PitchAlerted &&
                         Math.Abs(pitchDeg) >= Math.Abs(state.PitchSpokenDeg) + WorseningStepDeg;

            if (onset || worse)
            {
                return new Verdict
                {
                    Message = $"Pitch {(pitchDeg > 0 ? "up" : "down")} {Math.Abs(pitchDeg):0} degrees.",
                    Next = new State
                    {
                        BankAlerted = state.BankAlerted,
                        BankSpokenDeg = state.BankSpokenDeg,
                        PitchAlerted = true,
                        PitchSpokenDeg = pitchDeg
                    }
                };
            }
        }
        else if (state.PitchAlerted && Math.Abs(pitchDeg) <= PitchClearDeg)
        {
            return new Verdict
            {
                Message = "Pitch normal.",
                Next = new State
                {
                    BankAlerted = state.BankAlerted,
                    BankSpokenDeg = state.BankSpokenDeg,
                    PitchAlerted = false,
                    PitchSpokenDeg = 0
                }
            };
        }

        return new Verdict { Message = "", Next = state };
    }
}

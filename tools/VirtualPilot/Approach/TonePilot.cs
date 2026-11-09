using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;

namespace VirtualPilot.Approach;

/// <summary>
/// A pilot who flies only by what visual guidance gives a blind pilot: the two tones and the bank
/// callouts. Each tone's pan (bank) or pitch (frequency) pins at the profile's range, so past it
/// the pilot hears only "further that way" until the current tone pins too and the two sound
/// alike; a bank callout ("3 right", "matched") says how far. The pilot acts after a reaction
/// delay, rolls and pitches at limited rates, and cannot hear a difference under about half a
/// degree of bank or a tenth of a degree of pitch.
///
/// With <c>bankBySpeech</c> the pilot steers the bank by the callouts alone, as a pilot who leans
/// on speech: the latest bank callout of the last 2 s, and with none it holds its bank. The pitch
/// still follows the tone.
///
/// A gust (<see cref="Knock"/>) rolls the aircraft at once, but the pilot learns of it only
/// through what it hears after it, a reaction delay later: until then it steers the bank it
/// perceives, the actual bank less the knock it has not yet heard.
///
/// During a scripted turn (<see cref="ApproachScriptedTurns"/>) the pilot flies the turn's bank
/// instead of the tones; the pitch still follows the tone. During a scripted descent
/// (<see cref="ApproachVerticalScript"/>) the pilot flies the script's pitch instead of the pitch
/// tone; the bank still follows the tone.
/// </summary>
sealed class TonePilot
{
    private const double CalloutMemoryS = 2.0;
    private const double BankDeadbandDeg = 0.5, PitchDeadbandDeg = 0.1;
    private const double BankTimeConstantS = 0.5, PitchTimeConstantS = 0.7;

    private static readonly Regex BankErrorCallout = new(@"^(\d+) (left|right)$", RegexOptions.CultureInvariant);
    // "Matched, level" was "On commanded path" before 2026-11-06; both stay, for runs against an older app.
    private static readonly Regex BankMatchedCallout = new(@"^(matched|Matched, level|On commanded path)$", RegexOptions.CultureInvariant);

    private readonly double _rollRateDegS, _pitchRateDegS, _bankRangeDeg, _pitchRangeDeg, _reactionDelayS;
    private readonly bool _bankBySpeech;
    private double _bank, _pitch;
    private bool _pitchFrozen;
    private readonly double _startBank;
    // Each hearing carries the knock total at that moment; acting on it, the pilot has perceived that much.
    private readonly Queue<(double T, double Bank, double Pitch, double Knock)> _heard = new();
    private (double Bank, double Pitch)? _acting;
    private double _knockTotal, _perceivedKnock;
    private readonly List<(double T, double Bank)> _bankHistory = new();

    public TonePilot(ApproachAircraftKind kind, VisualGuidanceProfile profile, double reactionDelayS, double startBankStd, double startPitch, bool bankBySpeech = false)
    {
        var spec = ApproachAircraftSpecs.For(kind);
        _rollRateDegS = spec.RollRateDegS;
        _pitchRateDegS = spec.PitchRateDegS;
        _bankRangeDeg = profile.ToneBankRangeDeg;
        _pitchRangeDeg = profile.TonePitchRangeDeg;
        _reactionDelayS = reactionDelayS;
        _bankBySpeech = bankBySpeech;
        _bank = _startBank = startBankStd;
        _pitch = startPitch;
    }

    /// <summary>A gust adds <paramref name="bankDeltaDeg"/> (right positive) to the bank; the pilot reacts a reaction delay later.</summary>
    public void Knock(double bankDeltaDeg)
    {
        _bank += bankDeltaDeg;
        _knockTotal += bankDeltaDeg;
    }

    /// <summary>From now on the pitch stays at <paramref name="pitchDeg"/> (the self-feedback step test); the bank keeps following.</summary>
    public void FreezePitch(double pitchDeg) { _pitchFrozen = true; _pitch = pitchDeg; }

    /// <summary>One time step: hears the commands at <paramref name="t"/>, acts on what it heard a reaction delay ago.</summary>
    public (double BankStd, double Pitch) Step(double t, double dt, double desiredBankStd, double desiredPitch,
        IReadOnlyList<(double T, string Text, bool Immediate)> speech, double? scriptedBankStd = null, double? scriptedPitch = null)
    {
        _bankHistory.Add((t, _bank));
        _heard.Enqueue((t, BankTarget(t, desiredBankStd, speech), AxisTarget(_pitch, desiredPitch, _pitchRangeDeg), _knockTotal));
        while (_heard.Count > 0 && _heard.Peek().T <= t - _reactionDelayS + 1e-9)
        {
            var h = _heard.Dequeue();
            _acting = (h.Bank, h.Pitch);
            _perceivedKnock = h.Knock;
        }

        if (_acting is { } target)
        {
            // Without a knock still unheard, the same arithmetic as before gusts existed, to the bit.
            double unheard = _knockTotal - _perceivedKnock;
            if (scriptedBankStd == null)
                _bank = unheard == 0
                    ? Approach(_bank, target.Bank, BankDeadbandDeg, BankTimeConstantS, _rollRateDegS, dt)
                    : Approach(_bank - unheard, target.Bank, BankDeadbandDeg, BankTimeConstantS, _rollRateDegS, dt) + unheard;
            if (!_pitchFrozen && scriptedPitch == null) _pitch = Approach(_pitch, target.Pitch, PitchDeadbandDeg, PitchTimeConstantS, _pitchRateDegS, dt);
        }
        // A scripted descent (ApproachVerticalScript, a pilot obeying ATC's "descend and maintain"):
        // the pilot pitches to the script's pitch at its usual rate, with no reaction delay, whatever
        // the pitch tone says. It keeps hearing the tones, so when the script ends it acts on them.
        if (scriptedPitch is double scriptedP && !_pitchFrozen)
            _pitch = Approach(_pitch, scriptedP, PitchDeadbandDeg, PitchTimeConstantS, _pitchRateDegS, dt);
        // A scripted turn (ApproachScriptedTurns, a pilot obeying ATC): the pilot rolls to the bank the
        // turn needs at its usual rate, with no reaction delay (it is turning by its own heading), whatever
        // the tones say. It keeps hearing them, so when the turn is over it acts on what they said.
        if (scriptedBankStd is double scripted)
            _bank = Approach(_bank, scripted, BankDeadbandDeg, BankTimeConstantS, _rollRateDegS, dt);
        return (_bank, _pitch);
    }

    private static double Approach(double value, double target, double deadband, double timeConstantS, double maxRate, double dt)
    {
        double error = target - value;
        if (Math.Abs(error) < deadband) return value;
        return value + Math.Clamp(error / timeConstantS, -maxRate, maxRate) * dt;
    }

    private double BankTarget(double t, double desired, IReadOnlyList<(double T, string Text, bool Immediate)> speech)
    {
        if (_bankBySpeech) return CalloutTarget(t, speech) ?? _bank;
        if (Math.Abs(desired) < _bankRangeDeg) return desired;
        return CalloutTarget(t, speech) ?? AxisTarget(_bank, desired, _bankRangeDeg);
    }

    // The bank the latest bank callout of the last 2 s asks for ("3 right": 3° right of the bank
    // when it was spoken; "matched": that bank), or null with none.
    private double? CalloutTarget(double t, IReadOnlyList<(double T, string Text, bool Immediate)> speech)
    {
        for (int i = speech.Count - 1; i >= 0; i--)
        {
            var line = speech[i];
            if (line.T > t) continue;
            if (line.T < t - CalloutMemoryS) break;
            var m = BankErrorCallout.Match(line.Text);
            if (m.Success)
            {
                double n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return BankAt(line.T) + (m.Groups[2].Value == "left" ? -n : n);
            }
            if (BankMatchedCallout.IsMatch(line.Text)) return BankAt(line.T);
        }
        return null;
    }

    // Past the range the tone only says "further that way": the pilot moves until the current
    // tone pins too, then the two sound alike and they hold.
    private static double AxisTarget(double current, double desired, double range)
    {
        if (Math.Abs(desired) < range) return desired;
        if (Math.Clamp(current, -range, range) == Math.Clamp(desired, -range, range)) return current;
        return Math.Sign(desired) * range;
    }

    // The bank at the last step at or before `time` (the history is in time order).
    private double BankAt(double time)
    {
        int lo = 0, hi = _bankHistory.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_bankHistory[mid].T <= time) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found < 0 ? _startBank : _bankHistory[found].Bank;
    }
}

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
/// </summary>
sealed class TonePilot
{
    private const double CalloutMemoryS = 2.0;
    private const double BankDeadbandDeg = 0.5, PitchDeadbandDeg = 0.1;
    private const double BankTimeConstantS = 0.5, PitchTimeConstantS = 0.7;

    private static readonly Regex BankErrorCallout = new(@"^(\d+) (left|right)$", RegexOptions.CultureInvariant);
    private static readonly Regex BankMatchedCallout = new(@"^(matched|On commanded path)$", RegexOptions.CultureInvariant);

    private readonly double _rollRateDegS, _pitchRateDegS, _bankRangeDeg, _pitchRangeDeg, _reactionDelayS;
    private double _bank, _pitch;
    private bool _pitchFrozen;
    private readonly double _startBank;
    private readonly Queue<(double T, double Bank, double Pitch)> _heard = new();
    private (double Bank, double Pitch)? _acting;
    private readonly List<(double T, double Bank)> _bankHistory = new();

    public TonePilot(ApproachAircraftKind kind, VisualGuidanceProfile profile, double reactionDelayS, double startBankStd, double startPitch)
    {
        var spec = ApproachAircraftSpecs.For(kind);
        _rollRateDegS = spec.RollRateDegS;
        _pitchRateDegS = spec.PitchRateDegS;
        _bankRangeDeg = profile.ToneBankRangeDeg;
        _pitchRangeDeg = profile.TonePitchRangeDeg;
        _reactionDelayS = reactionDelayS;
        _bank = _startBank = startBankStd;
        _pitch = startPitch;
    }

    /// <summary>From now on the pitch stays at <paramref name="pitchDeg"/> (the self-feedback step test); the bank keeps following.</summary>
    public void FreezePitch(double pitchDeg) { _pitchFrozen = true; _pitch = pitchDeg; }

    /// <summary>One time step: hears the commands at <paramref name="t"/>, acts on what it heard a reaction delay ago.</summary>
    public (double BankStd, double Pitch) Step(double t, double dt, double desiredBankStd, double desiredPitch,
        IReadOnlyList<(double T, string Text, bool Immediate)> speech)
    {
        _bankHistory.Add((t, _bank));
        _heard.Enqueue((t, BankTarget(t, desiredBankStd, speech), AxisTarget(_pitch, desiredPitch, _pitchRangeDeg)));
        while (_heard.Count > 0 && _heard.Peek().T <= t - _reactionDelayS + 1e-9)
        {
            var h = _heard.Dequeue();
            _acting = (h.Bank, h.Pitch);
        }

        if (_acting is { } target)
        {
            _bank = Approach(_bank, target.Bank, BankDeadbandDeg, BankTimeConstantS, _rollRateDegS, dt);
            if (!_pitchFrozen) _pitch = Approach(_pitch, target.Pitch, PitchDeadbandDeg, PitchTimeConstantS, _pitchRateDegS, dt);
        }
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
        if (Math.Abs(desired) < _bankRangeDeg) return desired;

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
        return AxisTarget(_bank, desired, _bankRangeDeg);
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

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The FlyByWire A380X's thrust lock (the FMA's amber THR LK, the PFD's MOVE THR LEVERS), as FBW
/// #10940 (a380x 3b3c5dd1d, 6 Nov 2026) drives it: each engine's FADEC reports "memo thrust active"
/// in bit 12 of its ECU maintenance word 6, and the E/WD, the PFD's FMA and the FWS's ENG THRUST
/// LOCKED alert all OR the four words (EWD.tsx, FMA.tsx, FwsCore.ts).
/// </summary>
/// <remarks>
/// ⚠️ Not <c>L:A32NX_AUTOTHRUST_MODE_MESSAGE == 1</c>. On the A380 that var is written by the FG
/// shim (<c>FlyByWireInterface.cpp</c>, <c>athrModeMessage</c>), which only ever writes 0, 3 (LVR
/// CLB), 4 (LVR MCT) or 5 (LVR ASYM): the "Thrust Lock" entry of its description table could never
/// be reached, and FBW's own FWS read the same dead value until #10940. On a build older than
/// #10940 (and on the MSFS 2020 build) the ECU words do not exist: they read 0, which carries no
/// data, so the lock simply never reports.
/// </remarks>
public static class A380ThrustLock
{
    public const int EngineCount = 4;

    /// <summary>ECU maintenance word 6 bit 12: memo thrust active (the thrust is locked).</summary>
    public const int Bit = 12;

    /// <summary>The Ctrl+M row that owns the call-out, and the var whose description table words it.</summary>
    public const string MessageRow = "A32NX_AUTOTHRUST_MODE_MESSAGE";

    /// <summary>THR LK in the autothrust message enum (FBW's AutoThrustModeMessage.ThrustLock).</summary>
    public const int ThrustLockMessage = 1;

    /// <summary>The L:var, and the definition key, of one engine's ECU maintenance word 6.</summary>
    public static string WordKey(int engine) => $"A32NX_ECU_{engine}_MAINTENANCE_WORD_6";

    /// <summary>Whether <paramref name="key"/> is one of the four words, and which engine's.</summary>
    public static bool TryEngine(string key, out int engine)
    {
        for (engine = 1; engine <= EngineCount; engine++)
            if (key == WordKey(engine)) return true;
        engine = 0;
        return false;
    }

    /// <summary>Whether one word reports the lock. A word with no data (an unpowered FADEC, a build
    /// without the var) reports none, as FBW's <c>bitValueOr(12, false)</c> does.</summary>
    public static bool IsLocked(double rawWord) => new Arinc429Word(rawWord).BitValueOr(Bit, false);

    /// <summary>Whether any engine reports the lock, reading each word through <paramref name="read"/>.</summary>
    public static bool AnyLocked(Func<string, double?> read)
    {
        for (int engine = 1; engine <= EngineCount; engine++)
            if (IsLocked(read(WordKey(engine)) ?? 0)) return true;
        return false;
    }

    /// <summary>What the FMA's message cell shows: THR LK wins over every lever message
    /// (<c>computeA3Message</c> in the PFD's FMADefinitions.ts tests it first).</summary>
    public static int EffectiveMessage(bool locked, int shimMessage) => locked ? ThrustLockMessage : shimMessage;

    /// <summary>
    /// Follows the four words and reports the edges of the lock. Baseline first: nothing is
    /// reported until all four engines have delivered a word, so an app connected while the
    /// thrust is locked does not speak it as news. <see cref="Reset"/> belongs only where every
    /// word is about to be delivered again (the definition's OnVariableCacheCleared): wiped
    /// anywhere else, the tracker takes the next lock for its baseline and never reports it.
    /// </summary>
    public sealed class Tracker
    {
        private readonly bool[] _locked = new bool[EngineCount];
        private int _seen;
        private bool? _state;

        /// <summary>Whether the thrust is locked, as far as the words delivered so far say.</summary>
        public bool Locked => _state == true;

        /// <summary>
        /// Takes one engine's word. Returns true when the lock has just set, false when it has just
        /// released, and null when there is nothing to say (no change, or still gathering the baseline).
        /// </summary>
        public bool? Observe(int engine, double rawWord)
        {
            if (engine < 1 || engine > EngineCount) return null;
            _locked[engine - 1] = IsLocked(rawWord);
            _seen |= 1 << (engine - 1);
            if (_seen != (1 << EngineCount) - 1) return null;

            bool locked = Array.IndexOf(_locked, true) >= 0;
            bool? previous = _state;
            _state = locked;
            return previous.HasValue && previous.Value != locked ? locked : null;
        }

        /// <summary>Forgets the words and the baseline. Only for a cleared variable cache (see the class summary).</summary>
        public void Reset()
        {
            Array.Clear(_locked);
            _seen = 0;
            _state = null;
        }
    }
}

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The take-off roll call-outs (<see cref="A300TakeoffCallouts"/>). The machine's ARM is dropped on
/// every context reset and on a reconnect, never the speeds, exactly as the MD-11, the A380 and the
/// iFly drop theirs: a flight load delivers the per-frame airspeed ahead of the 1 Hz SIM_ON_GROUND,
/// so an arm kept from a parked aircraft would call V1, Rotate and V2 at the loaded cruise.
/// </summary>
public partial class IniA300Definition
{
    private readonly TakeoffVSpeedCallouts _takeoffCallouts = new();

    // The last SIM_ON_GROUND sample. Starts true: a ramp start is the norm, and an airborne start is
    // harmless, because the machine arms only on a sample below 40 knots, which the air never delivers.
    private bool _calloutOnGround = true;

    // The raw VR and FCU speed, so V2 can be judged again whenever either changes.
    private double _rawVr, _rawFcuSpeed;

    // V1 and VR's entry read-backs, baseline-first and seeded after a load as the altitude window's is.
    private readonly Dictionary<string, A300WindowTracker> _vSpeedEntries = A300TakeoffCallouts.Speeds
        .Where(s => A300TakeoffCallouts.IsSpoken(s.Key))
        .ToDictionary(s => s.Key, _ => new A300WindowTracker(), StringComparer.Ordinal);

    public override string? TakeoffCalloutFeedKey => A300TakeoffCallouts.IasKey;

    public override bool TakeoffCalloutFeedNeeded => _takeoffCallouts.NeedsSamples(_calloutOnGround);

    /// <summary>
    /// The three speeds ride the batch, each with the Ctrl+M row that mutes its entry read-back and its
    /// call (consumed here: <see cref="TryHandleTakeoffCallouts"/> speaks V1 and VR). They stay in
    /// "number": a batched L:var is read in its registered unit, and a speed unit would convert the
    /// FMS's plain knots. Airspeed streams per SIM_FRAME on its own subscription: the 1 Hz batch would
    /// call "Rotate" up to a second late.
    /// </summary>
    private static void RegisterTakeoffCallouts(Dictionary<string, SimVarDefinition> vars, HashSet<string> batchNames)
    {
        foreach (var (key, var, name) in A300TakeoffCallouts.Speeds)
        {
            var def = new SimVarDefinition
            {
                Name = var,
                DisplayName = name,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
            };
            batchNames.Add(ContinuousBatchLayout.FullName(def));
            vars[key] = def;
        }
        vars[A300TakeoffCallouts.IasKey] = new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Take-off callout airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true,
            HighFrequency = true,
            ExcludeFromMonitorManager = true,
        };
    }

    /// <summary>
    /// Feeds the machine; true when the delivery is consumed. SIM_ON_GROUND is only peeked, so the base
    /// still sees it. The calls one sample crosses are ONE AnnounceImmediate ("V1, Rotate"): an action
    /// cue's value is its timing, and spoken one by one the second would cut the first off.
    /// AnnounceImmediate bypasses MainForm's wrap, so the mute and the suppression are applied here.
    /// </summary>
    private bool TryHandleTakeoffCallouts(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        switch (varName)
        {
            case A300TakeoffCallouts.IasKey:
                var callouts = _takeoffCallouts.ProcessSample(value, _calloutOnGround);
                if (callouts.Count > 0 && !announcer.Suppressed
                    && TakeoffVSpeedCallouts.Compose(callouts, IsCalloutMuted) is string sentence)
                    announcer.AnnounceImmediate(sentence);
                return true;
            case "SIM_ON_GROUND":
                _calloutOnGround = value >= 0.5;
                return false;
            case A300TakeoffCallouts.V1Key:
                _takeoffCallouts.SetV1(value);
                SpeakEntry(varName, value, announcer);
                return true;
            case A300TakeoffCallouts.VrKey:
                _rawVr = value;
                _takeoffCallouts.SetVR(value);
                _takeoffCallouts.SetV2(A300TakeoffCallouts.PlausibleV2(_rawVr, _rawFcuSpeed));
                SpeakEntry(varName, value, announcer);
                return true;
            case A300TakeoffCallouts.V2Key:
                _rawFcuSpeed = value;
                _takeoffCallouts.SetV2(A300TakeoffCallouts.PlausibleV2(_rawVr, _rawFcuSpeed));
                return true;
        }
        return false;
    }

    /// <summary>"V1: 148 knots" as the pilot enters a speed, and "not set" when an FMS reset clears it.
    /// Spoken here, inside MainForm's mute wrap, and checked against the speed's own row as the lights
    /// are, so muting V1 silences its read-back and its call together.</summary>
    private void SpeakEntry(string key, double value, ScreenReaderAnnouncer announcer)
    {
        if (_seedGate.Armed)
            _seedGate.NoteValue(key, value, ownedByAircraft: IsAircraftOwned(key));
        if (_vSpeedEntries[key].Observe(A300TakeoffCallouts.Entry(key, value), Clock()) is string phrase
            && !announcer.Suppressed && !IsMuted(key))
            announcer.Announce(phrase);
    }

    private bool IsCalloutMuted(string callout) =>
        A300TakeoffCallouts.Keys.MuteKeyFor(callout) is { Length: > 0 } row && IsMuted(row);

    /// <summary>A reconnect: the roll's arm goes, the speeds stay.</summary>
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        _takeoffCallouts.Reset();
    }
}

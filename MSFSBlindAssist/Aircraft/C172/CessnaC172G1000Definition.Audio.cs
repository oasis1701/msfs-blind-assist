using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Radios → Audio: the GMA 1347 audio panel between the two screens. Nothing on either display reaches
/// it, so the G1000 windows cannot press its keys. Measured live on MSFS 2024's stock aircraft
/// (2026-10-17, docs/c172.md).
///
/// EVERY KEY IS A PUSH THAT TOGGLES (C172-10). Its input event (`AS1000_*_MIDCOM`) goes to 1 and drops
/// back to 0 by itself about 150 ms later, and each push flips the key: NAV1 pushed once turned the NAV 1
/// ident audio on, pushed again off. That is not the DA40's GMA 1347, whose `AS1000_MID_*` keys take a
/// value (DA40F-12). So a key is pushed only after a fresh read says it is not already where the pilot
/// asked, the shape the walkaround covers use (C172-4).
///
/// THE ROWS READ WHAT EACH KEY DRIVES: `COM TRANSMIT:n` for the MIC keys, `COM RECEIVE:n`, `NAV SOUND:n`,
/// `ADF SOUND:1`, `DME SOUND`, `MARKER BEACON TEST MUTE` (MKR/MUTE), `MARKER BEACON SENSITIVITY HIGH`
/// (HI SENS) and `SPEAKER ACTIVE`. The transmitting radio is always heard: its own key does nothing
/// (measured both ways round), so turning it off is refused aloud, as on the DA40.
///
/// THE THREE COM AUDIO ROWS MOVE EACH OTHER. COM2 MIC also turned COM 2's receive on, and once COM 1's off;
/// COM1 MIC put them back; what the knock-on is varies between runs, so the rows always show what the sim
/// reads back, never a prediction. They are spoken from here, not by MainForm's generic announcer, so a
/// knock-on of the pilot's own pick can stay quiet (<see cref="ComAudioGraceMs"/>, C172-12) while every
/// delivery still moves this definition's own baseline: swallowing one from the generic path would leave
/// its baseline stale and silence a later change back.
///
/// THE INTERCOM KEYS (PILOT, COPLT) HAVE NO SIMVAR OF THEIR OWN: two lamps live in two SimVars. With
/// `INTERCOM SYSTEM ACTIVE` 0 neither is lit; with it 1, `INTERCOM MODE` 0 is PILOT alone, 1 both, 2 COPLT
/// alone (measured pressing them in every order). So they are buttons that read the lamp back, and the
/// panel's status list shows both lamps (<see cref="IntercomLamps"/>).
///
/// NOT OFFERED, measured: COM 1/2, TEL, PA, AUX, MAN SQ and PLAY moved nothing readable; COM3 MIC and COM3
/// have no third radio behind them; the panel's own volume knob moved nothing; DISPLAY BACKUP put neither
/// display in reversionary. The volumes are set per radio through the stock events, each measured to set
/// the percent it was given: the PFD's VOL knobs act only on the radio holding the tuning box.
/// </summary>
public partial class CessnaC172G1000Definition
{
    private const string AudioPanel = "Audio";

    /// <summary>The Transmit Radio row: `COM TRANSMIT:2`, so 0 reads COM 1 and 1 COM 2.</summary>
    internal const string TransmitKey = "C172G_AUDIO_TRANSMIT";

    /// <summary>The MIC key that makes each radio the transmitter.</summary>
    internal static readonly IReadOnlyDictionary<int, string> MicKeys = new Dictionary<int, string>
    {
        [1] = "AS1000_COM_1_MIC_MIDCOM",
        [2] = "AS1000_COM_2_MIC_MIDCOM",
    };

    /// <summary>The audio keys a SimVar reads, and the key each one pushes.</summary>
    internal static readonly IReadOnlyDictionary<string, string> AudioKeyInputEvents = new Dictionary<string, string>
    {
        ["C172G_AUDIO_COM1"] = "AS1000_COM_1_MIDCOM",
        ["C172G_AUDIO_COM2"] = "AS1000_COM_2_MIDCOM",
        ["C172G_AUDIO_NAV1"] = "AS1000_NAV_1_MIDCOM",
        ["C172G_AUDIO_NAV2"] = "AS1000_NAV_2_MIDCOM",
        ["C172G_AUDIO_DME"] = "AS1000_DME_MIDCOM",
        ["C172G_AUDIO_ADF"] = "AS1000_ADF_MIDCOM",
        ["C172G_AUDIO_MKR_MUTE"] = "AS1000_MARKERMUTE_MIDCOM",
        ["C172G_AUDIO_HI_SENS"] = "AS1000_MARKERSENSITIVITYHIGH_MIDCOM",
        ["C172G_AUDIO_SPKR"] = "AS1000_SPEAKER_MIDCOM",
    };

    /// <summary>The intercom isolation keys and the key each button pushes.</summary>
    internal static readonly IReadOnlyDictionary<string, string> IntercomKeys = new Dictionary<string, string>
    {
        ["C172G_AUDIO_PILOT"] = "AS1000_ISOLATEPILOT_MIDCOM",
        ["C172G_AUDIO_COPLT"] = "AS1000_ISOLATECOPILOT_MIDCOM",
    };

    /// <summary>The two SimVars the intercom lamps are read from; each is one row of the status list.</summary>
    internal const string IntercomActiveKey = "C172G_INTERCOM_ACTIVE", IntercomModeKey = "C172G_INTERCOM_MODE";

    /// <summary>The VOL settings and the stock event that sets each one, in whole percent.</summary>
    internal static readonly IReadOnlyDictionary<string, string> VolumeEvents = new Dictionary<string, string>
    {
        ["C172G_AUDIO_COM1_VOL"] = "COM1_VOLUME_SET",
        ["C172G_AUDIO_COM2_VOL"] = "COM2_VOLUME_SET",
        ["C172G_AUDIO_NAV1_VOL"] = "NAV1_VOLUME_SET_EX1",
        ["C172G_AUDIO_NAV2_VOL"] = "NAV2_VOLUME_SET_EX1",
    };

    /// <summary>The three COM audio rows, which move each other in the sim.</summary>
    internal static readonly string[] ComAudioKeys = { TransmitKey, "C172G_AUDIO_COM1", "C172G_AUDIO_COM2" };

    /// <summary>
    /// How long after MSFSBA sets one COM audio row the others' changes stay quiet. FAST-SAMPLED: the
    /// knock-on arrives within a frame or two of the push, on the rows' own SIM_FRAME subscriptions; the
    /// window also outlasts a 1 s batch (DA40S-2), should a row ever ride it.
    /// </summary>
    internal const int ComAudioGraceMs = 2500;

    /// <summary>How long after "COMn is the transmitting radio" its combo is put back to On.</summary>
    internal const int TransmitRefusalRefreshMs = 1500;

    private DateTime _comAudioOwnWriteAt = DateTime.MinValue;
    private string _comAudioOwnKey = "";

    /// <summary>Each audio row's last reading, so a change can be told from the first one. UI thread only.</summary>
    private readonly Dictionary<string, double> _audioLast = new();

    /// <summary>The intercom's two SimVars as last delivered, for the status list's lamps.</summary>
    private double? _intercomActive, _intercomMode;

    private static readonly List<string> AudioControls = new()
    {
        TransmitKey, "C172G_AUDIO_COM1", "C172G_AUDIO_COM2",
        "C172G_AUDIO_NAV1", "C172G_AUDIO_NAV2", "C172G_AUDIO_DME", "C172G_AUDIO_ADF",
        "C172G_AUDIO_MKR_MUTE", "C172G_AUDIO_HI_SENS", "C172G_AUDIO_SPKR",
        "C172G_AUDIO_PILOT", "C172G_AUDIO_COPLT",
        "C172G_AUDIO_COM1_VOL", "C172G_AUDIO_COM2_VOL", "C172G_AUDIO_NAV1_VOL", "C172G_AUDIO_NAV2_VOL",
    };

    private static void AddAudioVariables(Dictionary<string, SimVarDefinition> v)
    {
        v[TransmitKey] = AudioKey("COM TRANSMIT:2", "Transmit Radio", new() { [0] = "COM 1", [1] = "COM 2" });
        v["C172G_AUDIO_COM1"] = AudioKey("COM RECEIVE:1", "COM1");
        v["C172G_AUDIO_COM2"] = AudioKey("COM RECEIVE:2", "COM2");
        v["C172G_AUDIO_NAV1"] = AudioKey("NAV SOUND:1", "NAV1");
        v["C172G_AUDIO_NAV2"] = AudioKey("NAV SOUND:2", "NAV2");
        v["C172G_AUDIO_DME"] = AudioKey("DME SOUND", "DME");
        v["C172G_AUDIO_ADF"] = AudioKey("ADF SOUND:1", "ADF");
        v["C172G_AUDIO_MKR_MUTE"] = AudioKey("MARKER BEACON TEST MUTE", "MKR/MUTE");
        v["C172G_AUDIO_HI_SENS"] = AudioKey("MARKER BEACON SENSITIVITY HIGH", "HI SENS");
        v["C172G_AUDIO_SPKR"] = AudioKey("SPEAKER ACTIVE", "SPKR");

        v["C172G_AUDIO_PILOT"] = KeyPress(IntercomKeys["C172G_AUDIO_PILOT"], "PILOT");
        v["C172G_AUDIO_COPLT"] = KeyPress(IntercomKeys["C172G_AUDIO_COPLT"], "COPLT");
        v[IntercomActiveKey] = IntercomState("INTERCOM SYSTEM ACTIVE", "PILOT", "Bool");
        v[IntercomModeKey] = IntercomState("INTERCOM MODE", "COPLT", "number");

        v["C172G_AUDIO_COM1_VOL"] = Volume("COM VOLUME:1", "COM1 VOL");
        v["C172G_AUDIO_COM2_VOL"] = Volume("COM VOLUME:2", "COM2 VOL");
        v["C172G_AUDIO_NAV1_VOL"] = Volume("NAV VOLUME:1", "NAV1 VOL");
        v["C172G_AUDIO_NAV2_VOL"] = Volume("NAV VOLUME:2", "NAV2 VOL");
    }

    /// <summary>
    /// A key's lamp, named as printed. Continuous and announced, so the open combo follows a key pressed
    /// in the cockpit and a change from outside MSFSBA is spoken; spoken from <see cref="NoteAudioUpdate"/>,
    /// which returns true, so the combo is refreshed on that path too, for every value: a Bool has only
    /// resting positions, none it sweeps through (VAR-6).
    ///
    /// FAST-SAMPLED: its own SIM_FRAME subscription, delivered on change. On the 1 s batch every pick
    /// waited for the next batch before its fresh read answered, and a combo put back to On waited another
    /// (the owner's first test, 2026-10-17: every push landed at the same 0.7 s past the second, and COM1
    /// felt laggy). Here the cache is at most a frame old, so the fresh read answers at once
    /// (<see cref="FreshReadPolicy.CacheIsFresh"/>) and a forced read goes out within a frame.
    /// </summary>
    private static SimVarDefinition AudioKey(string simVar, string placard, Dictionary<double, string>? positions = null) => new()
    {
        Name = simVar,
        DisplayName = placard,
        Type = SimVarType.SimVar,
        Units = "Bool",
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ExcludeFromBatch = true,
        HighFrequency = true,
        ValueDescriptions = positions ?? new Dictionary<double, string> { [0] = "Off", [1] = "On" },
        RefreshControlWhenDefHandled = _ => true
    };

    /// <summary>
    /// An intercom SimVar: cached for the status list, never spoken, so no Ctrl+M row (VAR-9). Fast-sampled
    /// like the keys, so a lamp's read-back answers at once.
    /// </summary>
    private static SimVarDefinition IntercomState(string simVar, string displayName, string units) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = units,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ExcludeFromBatch = true,
        HighFrequency = true,
        ExcludeFromMonitorManager = true
    };

    /// <summary>A VOL setting: a percentage, so a slider (DA40S-6), read when the panel opens and never spoken.</summary>
    private static SimVarDefinition Volume(string simVar, string displayName) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = "percent",
        UpdateFrequency = UpdateFrequency.OnRequest,
        RenderAsSlider = true,
        SliderMin = 0,
        SliderMax = 100
    };

    /// <summary>1 or 2: the radio `COM TRANSMIT:2` names.</summary>
    internal static int TransmitRadio(double comTransmit2) => comTransmit2 >= 0.5 ? 2 : 1;

    /// <summary>1 or 2 for the COM1 and COM2 receive rows, otherwise 0.</summary>
    internal static int ReceiveKeyRadio(string varKey) => varKey switch
    {
        "C172G_AUDIO_COM1" => 1,
        "C172G_AUDIO_COM2" => 2,
        _ => 0
    };

    /// <summary>Whether <paramref name="varName"/> moved because MSFSBA just set another COM audio row.</summary>
    internal static bool IsComAudioSideEffect(string varName, string ownKey, TimeSpan sinceOwnWrite)
        => ownKey.Length > 0 && varName != ownKey &&
           Array.IndexOf(ComAudioKeys, varName) >= 0 &&
           sinceOwnWrite.TotalMilliseconds < ComAudioGraceMs;

    /// <summary>
    /// The PILOT and COPLT lamps from the intercom's two SimVars; null where the mode is not known yet (or
    /// is one never measured). Pure so the suite can pin it.
    /// </summary>
    internal static (bool? Pilot, bool? Copilot) IntercomLamps(double active, double? mode)
    {
        if (active < 0.5) return (false, false);
        if (mode is not double m) return (null, null);
        return Math.Round(m) switch
        {
            0 => (true, false),
            1 => (true, true),
            2 => (false, true),
            _ => ((bool?)null, (bool?)null)
        };
    }

    /// <summary>"NAV1: On", "Transmit Radio: COM 2": the row's name and its new position.</summary>
    internal static string DescribeAudioKey(SimVarDefinition row, double value) =>
        $"{row.DisplayName}: {row.ValueDescriptions[value >= 0.5 ? 1 : 0]}";

    /// <summary>A lamp as the status list and the read-back say it.</summary>
    private static string LampText(bool? lit) => lit is bool on ? (on ? "On" : "Off") : "--";

    /// <summary>
    /// The intercom's status rows. The value passed is the cache's for that row, and is kept, so a list
    /// painted before any update reached <see cref="NoteAudioUpdate"/> still shows both lamps by its
    /// second row.
    /// </summary>
    private bool TryGetIntercomDisplay(string varKey, double value, out string displayText)
    {
        displayText = "";
        if (varKey == IntercomActiveKey)
        {
            _intercomActive = value;
            displayText = LampText(IntercomLamps(value, _intercomMode).Pilot);
            return true;
        }
        if (varKey == IntercomModeKey)
        {
            _intercomMode = value;
            displayText = _intercomActive is double active ? LampText(IntercomLamps(active, value).Copilot) : "--";
            return true;
        }
        return false;
    }

    /// <summary>
    /// The audio panel's deliveries: an audio row speaks a change after its first reading, unless it is a
    /// knock-on of MSFSBA's own COM audio set; the intercom SimVars are only kept. Every one returns true,
    /// so MainForm's generic announcer never speaks them (the combos still refresh: RefreshControlWhenDefHandled).
    /// </summary>
    private bool NoteAudioUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (varName == IntercomActiveKey) { _intercomActive = value; return true; }
        if (varName == IntercomModeKey) { _intercomMode = value; return true; }
        if (varName != TransmitKey && !AudioKeyInputEvents.ContainsKey(varName)) return false;

        bool known = _audioLast.TryGetValue(varName, out double was);
        _audioLast[varName] = value;
        if (known && (was >= 0.5) != (value >= 0.5)
            && !IsComAudioSideEffect(varName, _comAudioOwnKey, DateTime.UtcNow - _comAudioOwnWriteAt))
        {
            announcer.Announce(DescribeAudioKey(GetVariables()[varName], value));
        }
        return true;
    }

    private void ClearAudioBaselines()
    {
        _audioLast.Clear();
        _intercomActive = _intercomMode = null;
    }

    /// <summary>The audio panel's writes; false for any other control.</summary>
    private bool HandleAudioSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (varKey == TransmitKey)
        {
            _ = SetTransmitRadioAsync(value >= 0.5 ? 2 : 1, varDef, simConnect, announcer);
            return true;
        }

        if (AudioKeyInputEvents.TryGetValue(varKey, out string? keyEvent))
        {
            _ = SetAudioKeyAsync(varKey, keyEvent, value >= 0.5, varDef, simConnect, announcer);
            return true;
        }

        if (IntercomKeys.TryGetValue(varKey, out string? intercomEvent))
        {
            if (SetInputEvent(intercomEvent, 1, varDef, simConnect, announcer))
                _ = AnnounceIntercomLampAsync(varKey, varDef.DisplayName, simConnect, announcer);
            return true;
        }

        if (VolumeEvents.TryGetValue(varKey, out string? volumeEvent))
        {
            simConnect.SendEvent(volumeEvent, (uint)Math.Round(Math.Clamp(value, 0, 100)));
            return true;
        }

        return false;
    }

    private void MarkComAudioWrite(string varKey)
    {
        _comAudioOwnWriteAt = DateTime.UtcNow;
        _comAudioOwnKey = varKey;
    }

    /// <summary>
    /// The MIC key of the radio asked for, pushed only when a fresh read says it is not already the
    /// transmitter. Awaited on the UI thread.
    /// </summary>
    private async Task SetTransmitRadioAsync(int radio, SimVarDefinition varDef, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (await simConnect.ReadFreshAsync(TransmitKey, 1500) is not double current)
        {
            announcer.AnnounceImmediate($"{varDef.DisplayName} could not be read. Try again.");
            return;
        }
        if (TransmitRadio(current) == radio) return;
        MarkComAudioWrite(TransmitKey);
        SetInputEvent(MicKeys[radio], 1, varDef, simConnect, announcer);
    }

    /// <summary>
    /// An audio key, pushed only when a fresh read says it is not already where the pilot asked: a push
    /// against a stale state would put it the other way. Awaited on the UI thread.
    /// </summary>
    private async Task SetAudioKeyAsync(string varKey, string inputEvent, bool on, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (await simConnect.ReadFreshAsync(varKey, 1500) is not double current)
        {
            announcer.AnnounceImmediate($"{varDef.DisplayName} could not be read. Try again.");
            return;
        }
        if ((current >= 0.5) == on) return;

        // The transmitting radio's key does nothing, so its receive cannot be turned off. Said, or the
        // key reads as broken; then a forced read puts the combo back to On, within a frame, so it waits
        // for the sentence's start to be heard rather than have the combo's own "On" speak over it.
        // Read fresh when the transmitter has not been delivered yet (just after a connect).
        int radio = ReceiveKeyRadio(varKey);
        if (!on && radio > 0
            && (simConnect.GetCachedVariableValue(TransmitKey) ?? await simConnect.ReadFreshAsync(TransmitKey, 1500)) is double tx
            && TransmitRadio(tx) == radio)
        {
            announcer.AnnounceImmediate($"COM{radio} is the transmitting radio and is always heard.");
            await Task.Delay(TransmitRefusalRefreshMs);
            simConnect.RequestVariable(varKey, forceUpdate: true);
            return;
        }

        if (radio > 0) MarkComAudioWrite(varKey);
        SetInputEvent(inputEvent, 1, varDef, simConnect, announcer);
    }

    /// <summary>
    /// What an intercom key's push did, read back once the lamps have moved (they did within a frame,
    /// measured): "PILOT: On". The button shows no state of its own, so this is the one confirmation.
    /// Awaited on the UI thread.
    /// </summary>
    private static async Task AnnounceIntercomLampAsync(string varKey, string placard, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        await Task.Delay(300);
        double? active = await simConnect.ReadFreshAsync(IntercomActiveKey, 1500);
        double? mode = await simConnect.ReadFreshAsync(IntercomModeKey, 1500);
        (bool? Pilot, bool? Copilot) lamps = active is double a ? IntercomLamps(a, mode) : (null, null);
        bool? lit = varKey == "C172G_AUDIO_PILOT" ? lamps.Pilot : lamps.Copilot;
        if (lit is null) announcer.Announce($"{placard} pressed");
        else announcer.AnnounceImmediate($"{placard}: {LampText(lit)}");
    }
}

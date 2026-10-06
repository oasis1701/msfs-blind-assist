using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Audio. Both variants.
///
/// The DA40's audio panel is the GMA 1347 between the two displays: the stock
/// ASOBO_AS1000_MID template with NO_AUX, NO_COM_3 and NO_NAV_3 (both variants' IN.xml). Its
/// keys are on neither SCREEN, so the display windows cannot press them and they live here
/// (DA40-22 covers what a display draws, not the bezel between them). The model gives a push
/// node to exactly these: COM1 MIC, COM2 MIC, COM1, COM2, NAV1, NAV2, DME, ADF, MKR/MUTE,
/// HI SENS and DISPLAY BACKUP (the last on Standby Instruments). There is no SPKR, PA, AUX or
/// MAN SQ node, and PILOT has its lamp but no push node, so none of those is offered.
///
/// The two MIC keys are the Transmit Radio row (COM1_TRANSMIT_SELECT moved COM TRANSMIT:1
/// from 0 to 1); COM_RECEIVE_ALL_SET moves COM RECEIVE ALL and COM RECEIVE:2 together.
///
/// ⚠️ EACH KEY IS WRITTEN THROUGH ITS OWN INPUT EVENT, BY VALUE. AS1000_MID_NAV_1 = 1 lights
/// the key and turns the NAV 1 ident audio on, = 0 turns it off; a second 1 changes nothing
/// (measured live on the NG, 2026-10-06, for every key here). The stock K events move the
/// same simvars and are the fallback before the input events are enumerated.
///
/// ⚠️ MKR/MUTE IS THE MARKER MUTE, NOT THE MARKER AUDIO. The cockpit key moves MARKER BEACON
/// TEST MUTE and leaves MARKER SOUND at 1 (measured: AS1000_MID_MKR 1, then 0).
///
/// And the headset jack, which is a real clickspot on the console and the ONLY audio item
/// COWS models itself: L:HEADSET is referenced by exactly one file in the package,
/// sound.xml. It changes what the pilot hears, nothing else.
///
/// The volumes are the PFD's two VOL knobs, which act on whichever radio holds the tuning
/// box; here they are set per radio through COM1/COM2_VOLUME_SET and NAV1/NAV2_VOLUME_SET_EX1,
/// each measured to set and read back the percentage.
/// </summary>
public partial class CowsDA40Definition
{
    private const string AudioPanel = "Audio";

    private static Dictionary<string, SimVarDefinition> BuildAudioVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // Bound to COM 2's transmit flag rather than COM 1's so the encoding reads the
        // way the selection does: 0 is COM 1, 1 is COM 2.
        v["DA40_AUDIO_TRANSMIT"] = new SimVarDefinition
        {
            Name = "COM TRANSMIT:2",
            DisplayName = "Transmit Radio",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "COM 1",
                [1] = "COM 2"
            }
        };

        v["DA40_AUDIO_MONITOR_BOTH"] = new SimVarDefinition
        {
            Name = "COM RECEIVE ALL",
            DisplayName = "Monitor Both Radios",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Transmit radio only",
                [1] = "Both"
            }
        };

        v["DA40_AUDIO_HEADSET"] = new SimVarDefinition
        {
            Name = "HEADSET",
            DisplayName = "Headset",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Unplugged",
                [1] = "Plugged in"
            }
        };

        // ---------- Status ----------

        // ⚠️ NO SECOND DEFINITION OF A SIMVAR THE RADIOS PANEL ALREADY OWNS. These two keys
        // used to re-register COM ACTIVE FREQUENCY:1 and :2, which the Radios panel already
        // carries as DA40_RADIO_COM1_ACTIVE / _COM2_ACTIVE - one Continuous on its own
        // SIM_FRAME subscription, the other OnRequest, both asking the sim for the same
        // name. The Radios panel then read "COM 2 Active: --, NAV 1 Active: --, NAV 2
        // Active: --" while the Audio panel showed those very frequencies correctly.
        //
        // The house rule is one key per SimVar (VarNameCollisionTests exists for the batch
        // form of this), and a display row does not need a definition of its own - it needs
        // a KEY, and the Radios panel's keys are the ones being kept up to date.

        // ---------- The GMA 1347 keys, named as printed ----------
        foreach (var key in GmaKeys)
            AddGmaKey(v, key.VarKey, key.SimVar, key.Placard);

        // ---------- The PFD's VOL knobs, per radio ----------
        AddVolume(v, "DA40_AUDIO_COM1_VOL_SET", "COM VOLUME:1", "COM1 VOL");
        AddVolume(v, "DA40_AUDIO_COM2_VOL_SET", "COM VOLUME:2", "COM2 VOL");
        AddVolume(v, "DA40_AUDIO_NAV1_VOL_SET", "NAV VOLUME:1", "NAV1 VOL");
        AddVolume(v, "DA40_AUDIO_NAV2_VOL_SET", "NAV VOLUME:2", "NAV2 VOL");

        // The COM audio four move together (see ComAudioKeys), and a knock-on change MSFSBA
        // keeps quiet must still move the combo showing it.
        foreach (var key in ComAudioKeys)
            v[key].RefreshControlWhenDefHandled = _ => true;

        return v;
    }

    /// <summary>
    /// ⚠️ THE FOUR COM AUDIO CONTROLS ARE ONE SYSTEM IN THE SIM. Setting COM2 receive on
    /// also set COM RECEIVE ALL (measured, AS1000_MID_COM_2 = 1, NG, 2026-10-06), choosing
    /// Monitor Both moves COM RECEIVE:2, and a MIC selection moves the receive flags. So a
    /// pilot setting one of them heard the others "change" too - a direct interaction spoken
    /// as if it were somebody else's (CORE-7). After MSFSBA writes one, the others' changes
    /// are kept quiet for <see cref="ComAudioGraceMs"/>; a change from the cockpit or
    /// hardware, with no write of ours behind it, still speaks.
    /// </summary>
    internal static readonly string[] ComAudioKeys =
    {
        "DA40_AUDIO_TRANSMIT", "DA40_AUDIO_MONITOR_BOTH", "DA40_AUDIO_COM1_RECEIVE", "DA40_AUDIO_COM2_RECEIVE"
    };

    /// <summary>Outlasts the 1 s batch that delivers the knock-on change (DA40S-2).</summary>
    internal const int ComAudioGraceMs = 2500;

    private DateTime _comAudioOwnWriteAt = DateTime.MinValue;
    private string _comAudioOwnKey = "";

    private bool IsComAudioSideEffect(string varName)
        => IsComAudioSideEffect(varName, _comAudioOwnKey, DateTime.UtcNow - _comAudioOwnWriteAt);

    /// <summary>Whether <paramref name="varName"/> moved because MSFSBA just set another of the four.</summary>
    internal static bool IsComAudioSideEffect(string varName, string ownKey, TimeSpan sinceOwnWrite)
        => ownKey.Length > 0 && varName != ownKey &&
           Array.IndexOf(ComAudioKeys, varName) >= 0 &&
           sinceOwnWrite.TotalMilliseconds < ComAudioGraceMs;

    /// <summary>One GMA 1347 key: its MSFSBA key, the simvar its lamp follows, its placard,
    /// its input event, and the stock event that moves the same simvar.</summary>
    internal readonly record struct GmaKey(string VarKey, string SimVar, string Placard,
        string InputEvent, string KEvent, bool KEventToggles);

    internal static readonly GmaKey[] GmaKeys =
    {
        new("DA40_AUDIO_COM1_RECEIVE", "COM RECEIVE:1", "COM1", "AS1000_MID_COM_1", "COM1_RECEIVE_SELECT", false),
        new("DA40_AUDIO_COM2_RECEIVE", "COM RECEIVE:2", "COM2", "AS1000_MID_COM_2", "COM2_RECEIVE_SELECT", false),
        new("DA40_AUDIO_NAV1_IDENT", "NAV SOUND:1", "NAV1", "AS1000_MID_NAV_1", "RADIO_VOR1_IDENT_TOGGLE", true),
        new("DA40_AUDIO_NAV2_IDENT", "NAV SOUND:2", "NAV2", "AS1000_MID_NAV_2", "RADIO_VOR2_IDENT_TOGGLE", true),
        new("DA40_AUDIO_DME_IDENT", "DME SOUND", "DME", "AS1000_MID_DME", "RADIO_DME1_IDENT_TOGGLE", true),
        new("DA40_AUDIO_ADF_IDENT", "ADF SOUND:1", "ADF", "AS1000_MID_ADF", "RADIO_ADF_IDENT_TOGGLE", true),
        new("DA40_AUDIO_MKR_MUTE", "MARKER BEACON TEST MUTE", "MKR/MUTE", "AS1000_MID_MKR", "MARKER_BEACON_TEST_MUTE", false),
        new("DA40_AUDIO_HI_SENS", "MARKER BEACON SENSITIVITY HIGH", "HI SENS", "AS1000_MID_HI", "MARKER_BEACON_SENSITIVITY_HIGH", false)
    };

    private static void AddGmaKey(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string placard)
    {
        // A switch: settable, so Continuous and announced (DA40-9) - a key pressed in the
        // cockpit speaks, and MSFSBA's own set is covered by the global combo echo.
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = placard,
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On"
            }
        };
    }

    private static void AddVolume(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        // A percentage, so a slider (DA40S-6); a numeric setting stays silent (DA40-9).
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsSlider = true,
            SliderMin = 0,
            SliderMax = 100
        };
    }

    /// <summary>
    /// Frequencies, rendered here rather than by Format. Format is not enough for these -
    /// live, COM 1 read "128" and COM 2 "125" with Format set to F3 - which is the same
    /// trap the A380's RMP readouts hit and the same remedy: a display override.
    /// </summary>
    private bool TryGetAudioDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        if (varKey != "DA40_RADIO_COM1_ACTIVE" && varKey != "DA40_RADIO_COM2_ACTIVE") return false;

        displayText = $"{value:0.000} MHz";
        return true;
    }

    private static readonly List<string> AudioControls = new()
    {
        "DA40_AUDIO_TRANSMIT",
        "DA40_AUDIO_MONITOR_BOTH",
        "DA40_AUDIO_COM1_RECEIVE",
        "DA40_AUDIO_COM2_RECEIVE",
        "DA40_AUDIO_NAV1_IDENT",
        "DA40_AUDIO_NAV2_IDENT",
        "DA40_AUDIO_DME_IDENT",
        "DA40_AUDIO_ADF_IDENT",
        "DA40_AUDIO_MKR_MUTE",
        "DA40_AUDIO_HI_SENS",
        "DA40_AUDIO_COM1_VOL_SET",
        "DA40_AUDIO_COM2_VOL_SET",
        "DA40_AUDIO_NAV1_VOL_SET",
        "DA40_AUDIO_NAV2_VOL_SET",
        "DA40_AUDIO_HEADSET"
    };

    private static readonly List<string> AudioDisplay = new()
    {
        "DA40_RADIO_COM1_ACTIVE",
        "DA40_RADIO_COM2_ACTIVE"
    };

    private bool HandleAudioSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (Array.IndexOf(ComAudioKeys, varKey) >= 0)
        {
            _comAudioOwnWriteAt = DateTime.UtcNow;
            _comAudioOwnKey = varKey;
        }

        switch (varKey)
        {
            case "DA40_AUDIO_TRANSMIT":
                simConnect.ExecuteCalculatorCodeUnique(
                    value >= 0.5 ? "1 (>K:COM2_TRANSMIT_SELECT)" : "1 (>K:COM1_TRANSMIT_SELECT)");
                return true;

            case "DA40_AUDIO_MONITOR_BOTH":
                simConnect.ExecuteCalculatorCodeUnique(
                    FormattableString.Invariant($"{(value >= 0.5 ? 1 : 0)} (>K:COM_RECEIVE_ALL_SET)"));
                return true;

            case "DA40_AUDIO_HEADSET":
                simConnect.SetLVar("HEADSET", value >= 0.5 ? 1 : 0);
                return true;

            case "DA40_AUDIO_COM1_VOL_SET":
            case "DA40_AUDIO_COM2_VOL_SET":
            case "DA40_AUDIO_NAV1_VOL_SET":
            case "DA40_AUDIO_NAV2_VOL_SET":
                simConnect.ExecuteCalculatorCodeUnique(VolumeWrite(varKey, value));
                return true;
        }

        foreach (var key in GmaKeys)
        {
            if (key.VarKey != varKey) continue;
            int on = value >= 0.5 ? 1 : 0;

            // The cockpit's own key, by value (see the class note).
            if (simConnect.HasInputEvent(key.InputEvent) && simConnect.TrySetInputEvent(key.InputEvent, on))
                return true;

            simConnect.ExecuteCalculatorCodeUnique(GmaFallbackWrite(key, on));
            return true;
        }

        return false;
    }

    /// <summary>The stock-event write for a GMA key when its input event is not available.
    /// A toggle event is sent only when the simvar is not already where the pilot asked.</summary>
    internal static string GmaFallbackWrite(GmaKey key, int on) => key.KEventToggles
        ? FormattableString.Invariant($"(A:{key.SimVar}, Bool) {on} != if{{ 1 (>K:{key.KEvent}) }}")
        : FormattableString.Invariant($"{on} (>K:{key.KEvent})");

    /// <summary>A VOL setting, as the whole percentage the four volume events take.</summary>
    internal static string VolumeWrite(string varKey, double percent)
    {
        double p = Math.Clamp(Math.Round(percent), 0, 100);
        string ev = varKey switch
        {
            "DA40_AUDIO_COM1_VOL_SET" => "COM1_VOLUME_SET",
            "DA40_AUDIO_COM2_VOL_SET" => "COM2_VOLUME_SET",
            "DA40_AUDIO_NAV1_VOL_SET" => "NAV1_VOLUME_SET_EX1",
            _ => "NAV2_VOLUME_SET_EX1"
        };
        return FormattableString.Invariant($"{p:0} (>K:{ev})");
    }
}

using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Radios → COM, NAV and Transponder, rebuilt on 2026-10-18 from measurements on the G1000 cockpit
/// (docs/c172.md). Marshall's FS2020 panels showed the frequencies and a swap with no way to set a
/// frequency, and sent a typed squawk as a plain number.
///
/// TYPED, AS ON THE DA40'S RADIOS PANEL: the radios are tuned on the PFD's knobs one step at a time,
/// so a typed standby is the faster road to the same result (the DA40 keeps the same exception). The
/// standby is set and then swapped, which is how the radio is flown.
///
/// THE STANDBY GOES IN HERTZ (C172-14): `COM_STBY_RADIO_SET_HZ` and `COM2_STBY_RADIO_SET_HZ` set
/// 121.705 and 118.185 exactly, 8.33 kHz channels included, and `NAV1_STBY_SET_HZ`/`NAV2_STBY_SET_HZ`
/// 113.30 and 109.50; each swap event swaps its own radio only. A frequency outside the band is
/// refused aloud, never clamped.
///
/// THE SQUAWK IS BCD16 (C172-13), written by MainForm's own squawk path for the key
/// `TRANSPONDER_CODE_SET` (padding, octal check, "Squawk set to"), so this class returns false for it:
/// `XPNDR_SET` with 0x4567 read back 4567, with a plain 1234 it read 452.
///
/// THE MODE IS `TRANSPONDER STATE:1` (C172-15), written directly (1, 3, 4 and 5 measured to hold); ident
/// (`XPNDR_IDENT_ON`) holds `TRANSPONDER IDENT:1` for about 18 s, and only with the transponder On or
/// Altitude: in standby it does nothing, so it is refused aloud there.
/// </summary>
public abstract partial class CessnaC172BaseDefinition
{
    /// <summary>One radio: its spoken name, its keys, the stock events that set and swap it, and its band.</summary>
    internal sealed record Radio(string Label, string Kind, int Index, string SetEvent, string SwapEvent,
        double LowMhz, double HighMhz, string Format)
    {
        private string Name => $"{Kind}{Index}";
        public string SetKey => $"C172_{Name}_STANDBY_SET";
        public string StandbyKey => $"C172_{Name}_STANDBY";
        public string ActiveKey => $"C172_{Name}_ACTIVE";
        public string SwapKey => $"C172_{Name}_SWAP";
        public string Placard => Name;
    }

    internal static readonly Radio[] Radios =
    {
        new("COM 1", "COM", 1, "COM_STBY_RADIO_SET_HZ", "COM_STBY_RADIO_SWAP", 118.0, 136.99, "0.000"),
        new("COM 2", "COM", 2, "COM2_STBY_RADIO_SET_HZ", "COM2_RADIO_SWAP", 118.0, 136.99, "0.000"),
        new("NAV 1", "NAV", 1, "NAV1_STBY_SET_HZ", "NAV1_RADIO_SWAP", 108.0, 117.95, "0.00"),
        new("NAV 2", "NAV", 2, "NAV2_STBY_SET_HZ", "NAV2_RADIO_SWAP", 108.0, 117.95, "0.00"),
    };

    /// <summary>The key MainForm's squawk path answers to (BCD16, C172-13).</summary>
    internal const string SquawkSetKey = "TRANSPONDER_CODE_SET";

    internal const string TransponderModeKey = "C172_XPNDR_MODE";

    /// <summary>
    /// `TRANSPONDER STATE:1` positions: the G1000's XPDR modes (Standby, On, Altitude, Ground, each
    /// measured to set and hold), and Off, shown so the combo never keeps a stale mode when the
    /// transponder reads 0 (a combo keeps its last pick for a value it does not list), but refused when
    /// picked: the G1000 has no Off key. Test (2) also holds and is not a G1000 mode, so it is not offered.
    /// </summary>
    internal static Dictionary<double, string> TransponderModes() =>
        new() { [0] = "Off", [1] = "Standby", [3] = "On", [4] = "Altitude", [5] = "Ground" };

    /// <summary>
    /// True for a transponder mode the combo does not list (2, Test), which ProcessSimVarUpdate
    /// swallows so it is never spoken as "Transponder Mode: 2". The combo loses nothing: it keeps its
    /// pick for a value it does not list whether or not MainForm refreshes it.
    /// </summary>
    private static bool IsUnlistedTransponderMode(string varName, double value) =>
        varName == TransponderModeKey && !TransponderModes().ContainsKey(Math.Round(value));

    private static void AddRadioVariables(Dictionary<string, SimVarDefinition> v)
    {
        foreach (var r in Radios)
        {
            v[r.ActiveKey] = FrequencyRow($"{r.Kind} ACTIVE FREQUENCY:{r.Index}", $"{r.Placard} Active");
            v[r.StandbyKey] = FrequencyRow($"{r.Kind} STANDBY FREQUENCY:{r.Index}", $"{r.Placard} Standby");
            // Typed, never read: an L:var name nobody reads, and a typo is NaN, not 0.
            v[r.SetKey] = new SimVarDefinition
            {
                Name = r.SetKey,
                DisplayName = $"{r.Placard} Standby",
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.Never,
                UnparseableTextAsNaN = true
            };
            v[r.SwapKey] = new SimVarDefinition
            {
                Name = r.SwapEvent,
                DisplayName = $"Swap {r.Placard}",
                Type = SimVarType.Event,
                UpdateFrequency = UpdateFrequency.Never,
                RenderAsButton = true
            };
        }

        v[SquawkSetKey] = new SimVarDefinition
        {
            Name = "XPNDR_SET",
            DisplayName = "Squawk",
            Type = SimVarType.Event,
            UpdateFrequency = UpdateFrequency.Never
        };
        v["C172_XPNDR_CODE"] = new SimVarDefinition
        {
            Name = "TRANSPONDER CODE:1",
            DisplayName = "Squawk",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest
        };
        // Continuous and announced, so the combo follows a mode set on the PFD's XPDR softkeys and the
        // change is spoken. Never OnlyAnnounceValueDescriptionMatches: it makes the row a read-only
        // status box (PanelRowRules), which is how the first build shipped. A value the combo does not
        // list (2, Test) is swallowed in ProcessSimVarUpdate instead (IsUnlistedTransponderMode).
        v[TransponderModeKey] = new SimVarDefinition
        {
            Name = "TRANSPONDER STATE:1",
            DisplayName = "Transponder Mode",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = TransponderModes()
        };
        v["C172_XPNDR_IDENT"] = new SimVarDefinition
        {
            Name = "XPNDR_IDENT_ON",
            DisplayName = "Ident",
            Type = SimVarType.Event,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true
        };
        // Read only by the ident button's read-back.
        v["C172_XPNDR_IDENTING"] = new SimVarDefinition
        {
            Name = "TRANSPONDER IDENT:1",
            DisplayName = "Ident Active",
            Type = SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = UpdateFrequency.OnRequest
        };
    }

    /// <summary>A frequency for a panel's status list, read when the panel opens and after each write.</summary>
    private static SimVarDefinition FrequencyRow(string simVar, string displayName) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = "MHz",
        UpdateFrequency = UpdateFrequency.OnRequest
    };

    private static readonly Dictionary<string, List<string>> RadioControls = new()
    {
        ["COM"] = new() { "C172_COM1_STANDBY_SET", "C172_COM1_SWAP", "C172_COM2_STANDBY_SET", "C172_COM2_SWAP" },
        ["NAV"] = new() { "C172_NAV1_STANDBY_SET", "C172_NAV1_SWAP", "C172_NAV2_STANDBY_SET", "C172_NAV2_SWAP" },
        ["Transponder"] = new() { SquawkSetKey, TransponderModeKey, "C172_XPNDR_IDENT" },
    };

    private static readonly Dictionary<string, List<string>> RadioDisplay = new()
    {
        ["COM"] = new() { "C172_COM1_ACTIVE", "C172_COM1_STANDBY", "C172_COM2_ACTIVE", "C172_COM2_STANDBY" },
        ["NAV"] = new() { "C172_NAV1_ACTIVE", "C172_NAV1_STANDBY", "C172_NAV2_ACTIVE", "C172_NAV2_STANDBY" },
        ["Transponder"] = new() { "C172_XPNDR_CODE" },
    };

    /// <summary>
    /// A frequency row as the status list shows it ("124.850 MHz", "110.50 MHz") and the squawk as four
    /// digits ("0074"); false for any other key.
    /// </summary>
    private static bool TryGetRadioDisplay(string varKey, double value, out string displayText)
    {
        displayText = "";
        if (varKey == "C172_XPNDR_CODE")
        {
            displayText = Math.Round(value).ToString("0000", CultureInfo.InvariantCulture);
            return true;
        }
        foreach (var r in Radios)
        {
            if (varKey != r.ActiveKey && varKey != r.StandbyKey) continue;
            displayText = value.ToString(r.Format, CultureInfo.InvariantCulture) + " MHz";
            return true;
        }
        return false;
    }

    /// <summary>
    /// Why a typed standby is refused, or null when it is in the band: NaN (text that is not a number) and
    /// anything outside it, never clamped. Invariant digits, as every spoken number here. Pure so the suite
    /// can pin it.
    /// </summary>
    internal static string? StandbyRefusal(Radio r, double mhz)
    {
        var inv = CultureInfo.InvariantCulture;
        string band = $"{r.LowMhz.ToString(r.Format, inv)} to {r.HighMhz.ToString(r.Format, inv)}";
        if (double.IsNaN(mhz)) return $"Enter a {r.Kind} frequency, {band}.";
        if (mhz < r.LowMhz || mhz > r.HighMhz)
            return $"{mhz.ToString(r.Format, inv)} is outside the {r.Kind} band, {band}. Not set.";
        return null;
    }

    /// <summary>The standby in whole hertz, through whole kilohertz so 121.705 is exactly 121705000.</summary>
    internal static uint StandbyHz(double mhz) => (uint)(Math.Round(mhz * 1000) * 1000);

    /// <summary>"COM 1 standby 121.705", "NAV 2 standby 109.50". Pure so the suite can pin it.</summary>
    internal static string DescribeStandby(Radio r, double mhz) =>
        $"{r.Label} standby {mhz.ToString(r.Format, CultureInfo.InvariantCulture)}";

    /// <summary>A standby in the band, sent and its row re-read; the caller confirms. On the UI thread.</summary>
    private static void SendStandby(Radio r, double mhz, SimConnectManager simConnect)
    {
        simConnect.SendEvent(r.SetEvent, StandbyHz(mhz));
        _ = RefreshRowsAsync(simConnect, r.StandbyKey);
    }

    /// <summary>The radio panels' writes; false for any other control, and for the squawk, which MainForm writes.</summary>
    private static bool HandleRadioSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        foreach (var r in Radios)
        {
            if (varKey == r.SetKey)
            {
                if (StandbyRefusal(r, value) is string refusal)
                {
                    announcer.AnnounceImmediate(refusal);
                    return true;
                }
                SendStandby(r, value, simConnect);
                announcer.AnnounceImmediate(DescribeStandby(r, value));
                return true;
            }
            if (varKey == r.SwapKey)
            {
                // A swap is silent in the cockpit, so it is said here; the rows show what it did.
                simConnect.SendEvent(r.SwapEvent);
                announcer.AnnounceImmediate($"{r.Label} swapped");
                _ = RefreshRowsAsync(simConnect, r.ActiveKey, r.StandbyKey);
                return true;
            }
        }

        if (varKey == TransponderModeKey)
        {
            if (value < 0.5)
            {
                // Shown, never set: the forced read puts the combo back on the real mode (SIM-4).
                announcer.AnnounceImmediate("The G1000 transponder has no Off key. Pick Standby, On, Altitude or Ground.");
                simConnect.RequestVariable(TransponderModeKey, forceUpdate: true);
                return true;
            }
            // The combo is the confirmation (CORE-7); the re-read puts it back if the write did not take.
            simConnect.SetSimVar("TRANSPONDER STATE:1", Math.Round(value), "number");
            _ = RefreshRowsAsync(simConnect, TransponderModeKey);
            return true;
        }

        if (varKey == "C172_XPNDR_IDENT")
        {
            if (simConnect.GetCachedVariableValue(TransponderModeKey) is double mode && mode < 2.5)
            {
                announcer.AnnounceImmediate("The transponder is in standby. Ident needs On or Altitude.");
                return true;
            }
            simConnect.SendEvent("XPNDR_IDENT_ON");
            _ = AnnounceIdentAsync(simConnect, announcer);
            return true;
        }

        return false;
    }

    /// <summary>NAV 1 and NAV 2, the two radios input Ctrl+N sets.</summary>
    internal static readonly Radio[] NavRadios = Radios.Where(r => r.Kind == "NAV").ToArray();

    /// <summary>
    /// Input Ctrl+N: both NAV standbys in ONE window with two fields (the owner's choice, 2026-10-18,
    /// over one window per radio), each pre-filled with what its radio holds now and selected, so
    /// overtyping replaces it. Enter sets both, one confirmation for the two ("NAV 1 standby 110.50,
    /// NAV 2 standby 113.90"), and the window stays open for the next pair, as the C172's other value
    /// windows do; Escape closes it. A field's own Set button sets that radio alone and closes the
    /// window, the two-field window's way (the DA40's two altimeters). Each field is checked against
    /// its band and parsed as typed (CORE-17), and written as the Radios panel writes it (C172-14).
    /// </summary>
    private static async Task ShowNavStandbyWindowAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var seeds = new List<string>();
        foreach (var r in NavRadios)
            seeds.Add(await simConnect.ReadFreshAsync(r.StandbyKey, 1500) is double mhz
                ? mhz.ToString(r.Format, CultureInfo.InvariantCulture)
                : "");

        Forms.ValueInputForm? dialog = null;
        dialog = new Forms.ValueInputForm("Set NAV Standby", $"{NavRadios[0].Label} standby",
            "108.00 to 117.95 megahertz", announcer, input => NavStandbyValidator(NavRadios[0], input),
            new List<Forms.ToggleButtonDef>(),
            main => SetNavStandbys(new[] { main, dialog!.ExtraValues.Count > 0 ? dialog.ExtraValues[0] : "" },
                simConnect, announcer),
            null,
            new List<Forms.ValueInputForm.ExtraFieldDef>
            {
                new()
                {
                    Label = $"{NavRadios[1].Label} standby",
                    InitialValue = seeds[1],
                    Validator = input => NavStandbyValidator(NavRadios[1], input)
                }
            })
        {
            InitialValue = seeds[0],
            ShowCancelButton = false
        };

        // A field's own Set button closes the window without the callback: apply that one radio. Keyed
        // on SetFieldIndex, which only that button sets (Enter and Escape leave it at -1), not on
        // DialogResult, which a window shown with Show() does not promise to keep.
        dialog.FormClosed += (_, _) =>
        {
            if (dialog.SetFieldIndex < 0 || !dialog.IsValidInput) return;
            var typed = new[] { "", "" };
            typed[dialog.SetFieldIndex] = dialog.SetFieldIndex == 0
                ? dialog.InputValue
                : dialog.ExtraValues[dialog.SetFieldIndex - 1];
            SetNavStandbys(typed, simConnect, announcer);
        };
        dialog.Show(parentForm);
    }

    /// <summary>A Ctrl+N field's check: its radio's band, parsed as typed (CORE-17). Pure so the suite can pin it.</summary>
    internal static (bool isValid, string message) NavStandbyValidator(Radio r, string input) =>
        TypedValidator(input, mhz => StandbyRefusal(r, mhz));

    /// <summary>
    /// Sets each NAV standby whose field holds a frequency in its band (an empty field is left alone)
    /// and confirms them in one sentence. On the UI thread.
    /// </summary>
    private static void SetNavStandbys(IReadOnlyList<string> typed, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        var set = PlanNavStandbys(typed);
        foreach (var (r, mhz) in set) SendStandby(r, mhz, simConnect);
        if (set.Count > 0) announcer.AnnounceImmediate(string.Join(", ", set.Select(s => DescribeStandby(s.Radio, s.Mhz))));
    }

    /// <summary>
    /// Which NAV standbys a Ctrl+N entry sets, NAV 1 first: each field that parses as typed and lies in
    /// its band; an empty or refused field sets nothing. Pure so the suite can pin it.
    /// </summary>
    internal static List<(Radio Radio, double Mhz)> PlanNavStandbys(IReadOnlyList<string> typed)
    {
        var set = new List<(Radio, double)>();
        for (int i = 0; i < NavRadios.Length && i < typed.Count; i++)
        {
            if (!Utils.NumberParsing.TryParseTyped(typed[i], out double mhz)) continue;
            if (StandbyRefusal(NavRadios[i], mhz) != null) continue;
            set.Add((NavRadios[i], mhz));
        }
        return set;
    }

    /// <summary>Re-reads rows a write moved, once the sim has applied it, so the status list repaints. UI thread.</summary>
    private static async Task RefreshRowsAsync(SimConnectManager simConnect, params string[] keys)
    {
        await Task.Delay(300);
        foreach (var key in keys) simConnect.RequestVariable(key, forceUpdate: true);
    }

    /// <summary>Whether the ident took, read back once it has (it held within a frame, measured). UI thread.</summary>
    private static async Task AnnounceIdentAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        await Task.Delay(300);
        double? identing = await simConnect.ReadFreshAsync("C172_XPNDR_IDENTING", 1500);
        announcer.AnnounceImmediate(identing is double d ? (d > 0.5 ? "Ident" : "Ident did not start") : "Ident pressed");
    }
}

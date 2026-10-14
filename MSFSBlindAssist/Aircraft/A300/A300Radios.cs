using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// A VHF or ADF control panel: two windows, each with its own knobs, and a transfer switch that picks the window in
/// use. It does not swap them (measured 2026-10-10), so after a transfer the knobs iniBuilds calls "standby" tune the
/// frequency in use: every line and read-back names a window by its role now, from the transfer switch.
/// </summary>
/// <param name="Window1ActiveAt">The transfer switch's value while the first window is in use (VHF 0, ADF 1).</param>
/// <param name="Divisor">Window value to the frequency shown (the VHF windows hold kilohertz).</param>
public sealed record A300Radio(string Name, string Id, string Panel, string Window1Var, string Window2Var,
    string TransferVar, double Window1ActiveAt, double Divisor, string Format)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Window1Key => $"A300_RADIO_{Id}_W1";
    public string Window2Key => $"A300_RADIO_{Id}_W2";
    public string TransferKey => $"A300_RADIO_{Id}_TFR";

    /// <summary>The status display's lines, each carried by the transfer switch's variable.</summary>
    public string ActiveKey => $"A300_RO_{Id}_ACTIVE";
    public string StandbyKey => $"A300_RO_{Id}_STBY";

    public bool Window1InUse(double transfer) => Math.Round(transfer) == Window1ActiveAt;

    /// <summary>"122.800", "900.5".</summary>
    public string Frequency(double window) => (window / Divisor).ToString(Format, Inv);

    public string Active(double window1, double window2, double transfer) =>
        Frequency(Window1InUse(transfer) ? window1 : window2);

    public string Standby(double window1, double window2, double transfer) =>
        Frequency(Window1InUse(transfer) ? window2 : window1);

    /// <summary>"VHF 1 standby 124.805": the window a knob tunes, named by its role now.</summary>
    public string KnobPhrase(int window, double value, double transfer) =>
        $"{Name} {(window == 1 == Window1InUse(transfer) ? "active" : "standby")} {Frequency(value)}";

    /// <summary>"VHF 1 active 124.805": the frequency a transfer put in use.</summary>
    public string TransferPhrase(double window1, double window2, double transfer) =>
        $"{Name} active {Active(window1, window2, transfer)}";
}

public static class A300Radios
{
    public static readonly IReadOnlyList<A300Radio> All = new[]
    {
        new A300Radio("VHF 1", "VHF1", "VHF Radios", "INI_COM1_FREQUENCY", "INI_COM1_STBY_FREQUENCY", "INI_CPT_VHF_TRANSFER_SWITCH", 0, 1000, "0.000"),
        new A300Radio("VHF 2", "VHF2", "VHF Radios", "INI_COM2_FREQUENCY", "INI_COM2_STBY_FREQUENCY", "INI_FO_VHF_TRANSFER_SWITCH", 0, 1000, "0.000"),
        new A300Radio("ADF 1", "ADF1", "ADF Radios", "INI_ADF1_FREQUENCY", "INI_ADF1_STBY_FREQUENCY", "INI_ADF1_TRANSFER_SWITCH", 1, 1, "0.#"),
        new A300Radio("ADF 2", "ADF2", "ADF Radios", "INI_ADF2_FREQUENCY", "INI_ADF2_STBY_FREQUENCY", "INI_ADF2_TRANSFER_SWITCH", 1, 1, "0.#"),
    };

    private static A300Radio Named(string name) => All.Single(r => r.Name == name);

    /// <summary>Each frequency knob: the radio and the window (1 or 2) it tunes.</summary>
    public static readonly IReadOnlyDictionary<string, (A300Radio Radio, int Window)> ByKnob = Knobs();

    /// <summary>Each transfer button's radio.</summary>
    public static readonly IReadOnlyDictionary<string, A300Radio> ByTransfer = new Dictionary<string, A300Radio>(StringComparer.Ordinal)
    {
        ["A300_CPT_VHF_TFR"] = Named("VHF 1"),
        ["A300_FO_VHF_TFR"] = Named("VHF 2"),
        ["A300_ADF1_TFR"] = Named("ADF 1"),
        ["A300_ADF2_TFR"] = Named("ADF 2"),
    };

    /// <summary>Each status line: its radio and whether it is the active line.</summary>
    public static readonly IReadOnlyDictionary<string, (A300Radio Radio, bool Active)> ByLine =
        All.SelectMany(r => new[] { (r.ActiveKey, (r, true)), (r.StandbyKey, (r, false)) })
           .ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal);

    /// <summary>How far an ADF signal sample must move to count as a change ([SIM-7]): <see cref="Bearing"/> compares the
    /// signal with zero only, from the cache, which takes every sample whatever this is. It only keeps a signal that
    /// wanders as the aircraft flies from waking MainForm once a second.</summary>
    public const double SignalChangeTolerance = 0.5;

    /// <summary>The ADF bearing lines: ADF RADIAL (the relative bearing), read only while ADF SIGNAL is not 0.</summary>
    public static readonly IReadOnlyDictionary<string, (string SignalKey, string SignalVar)> BearingLines =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["A300_RO_ADF1_BEARING"] = ("A300_RADIO_ADF1_SIGNAL", "ADF SIGNAL:1"),
            ["A300_RO_ADF2_BEARING"] = ("A300_RADIO_ADF2_SIGNAL", "ADF SIGNAL:2"),
        };

    /// <summary>"45 degrees right", "30 degrees left", "ahead", "behind", or "no signal".</summary>
    public static string Bearing(double signal, double radial)
    {
        if (signal == 0)
            return "no signal";
        int relative = (int)Math.Round(((radial % 360) + 540) % 360 - 180);
        return relative switch
        {
            0 => "ahead",
            180 or -180 => "behind",
            > 0 => $"{relative.ToString(CultureInfo.InvariantCulture)} degrees right",
            _ => $"{(-relative).ToString(CultureInfo.InvariantCulture)} degrees left",
        };
    }

    private static Dictionary<string, (A300Radio, int)> Knobs()
    {
        var knobs = new Dictionary<string, (A300Radio, int)>(StringComparer.Ordinal);
        void Add(string radio, int window, params string[] keys)
        {
            foreach (var key in keys)
                knobs[key] = (Named(radio), window);
        }
        Add("VHF 1", 1, "A300_CPT_VHF1_MHZ", "A300_CPT_VHF1_KHZ");
        Add("VHF 1", 2, "A300_CPT_VHF2_MHZ", "A300_CPT_VHF2_KHZ");
        Add("VHF 2", 1, "A300_FO_VHF1_MHZ", "A300_FO_VHF1_KHZ");
        Add("VHF 2", 2, "A300_FO_VHF2_MHZ", "A300_FO_VHF2_KHZ");
        Add("ADF 1", 1, "A300_CPT_ADF1_BIG", "A300_CPT_ADF1_MED", "A300_CPT_ADF1_SMALL");
        Add("ADF 1", 2, "A300_CPT_ADF2_BIG", "A300_CPT_ADF2_MED", "A300_CPT_ADF2_SMALL");
        Add("ADF 2", 1, "A300_FO_ADF1_BIG", "A300_FO_ADF1_MED", "A300_FO_ADF1_SMALL");
        Add("ADF 2", 2, "A300_FO_ADF2_BIG", "A300_FO_ADF2_MED", "A300_FO_ADF2_SMALL");
        return knobs;
    }
}

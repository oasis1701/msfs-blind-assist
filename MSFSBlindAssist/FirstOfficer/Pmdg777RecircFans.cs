namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The PMDG 777's recirculation fans, per variant. The passenger 777s (-200ER, -200LR, -300ER) have
/// an UPPER and a LOWER switch (<c>EVT_OH_AIRCOND_RECIRC_FAN_UPP/LWR_SWITCH</c>, SDK +142/+143, read
/// back on <c>AIR_RecircFan_Sw_On[2]</c>, "upper / lower"); the 777F has one RECIRC FANS switch
/// (+1052). Each cockpit model carries only its own variant's switches (<c>switch_142_a</c> and
/// <c>switch_143_a</c> in the 77W, 77ER and 77L, <c>switch_1052_a</c> in the 77F; checked in the
/// installed packages on 2026-10-18). The First Officer once wrote the freighter's switch on every
/// variant, unverified, and latched "Recirculation fans: ON" over fans it never moved.
///
/// The variant is the CDA's own <c>AircraftModel</c> field (1 -200, 2 -200ER, 3 -300, 4 -200LR,
/// 5 777F, 6 -300ER), never the ICAO type: the 777F and the 777-200LR both declare B77L
/// (PMDG777Definition.GetAircraftModel). Pure logic.
/// </summary>
public static class Pmdg777RecircFans
{
    /// <summary>Flow pseudo-key: the executor writes the loaded variant's own switches.</summary>
    public const string PseudoKey = "RECIRC_FANS";

    /// <summary>Synthetic state field: 1 = the variant's recirculation fans on, 0 = not, NaN = not
    /// known yet (no CDA snapshot, or a model the SDK does not list).</summary>
    public const string OnField = "FO_RECIRC_FANS_ON";

    public const string ModelField = "AircraftModel";
    public const string UpperField = "AIR_RecircFan_Sw_On_0";
    public const string LowerField = "AIR_RecircFan_Sw_On_1";

    public const string UpperEvent = "EVT_OH_AIRCOND_RECIRC_FAN_UPP_SWITCH";
    public const string LowerEvent = "EVT_OH_AIRCOND_RECIRC_FAN_LWR_SWITCH";
    public const string FreighterEvent = "EVT_OH_AIRCOND_RECIRC_FANS_SWITCH";

    /// <summary>SDK <c>AircraftModel</c> of the 777F.</summary>
    public const int FreighterModel = 5;

    // A model the SDK lists, freighter or not; null for anything else (0, or NaN before the first
    // CDA snapshot).
    private static bool? IsFreighter(double model)
    {
        if (double.IsNaN(model)) return null;
        int m = (int)Math.Round(model);
        return m is >= 1 and <= 6 ? m == FreighterModel : null;
    }

    /// <summary>The writes that set the loaded variant's fans to <paramref name="position"/> (0 off,
    /// 1 on): UPPER then LOWER on a passenger 777, the single switch on the 777F. Empty while the
    /// variant is not known, so nothing is written to a switch the aircraft may not have.</summary>
    public static IReadOnlyList<(string EventName, int? TargetValue)> Writes(double aircraftModel, int position) =>
        IsFreighter(aircraftModel) switch
        {
            true => new (string, int?)[] { (FreighterEvent, position) },
            false => new (string, int?)[] { (UpperEvent, position), (LowerEvent, position) },
            null => Array.Empty<(string, int?)>(),
        };

    /// <summary>
    /// 1 when the loaded variant's fans are on, 0 when not, NaN while the model or either reading is
    /// unknown. A passenger 777 needs both UPPER and LOWER on. On the 777F either reading on is
    /// enough: the SDK names the two readings only "upper / lower", and which of them its single
    /// switch drives has not been measured.
    /// </summary>
    public static double OnValue(Func<string, double> read)
    {
        double upper = read(UpperField), lower = read(LowerField);
        bool? freighter = IsFreighter(read(ModelField));
        if (freighter == null || double.IsNaN(upper) || double.IsNaN(lower)) return double.NaN;
        bool on = freighter.Value ? upper > 0.5 || lower > 0.5 : upper > 0.5 && lower > 0.5;
        return on ? 1 : 0;
    }
}

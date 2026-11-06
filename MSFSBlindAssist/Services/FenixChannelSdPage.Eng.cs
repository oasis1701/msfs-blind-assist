namespace MSFSBlindAssist.Services;

// The ENG page (8) of the Fenix SD: the fuel used and each engine's oil, vibration, nacelle and starting values, as the
// SD draws them, engine 1 then engine 2. Written as the STATUS page is (FenixChannelSdPage.Status.cs): one reader, every
// field read before any row is built, and the reader's problem returned as the page's one row.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The ENG page's rows, each a pair, engine 1 then engine 2 ("OIL QT: 13.9, 14.1"): the fuel used by each engine
    /// (<c>F.USED KG</c>, or <c>LBS</c>, from the weight unit, 154), the oil quantity (<c>OIL QT</c>, to one decimal),
    /// pressure (<c>OIL PSI</c>) and temperature (<c>OIL °C</c>), then the vibration of the N1 and N2 shafts (<c>VIB N1</c>
    /// and <c>VIB N2</c>, to one decimal). While the engine message says the SD draws the nacelle (129.7) the page adds
    /// <c>NAC °C</c>, and while it says the SD draws the start (129.6) the igniters in use (<c>IGN</c>: "A", "B", "A B"
    /// or "off"), the start valve (<c>start valve</c>, in the valve words) and the starter pressure (<c>PSI</c>). The rows
    /// speak what the Fenix sends: an oil quantity or pressure the SD makes pulse (129.n.26, 129.n.29) gains " pulsing"
    /// right after its value (<c>OIL QT: 12.8 pulsing, 13.0</c>); the other rows are values the app builds.
    /// </summary>
    /// <remarks>
    /// <para>Both engines, and the fuel used, must be in the push: a part left out is "no field 129.2" (or 129.1, 129, 134,
    /// 134.13), and a field in another wire shape is that field's wrong shape, both as one row with no values, never an
    /// engine at its defaults. The page returns that problem and never reports it: the window reports it once, under the
    /// page's name. An unknown start valve value reads "not available" and is reported without stopping the
    /// page.</para>
    /// <para>A field a flag keeps off the page is not read, so that a value the SD does not draw can neither stop the page
    /// nor be reported. The two flags, and which letter goes with which igniter (17 is A, 18 is B), are inferred from
    /// the contract's names and the plan's text: Task 1 measures what the SD draws with each, and a difference changes the
    /// rows and their tests together.</para>
    /// </remarks>
    internal static FenixChannelRows EngRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);

        FenixChannelSdReader engines = remote.Message(FenixChannelFields.Remote.EngineState);
        FenixChannelSdReader left = engines.Message(FenixChannelFields.EngineControlState.Left);
        FenixChannelSdReader right = engines.Message(FenixChannelFields.EngineControlState.Right);
        bool showStarting = engines.Bool(FenixChannelFields.EngineControlState.ShowStarting);
        bool showNacelle = engines.Bool(FenixChannelFields.EngineControlState.ShowNacelle);

        FenixChannelSdReader used = remote.Message(FenixChannelFields.Remote.FuelState)
            .Message(FenixChannelFields.FuelState.Used);
        double usedLeft = used.Double(FenixChannelFields.FuelUsedData.Engine1);
        double usedRight = used.Double(FenixChannelFields.FuelUsedData.Engine2);
        string unit = FenixChannelSd.WeightUnit(remote);

        EngReading first = ReadEng(left, showNacelle, showStarting);
        EngReading second = ReadEng(right, showNacelle, showStarting);

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        var rows = new List<string>
        {
            $"F.USED {unit}: {FenixChannelSd.Whole(usedLeft)}, {FenixChannelSd.Whole(usedRight)}",
            $"OIL QT: {FenixChannelSd.OneDecimal(first.OilQuantity)}{EngPulsing(first.OilQtyPulse)}, "
                + $"{FenixChannelSd.OneDecimal(second.OilQuantity)}{EngPulsing(second.OilQtyPulse)}",
            $"OIL PSI: {FenixChannelSd.Whole(first.OilPressure)}{EngPulsing(first.OilPressPulse)}, "
                + $"{FenixChannelSd.Whole(second.OilPressure)}{EngPulsing(second.OilPressPulse)}",
            $"OIL °C: {FenixChannelSd.Whole(first.OilTemperature)}, {FenixChannelSd.Whole(second.OilTemperature)}",
            $"VIB N1: {FenixChannelSd.OneDecimal(first.VibrationN1)}, {FenixChannelSd.OneDecimal(second.VibrationN1)}",
            $"VIB N2: {FenixChannelSd.OneDecimal(first.VibrationN2)}, {FenixChannelSd.OneDecimal(second.VibrationN2)}"
        };

        if (showNacelle)
        {
            rows.Add($"NAC °C: {FenixChannelSd.Whole(first.NacelleTemp)}, {FenixChannelSd.Whole(second.NacelleTemp)}");
        }

        if (showStarting)
        {
            rows.Add($"IGN: {EngIgniters(first)}, {EngIgniters(second)}");
            rows.Add($"start valve: {first.StartValve}, {second.StartValve}");
            rows.Add($"PSI: {FenixChannelSd.Whole(first.StarterPsi)}, {FenixChannelSd.Whole(second.StarterPsi)}");
        }

        return FenixChannelRows.Of(rows);
    }

    /// <summary>The ENG page (8).</summary>
    static partial void ComposeEng(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = EngRows(ctx);

    // What the page reads of one engine. The nacelle, the igniters, the start valve and the starter pressure stay at their
    // defaults while the SD does not draw them.
    private readonly record struct EngReading(
        double OilQuantity, double OilPressure, double OilTemperature, double VibrationN1, double VibrationN2,
        double NacelleTemp, bool IgniterA, bool IgniterB, string StartValve, double StarterPsi, bool OilQtyPulse,
        bool OilPressPulse);

    // " pulsing" right after an oil value the SD makes pulse, no comma (shared with the CRUISE page's OIL QT).
    private static string EngPulsing(bool pulse) => pulse ? " pulsing" : "";

    private static EngReading ReadEng(FenixChannelSdReader engine, bool showNacelle, bool showStarting)
    {
        double oilQuantity = engine.Double(FenixChannelFields.SingleEngineState.OilQuantity);
        double oilPressure = engine.Double(FenixChannelFields.SingleEngineState.OilPressure);
        double oilTemperature = engine.Double(FenixChannelFields.SingleEngineState.OilTemperature);
        double vibrationN1 = engine.Double(FenixChannelFields.SingleEngineState.VibrationN1);
        double vibrationN2 = engine.Double(FenixChannelFields.SingleEngineState.VibrationN2);
        // Pulse fields checked against the installed Fenix 3.0.0.7613 on 2026-11-03 with the team's metadata tool ([FNX-23]).
        bool oilQtyPulse = engine.Bool(FenixChannelFields.SingleEngineState.OilQtyPulse);
        bool oilPressPulse = engine.Bool(FenixChannelFields.SingleEngineState.OilPressPulse);

        double nacelle = showNacelle ? engine.Double(FenixChannelFields.SingleEngineState.NacelleTemp) : 0;

        bool igniterA = false;
        bool igniterB = false;
        string startValve = "";
        double starterPsi = 0;
        if (showStarting)
        {
            igniterA = engine.Bool(FenixChannelFields.SingleEngineState.Igniter1);
            igniterB = engine.Bool(FenixChannelFields.SingleEngineState.Igniter2);
            startValve = engine.Valve(FenixChannelFields.SingleEngineState.StartValve);
            starterPsi = engine.Double(FenixChannelFields.SingleEngineState.StarterPsi);
        }

        return new EngReading(
            oilQuantity, oilPressure, oilTemperature, vibrationN1, vibrationN2, nacelle, igniterA, igniterB, startValve,
            starterPsi, oilQtyPulse, oilPressPulse);
    }

    // The igniters in use as the SD letters them: "A", "B", "A B", or "off" when neither is.
    private static string EngIgniters(EngReading engine) => (engine.IgniterA, engine.IgniterB) switch
    {
        (true, true) => "A B",
        (true, false) => "A",
        (false, true) => "B",
        _ => "off"
    };
}

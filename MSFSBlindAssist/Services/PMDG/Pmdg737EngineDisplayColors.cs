namespace MSFSBlindAssist.Services.PMDG;

/// <summary>How PMDG draws a gauge's value: normal, amber, red, or (vibration above its limit) high: in
/// reverse video, a white box, PMDG's only mark for it.</summary>
public enum GaugeColor { Normal, Amber, Red, High }

/// <summary>The engine gauges PMDG colours.</summary>
public enum Gauge { N1, Egt, N2, OilPressure, OilTemperature, Vibration }

/// <summary>
/// PMDG's own colour tests for the N1, EGT and N2 gauges and the oil pressure, oil temperature and vibration
/// readouts, applied to the limits in the sample: N1 and N2 red
/// above their limit or while their exceedance latch is set (PMDG's engine model keeps a readout red after
/// an exceedance: the EGT digits, the N1 and N2 boxes); EGT red above its red line or while latched, else
/// amber above its maximum continuous; oil pressure,
/// with the engine running, amber below the engine's amber limit and red below the red line; oil
/// temperature amber above its amber limit and red above the red one; vibration high above its limit.
/// Every comparison is PMDG's strict one. Not modelled (known differences): PMDG's EGT amber inhibit for
/// the first minutes of a takeoff under two of its options. Pure.
/// </summary>
public static class Pmdg737EngineDisplayColors
{
    /// <summary>The colour of <paramref name="gauge"/> for engine index <paramref name="engine"/> (0 is engine 1).</summary>
    public static GaugeColor ColorOf(Gauge gauge, EngineDisplaySample s, int engine)
    {
        var e = s.Engines[engine];
        return gauge switch
        {
            Gauge.N1 => e.N1 > s.N1RedLine || e.N1RedLatch ? GaugeColor.Red : GaugeColor.Normal,
            Gauge.Egt => e.Egt > s.EgtRedLine || e.EgtRedLatch ? GaugeColor.Red
                : e.Egt > s.EgtMaxContinuous ? GaugeColor.Amber : GaugeColor.Normal,
            Gauge.N2 => e.N2 > s.N2Max || e.N2RedLatch ? GaugeColor.Red : GaugeColor.Normal,
            Gauge.OilPressure => e.Running && e.OilPressure < e.OilPressureAmber
                ? e.OilPressure < s.OilPressureRed ? GaugeColor.Red : GaugeColor.Amber
                : GaugeColor.Normal,
            Gauge.OilTemperature => e.OilTemperature > s.OilTemperatureAmber
                ? e.OilTemperature > s.OilTemperatureRed ? GaugeColor.Red : GaugeColor.Amber
                : GaugeColor.Normal,
            Gauge.Vibration => e.Vibration > s.VibrationLimit ? GaugeColor.High : GaugeColor.Normal,
            _ => GaugeColor.Normal,
        };
    }

    /// <summary>The word for a colour in a list row or a sentence.</summary>
    public static string Word(GaugeColor color) => color switch
    {
        GaugeColor.Amber => "amber",
        GaugeColor.Red => "red",
        GaugeColor.High => "high",
        _ => "normal",
    };
}

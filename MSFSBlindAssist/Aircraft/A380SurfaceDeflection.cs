using System;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// How an A380 control-surface deflection is spoken.
///
/// ⚠️ FBW MIRRORS THE LEFT AILERON, AND READING IT VERBATIM ANNOUNCED A DROOP AS A ROLL.
///
/// Their own <c>a380_systems_wasm/src/ailerons.rs</c> says it in a comment - "we just invert
/// left side direction" - and only the LEFT mapping carries the leading minus:
///
///   HYD_AIL_LEFT_OUTWARD_DEFLECTION   -> -hyd_deflection_to_msfs_deflection(...)
///   HYD_AIL_RIGHT_OUTWARD_DEFLECTION  ->  hyd_deflection_to_msfs_deflection(...)
///
/// So both wings drooping DOWN together - which the A380 does whenever the flaps extend -
/// reaches MSFSBA as one positive number and one negative one. Measured live at flaps 2:
/// left 16/16/12, right -17/-17/-20, with YOKE X POSITION at -8.3e-7 and the stock
/// AILERON POSITION at -3.5e-6, i.e. no roll commanded at all. Printed as bare signed
/// percentages that reads exactly like a roll input with the sidestick centred, and a pilot
/// reasonably concluded the aeroplane was broken.
///
/// ⚠️ THE ELEVATORS ARE NOT MIRRORED - <c>elevators.rs</c> applies the same conversion to
/// both sides with no negation anywhere - so they must NOT be passed through the mirrored
/// form. Getting either backwards announces a droop as a roll or a roll as a droop, on the
/// one page a pilot opens to find out what the surfaces are doing.
/// </summary>
public static class A380SurfaceDeflection
{
    /// <summary>
    /// A surface in its own published frame. UP IS POSITIVE on the un-inverted side:
    /// <c>hyd_deflection_to_msfs_deflection</c> maps [0,1] onto (hyd*50 - 20)/30 with 30
    /// degrees of UP travel against 20 of DOWN, so the positive extreme is up.
    ///
    /// Zero reads "neutral" rather than "0 percent up" - a surface at rest has no direction,
    /// and naming one there is the kind of detail that makes a scan sound wrong.
    /// </summary>
    public static string Describe(double normalized)
    {
        int pct = (int)Math.Round(Math.Abs(normalized) * 100.0);
        return pct == 0 ? "neutral" : $"{pct} percent {(normalized > 0 ? "up" : "down")}";
    }

    /// <summary>
    /// A surface FBW publishes inverted - the left aileron, and nothing else. Negating undoes
    /// their inversion and puts both wings in one physical frame, so a symmetric droop reads
    /// as two downs rather than an up and a down.
    /// </summary>
    public static string DescribeMirrored(double normalized) => Describe(-normalized);
}

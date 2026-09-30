namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The stabiliser-trim call-out for a 737 whose add-on publishes its trim in UNITS (the scale the
/// cockpit indicator and the FMC's takeoff trim use): the PMDG 737's L-var <c>ElevTrimTT</c> and the
/// iFly 737 MAX's SDK field <c>Stabilizer_Trim_Pointer_Status</c>. Both airframes use this one rule
/// so they can never say trim two different ways.
///
/// <para>
/// Spoken as "Trim 5.3" — tenths, no unit word, invariant culture (a trim value reads with a dot, as
/// the cockpit writes it). The first sample is a silent baseline. A new tenth is spoken only once
/// the value is the shared hysteresis past the 0.05 midpoint, judged on the RAW value: comparing two
/// rounded values let a trim resting on x.x5 flip every sample.
/// </para>
/// <para>
/// The PMDG 777 is different on purpose: it publishes no units field, so it converts the stock
/// degrees and speaks quarter units with the word "units" (<see cref="Pmdg777StabTrim"/>).
/// </para>
/// </summary>
public sealed class StabTrimUnitsCallout
{
    private double _last = double.NaN;

    /// <summary>Forget the last value spoken, so the next sample is a silent baseline again.</summary>
    public void Reset() => _last = double.NaN;

    /// <summary>
    /// Take <paramref name="units"/> as the baseline without speaking it. For a source whose
    /// opening value never arrives as a sample (the iFly SDK's initial snapshot is dropped before
    /// the call-out sees it): without a seed, the pilot's first real move would become the silent
    /// baseline instead of being spoken.
    /// </summary>
    public void Seed(double units)
    {
        if (double.IsFinite(units)) _last = Math.Round(units, 1);
    }

    /// <summary>
    /// A new trim sample in units. Returns the sentence to speak, or null when nothing is said.
    /// </summary>
    /// <param name="hysteresis">How far past a step boundary the value must travel
    /// (<c>BaseAircraftDefinition.TrimHysteresis</c>).</param>
    public string? Next(double units, double hysteresis)
    {
        if (!double.IsFinite(units)) return null;
        double rounded = Math.Round(units, 1);
        if (double.IsNaN(_last))
        {
            _last = rounded;
            return null;
        }
        if (Math.Abs(units - _last) < 0.05 + hysteresis)
            return null;
        _last = rounded;
        return FormattableString.Invariant($"Trim {rounded:F1}");
    }
}

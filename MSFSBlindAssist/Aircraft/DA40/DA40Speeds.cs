namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// DA40 V-speeds, from the COWS POH section IV (Important Speeds and Operating
/// Limitations) and cross-checked against AFM section 2.
///
/// These exist because the Airbus characteristic speeds the output-mode keys were built
/// for — green dot, S, F, VLS — have no meaning on a DA40. The same keys carry these
/// instead, so the pilot gets the speeds this aeroplane is actually flown on.
///
/// Where the POH quotes a range, the figure used is the one a pilot would fly to:
/// the higher end of an approach-speed range (the safe end at weight) and the AFM's
/// manoeuvring speed at maximum mass.
/// </summary>
public sealed record DA40Speeds
{
    public double Vr { get; init; }
    public double VyFlapsTakeoff { get; init; }
    public double VyFlapsUp { get; init; }
    public double Vref { get; init; }
    public double VbestGlide { get; init; }
    public double VrefShortField { get; init; }
    public double Va { get; init; }
    public double VfeTakeoff { get; init; }
    public double VfeLanding { get; init; }
    public double Vno { get; init; }
    public double Vne { get; init; }

    /// <summary>DA40-NG, 2888 lb. AFM section 2 "Limitations".</summary>
    private static readonly DA40Speeds Ng = new()
    {
        Vr = 67,
        VyFlapsTakeoff = 72,
        VyFlapsUp = 88,
        Vref = 77,               // POH range 66-77
        VbestGlide = 88,
        VrefShortField = 69,     // POH range 63-69
        Va = 113,                // 111 / 108 / 113 by mass; this is the figure at max
        VfeTakeoff = 110,
        VfeLanding = 98,
        Vno = 130,
        Vne = 172
    };

    /// <summary>DA40-XLS, 2646 lb. POH sections 2, 4 and 5 of the AFM.</summary>
    private static readonly DA40Speeds Xls = new()
    {
        Vr = 59,
        VyFlapsTakeoff = 67,     // POH range 66-67
        VyFlapsUp = 73,
        Vref = 73,
        VbestGlide = 76,         // POH range 73-76
        VrefShortField = 64,     // POH range 62-64
        Va = 111,                // 94 below 2284 lb, 111 above
        VfeTakeoff = 108,
        VfeLanding = 91,
        Vno = 129,
        Vne = 178
    };

    public static DA40Speeds For(DA40Variant variant)
        => variant == DA40Variant.NG ? Ng : Xls;

    /// <summary>True when the airframe's never-exceed speed has been passed.</summary>
    public bool ExceedsVne(double kias) => kias > Vne;

    /// <summary>
    /// True when the current flap setting's limit speed has been passed. Flap index
    /// follows SimConnect FLAPS HANDLE INDEX: 0 = UP, 1 = T/O, 2 = LDG.
    /// </summary>
    public bool ExceedsVfe(double kias, int flapIndex) => flapIndex switch
    {
        1 => kias > VfeTakeoff,
        2 => kias > VfeLanding,
        _ => false
    };
}

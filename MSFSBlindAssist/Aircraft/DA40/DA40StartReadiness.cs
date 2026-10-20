namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// What the start-readiness row reads. Every figure is the aircraft's own; the priming
/// state is <see cref="DA40Priming"/>'s classification, or null before the manifold
/// evaporation has been read (it is 0 until the model has run a tick).
/// </summary>
public sealed record DA40StartInputs(
    bool EngineRunning, bool MasterOn, double SpreadPressure, int SelectorPosition,
    double FeedQuantityGal, bool PumpOn, double FuelPressureBar, double BoilFactor,
    PrimeState? Prime);

/// <summary>
/// The first thing stopping an XLS start, in the order a pilot can act on them
/// (docs/da40-xls-variables.md — the cold start, and the state trap above it).
///
/// The ORDER is the point. Master first, because with the bus dead the model's fuel logic
/// does not tick at all: measured unpowered, the servo charge sat at 21.6 g against a cap
/// that computes to 0.51 and fuel pressure read 19.75 — numbers frozen from before the
/// reload, and nothing below may be judged from them. Then the variations, because a zero
/// <c>FUEL_SPREAD_PRESSURE</c> multiplies into zero pressure, zero feed and zero jet, and
/// the cockpit shows nothing for it — this is the one blocker whose FIX is named, because
/// it is the one nothing in the aeroplane names. Then the selector before the tank (an OFF
/// selector reads an empty feed), vapour before pressure (it is why the pressure is low),
/// and the priming state last: the answer, once everything above it is clear.
/// </summary>
public static class DA40StartReadiness
{
    /// <summary>The idle jet is scaled by min(pressure / 0.8, 1) (Logic 382): under this it starves.</summary>
    public const double JetPressureGateBar = 0.8;

    /// <summary>Under this the vapour factor is eating the pressure (1.0 is none).</summary>
    public const double NoVapourFactor = 0.995;

    public const int SelectorOff = 2;

    /// <summary>
    /// Why a fuel-derived reading cannot be trusted, or null when it can. Only a master KNOWN
    /// to be off freezes the logic; unknown (nothing read yet) renders normally rather than
    /// claiming the master is off for the first second of every session.
    /// </summary>
    public static string? FrozenReason(bool? masterOn)
        => masterOn == false ? "Not computed, master off" : null;

    public static string Describe(DA40StartInputs i)
    {
        if (i.EngineRunning) return "Engine running";
        if (!i.MasterOn) return "Master off";
        if (i.SpreadPressure <= 0) return "Engine variations not generated - Clear Engine Damage on the Reset panel";
        if (i.SelectorPosition == SelectorOff) return "Fuel selector off";
        if (i.FeedQuantityGal <= 0) return "Selected tank empty";
        if (i.BoilFactor < NoVapourFactor) return "Vapour in the fuel lines";
        if (i.FuelPressureBar < JetPressureGateBar)
            return i.PumpOn ? "No fuel pressure" : "No fuel pressure - electric pump off";

        return i.Prime switch
        {
            PrimeState.Primed => "Ready to crank",
            PrimeState.Flooded => "Flooded",
            PrimeState.NotPrimed => "Not primed",
            _ => "Fuel pressure up"
        };
    }
}

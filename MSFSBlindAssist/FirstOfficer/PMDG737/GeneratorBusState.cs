using System;
using System.Collections.Generic;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// Whether each engine generator is powering its bus, read the way a crew reads it: from
/// its GEN OFF BUS light. The raw <c>ELEC_GenSw</c> struct bool reads 1 at rest, and the app
/// no longer remembers a GEN switch position (#295, which made GEN 1/2 stateless push
/// pairs). Pure logic; <see cref="AircraftStateEvaluator"/> feeds it the live CDA fields
/// and publishes the verdicts as the synthetic state fields <see cref="OnBusFields"/>.
/// </summary>
public static class GeneratorBusState
{
    /// <summary>Synthetic state fields, per engine generator: 1 = on its bus, 0 = not, NaN = no data yet.</summary>
    public static IReadOnlyList<string> OnBusFields { get; } = new[] { "FO_GEN1_ON_BUS", "FO_GEN2_ON_BUS" };

    /// <summary>The GEN OFF BUS light per engine generator: lit while that generator is not powering its bus.</summary>
    public static IReadOnlyList<string> OffBusLightFields { get; } =
        new[] { "ELEC_annunGEN_BUS_OFF_0", "ELEC_annunGEN_BUS_OFF_1" };

    /// <summary>Each generator's AC transfer bus (ELEC_BusPowered 7 and 8).</summary>
    public static IReadOnlyList<string> TransferBusFields { get; } = new[] { "ELEC_BusPowered_7", "ELEC_BusPowered_8" };

    /// <summary>
    /// Generator <paramref name="gen"/> (0 or 1) is on its bus: its GEN OFF BUS light is DARK
    /// and its transfer bus is powered. The bus half stops an unpowered aircraft, where every
    /// light is dark, reading "on"; the light half stops an APU-fed bus reading "on" (the engine
    /// generator's light stays lit, #295's in-sim test 2026-10-11). A NaN on either side reads
    /// "not on bus".
    /// </summary>
    public static bool IsOnBus(int gen, Func<string, double> readField)
        => readField(OffBusLightFields[gen]) < 0.5 && readField(TransferBusFields[gen]) > 0.5;

    // -----------------------------------------------------------------------
    // Power for the Shutdown flow's start levers (PR #160 second review). Cutting the levers
    // takes the engine generators off their transfer buses, and nothing picks the buses up by
    // itself: on the 737 the APU generators must be pressed on first, and only once the APU
    // generator is up. Synthetic fields <see cref="LoadOffEnginesField"/> and
    // <see cref="ShutdownPowerReadyField"/>, published like <see cref="OnBusFields"/>.
    // -----------------------------------------------------------------------

    /// <summary>Synthetic: 1 when <see cref="IsLoadOffEngines"/>, 0 when not, NaN = no data yet.</summary>
    public const string LoadOffEnginesField = "FO_LOAD_OFF_ENGINES";

    /// <summary>Synthetic: 1 when <see cref="IsShutdownPowerReady"/>, 0 when not, NaN = no data yet.</summary>
    public const string ShutdownPowerReadyField = "FO_SHUTDOWN_POWER_READY";

    /// <summary>The APU GEN OFF BUS light: lit while the APU generator is up and powering no bus.</summary>
    public const string ApuGenOffBusLightField = "ELEC_annunAPU_GEN_OFF_BUS";

    /// <summary>
    /// Generator <paramref name="gen"/>'s transfer bus is powered WITHOUT that generator: its GEN
    /// OFF BUS light is LIT and the bus powered, so the APU generator or ground power carries it
    /// (or, on one side only, the other engine generator through the bus transfer). NaN reads "no".
    /// </summary>
    public static bool IsCarriedWithoutItsGenerator(int gen, Func<string, double> readField)
        => readField(OffBusLightFields[gen]) > 0.5 && readField(TransferBusFields[gen]) > 0.5;

    /// <summary>
    /// The load is off the engine generators: both transfer buses powered with both GEN OFF BUS
    /// lights lit, so neither engine generator carries anything and the APU generator or ground
    /// power carries both buses. What the start levers need before CUTOFF; no GPU-versus-APU
    /// discriminator exists on the NG3, and neither is needed here.
    /// </summary>
    public static bool IsLoadOffEngines(Func<string, double> readField)
        => IsCarriedWithoutItsGenerator(0, readField) && IsCarriedWithoutItsGenerator(1, readField);

    /// <summary>
    /// Another source can take the load: the APU generator is up (its OFF BUS light lit), or a
    /// transfer bus is already carried without its engine generator (the APU generator on one bus
    /// after a transfer that half took, ground power the pilot put on the buses, a re-run after a
    /// completed transfer). An engine generator that failed while the other carries both buses
    /// reads ready too; the transfer's own check (<see cref="IsLoadOffEngines"/>) then stops the
    /// Shutdown flow before the levers.
    /// </summary>
    public static bool IsShutdownPowerReady(Func<string, double> readField)
        => readField(ApuGenOffBusLightField) > 0.5
           || IsCarriedWithoutItsGenerator(0, readField) || IsCarriedWithoutItsGenerator(1, readField);
}

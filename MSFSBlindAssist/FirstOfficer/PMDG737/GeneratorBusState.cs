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
}

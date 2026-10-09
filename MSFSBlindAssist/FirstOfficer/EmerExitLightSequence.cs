namespace MSFSBlindAssist.FirstOfficer;

/// <summary>What a single step of the emergency-exit light sequence drives.</summary>
public enum EmerExitStepKind
{
    /// <summary>The guard itself (CDA momentary event).</summary>
    Guard,
    /// <summary>The switch under the guard (absolute-target TransmitClientEvent).</summary>
    Switch,
}

/// <summary>One ordered step: which actuator, and the value to send it.</summary>
public readonly record struct EmerExitStep(EmerExitStepKind Kind, int Value);

/// <summary>
/// Pure sequencing for the PMDG 777 GUARDED emergency-exit light switch.
/// </summary>
public static class EmerExitLightSequence
{
    /// <summary>Switch position: lights off.</summary>
    public const int Off = 0;
    /// <summary>Switch position: armed. The normal, guard-CLOSED position.</summary>
    public const int Armed = 1;
    /// <summary>Switch position: lights on.</summary>
    public const int On = 2;

    /// <summary>Guard value for CLOSED.</summary>
    public const int GuardClosed = 0;
    /// <summary>Guard value for OPEN.</summary>
    public const int GuardOpen = 1;

    /// <summary>Ordered actuator steps to move from <paramref name="current"/> to
    /// <paramref name="target"/>. Empty when already there.</summary>
    public static IReadOnlyList<EmerExitStep> Plan(int current, int target, bool haveGuard)
    {
        if (current == target) return Array.Empty<EmerExitStep>();

        // No guard event resolved — move the switch alone rather than doing nothing.
        if (!haveGuard) return new[] { new EmerExitStep(EmerExitStepKind.Switch, target) };

        // Branch on the TARGET, not the origin: ARMED is the guard-closed position,
        // OFF and ON both sit outside the guard.
        return target != Armed
            // Leaving the guarded position: lift the guard, let it land, then move.
            ? new[]
            {
                new EmerExitStep(EmerExitStepKind.Guard, GuardOpen),
                new EmerExitStep(EmerExitStepKind.Switch, target),
            }
            // Back to ARMED: move the switch first, then close the guard over it.
            : new[]
            {
                new EmerExitStep(EmerExitStepKind.Switch, Armed),
                new EmerExitStep(EmerExitStepKind.Guard, GuardClosed),
            };
    }
}

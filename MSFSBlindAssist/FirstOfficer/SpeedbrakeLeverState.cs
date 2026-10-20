using System.Collections.Generic;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>Where a Boeing speed-brake lever is, as the First Officer judges it.</summary>
public enum SpeedbrakeLeverPosition
{
    /// <summary>Not read yet. Never ticks, never un-ticks, never clicked blind.</summary>
    Unknown,
    /// <summary>Anything short of ARM (a hardware axis parks the 777 at 22 for DOWN).</summary>
    Down,
    /// <summary>Exactly at ARM, within the ARM row's own exact tolerance.</summary>
    Armed,
    /// <summary>Anything past ARM: the spoilers are up.</summary>
    Deployed,
}

/// <summary>What an arm action may do with the lever as it is.</summary>
public enum SpeedbrakeArmDecision
{
    /// <summary>Armed already (and, where the aircraft has one, the ARMED light lit): no click.</summary>
    AlreadyArmed,
    /// <summary>Click / write ARM.</summary>
    Arm,
    /// <summary>The speed brake is extended: clicking ARM would retract it. Never touch it.</summary>
    LeaveAlone,
    /// <summary>The lever has not been read: never click a lever nobody can see.</summary>
    Unreadable,
}

/// <summary>One aircraft's speed-brake lever: main's detent table, its settle tolerance, and the
/// key the First Officer reads it from.</summary>
public sealed record SpeedbrakeLeverTable(IReadOnlyList<PmdgLeverDetent> Detents, double SettleTolerance, string LeverKey)
{
    /// <summary>The ARM detent's rest value (every table's second row).</summary>
    public double ArmValue => Detents[1].Value;
}

/// <summary>
/// The First Officer's speed-brake truth for the PMDG 737, PMDG 777 and iFly 737 MAX, judged on
/// main's measured detent tables (PR #261, 2026-09-30, hydraulics pressurised). ARM is EXACT on
/// all three (one step past it the spoilers are already up), so "armed" is the lever AT ARM,
/// never a light alone: the 737's ARMED light stays lit to about 342 and the iFly's all the way
/// to 224, and the 777 SDK byte truncates 201-203 to exactly "armed". A deployed speed brake is
/// never moved: clicking ARM over it retracts it.
/// </summary>
public static class SpeedbrakeLeverState
{
    /// <summary>FO synthetic: the raw lever value, NaN until read (the PMDG evaluators).</summary>
    public const string LeverField = "FO_SPEEDBRAKE_LEVER";

    /// <summary>FO synthetic: 1 when the lever is exactly at ARM and the ARMED light is lit, 0
    /// when either is not, NaN while either is unknown (the 737 and iFly evaluators).</summary>
    public const string ArmedField = "FO_SPEEDBRAKE_ARMED";

    /// <summary>The verified-arm flow step key every Boeing executor intercepts.</summary>
    public const string ArmPseudoKey = "SPEEDBRAKE_ARM";

    /// <summary>Spoken when the First Officer leaves a deployed speed brake alone.</summary>
    public const string LeaveAloneText = "Speedbrake extended, not armed. Left as it is.";

    /// <summary>PMDG 737 NG3: L:switch_679_73X, read through main's MON_PMDG737_SpeedBrake key.</summary>
    public static readonly SpeedbrakeLeverTable Pmdg737 =
        new(PmdgSpeedBrakeLever.Ng3, PmdgSpeedBrakeLever.Ng3SettleTolerance, "MON_PMDG737_SpeedBrake");

    /// <summary>PMDG 777: L:switch_498_a, read through main's FCTL_Speedbrake key.</summary>
    public static readonly SpeedbrakeLeverTable Pmdg777 =
        new(PmdgSpeedBrakeLever.B777, PmdgSpeedBrakeLever.B777SettleTolerance, "FCTL_Speedbrake");

    /// <summary>iFly 737 MAX: the SDK field Spoiler_Lever_Status.</summary>
    public static readonly SpeedbrakeLeverTable IFly737 =
        new(IFly737SpeedBrakeLever.Detents, IFly737SpeedBrakeLever.SettleTolerance, IFly737SpeedBrakeLever.FieldName);

    /// <summary>Where the lever is: main's PositionIndex rule (a detent within tolerance, DOWN for
    /// anything short of ARM) mapped to four answers.</summary>
    public static SpeedbrakeLeverPosition Classify(SpeedbrakeLeverTable table, double lever)
    {
        if (double.IsNaN(lever)) return SpeedbrakeLeverPosition.Unknown;
        return PmdgSpeedBrakeLever.PositionIndex(table.Detents, lever, table.SettleTolerance) switch
        {
            0 => SpeedbrakeLeverPosition.Down,
            1 => SpeedbrakeLeverPosition.Armed,
            _ => SpeedbrakeLeverPosition.Deployed,
        };
    }

    /// <summary>The <see cref="ArmedField"/> value: the lever exactly at ARM AND the ARMED light lit
    /// (above 0.5: the iFly's DIM counts). NaN while either is unknown.</summary>
    public static double ArmedValue(SpeedbrakeLeverTable table, double lever, double armedLight)
    {
        if (double.IsNaN(lever) || double.IsNaN(armedLight)) return double.NaN;
        return Classify(table, lever) == SpeedbrakeLeverPosition.Armed && armedLight > 0.5 ? 1 : 0;
    }

    /// <summary>What an arm action may do. <paramref name="armedLightLit"/> is true for an aircraft
    /// with no ARMED light (the 777: the lever alone decides); <paramref name="extendedLightLit"/>
    /// false for one with no EXTENDED light.</summary>
    public static SpeedbrakeArmDecision DecideArm(SpeedbrakeLeverPosition position, bool armedLightLit, bool extendedLightLit)
    {
        if (position == SpeedbrakeLeverPosition.Deployed || extendedLightLit) return SpeedbrakeArmDecision.LeaveAlone;
        if (position == SpeedbrakeLeverPosition.Unknown) return SpeedbrakeArmDecision.Unreadable;
        if (position == SpeedbrakeLeverPosition.Armed && armedLightLit) return SpeedbrakeArmDecision.AlreadyArmed;
        return SpeedbrakeArmDecision.Arm;
    }
}

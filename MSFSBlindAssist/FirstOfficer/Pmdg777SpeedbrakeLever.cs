using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The PMDG 777 speed-brake lever as the First Officer reads it: main's <c>L:switch_498_a</c>
/// (key <c>FCTL_Speedbrake</c>, 0 / 200 / 300 / 400), judged on main's
/// <see cref="PmdgSpeedBrakeLever.B777"/> table through <see cref="SpeedbrakeLeverState"/>, so
/// the two can never drift apart again. Never the SDK's <c>FCTL_Speedbrake_Lever</c> byte: it is
/// that value / 4, TRUNCATED, so a lever at 201-203 (spoilers already 34 percent up) reads
/// exactly 50, "armed", and a hardware axis's DOWN at 22 reads 5, "not down" (measured
/// 2026-09-30, docs/pmdg-777.md).
/// </summary>
public static class Pmdg777SpeedbrakeLever
{
    /// <summary>The evaluator field the lever is read through.</summary>
    public const string LeverField = SpeedbrakeLeverState.LeverField;

    public static double DownValue => PmdgSpeedBrakeLever.B777[0].Value;
    public static double ArmedValue => PmdgSpeedBrakeLever.B777[1].Value;
    public static double HalfDeployedValue => PmdgSpeedBrakeLever.B777[2].Value;
    public static double UpValue => PmdgSpeedBrakeLever.B777[^1].Value;

    public static SpeedbrakeLeverPosition Position(double lever) =>
        SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.Pmdg777, lever);

    /// <summary>Anything short of ARM (a hardware axis parks DOWN at 22). False while unread.</summary>
    public static bool IsDown(double lever) => Position(lever) == SpeedbrakeLeverPosition.Down;

    /// <summary>Exactly at ARM. False while unread.</summary>
    public static bool IsArmed(double lever) => Position(lever) == SpeedbrakeLeverPosition.Armed;

    /// <summary>Anything past ARM. False while unread.</summary>
    public static bool IsDeployed(double lever) => Position(lever) == SpeedbrakeLeverPosition.Deployed;
}

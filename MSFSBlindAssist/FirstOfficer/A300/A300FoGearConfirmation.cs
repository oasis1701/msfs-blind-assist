namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// The A300 gear lines' read-only confirmations ([FO-13]), from the lights a pilot reads: down is the lever down
/// and three green down lights; up is the lever up (or neutral) with every down and unlocked light dark. Null
/// while any input is unread: unknown is never down or up.
/// </summary>
public static class A300FoGearConfirmation
{
    public static bool? Down(double? lever, IReadOnlyList<bool?> downLights)
        => lever is not double l || downLights.Any(x => x is null) ? null
            : l >= 0.5 && downLights.All(x => x == true);

    public static bool? Up(double? lever, IReadOnlyList<bool?> downLights, IReadOnlyList<bool?> unlockedLights)
        => lever is not double l || downLights.Concat(unlockedLights).Any(x => x is null) ? null
            : l < 0.5 && downLights.Concat(unlockedLights).All(x => x == false);
}

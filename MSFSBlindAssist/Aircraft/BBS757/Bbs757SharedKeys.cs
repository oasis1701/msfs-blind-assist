namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The 757 variable keys more than one gap plan names. Each is
/// registered by exactly one plan: the EXT PWR lights by plan 06, the ground power connection by plan 07.
/// </summary>
public static class Bbs757SharedKeys
{
    /// <summary>The EXT PWR button's ON light.</summary>
    public const string ExtPwrOn = "BBS757_EXT_PWR_ON";

    /// <summary>The EXT PWR button's AVAIL light.</summary>
    public const string ExtPwrAvail = "BBS757_EXT_PWR_AVAIL";

    /// <summary>Whether BlueBird's ground power unit is connected.</summary>
    public const string GpuConnected = "BBS757_GPU_CONNECTED";
}

using System.Runtime.CompilerServices;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>Pins <see cref="SimConnectManager.DetectedSimulatorVersion"/> ("FS2020" / "FS2024") on a
/// test manager, through its compiler-generated private setter — the same no-reflection route
/// <see cref="SeededSimConnectCache"/> takes, so the SimConnect SDK's static initializer never runs.</summary>
internal static class SimBuildSeeding
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_DetectedSimulatorVersion")]
    private static extern void SetDetected(SimConnectManager manager, string value);

    public static SimConnectManager On(this SimConnectManager manager, string simulator)
    {
        SetDetected(manager, simulator);
        return manager;
    }
}

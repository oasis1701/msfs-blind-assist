using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// A never-connected <see cref="SimConnectManager"/> whose variable cache holds the given values,
/// so an L:var First Officer evaluator (<c>LVarStateEvaluator.GetValue</c> reads
/// <c>GetCachedVariableValue</c>) can be driven through a flow step's real SkipCondition. The
/// cache (<c>lastVariableValues</c>) is private with no seeding seam (see FbwA320VfeNextTests), and
/// none is added to production for tests. It is reached through an <see cref="UnsafeAccessorAttribute"/>,
/// a direct field access: reflection (<c>FieldInfo.GetValue</c>) would run SimConnectManager's
/// static initializer, which loads the SimConnect SDK assembly and fails where its native DLL is
/// absent (the test output, CI). A renamed field throws MissingFieldException here, loudly. A key
/// not given reads as unread (NaN), as in the sim. <see cref="ConnectedWith"/> is the one exception
/// to "never connected": it reads as connected, and still holds no SimConnect handle.
/// </summary>
internal static class SeededSimConnectCache
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "lastVariableValues")]
    private static extern ref ConcurrentDictionary<string, double> Cache(SimConnectManager manager);

    // IsConnected is an auto-property with a private setter; its compiler-generated setter is reached
    // the same way as the cache, never through reflection.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_IsConnected")]
    private static extern void SetIsConnected(SimConnectManager manager, bool value);

    public static SimConnectManager With(params (string Key, double Value)[] values)
    {
        var mgr = new SimConnectManager(IntPtr.Zero);
        var cache = Cache(mgr);
        foreach (var (key, value) in values)
            cache[key] = value;
        return mgr;
    }

    /// <summary>As <see cref="With"/>, but reading as connected (<c>IsConnected</c> true), so an
    /// evaluator given it is <c>IsAvailable</c> and FlowManager consults a step's SkipCondition (its
    /// "Already set" path). Nothing can be sent through it: it never opened a SimConnect handle. Give
    /// it to the evaluator only; an executor left without it stays unavailable, so every write fails.</summary>
    public static SimConnectManager ConnectedWith(params (string Key, double Value)[] values)
    {
        var mgr = With(values);
        SetIsConnected(mgr, true);
        return mgr;
    }
}

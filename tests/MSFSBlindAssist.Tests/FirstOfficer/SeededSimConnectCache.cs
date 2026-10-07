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
/// not given reads as unread (NaN), as in the sim.
/// </summary>
internal static class SeededSimConnectCache
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "lastVariableValues")]
    private static extern ref ConcurrentDictionary<string, double> Cache(SimConnectManager manager);

    public static SimConnectManager With(params (string Key, double Value)[] values)
    {
        var mgr = new SimConnectManager(IntPtr.Zero);
        var cache = Cache(mgr);
        foreach (var (key, value) in values)
            cache[key] = value;
        return mgr;
    }
}

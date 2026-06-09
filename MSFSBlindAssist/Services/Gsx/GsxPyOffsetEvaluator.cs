using System.Collections.Generic;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Evaluates a GSX <c>.py</c> per-aircraft stop offset for a given gate + aircraft,
/// reproducing each profile dispatch idiom exactly. Returns <see cref="GsxOffset.Zero"/>
/// (the safe base position) on any miss; never throws.
/// </summary>
public static class GsxPyOffsetEvaluator
{
    public static GsxOffset Evaluate(
        GsxPyProfileReader profile, int number, string? suffix, GsxAircraftId ac)
    {
        if (profile == null || ac == null)
            return GsxOffset.Zero;

        if (!profile.TryGetOffsetFunctionName(number, suffix, out string funcName))
            return GsxOffset.Zero;

        var fn = profile.GetFunction(funcName);
        return Evaluate(fn, ac);
    }

    /// <summary>Evaluates an already-resolved function (exposed for direct unit probing).</summary>
    public static GsxOffset Evaluate(GsxPyProfileReader.OffsetFunction fn, GsxAircraftId ac)
    {
        if (fn == null || ac == null)
            return GsxOffset.Zero;

        switch (fn.Kind)
        {
            case GsxPyProfileReader.IdiomKind.Zero:
            case GsxPyProfileReader.IdiomKind.Unclassified:
                return GsxOffset.Zero;

            case GsxPyProfileReader.IdiomKind.ByIdMajor:
                return Lookup(fn.GenericTable, ac.IdMajor);

            case GsxPyProfileReader.IdiomKind.ByGroup:
                return (!string.IsNullOrEmpty(ac.Group) && fn.TableGroup.TryGetValue(ac.Group, out var gv))
                    ? ToOffset(gv) : GsxOffset.Zero;

            case GsxPyProfileReader.IdiomKind.HandleAircraftOffsets:
                return EvaluateHandleAircraftOffsets(fn, ac);

            case GsxPyProfileReader.IdiomKind.IcaoAircraftOffsets:
                return EvaluateIcaoAircraftOffsets(fn, ac);

            default:
                return GsxOffset.Zero;
        }
    }

    // HandleAircraftOffsets(ad, specificTables, genericTable):
    //   if idMajor in specificTables:
    //       (sub, fbKey) = specificTables[idMajor]
    //       result = sub.get(idMinor) ?? sub.get(fbKey)   # (None if both miss)
    //   else:
    //       result = genericTable.get(idMajor, 0)
    private static GsxOffset EvaluateHandleAircraftOffsets(
        GsxPyProfileReader.OffsetFunction fn, GsxAircraftId ac)
    {
        if (fn.SpecificTables.TryGetValue(ac.IdMajor, out var entry))
        {
            if (entry.SubTable.TryGetValue(ac.IdMinor, out var v))
                return ToOffset(v);
            if (entry.SubTable.TryGetValue(entry.FallbackKey, out var fb))
                return ToOffset(fb);
            // Both miss -> Python returns None -> Distance.fromMeters(None) -> 0 (base).
            return GsxOffset.Zero;
        }
        return Lookup(fn.GenericTable, ac.IdMajor);
    }

    // ICAOAircraftOffsets(ad, aircraftValues, TableIcao, TableGroup):
    //   TableIcao.get(icao, aircraftValues.get(idMajor, TableGroup.get(group, 0)))
    private static GsxOffset EvaluateIcaoAircraftOffsets(
        GsxPyProfileReader.OffsetFunction fn, GsxAircraftId ac)
    {
        if (!string.IsNullOrEmpty(ac.Icao) && fn.TableIcao.TryGetValue(ac.Icao, out var icaoV))
            return ToOffset(icaoV);
        if (fn.AircraftValues.TryGetValue(ac.IdMajor, out var majorV))
            return ToOffset(majorV);
        if (!string.IsNullOrEmpty(ac.Group) && fn.TableGroup.TryGetValue(ac.Group, out var groupV))
            return ToOffset(groupV);
        return GsxOffset.Zero;
    }

    private static GsxOffset Lookup(Dictionary<int, GsxPyProfileReader.TableValue> table, int idMajor)
        => table.TryGetValue(idMajor, out var v) ? ToOffset(v) : GsxOffset.Zero;

    private static GsxOffset ToOffset(GsxPyProfileReader.TableValue v)
        => new(v.Longitudinal, v.Lateral);
}

using System.Globalization;

namespace MSFSBlindAssist.Services.SimPerformance;

/// <summary>
/// Parses the instance names of the Windows "GPU Engine" and "GPU Process Memory" performance
/// counters, which are
/// <c>pid_30804_luid_0x00000000_0x0000ee5d_phys_0_eng_0_engtype_3d</c> (an engine of one process) and
/// <c>pid_30804_luid_0x00000000_0x0000ee5d_phys_0</c> (one process's memory on one adapter). The
/// adapter key is the <c>luid_…_phys_N</c> part, which is also the full instance name of the matching
/// "GPU Adapter Memory" counter. Pure, so the shapes are pinned by tests.
/// </summary>
public static class GpuCounterInstance
{
    public static bool TryParse(string? instance, out int pid, out string adapterKey, out string? engineType)
    {
        pid = 0;
        adapterKey = string.Empty;
        engineType = null;
        if (string.IsNullOrEmpty(instance) || !instance.StartsWith("pid_", StringComparison.Ordinal)) return false;

        int luidAt = instance.IndexOf("_luid_", StringComparison.Ordinal);
        // At least one pid digit between "pid_" and "_luid_": "pid_luid_…" would give the span a
        // negative length and throw out of a Try method.
        if (luidAt <= 4) return false;
        if (!int.TryParse(instance.AsSpan(4, luidAt - 4), NumberStyles.None, CultureInfo.InvariantCulture, out pid)) return false;

        string rest = instance.Substring(luidAt + 1); // "luid_…_phys_0[_eng_0_engtype_3d]"
        int engAt = rest.IndexOf("_eng_", StringComparison.Ordinal);
        adapterKey = engAt < 0 ? rest : rest.Substring(0, engAt);

        int typeAt = rest.IndexOf("_engtype_", StringComparison.Ordinal);
        if (typeAt >= 0) engineType = rest.Substring(typeAt + "_engtype_".Length);
        return true;
    }
}

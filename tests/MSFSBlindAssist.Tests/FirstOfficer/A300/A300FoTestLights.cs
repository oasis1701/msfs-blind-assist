using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>Puts a board light lit or dark in a test cache, under its own generated rule ([A300-25]): it searches
/// 0/1 values for the rule's inputs with the bus's light power on, never assuming "all inputs 1 means lit".</summary>
internal static class A300FoTestLights
{
    public static void Set(Dictionary<string, double> cache, string id, bool lit)
    {
        var lamp = A300LampBoard.ById[id];
        var power = lamp.Inputs.Where(i => i is A300LampBoard.AcPowerKey or A300LampBoard.DcPowerKey).ToList();
        var inputs = lamp.Inputs.Except(power).ToList();
        foreach (var key in power)
            cache[key] = 1;
        for (int mask = 0; mask < 1 << inputs.Count; mask++)
        {
            for (int bit = 0; bit < inputs.Count; bit++)
                cache[inputs[bit]] = (mask >> bit) & 1;
            if (A300LampBoard.IsLit(lamp, k => cache.TryGetValue(k, out var v) ? v : null) == lit)
                return;
        }
        throw new InvalidOperationException($"{id} cannot be made {(lit ? "lit" : "dark")} with 0/1 inputs");
    }
}

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The per-cylinder states the XLS's Mixture and Propeller panel describes and calls out,
/// each transcribed from the model rather than inferred from a name:
///
///  • Plug fouling: <c>DAMAGE_MAG_FOUL:nR</c> / <c>:nL</c>, 0-100 PER PLUG (eight), growing
///    by <c>DAMAGE_MAG_FOUL_RATE:n</c>/60 per tick while rich, cold and at low power
///    (rate = 4 x (14.7 - AFR) - a power term - a CHT term, scaled by rpm/1000), so it
///    accumulates at a rich idle and cleans off at power. It costs spark: the model's
///    <c>ENG_MAG_FOUL_PWR = sqrt(foul/100) x 0.8</c> is the misfire odds on that plug.
///  • Detonation is NOT here: the model's detonation block (Logic 1857-1897) sits inside a
///    comment that is opened, "(*--Detonation--", and only closed forty lines later, so it
///    never runs in this build, nothing consumes DETONATION:n, and the 4 it read live was
///    residue. A row for it would announce a fault the aeroplane cannot have.
///  • Shock cooling: <c>CHT_TEMP_INC:n</c> under -0.15 damages the cylinder directly
///    (DAMAGE_CYL += (inc + 0.15) x -0.2). The threshold is the model's.
///  • Damage: <c>DAMAGE_CYL:n</c> 0-100, and over 99 the cylinder is dead (<c>KAPUTT_CYL</c>).
/// </summary>
public static class DA40CylinderState
{
    public const int CylinderCount = 4;

    /// <summary>A fouling onset speaks at this, and a worsening at each further step of it.</summary>
    public const double FoulingStep = 25;

    public const double ShockCoolingInc = -0.15;
    public const double DeadDamage = 99;

    /// <summary>The plug order the model's variables are captured in: 1R 1L 2R 2L 3R 3L 4R 4L.</summary>
    public static string PlugName(int index) => $"cylinder {index / 2 + 1} {(index % 2 == 0 ? "right" : "left")}";

    public static int WorstPlug(double[] plugs)
    {
        int worst = 0;
        for (int i = 1; i < plugs.Length; i++) if (plugs[i] > plugs[worst]) worst = i;
        return worst;
    }

    public static string DescribeFouling(double[] plugs)
    {
        if (plugs.Length == 0) return "Clean";
        int w = WorstPlug(plugs);
        if (plugs[w] < 0.5) return "Clean";
        return $"Worst plug {PlugName(w)}, {plugs[w]:F0} percent fouled";
    }

    /// <summary>
    /// The graded rule (the coolant leak's): the onset at the first step, then only a
    /// worsening that reaches the next step - never the value as it creeps.
    /// </summary>
    public static string? FoulingCallout(double previousWorst, double worst, string plugName)
    {
        int before = (int)Math.Floor(previousWorst / FoulingStep);
        int now = (int)Math.Floor(worst / FoulingStep);
        if (now <= before || now == 0) return null;
        return before == 0
            ? $"Plug fouling, {plugName} at {worst:F0} percent"
            : $"Plug fouling worsening, {plugName} at {worst:F0} percent";
    }

    /// <summary>
    /// The damage call-out for cylinder <paramref name="i"/>, or null. A fall of five points on
    /// a cylinder already seen - damage happens during something.
    ///
    /// ⚠️ EACH CYLINDER'S FIRST READING IS ITS OWN BASELINE. A batch delivers its variables
    /// one at a time, so a baseline cloned from the whole array on the first delivery holds
    /// zeros for every cylinder not delivered yet, and damage persisted from the last flight
    /// was then announced as new on every connect. A reading BELOW the baseline is a repair
    /// (Reset: Damage) and lowers it, so damage done after a repair is heard.
    /// </summary>
    public static string? NextDamageCallout(double?[] spoken, int i, double damage)
    {
        if (spoken[i] is not double was || damage < was) { spoken[i] = damage; return null; }
        if (damage - was < 5) return null;
        spoken[i] = damage;
        return damage > DeadDamage ? $"Cylinder {i + 1} dead" : $"Cylinder {i + 1} damaged, {100 - damage:F0} percent";
    }

    /// <summary>
    /// The fouling call-out after plug <paramref name="plug"/> reported, or null. Same
    /// first-reading rule as <see cref="NextDamageCallout"/>: a plug's first reading only
    /// raises the baseline, so fouling persisted from the last flight is never announced as new.
    /// </summary>
    public static string? NextFoulingCallout(bool[] seen, ref double worstSpoken, double[] plugs, int plug)
    {
        int w = WorstPlug(plugs);
        double worst = Math.Min(100, plugs[w]);
        if (!seen[plug])
        {
            seen[plug] = true;
            worstSpoken = Math.Max(worstSpoken, Math.Min(100, plugs[plug]));
            return null;
        }
        string? callout = FoulingCallout(Math.Max(0, worstSpoken), worst, PlugName(w));
        worstSpoken = Math.Max(worstSpoken, worst);
        return callout;
    }

    public static string DescribeShockCooling(double[] tempInc)
    {
        var hit = Where(tempInc, v => v < ShockCoolingInc);
        return hit.Count == 0 ? "None" : "Shock cooling, " + Cylinders(hit);
    }

    public static string DescribeHealth(double[] damage)
    {
        var parts = new List<string>();
        int fine = 0;
        for (int i = 0; i < damage.Length; i++)
        {
            if (damage[i] > DeadDamage) parts.Add($"cylinder {i + 1} dead");
            else if (damage[i] >= 0.5) parts.Add($"cylinder {i + 1} at {100 - damage[i]:F0} percent");
            else fine++;
        }
        if (parts.Count == 0) return $"All {Words(damage.Length)} at 100 percent";
        string text = string.Join(", ", parts);
        if (fine > 0) text += ", the rest at 100";
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    /// <summary>"cylinder 3", "cylinders 1 and 4", "cylinders 1, 2 and 4".</summary>
    public static string Cylinders(List<int> numbers)
    {
        if (numbers.Count == 1) return $"cylinder {numbers[0]}";
        return "cylinders " + string.Join(", ", numbers.Take(numbers.Count - 1)) + " and " + numbers[^1];
    }

    private static List<int> Where(double[] values, Func<double, bool> test)
    {
        var hit = new List<int>();
        for (int i = 0; i < values.Length; i++) if (test(values[i])) hit.Add(i + 1);
        return hit;
    }

    private static string Words(int n) => n switch { 4 => "four", 2 => "two", 6 => "six", _ => n.ToString() };
}

/// <summary>
/// Which members of a group of variables have delivered at least once. A state composed from
/// several variables is baselined only once every member has reported, because a batch
/// delivers them one at a time.
/// </summary>
public sealed class DA40SeenMask
{
    private readonly bool[] _seen;
    private int _count;

    public DA40SeenMask(int size) => _seen = new bool[size];

    /// <summary>Records member <paramref name="i"/>; true once every member has been seen.</summary>
    public bool Mark(int i)
    {
        if (!_seen[i]) { _seen[i] = true; _count++; }
        return _count == _seen.Length;
    }

    public void Reset() { Array.Clear(_seen); _count = 0; }
}

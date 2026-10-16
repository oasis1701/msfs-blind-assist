using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Groups the circuit breakers by the bus each one hangs off. The bus is the part of the breaker's
/// electrical line before its junction (<c>LH_ELEC_BUS_3_HZ107_To_…</c> gives <c>LH_ELEC_BUS_3</c>),
/// and the panels keep the order the model declares the breakers in.
/// </summary>
public static class C680BreakerBuses
{
    private static readonly Regex Junction = new(@"_[A-Z]{2}\d+$", RegexOptions.CultureInvariant);

    public static string BusOf(string line)
    {
        int to = line.IndexOf("_To_", StringComparison.Ordinal);
        string head = to < 0 ? line : line[..to];
        return Junction.Replace(head, "");
    }

    /// <summary>"LH_ELEC_BUS_3" gives "Left ELEC Bus 3 Breakers".</summary>
    public static string PanelTitle(string bus)
    {
        var words = bus.Split('_').Select(w => w switch { "LH" => "Left", "RH" => "Right", "BUS" => "Bus", _ => w });
        return string.Join(" ", words) + " Breakers";
    }

    public sealed record Panel(string Title, IReadOnlyList<string> Keys);

    public static IReadOnlyList<Panel> Panels()
        => C680BreakerTable.Rows
            .GroupBy(r => BusOf(r.Line))
            .Select(g => new Panel(PanelTitle(g.Key), g.Select(r => r.Key).ToList()))
            .ToList();
}

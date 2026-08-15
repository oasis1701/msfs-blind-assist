using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// One row of GSX's published <c>services</c> array. Replaces the tooltip-prose
/// regexes in the old GsxService.TextRules: every field here is stated by GSX.
/// Optional members (detail / progress / operator) are genuinely absent on most
/// services, so they are nullable rather than defaulted.
/// </summary>
public sealed class GsxServiceState
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string State { get; init; } = "";
    public int StateRaw { get; init; }
    public string StateText { get; init; } = "";
    public string? Operator { get; init; }
    public bool CanTrigger { get; init; }
    public bool CanBypass { get; init; }

    public string? BusPhase { get; init; }
    public string? Phase { get; init; }
    public int? PaxDone { get; init; }
    public int? PaxTotal { get; init; }
    public int? BagsPercent { get; init; }

    /// <summary>
    /// The refuel row's quantity — <c>detail.fuel.{current,target,unit,aircraftTotal}</c>.
    /// This is where a live Refueling row carries its numbers (never the generic
    /// <c>progress</c> object): e.g. <c>{"current":2221,"target":2231,"unit":"kg",
    /// "startTotal":3004,"aircraftTotal":5252}</c>. <c>current</c> is fuel LOADED so
    /// far and <c>aircraftTotal</c> the fuel on board now. <c>target</c> is NOT a
    /// fixed uplift target: in GSX's progressive mode it is a ROLLING figure that
    /// tracks <c>current</c> (live: target ≈ current + 8 on every 1 Hz patch,
    /// progressText "100 %" throughout), so it must never be spoken as "of N" nor
    /// treated as a revision worth announcing — a first version did both and read
    /// the row aloud once a second. Before the hose is on, <c>fuel</c> carries only
    /// <c>aircraftTotal</c>+<c>unit</c> — then <c>FuelCurrent</c> stays null. Doubles:
    /// GSX may publish fractional pounds.
    /// </summary>
    public double? FuelCurrent { get; init; }
    public double? FuelTarget { get; init; }
    public double? FuelAircraftTotal { get; init; }
    public string? FuelUnit { get; init; }

    public int? ProgressCurrent { get; init; }
    public int? ProgressTotal { get; init; }
    public string? ProgressUnit { get; init; }
    public string ProgressText { get; init; } = "";

    /// <summary>
    /// GSX's own multi-line status block for this row, e.g.
    /// <c>"bus in position\npax 181/186\nbags 100%"</c>. This is the row's REAL
    /// detail: <see cref="ProgressText"/> on the same captured row reads
    /// <c>"181/181"</c> (progress.current out of progress.total, which GSX
    /// clamps to the current count), which a pilot hears as "everybody is off"
    /// while five passengers are still aboard. Prefer this.
    /// </summary>
    public string StatusText { get; init; } = "";

    public static IReadOnlyList<GsxServiceState> ParseList(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array) return Array.Empty<GsxServiceState>();
        var list = new List<GsxServiceState>();
        foreach (var e in array.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            JsonElement detail = Obj(e, "detail");
            JsonElement pax = detail.ValueKind == JsonValueKind.Object ? Obj(detail, "pax") : default;
            JsonElement fuel = detail.ValueKind == JsonValueKind.Object ? Obj(detail, "fuel") : default;
            JsonElement prog = Obj(e, "progress");

            list.Add(new GsxServiceState
            {
                Id = Str(e, "id") ?? "",
                DisplayName = Str(e, "displayName") ?? "",
                State = Str(e, "state") ?? "",
                StateRaw = Int(e, "stateRaw") ?? 0,
                StateText = Str(e, "stateText") ?? "",
                Operator = Str(e, "operator"),
                CanTrigger = Bool(e, "canTrigger"),
                CanBypass = Bool(e, "canBypass"),
                BusPhase = detail.ValueKind == JsonValueKind.Object ? Str(detail, "busPhase") : null,
                Phase = detail.ValueKind == JsonValueKind.Object ? Str(detail, "phase") : null,
                PaxDone = pax.ValueKind == JsonValueKind.Object ? Int(pax, "done") : null,
                PaxTotal = pax.ValueKind == JsonValueKind.Object ? Int(pax, "total") : null,
                BagsPercent = detail.ValueKind == JsonValueKind.Object ? Int(detail, "bagsPercent") : null,
                FuelCurrent = fuel.ValueKind == JsonValueKind.Object ? Num(fuel, "current") : null,
                FuelTarget = fuel.ValueKind == JsonValueKind.Object ? Num(fuel, "target") : null,
                FuelAircraftTotal = fuel.ValueKind == JsonValueKind.Object ? Num(fuel, "aircraftTotal") : null,
                FuelUnit = fuel.ValueKind == JsonValueKind.Object ? Str(fuel, "unit") : null,
                ProgressCurrent = prog.ValueKind == JsonValueKind.Object ? Int(prog, "current") : null,
                ProgressTotal = prog.ValueKind == JsonValueKind.Object ? Int(prog, "total") : null,
                ProgressUnit = prog.ValueKind == JsonValueKind.Object ? Str(prog, "unit") : null,
                ProgressText = Str(e, "progressText") ?? "",
                StatusText = Str(e, "statusText") ?? "",
            });
        }
        return list;
    }

    private static JsonElement Obj(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) ? v : default;

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int? Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)
           ? i : null;

    private static double? Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)
           ? d : null;
}

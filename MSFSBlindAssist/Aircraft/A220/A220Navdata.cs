using System.Text.Json;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// The captain MFW's DATALOAD format as the displays agent reports it
/// (<c>__a220Displays.dataload()</c>): the Navigraph sign-in dialog, the
/// "Load New Databases" package list, and the load's progress. Pure parsing +
/// wording, so it can be pinned without a sim.
///
/// Why this exists: the A220's working Navigraph updater is in the MFW
/// Maintenance → Data Load pages, NOT the EFB (v1.0.9's EFB "UPDATE NAVDATA"
/// calls a WASM callback that never answers — "No response was received from the
/// A220 navdata updater"). A successful load writes L:A220 Nav Data Source = 1
/// (Navigraph); 0 is the sim's native database.
/// </summary>
public sealed class A220NavdataState
{
    public sealed record Row(string Name, bool Selected, bool Disabled, string Status);

    /// <summary>Captain MFW format (8 = DATALOAD), -1 when the display store is unreachable.</summary>
    public int Format { get; init; } = -1;
    /// <summary>"databases", "menu" or "none".</summary>
    public string Page { get; init; } = "none";
    /// <summary>Navigraph device-flow code while sign-in is required, else null.</summary>
    public string? AuthCode { get; init; }
    public bool AuthRequired { get; init; }
    public IReadOnlyList<Row> Rows { get; init; } = Array.Empty<Row>();
    public bool CanStart { get; init; }
    public int? Progress { get; init; }
    /// <summary>"ok", "errors" or null while not finished.</summary>
    public string? Complete { get; init; }

    public const int DataloadFormat = 8;
    public const int FmsFormat = 2;

    public static A220NavdataState? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;

            string? code = null;
            bool auth = false;
            if (r.TryGetProperty("auth", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                auth = true;
                if (a.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString();
            }

            var rows = new List<Row>();
            if (r.TryGetProperty("rows", out var rs) && rs.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rs.EnumerateArray())
                {
                    string name = row.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.Length == 0) continue;
                    rows.Add(new Row(
                        name,
                        row.TryGetProperty("selected", out var s) && s.ValueKind == JsonValueKind.True,
                        row.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True,
                        row.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : ""));
                }
            }

            return new A220NavdataState
            {
                Format = r.TryGetProperty("fmt", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : -1,
                Page = r.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "none" : "none",
                AuthRequired = auth,
                AuthCode = code,
                Rows = rows,
                CanStart = r.TryGetProperty("canStart", out var cs) && cs.ValueKind == JsonValueKind.True,
                Progress = r.TryGetProperty("progress", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetInt32() : null,
                Complete = r.TryGetProperty("complete", out var cp) && cp.ValueKind == JsonValueKind.String ? cp.GetString() : null
            };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// A Navigraph package id reads "Navigraph_v2_2609_03SEP26" — speak it as
    /// "Navigraph cycle 2609, effective 03SEP26". Anything else is returned as-is.
    /// </summary>
    public static string DescribePackage(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(?i)^Navigraph_v\d+_(\d{4})_(\w+)$");
        return m.Success ? $"Navigraph cycle {m.Groups[1].Value}, effective {m.Groups[2].Value}" : name;
    }

    /// <summary>Spoken/visible status of the source switch (L:A220 Nav Data Source).</summary>
    public static string DescribeSource(double source) => source switch
    {
        < 0 => "unknown",
        >= 0.5 => "Navigraph (the aircraft's own downloaded database)",
        _ => "MSFS native (the simulator's navigation data)"
    };

    /// <summary>One line per list row for the form, in the aircraft's order.</summary>
    public static string DescribeRow(Row row)
    {
        string state = row.Status.Length > 0 ? row.Status : row.Selected ? "selected" : "not selected";
        return $"{DescribePackage(row.Name)}: {state}";
    }
}

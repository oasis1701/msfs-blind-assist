using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Parses PMDG 777 PROG-page CDU text into structured data.
///
/// The PMDG SDK provides 14 rows × 24 cols of ASCII-mapped CDU text. The
/// PROG page lays out destination distance/ETA/fuel and the active phase
/// targets (TOC / step climb / TOD) in a specific row pattern. Each phase
/// label sits in one row and the data values for that phase live in the
/// row immediately beneath it.
///
/// This reader is a direct port of TFM's PMDG 747 ProgressPage parser
/// (see talking-flight-monitor/source/PMDG/PMDG 747/PMDG747Aircraft.cs).
/// The 747 and 777 use the same PMDG CDU SDK and share row layout, so the
/// scan-by-keyword-with-next-row-as-data approach works identically. The
/// label keywords ("STEP", "S/C", "T/C", "T/D", "DEST") are uppercase
/// and the comparison is case-insensitive — robust against minor PMDG
/// styling differences.
/// </summary>
public static class PMDGProgPageReader
{
    /// <summary>Decoded PROG-page snapshot. Use <see cref="IsValid"/> to check freshness.</summary>
    public sealed class ProgPageData
    {
        public double DistanceToTOC { get; set; } = -1;
        public string ETAToTOC { get; set; } = "";
        public bool TOCPassed { get; set; } = true;

        public double DistanceToStepClimb { get; set; } = -1;
        public string ETAToStepClimb { get; set; } = "";
        /// <summary>True when PROG shows "STEP CLIMB: NONE" (or equivalent).</summary>
        public bool StepClimbIsNone { get; set; } = true;

        public double DistanceToTOD { get; set; } = -1;
        public string ETAToTOD { get; set; } = "";
        public bool TODPassed { get; set; } = false;

        public double DistanceToDest { get; set; } = -1;
        public string ETAToDest { get; set; } = "";
        public double LandingFuel { get; set; } = -1;

        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public bool IsValid => LastUpdated > DateTime.MinValue;
    }

    /// <summary>
    /// Returns true if the given rows look like the PROG page (title row 0
    /// contains "PROGRESS"). This is the cheapest way to verify the page
    /// before doing a full parse — useful from the page-monitor loop to
    /// decide whether to switch the CDU to PROG.
    /// </summary>
    public static bool IsProgPage(IReadOnlyList<string>? rows)
    {
        if (rows == null || rows.Count == 0) return false;
        return rows[0].Trim().ToUpperInvariant().Contains("PROGRESS");
    }

    /// <summary>
    /// Parses 14 PROG-page rows into a <see cref="ProgPageData"/>. Returns
    /// null if the rows aren't PROG. The PROG page can span multiple pages
    /// in PMDG (page 1 has DEST + T/C + T/D, page 2 has step climb on some
    /// builds); pass <paramref name="freshStart"/>=false on page 2+ to
    /// merge into the existing cached data instead of clobbering it.
    /// </summary>
    public static ProgPageData? Parse(IReadOnlyList<string>? rows, ProgPageData? existing = null, bool freshStart = true)
    {
        if (!IsProgPage(rows)) return null;
        // rows is non-null here — IsProgPage gates on rows != null && rows.Count > 0
        var data = (!freshStart && existing != null && existing.IsValid)
            ? existing
            : new ProgPageData();
        data.LastUpdated = DateTime.Now;

        for (int i = 0; i < rows!.Count; i++)
        {
            string row = rows[i];
            string rowUpper = row.ToUpperInvariant();
            string nextRow = (i + 1 < rows.Count) ? rows[i + 1] : "";

            // Match step climb FIRST so "STEP" doesn't get caught by the
            // T/C check below ("S/C" abbreviation can also apply).
            if (rowUpper.Contains("STEP") || (rowUpper.Contains("S/C") && !rowUpper.Contains("T/C")))
            {
                if (rowUpper.Contains("NONE"))
                {
                    data.StepClimbIsNone = true;
                }
                else
                {
                    data.StepClimbIsNone = false;
                    ParsePhaseRow(nextRow, out double dist, out string eta);
                    data.DistanceToStepClimb = dist;
                    data.ETAToStepClimb = eta;
                }
            }
            // T/C label row → next row carries distance/ETA. Empty next row
            // means PROG is no longer showing T/C (TOC has been passed).
            else if (rowUpper.Contains("T/C"))
            {
                if (!string.IsNullOrWhiteSpace(nextRow))
                {
                    ParsePhaseRow(nextRow, out double dist, out string eta);
                    data.DistanceToTOC = dist;
                    data.ETAToTOC = eta;
                    data.TOCPassed = false;
                }
                else
                {
                    data.TOCPassed = true;
                }
            }
            else if (rowUpper.Contains("T/D"))
            {
                ParsePhaseRow(nextRow, out double dist, out string eta);
                data.DistanceToTOD = dist;
                data.ETAToTOD = eta;
                data.TODPassed = (dist <= 0);
            }
            else if (rowUpper.Contains("DEST"))
            {
                ParseDestDataRow(nextRow, out double dist, out string eta, out double fuel);
                data.DistanceToDest = dist;
                data.ETAToDest = eta;
                data.LandingFuel = fuel;
            }
        }

        return data;
    }

    /// <summary>
    /// Destination data row layout: <c>"EGLL     301 1515z  29.0"</c>.
    /// First number = distance (NM), <c>NNNN[zZ]</c> = ETA in Z time,
    /// last number = landing fuel (typically klbs or t depending on PMDG
    /// units setting; we just pass it through as-is). Anything we can't
    /// parse stays at the default (-1 / empty).
    /// </summary>
    private static void ParseDestDataRow(string row, out double distance, out string eta, out double fuel)
    {
        distance = -1;
        eta = "";
        fuel = -1;
        if (string.IsNullOrWhiteSpace(row)) return;

        string[] parts = row.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            // ETA: ends with z/Z, e.g. "1515z", "1430Z"
            if ((trimmed.EndsWith("Z", StringComparison.Ordinal) || trimmed.EndsWith("z", StringComparison.Ordinal))
                && trimmed.Length >= 4)
            {
                eta = trimmed.ToUpperInvariant();
                continue;
            }
            // Plain number: first one is distance, second one is fuel.
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            {
                if (distance < 0) distance = val;
                else fuel = val;
            }
        }
    }

    /// <summary>
    /// T/C / T/D / step-climb phase row: <c>"250         1517z/  67nm"</c>.
    /// <c>NNnm</c> = distance (nautical miles), <c>NNNN[zZ]</c> = ETA Z time,
    /// plain numbers (the first column shows speed) are ignored. The slash
    /// in tokens like <c>"0350z/1373nm"</c> is treated as a separator so we
    /// don't accidentally concatenate fields.
    /// </summary>
    private static void ParsePhaseRow(string row, out double distance, out string eta)
    {
        distance = -1;
        eta = "";
        if (string.IsNullOrWhiteSpace(row)) return;

        string[] parts = row.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            string etaCandidate = trimmed.TrimEnd('/');
            if ((etaCandidate.EndsWith("Z", StringComparison.Ordinal) ||
                 etaCandidate.EndsWith("z", StringComparison.Ordinal)) &&
                etaCandidate.Length >= 4)
            {
                eta = etaCandidate.ToUpperInvariant();
                continue;
            }
            string upper = trimmed.ToUpperInvariant();
            if (upper.EndsWith("NM", StringComparison.Ordinal))
            {
                string numPart = upper.Substring(0, upper.Length - 2);
                if (double.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double distVal))
                {
                    distance = distVal;
                }
            }
        }
    }
}

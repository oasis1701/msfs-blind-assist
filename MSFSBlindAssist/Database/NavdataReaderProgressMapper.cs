namespace MSFSBlindAssist.Database;

/// <summary>One progress reading. <see cref="Percent"/> of -1 means "leave the bar alone".</summary>
public sealed record NavdataReaderProgress(int Percent, string? Status, string? Details);

/// <summary>
/// Maps a navdatareader output line to a progress reading.
///
/// The percentages are keyword heuristics, not real progress — navdatareader emits no
/// machine-readable percentage — so the mapper enforces monotonicity. Without it the bar
/// walks backwards for the whole build, because "Reading ..." lines are interleaved
/// throughout rather than confined to an opening phase.
/// </summary>
public sealed class NavdataReaderProgressMapper
{
    private int _highestReported;

    public NavdataReaderProgress? Map(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // Index creation is checked FIRST: its lines also contain "Creating", so the
        // generic write arm below would otherwise swallow every one of them.
        if (Has(line, "index") && Has(line, "Creating"))
            return Advance(92, "Creating indexes...", "Building database indexes for fast queries");

        if (Has(line, "Reading"))
        {
            string? detail = Has(line, "scenery") ? "Reading scenery files from disk"
                           : Has(line, "BGL") ? "Reading BGL files"
                           : null;
            return Advance(25, "Reading scenery files...", detail);
        }

        if (Has(line, "Processing"))
            return Advance(50, "Processing airport data...", line.Length < 100 ? line.Trim() : null);

        if (Has(line, "Creating") || Has(line, "Writing"))
            return Advance(75, "Writing database...", Has(line, "database") ? "Writing database structure" : null);

        if (Has(line, "Vacuum"))
            return Advance(85, "Optimizing database...", "Running vacuum to compact database");

        if (Has(line, "Analyz"))
            return Advance(90, "Analyzing database...", "Gathering statistics for query optimization");

        if (Has(line, "Done") || Has(line, "Finished") || Has(line, "compiled"))
            return Advance(95, "Finalizing database...", "Completing final operations");

        return null;
    }

    /// <summary>
    /// Returns the reading, downgrading the percentage to -1 when it would move the bar
    /// backwards. The status and detail text still flow through, so the pilot keeps seeing
    /// what the tool is doing even when the bar holds still.
    /// </summary>
    private NavdataReaderProgress Advance(int percent, string status, string? details)
    {
        if (percent < _highestReported)
            return new NavdataReaderProgress(-1, status, details);

        _highestReported = percent;
        return new NavdataReaderProgress(percent, status, details);
    }

    private static bool Has(string line, string token)
        => line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
}

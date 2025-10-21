namespace FBWBA.Database.Models;

/// <summary>
/// Represents metadata information from the Little Navmap database
/// </summary>
public class DatabaseMetadata
{
    /// <summary>
    /// Database schema major version
    /// </summary>
    public int DbVersionMajor { get; set; }

    /// <summary>
    /// Database schema minor version (revision)
    /// </summary>
    public int DbVersionMinor { get; set; }

    /// <summary>
    /// Timestamp when database was last built/loaded
    /// </summary>
    public string LastLoadTimestamp { get; set; } = string.Empty;

    /// <summary>
    /// Whether the database contains SID/STAR procedures
    /// </summary>
    public bool HasSidStar { get; set; }

    /// <summary>
    /// AIRAC cycle number (e.g., "2510")
    /// </summary>
    public string AiracCycle { get; set; } = string.Empty;

    /// <summary>
    /// AIRAC validity end date
    /// </summary>
    public string ValidThrough { get; set; } = string.Empty;

    /// <summary>
    /// Data source (e.g., "MSFS24", "MSFS")
    /// </summary>
    public string DataSource { get; set; } = string.Empty;

    /// <summary>
    /// Compiler/tool version information
    /// </summary>
    public string CompilerVersion { get; set; } = string.Empty;

    /// <summary>
    /// Additional properties (e.g., "NavigraphUpdate=true")
    /// </summary>
    public string Properties { get; set; } = string.Empty;

    /// <summary>
    /// Returns true if AIRAC cycle information is available
    /// </summary>
    public bool HasAiracData => !string.IsNullOrWhiteSpace(AiracCycle);

    /// <summary>
    /// Returns formatted navigation data source information string
    /// </summary>
    public string GetAiracDisplayString()
    {
        // Check if Navigraph data is available
        if (!string.IsNullOrWhiteSpace(Properties) &&
            Properties.IndexOf("NavigraphUpdate=true", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Navigraph data available";
        }

        return "No navigraph, using Microsoft's Native nav data";
    }
}

namespace MSFSBlindAssist.Models;

/// <summary>
/// A single AI/multiplayer aircraft visible in the TCAS display.
/// Relative fields (DistanceNm etc.) are populated by TcasService from own-aircraft position.
/// </summary>
public class TcasTraffic
{
    public uint   ObjectId          { get; set; }
    public string Callsign          { get; set; } = "";
    public string AircraftType      { get; set; } = "";
    public double Latitude          { get; set; }
    public double Longitude         { get; set; }
    public double AltitudeFt        { get; set; }
    public double HeadingMagnetic   { get; set; }
    public double GroundSpeedKnots  { get; set; }
    public bool   OnGround          { get; set; }
    public string FromAirport       { get; set; } = "";
    public string ToAirport         { get; set; } = "";
    public string Airline           { get; set; } = "";

    // Computed relative to own aircraft by TcasService
    public double DistanceNm        { get; set; }
    public double AltitudeDiffFt    { get; set; }  // positive = above own aircraft
    public double RelativeBearing   { get; set; }  // -180..+180 relative to own heading

    /// <summary>
    /// Compact description: "2.3nm, 2,000ft above, 1 o'clock" used as the list line label.
    /// </summary>
    public string RelativePositionSummary => FormatRelativePosition(includeAltitude: true);

    /// <summary>
    /// Position summary without altitude info, for ground traffic where altitude is irrelevant.
    /// </summary>
    public string RelativePositionSummaryNoAltitude => FormatRelativePosition(includeAltitude: false);

    /// <summary>
    /// Relative direction as a clock position (12 = straight ahead, 3 = right, 6 = behind,
    /// 9 = left), derived from <see cref="RelativeBearing"/>. Far more useful to a pilot
    /// than a bare ahead/behind — it conveys side and angle in one word the way a real
    /// traffic call does ("traffic, two o'clock").
    /// </summary>
    public int ClockPosition
    {
        get
        {
            int c = (int)Math.Round(((RelativeBearing + 360.0) % 360.0) / 30.0) % 12;
            return c == 0 ? 12 : c;
        }
    }

    private string FormatRelativePosition(bool includeAltitude)
    {
        string distStr = DistanceNm < 10.0
            ? $"{DistanceNm:F1}nm"
            : $"{(int)Math.Round(DistanceNm)}nm";

        string posStr = $"{ClockPosition} o'clock";

        if (!includeAltitude)
            return $"{distStr}, {posStr}";

        int altDiff = (int)Math.Abs(AltitudeDiffFt);
        string altStr = altDiff < 200
            ? "same altitude"
            : AltitudeDiffFt > 0
                ? $"{altDiff:N0}ft above"
                : $"{altDiff:N0}ft below";

        return $"{distStr}, {altStr}, {posStr}";
    }

    /// <summary>
    /// Short announcement used by the Shift+S hotkey.
    /// </summary>
    public string BriefAnnouncement =>
        string.IsNullOrEmpty(Callsign)
            ? $"{AircraftType}, {RelativePositionSummary}, {(int)GroundSpeedKnots}kts"
            : $"{Callsign}, {RelativePositionSummary}, {(int)GroundSpeedKnots}kts";
}

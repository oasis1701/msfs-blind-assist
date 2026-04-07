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

    // Computed relative to own aircraft by TcasService
    public double DistanceNm        { get; set; }
    public double AltitudeDiffFt    { get; set; }  // positive = above own aircraft
    public double RelativeBearing   { get; set; }  // -180..+180 relative to own heading

    /// <summary>
    /// Compact description: "2.3nm, 2,000ft above, ahead" used as the tree node label.
    /// </summary>
    public string RelativePositionSummary
    {
        get
        {
            string distStr = DistanceNm < 10.0
                ? $"{DistanceNm:F1}nm"
                : $"{(int)Math.Round(DistanceNm)}nm";

            int altDiff = (int)Math.Abs(AltitudeDiffFt);
            string altStr = altDiff < 200
                ? "same altitude"
                : AltitudeDiffFt > 0
                    ? $"{altDiff:N0}ft above"
                    : $"{altDiff:N0}ft below";

            string posStr = Math.Abs(RelativeBearing) <= 90.0 ? "ahead" : "behind";

            return $"{distStr}, {altStr}, {posStr}";
        }
    }

    /// <summary>
    /// Short announcement used by the Shift+S hotkey.
    /// </summary>
    public string BriefAnnouncement =>
        string.IsNullOrEmpty(Callsign)
            ? $"{AircraftType}, {RelativePositionSummary}, {(int)GroundSpeedKnots}kts"
            : $"{Callsign}, {RelativePositionSummary}, {(int)GroundSpeedKnots}kts";
}

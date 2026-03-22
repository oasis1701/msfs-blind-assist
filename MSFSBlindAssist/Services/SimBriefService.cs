using System.Net;
using System.Text;
using System.Xml;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;
/// <summary>
/// Service for fetching and parsing SimBrief flight plans
/// </summary>
public class SimBriefService
{
    private const string SIMBRIEF_API_URL = "https://www.simbrief.com/api/xml.fetcher.php";
    private const int TIMEOUT_SECONDS = 30;

    private readonly NavigationDatabaseProvider _navigationDatabase;

    public SimBriefService(NavigationDatabaseProvider navigationDatabase)
    {
        _navigationDatabase = navigationDatabase;
    }

    /// <summary>
    /// Fetches the latest flight plan for a given SimBrief username
    /// </summary>
    /// <param name="username">SimBrief username (Navigraph Alias)</param>
    /// <returns>FlightPlan object with parsed data</returns>
    public FlightPlan FetchFlightPlan(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty", nameof(username));

        string url = $"{SIMBRIEF_API_URL}?username={Uri.EscapeDataString(username)}";

        try
        {
            string xmlContent = DownloadXML(url);
            return ParseSimBriefXML(xmlContent, username);
        }
        catch (WebException ex)
        {
            if (ex.Response is HttpWebResponse response)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    throw new Exception($"Invalid SimBrief username or no flight plan found for user: {username}", ex);
                }
            }
            throw new Exception($"Failed to download flight plan from SimBrief: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching SimBrief flight plan: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Downloads XML data from SimBrief API
    /// </summary>
    private string DownloadXML(string url)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Timeout = TIMEOUT_SECONDS * 1000;
        request.UserAgent = "FBWBA-EFB/1.0";

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Parses SimBrief XML OFP into a FlightPlan object
    /// </summary>
    private FlightPlan ParseSimBriefXML(string xmlContent, string username)
    {
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xmlContent);

        FlightPlan flightPlan = new FlightPlan
        {
            SimBriefUsername = username,
            LoadedTime = DateTime.Now,
            ExtractedFlightData = ExtractFlightData(xmlContent, doc)
        };

        // Parse origin and destination
        XmlNode? originNode = doc.SelectSingleNode("//origin");
        XmlNode? destinationNode = doc.SelectSingleNode("//destination");

        if (originNode != null)
        {
            flightPlan.DepartureICAO = GetNodeValue(originNode, "icao_code") ?? "";

            // Get departure runway
            XmlNode? depRunwayNode = originNode.SelectSingleNode("plan_rwy");
            if (depRunwayNode != null)
            {
                flightPlan.DepartureRunway = depRunwayNode.InnerText.Trim();
            }
        }

        if (destinationNode != null)
        {
            flightPlan.ArrivalICAO = GetNodeValue(destinationNode, "icao_code") ?? "";

            // Get arrival runway
            XmlNode? arrRunwayNode = destinationNode.SelectSingleNode("plan_rwy");
            if (arrRunwayNode != null)
            {
                flightPlan.ArrivalRunway = arrRunwayNode.InnerText.Trim();
            }
        }

        // Create departure airport waypoint (Section A)
        if (!string.IsNullOrEmpty(flightPlan.DepartureICAO))
        {
            var depAirport = CreateAirportWaypoint(originNode, flightPlan.DepartureICAO, FlightPlanSection.DepartureAirport);
            if (depAirport != null)
                flightPlan.DepartureAirportWaypoints.Add(depAirport);

            // Add departure runway as second waypoint in Section A
            if (!string.IsNullOrEmpty(flightPlan.DepartureRunway))
            {
                var depRunway = new WaypointFix
                {
                    Ident = $"RW{flightPlan.DepartureRunway}",
                    Name = $"{flightPlan.DepartureICAO} Runway {flightPlan.DepartureRunway}",
                    Type = "Runway",
                    Section = FlightPlanSection.DepartureAirport,
                    InboundAirway = "DEPART",
                    Latitude = depAirport?.Latitude ?? 0,
                    Longitude = depAirport?.Longitude ?? 0
                };
                flightPlan.DepartureAirportWaypoints.Add(depRunway);
            }
        }

        // Parse navlog for enroute waypoints (Section C)
        XmlNodeList? navlogNodes = doc.SelectNodes("//navlog/fix");
        if (navlogNodes != null)
        {
            foreach (XmlNode fixNode in navlogNodes)
            {
                // Check if this is a SID/STAR waypoint (we skip these per requirements)
                string isSidStar = GetNodeValue(fixNode, "is_sid_star") ?? "";
                if (isSidStar == "1")
                    continue;

                WaypointFix? waypoint = ParseNavlogFix(fixNode);
                if (waypoint != null)
                {
                    waypoint.Section = FlightPlanSection.Enroute;
                    flightPlan.EnrouteWaypoints.Add(waypoint);
                }
            }
        }

        // Create arrival airport waypoints (Section F)
        if (!string.IsNullOrEmpty(flightPlan.ArrivalICAO))
        {
            // Add arrival runway as first waypoint in Section F
            if (!string.IsNullOrEmpty(flightPlan.ArrivalRunway))
            {
                var arrAirport = CreateAirportWaypoint(destinationNode, flightPlan.ArrivalICAO, FlightPlanSection.ArrivalAirport);

                var arrRunway = new WaypointFix
                {
                    Ident = $"RW{flightPlan.ArrivalRunway}",
                    Name = $"{flightPlan.ArrivalICAO} Runway {flightPlan.ArrivalRunway}",
                    Type = "Runway",
                    Section = FlightPlanSection.ArrivalAirport,
                    InboundAirway = "ARRIVAL",
                    Latitude = arrAirport?.Latitude ?? 0,
                    Longitude = arrAirport?.Longitude ?? 0
                };
                flightPlan.ArrivalAirportWaypoints.Add(arrRunway);
            }

            // Add arrival airport as second waypoint in Section F
            var arrAirportWpt = CreateAirportWaypoint(destinationNode, flightPlan.ArrivalICAO, FlightPlanSection.ArrivalAirport);
            if (arrAirportWpt != null)
                flightPlan.ArrivalAirportWaypoints.Add(arrAirportWpt);
        }

        return flightPlan;
    }

    /// <summary>
    /// Creates an airport waypoint from XML node
    /// </summary>
    private WaypointFix? CreateAirportWaypoint(XmlNode? airportNode, string icao, FlightPlanSection section)
    {
        if (airportNode == null)
            return null;

        double lat = ParseDouble(GetNodeValue(airportNode, "pos_lat"));
        double lon = ParseDouble(GetNodeValue(airportNode, "pos_long"));
        int elevation = ParseInt(GetNodeValue(airportNode, "elevation"));

        return new WaypointFix
        {
            Ident = icao,
            Name = GetNodeValue(airportNode, "name") ?? icao,
            Type = "Airport",
            Latitude = lat,
            Longitude = lon,
            Altitude = elevation,
            Section = section,
            InboundAirway = section == FlightPlanSection.DepartureAirport ? "ORIGIN" : "DESTINATION"
        };
    }

    /// <summary>
    /// Parses a navlog fix node into a WaypointFix object
    /// </summary>
    private WaypointFix? ParseNavlogFix(XmlNode fixNode)
    {
        if (fixNode == null)
            return null;

        string? ident = GetNodeValue(fixNode, "ident");
        if (string.IsNullOrEmpty(ident))
            return null;

        WaypointFix waypoint = new WaypointFix
        {
            Ident = ident,
            Name = GetNodeValue(fixNode, "name") ?? ident,
            Type = GetNodeValue(fixNode, "type") ?? "Waypoint",
            Latitude = ParseDouble(GetNodeValue(fixNode, "pos_lat")),
            Longitude = ParseDouble(GetNodeValue(fixNode, "pos_long")),
        };

        // Enrich with database data if available (for Region and more accurate coordinates)
        if (_navigationDatabase != null)
        {
            var dbWaypoint = _navigationDatabase.GetWaypoint(ident);
            if (dbWaypoint != null)
            {
                waypoint.Region = dbWaypoint.Region;
                // Use database coordinates as they're more accurate than SimBrief
                waypoint.Latitude = dbWaypoint.Latitude;
                waypoint.Longitude = dbWaypoint.Longitude;
                // Keep SimBrief's Type as it may be more descriptive than database
            }
        }

        // Parse airway information
        string? viaAirway = GetNodeValue(fixNode, "via_airway");
        if (!string.IsNullOrEmpty(viaAirway) && viaAirway.ToUpper() == "DCT")
        {
            waypoint.InboundAirway = "DCT";
        }
        else if (!string.IsNullOrEmpty(viaAirway))
        {
            waypoint.InboundAirway = viaAirway;
        }
        else
        {
            waypoint.InboundAirway = "DCT";
        }

        // Parse altitude if available
        string? altitudeFt = GetNodeValue(fixNode, "altitude_feet");
        if (!string.IsNullOrEmpty(altitudeFt))
        {
            waypoint.Altitude = ParseInt(altitudeFt);
        }

        // Parse frequency for VOR/NDB
        string? frequency = GetNodeValue(fixNode, "frequency");
        if (!string.IsNullOrEmpty(frequency))
        {
            waypoint.Notes = $"Frequency: {frequency}";
        }

        return waypoint;
    }

    /// <summary>
    /// Helper method to get node value from XML
    /// </summary>
    private string? GetNodeValue(XmlNode? parentNode, string childNodeName)
    {
        XmlNode? node = parentNode?.SelectSingleNode(childNodeName);
        return node?.InnerText?.Trim();
    }

    /// <summary>
    /// Parse double value safely
    /// </summary>
    private double ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        if (double.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double result))
        {
            return result;
        }
        return 0;
    }

    /// <summary>
    /// Parse integer value safely
    /// </summary>
    private int ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        if (int.TryParse(value, out int result))
        {
            return result;
        }
        return 0;
    }

    /// <summary>
    /// Extracts relevant flight data from SimBrief OFP XML into a readable text summary
    /// for use in AI route description generation.
    /// </summary>
    private string ExtractFlightData(string ofpXml, XmlDocument doc)
    {
        var sb = new StringBuilder();

        // General flight info
        var general = doc.SelectSingleNode("//general");
        if (general != null)
        {
            AppendXmlIfPresent(sb, "Route", general, "route");
            AppendXmlIfPresent(sb, "Cruise Altitude", general, "initial_altitude");
            AppendXmlIfPresent(sb, "Distance", general, "route_distance");
            AppendXmlIfPresent(sb, "Air Time", general, "air_time");
            AppendXmlIfPresent(sb, "Airline ICAO", general, "icao_airline");
        }

        // Aircraft info
        var aircraft = doc.SelectSingleNode("//aircraft");
        if (aircraft != null)
        {
            AppendXmlIfPresent(sb, "Aircraft", aircraft, "name");
            AppendXmlIfPresent(sb, "Aircraft ICAO", aircraft, "icaocode");
        }

        // Origin
        sb.AppendLine();
        sb.AppendLine("DEPARTURE:");
        var origin = doc.SelectSingleNode("//origin");
        if (origin != null)
        {
            AppendXmlIfPresent(sb, "Airport", origin, "icao_code");
            AppendXmlIfPresent(sb, "Name", origin, "name");
            AppendXmlIfPresent(sb, "Elevation", origin, "elevation");
            AppendXmlIfPresent(sb, "Runway", origin, "plan_rwy");
            AppendXmlIfPresent(sb, "SID", origin, "sid_id");
            AppendXmlIfPresent(sb, "SID Transition", origin, "sid_trans");
            AppendXmlIfPresent(sb, "METAR", origin, "metar");
            AppendXmlIfPresent(sb, "TAF", origin, "taf");
        }

        // Destination
        sb.AppendLine();
        sb.AppendLine("ARRIVAL:");
        var destination = doc.SelectSingleNode("//destination");
        if (destination != null)
        {
            AppendXmlIfPresent(sb, "Airport", destination, "icao_code");
            AppendXmlIfPresent(sb, "Name", destination, "name");
            AppendXmlIfPresent(sb, "Elevation", destination, "elevation");
            AppendXmlIfPresent(sb, "Runway", destination, "plan_rwy");
            AppendXmlIfPresent(sb, "STAR", destination, "star_id");
            AppendXmlIfPresent(sb, "STAR Transition", destination, "star_trans");
            AppendXmlIfPresent(sb, "METAR", destination, "metar");
            AppendXmlIfPresent(sb, "TAF", destination, "taf");
        }

        // Alternate
        var alternate = doc.SelectSingleNode("//alternate");
        if (alternate != null)
        {
            string? altIcao = alternate.SelectSingleNode("icao_code")?.InnerText?.Trim();
            if (!string.IsNullOrEmpty(altIcao))
            {
                sb.AppendLine();
                sb.AppendLine("ALTERNATE:");
                AppendXmlIfPresent(sb, "Airport", alternate, "icao_code");
                AppendXmlIfPresent(sb, "Name", alternate, "name");
            }
        }

        // SigMets / significant weather
        var sigmets = doc.SelectNodes("//sigmets/sigmet");
        if (sigmets != null && sigmets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SIGNIFICANT WEATHER (SIGMETs):");
            foreach (XmlNode sigmet in sigmets)
            {
                string? sigmetText = sigmet.InnerText?.Trim();
                if (!string.IsNullOrEmpty(sigmetText))
                    sb.AppendLine(sigmetText);
            }
        }

        // Navlog waypoints - separate SID, enroute, and STAR
        var fixes = doc.SelectNodes("//navlog/fix");
        if (fixes != null && fixes.Count > 0)
        {
            var sidWaypoints = new List<string>();
            var enrouteWaypoints = new List<string>();
            var starWaypoints = new List<string>();
            bool passedSid = false;

            foreach (XmlNode fix in fixes)
            {
                string? ident = fix.SelectSingleNode("ident")?.InnerText?.Trim();
                string? isSidStar = fix.SelectSingleNode("is_sid_star")?.InnerText?.Trim();
                string? alt = fix.SelectSingleNode("altitude_feet")?.InnerText?.Trim();
                string? airway = fix.SelectSingleNode("via_airway")?.InnerText?.Trim();
                string? lat = fix.SelectSingleNode("pos_lat")?.InnerText?.Trim();
                string? lon = fix.SelectSingleNode("pos_long")?.InnerText?.Trim();
                string? windDir = fix.SelectSingleNode("wind_dir")?.InnerText?.Trim();
                string? windSpd = fix.SelectSingleNode("wind_spd")?.InnerText?.Trim();
                string? name = fix.SelectSingleNode("name")?.InnerText?.Trim();

                if (string.IsNullOrEmpty(ident)) continue;

                var parts = new List<string> { ident };
                if (!string.IsNullOrEmpty(name) && name != ident) parts.Add(name);
                if (!string.IsNullOrEmpty(airway)) parts.Add($"via {airway}");
                if (!string.IsNullOrEmpty(alt) && int.TryParse(alt, out int altFeet)) parts.Add($"FL{altFeet / 100}");
                if (!string.IsNullOrEmpty(lat) && !string.IsNullOrEmpty(lon)) parts.Add($"({lat}, {lon})");
                if (!string.IsNullOrEmpty(windDir) && !string.IsNullOrEmpty(windSpd)) parts.Add($"wind {windDir}/{windSpd}kt");
                string line = string.Join(" | ", parts);

                if (isSidStar == "1")
                {
                    if (!passedSid)
                        sidWaypoints.Add(line);
                    else
                        starWaypoints.Add(line);
                }
                else
                {
                    passedSid = true;
                    enrouteWaypoints.Add(line);
                }
            }

            if (sidWaypoints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("SID WAYPOINTS:");
                foreach (var wp in sidWaypoints) sb.AppendLine(wp);
            }

            if (enrouteWaypoints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ENROUTE WAYPOINTS:");
                foreach (var wp in enrouteWaypoints) sb.AppendLine(wp);
            }

            if (starWaypoints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("STAR WAYPOINTS:");
                foreach (var wp in starWaypoints) sb.AppendLine(wp);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Helper to append an XML child node value if present.
    /// </summary>
    private void AppendXmlIfPresent(StringBuilder sb, string label, XmlNode parent, string childName)
    {
        string? value = parent.SelectSingleNode(childName)?.InnerText?.Trim();
        if (!string.IsNullOrEmpty(value))
            sb.AppendLine($"{label}: {value}");
    }
}

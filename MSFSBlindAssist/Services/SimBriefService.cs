using System.Net;
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
            LoadedTime = DateTime.Now
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
}

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Navigation;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;
/// <summary>
/// Service for fetching and parsing SimBrief flight plans
/// </summary>
public class SimBriefService
{
    private const string SIMBRIEF_API_URL = "https://www.simbrief.com/api/xml.fetcher.php";
    private const string SIMBRIEF_AIRCRAFT_LIST_URL = "https://www.simbrief.com/api/inputs.list.json";
    private const int TIMEOUT_SECONDS = 30;

    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private NavigationDatabaseProvider? _navigationDatabase;

    public SimBriefService(NavigationDatabaseProvider? navigationDatabase = null)
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
            ExtractedFlightData = ExtractFlightData(doc)
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
            try
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
            catch (Exception)
            {
                // Navigation database unavailable (e.g. SQLite DLL not deployed);
                // fall back to SimBrief coordinates for all remaining waypoints.
                _navigationDatabase = null;
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

    // ─────────────────────────────────────────────────────────────────────────
    // TLR text extraction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches the OFP XML text nodes for a Takeoff and Landing Report section
    /// and returns it as cleaned plain text. Returns empty string if not found.
    /// </summary>
    private string ExtractTlrText(XmlDocument doc)
    {
        // SimBrief stores the briefing text under several possible paths depending
        // on the OFP layout selected by the user.
        XmlNode?[] candidates =
        {
            doc.SelectSingleNode("//text/plan_html"),
            doc.SelectSingleNode("//text/ofp"),
            doc.SelectSingleNode("//plan_html"),
            doc.SelectSingleNode("//text"),
        };

        foreach (var node in candidates)
        {
            if (node == null) continue;
            string raw = node.InnerText ?? "";
            if (raw.IndexOf("TAKEOFF", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("TLR",     StringComparison.Ordinal)             < 0)
                continue;

            return CleanAndExtractTlr(raw);
        }
        return "";
    }

    private static string CleanAndExtractTlr(string rawHtml)
    {
        // Replace block-level tags with newlines before stripping, so line breaks are preserved.
        // This is critical for the TLR fixed-width table format.
        string text = rawHtml;
        text = Regex.Replace(text, @"<br\s*/?>",               "\n",  RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(?:p|div|tr|pre|li)>",  "\n",  RegexOptions.IgnoreCase);
        // Strip remaining HTML tags (replace with empty string, NOT a space)
        text = Regex.Replace(text, "<[^>]+>", "");
        // Decode HTML entities (&nbsp; → space, &gt; → >, etc.)
        text = System.Net.WebUtility.HtmlDecode(text);
        // Normalize CRLF, then trim only trailing whitespace per line (NOT leading — RMKS
        // continuation lines start with spaces, and data uses fixed-width columns)
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n').Select(l => l.TrimEnd());
        text = string.Join("\n", lines);
        // Collapse 3+ blank lines to at most 2
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        // Locate the TLR block.  Try several possible section header strings.
        string[] startMarkers =
        {
            "TAKEOFF AND LANDING REPORT",
            "/// TAKEOFF DATA ///",
            "TAKEOFF DATA",
            "TLR-",
        };
        int start = -1;
        foreach (var marker in startMarkers)
        {
            int idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) { start = idx; break; }
        }
        if (start < 0) return "";

        // The TLR runs until a clearly different major section begins.
        string[] endMarkers =
        {
            "FLIGHT PLAN REMARKS",
            "DISPATCH RELEASE",
            "ATC FLIGHT PLAN",
            "TRACK MILES",
            "WIND COMPONENT TABLE",
            "ENROUTE WINDS",
            "NOTAM",
        };
        int end = text.Length;
        foreach (var marker in endMarkers)
        {
            int idx = text.IndexOf(marker, start + 100, StringComparison.OrdinalIgnoreCase);
            if (idx > start && idx < end) end = idx;
        }

        return text[start..end].Trim();
    }

    /// <summary>
    /// Returns the first non-empty value found among the given child node names.
    /// </summary>
    private string First(XmlNode? parent, params string[] names)
    {
        foreach (var name in names)
        {
            var val = parent?.SelectSingleNode(name)?.InnerText?.Trim();
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return "";
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
    private string ExtractFlightData(XmlDocument doc)
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

    // ─────────────────────────────────────────────────────────────────────────
    // Full OFP fetch (for SimBrief Flight Planner)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the latest SimBrief OFP and returns a fully populated SimBriefOFP model.
    /// </summary>
    public async Task<SimBriefOFP> FetchFullOFPAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty", nameof(username));

        string url = $"{SIMBRIEF_API_URL}?username={Uri.EscapeDataString(username)}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("MSFSBlindAssist/1.0");

        HttpResponseMessage response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            int code = (int)response.StatusCode;
            throw new Exception(code == 400
                ? $"No flight plan found for username: {username}"
                : $"SimBrief returned HTTP {code}");
        }

        string xml = await response.Content.ReadAsStringAsync();
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return ParseFullOFP(doc);
    }

    /// <summary>
    /// Fetches the list of available SimBrief aircraft types.
    /// Returns list of (Id, DisplayName) tuples, sorted by name.
    /// </summary>
    public async Task<List<(string Id, string Name)>> FetchAircraftTypesAsync()
    {
        var result = new List<(string Id, string Name)>();

        var request = new HttpRequestMessage(HttpMethod.Get, SIMBRIEF_AIRCRAFT_LIST_URL);
        request.Headers.UserAgent.ParseAdd("MSFSBlindAssist/1.0");

        HttpResponseMessage response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return result;

        string json = await response.Content.ReadAsStringAsync();
        var root = JObject.Parse(json);
        var aircraft = root["aircraft"];
        if (aircraft == null) return result;

        if (aircraft.Type == JTokenType.Object)
        {
            // Format: { "B738": { "name": "Boeing 737-800", ... }, ... }
            foreach (var prop in ((JObject)aircraft).Properties())
            {
                string id = prop.Name;
                string name = prop.Value["name"]?.ToString() ?? id;
                result.Add((id, name));
            }
        }
        else if (aircraft.Type == JTokenType.Array)
        {
            // Format: [ { "id": "B738", "name": "Boeing 737-800" }, ... ]
            foreach (var item in aircraft)
            {
                string id = item["id"]?.ToString() ?? "";
                string name = item["name"]?.ToString() ?? id;
                if (!string.IsNullOrEmpty(id))
                    result.Add((id, name));
            }
        }

        return result.OrderBy(t => t.Name).ToList();
    }

    /// <summary>
    /// Parses a SimBrief XML OFP document into a SimBriefOFP model.
    /// All fields gracefully fall back to empty string if not present.
    /// </summary>
    private SimBriefOFP ParseFullOFP(XmlDocument doc)
    {
        var ofp = new SimBriefOFP();
        string V(XmlNode? n, string child) => n?.SelectSingleNode(child)?.InnerText?.Trim() ?? "";

        // ── General ──────────────────────────────────────────────────────────
        var gen = doc.SelectSingleNode("//general");
        if (gen != null)
        {
            ofp.AirlineIcao     = V(gen, "icao_airline");
            ofp.FlightNumber    = V(gen, "flight_number");
            ofp.Callsign        = V(gen, "callsign");
            ofp.Route           = V(gen, "route");
            ofp.RouteDistance   = V(gen, "route_distance");
            ofp.AirTime         = V(gen, "air_time");
            ofp.CostIndex       = V(gen, "costindex");
            ofp.InitialAltitude = V(gen, "initial_altitude");
            ofp.CruiseMach      = V(gen, "cruise_mach");
            ofp.CruiseTas       = V(gen, "cruise_tas");
            ofp.ClimbProfile    = V(gen, "climb_profile");
            ofp.CruiseProfile   = V(gen, "cruise_profile");
            ofp.DescentProfile  = V(gen, "descent_profile");
            ofp.Passengers      = V(gen, "passengers");
            ofp.AvgWindComp     = V(gen, "avg_wind_comp");
            ofp.AvgIsaDev       = V(gen, "avg_temp_dev");
            ofp.Units           = V(gen, "units").ToUpperInvariant() is "KGS" or "KG" ? "kgs" : "lbs";
            ofp.StepClimbString = V(gen, "stepclimb_string");
        }

        // ── Aircraft ─────────────────────────────────────────────────────────
        var ac = doc.SelectSingleNode("//aircraft");
        if (ac != null)
        {
            ofp.AircraftName = V(ac, "name");
            ofp.AircraftIcao = V(ac, "icaocode");
            ofp.AircraftReg  = V(ac, "reg");
        }

        // ── Origin ───────────────────────────────────────────────────────────
        var orig = doc.SelectSingleNode("//origin");
        if (orig != null)
        {
            ofp.OriginIcao      = V(orig, "icao_code");
            ofp.OriginName      = V(orig, "name");
            ofp.OriginElevation = V(orig, "elevation");
            ofp.OriginRunway    = V(orig, "plan_rwy");
            ofp.OriginSid       = First(orig, "sid_id", "sid");
            ofp.OriginSidTrans  = First(orig, "sid_trans", "sid_transition");
            ofp.OriginMetar     = V(orig, "metar");
            ofp.OriginTaf       = V(orig, "taf");
            ofp.OriginWindDir    = V(orig, "wind_dir");
            ofp.OriginWindSpd    = V(orig, "wind_spd");
            ofp.OriginTransAlt   = V(orig, "trans_alt");
            ofp.OriginTransLevel = V(orig, "trans_level");
        }

        // ── Destination ──────────────────────────────────────────────────────
        var dest = doc.SelectSingleNode("//destination");
        if (dest != null)
        {
            ofp.DestIcao          = V(dest, "icao_code");
            ofp.DestName          = V(dest, "name");
            ofp.DestElevation     = V(dest, "elevation");
            ofp.DestRunway        = V(dest, "plan_rwy");
            ofp.DestStar          = First(dest, "star_id", "star");
            ofp.DestStarTrans     = First(dest, "star_trans", "star_transition");
            ofp.DestApproach      = First(dest, "appr_id", "approach_id", "appr");
            ofp.DestApproachTrans = First(dest, "appr_trans", "approach_trans");
            ofp.DestIlsFreq       = V(dest, "ils_freq");
            ofp.DestMetar         = V(dest, "metar");
            ofp.DestTaf           = V(dest, "taf");
            ofp.DestTransAlt      = V(dest, "trans_alt");
            ofp.DestTransLevel    = V(dest, "trans_level");
        }

        // ── Alternate ────────────────────────────────────────────────────────
        var altn = doc.SelectSingleNode("//alternate");
        if (altn != null)
        {
            ofp.AltnIcao  = V(altn, "icao_code");
            ofp.AltnName  = V(altn, "name");
            ofp.AltnMetar = V(altn, "metar");
            ofp.AltnTaf   = V(altn, "taf");
        }

        // ── Fuel ─────────────────────────────────────────────────────────────
        var fuel = doc.SelectSingleNode("//fuel");
        if (fuel != null)
        {
            ofp.FuelBlockRamp      = V(fuel, "plan_ramp");
            ofp.FuelTrip           = V(fuel, "plan_trip");
            ofp.FuelReserve        = V(fuel, "reserve");
            ofp.FuelAlternate      = V(fuel, "alternate_burn");
            ofp.FuelContingency    = V(fuel, "contingency");
            ofp.FuelExtra          = V(fuel, "extra");
            ofp.FuelMinTakeoff     = V(fuel, "min_takeoff");
            ofp.FuelTaxi           = V(fuel, "taxi");
            ofp.FuelPlannedLanding = V(fuel, "plan_landing");
        }

        // ── Weights ──────────────────────────────────────────────────────────
        var wts = doc.SelectSingleNode("//weights");
        if (wts != null)
        {
            ofp.WeightOew       = V(wts, "oew");
            ofp.WeightPayload   = V(wts, "payload");
            ofp.WeightPaxWeight = V(wts, "paxWeight");
            ofp.WeightCargo     = V(wts, "cargo");
            ofp.WeightZfw       = V(wts, "est_zfw");
            ofp.WeightTow       = V(wts, "est_tow");
            ofp.WeightLw        = V(wts, "est_ldw");
            ofp.WeightMaxZfw    = V(wts, "max_zfw");
            ofp.WeightMaxTow    = V(wts, "max_tow");
            ofp.WeightMaxLw     = V(wts, "max_ldw");
        }

        // ── Performance – Takeoff ─────────────────────────────────────────────
        // SimBrief uses several node names depending on OFP version; try all common ones,
        // then fall back to any node that has a <v1> child.
        var toa = doc.SelectSingleNode("//takeoff_analysis")
               ?? doc.SelectSingleNode("//toa")
               ?? doc.SelectSingleNode("//takeoff")
               ?? doc.SelectSingleNode("//params/takeoff_analysis")
               ?? doc.SelectSingleNode("//params/toa")
               ?? doc.SelectSingleNode("//params/takeoff")
               ?? doc.SelectSingleNode("//params/per/takeoff")
               ?? doc.SelectSingleNode("//per/takeoff")
               ?? doc.SelectSingleNode("//perf/takeoff")
               ?? doc.SelectSingleNode("//tlr/takeoff")
               ?? doc.SelectSingleNode("//*[v1]");
        if (toa != null)
        {
            ofp.TakeoffV1       = V(toa, "v1");
            ofp.TakeoffVr       = V(toa, "vr");
            ofp.TakeoffV2       = V(toa, "v2");
            // Field name for flaps varies across SimBrief OFP versions
            ofp.TakeoffFlaps    = First(toa, "flap_setting", "flaps", "flap", "takeoff_flap");
            ofp.TakeoffTrim     = First(toa, "trim", "stab_trim", "cg_trim");
            ofp.TakeoffHw       = First(toa, "headwind", "head_wind", "hw");
            ofp.TakeoffXw       = First(toa, "crosswind", "cross_wind", "xw");
            ofp.PerfLimitFactor = First(toa, "limiting_factor", "limit", "perf_limit");
        }

        // ── Performance – Landing ─────────────────────────────────────────────
        var lda = doc.SelectSingleNode("//landing_analysis")
               ?? doc.SelectSingleNode("//lda")
               ?? doc.SelectSingleNode("//landing")
               ?? doc.SelectSingleNode("//params/landing_analysis")
               ?? doc.SelectSingleNode("//params/lda")
               ?? doc.SelectSingleNode("//params/landing")
               ?? doc.SelectSingleNode("//params/per/landing")
               ?? doc.SelectSingleNode("//per/landing")
               ?? doc.SelectSingleNode("//perf/landing")
               ?? doc.SelectSingleNode("//tlr/landing")
               ?? doc.SelectSingleNode("//*[vapp]");
        if (lda != null)
        {
            ofp.LandingVapp         = V(lda, "vapp");
            ofp.LandingFlaps        = First(lda, "ldg_flap", "flap_setting", "flaps", "flap");
            ofp.LandingDistDry      = First(lda, "dist_dry", "ldg_dist_dry", "dry_dist");
            ofp.LandingDistWet      = First(lda, "dist_wet", "ldg_dist_wet", "wet_dist");
            ofp.LandingBrakeSetting = First(lda, "autobrakes", "autobrake", "brake_setting", "brakes");
            ofp.LandingHw           = First(lda, "headwind", "head_wind", "hw");
            ofp.LandingXw           = First(lda, "crosswind", "cross_wind", "xw");
        }

        // ── Nav Log ───────────────────────────────────────────────────────────
        // Use doc.DocumentElement to scope to the top-level navlog only,
        // preventing alternate-route navlog fixes from being included.
        var fixes = doc.DocumentElement?.SelectNodes("navlog/fix");
        if (fixes != null)
        {
            foreach (XmlNode fix in fixes)
            {
                string fixIdent = V(fix, "ident");
                string fixName  = V(fix, "name");
                var navFix = new SimBriefNavFix
                {
                    Ident      = fixIdent,
                    ViaAirway  = V(fix, "via_airway"),
                    Type       = V(fix, "type"),
                    AltitudeFt = First(fix, "altitude_feet", "flight_level"),
                    DistLeg    = First(fix, "dist", "dist_leg", "leg_dist", "gc_dist", "distance"),
                    WindDir    = V(fix, "wind_dir"),
                    WindSpd    = V(fix, "wind_spd"),
                    WindComp   = First(fix, "wind_comp", "wind_component", "wcomp", "head_wind"),
                    Efob       = First(fix, "efob", ".//efob", "fuel_ob", ".//fuel_ob", "fob",
                                       "fuel_onboard", "fuel_remaining", "fuel_eta", "fuel_est"),
                    TimeLeg    = First(fix, "time_leg", "leg_time"),
                    TimeTotal  = First(fix, "time_total", "total_time"),
                    VorName    = (!string.IsNullOrEmpty(fixName) && fixName != fixIdent) ? fixName : "",
                    Frequency  = V(fix, "frequency"),
                    IsSidStar  = V(fix, "is_sid_star") == "1",
                    Course     = First(fix, "track_mag", "track_true"),
                    Ias        = V(fix, "ind_airspeed"),
                    Mach       = V(fix, "mach"),
                    Oat        = V(fix, "oat"),
                    IsaDev     = V(fix, "isa_dev"),
                    Mora           = First(fix, "mora", "grid_mora"),
                    IcaoFir        = First(fix, "icao_fir", "fir"),
                    FuelPlanOnboard = V(fix, "fuel_plan_onboard"),
                    FuelTotalUsed   = V(fix, "fuel_totalused"),
                    FuelLeg         = V(fix, "fuel_leg"),
                    DistCum         = First(fix, "dist_cum", "cum_dist", "cumulative_dist"),
                };
                if (!string.IsNullOrEmpty(navFix.Ident))
                    ofp.NavLog.Add(navFix);
            }
        }

        // Remove SimBrief positional markers (TOC/TOD) — type "ltlg" is a lat/long
        // marker used by SimBrief to show top-of-climb / top-of-descent positions.
        // These are NOT real waypoints and should not appear in the navlog display.
        ofp.NavLog.RemoveAll(f => f.Type.Equals("ltlg", StringComparison.OrdinalIgnoreCase));

        // Truncate navlog at the destination airport — SimBrief sometimes appends
        // alternate-route fixes after the destination which should not be shown.
        // Use FindLastIndex so an enroute waypoint with the same ident as the destination
        // doesn't cause premature truncation.
        if (!string.IsNullOrEmpty(ofp.DestIcao))
        {
            int destIdx = ofp.NavLog.FindLastIndex(
                f => f.Ident.Equals(ofp.DestIcao, StringComparison.OrdinalIgnoreCase));
            if (destIdx >= 0 && destIdx < ofp.NavLog.Count - 1)
                ofp.NavLog.RemoveRange(destIdx + 1, ofp.NavLog.Count - destIdx - 1);
        }

        // Normalize destination fix if already present from the XML — SimBrief marks it
        // as is_sid_star=1 with full navlog data, but it should display as a clean airport
        // endpoint (matching the synthetic entry we'd otherwise append).
        // Replace it entirely so no stale flight data leaks into child nodes.
        if (!string.IsNullOrEmpty(ofp.DestIcao) && ofp.NavLog.Count > 0 &&
            ofp.NavLog[^1].Ident.Equals(ofp.DestIcao, StringComparison.OrdinalIgnoreCase))
        {
            ofp.NavLog[^1] = new SimBriefNavFix
            {
                Ident      = ofp.DestIcao,
                VorName    = ofp.DestName,
                Type       = "apt",
                IsSidStar  = false,
                AltitudeFt = ofp.DestElevation,
            };
        }

        // Prepend the departure airport as the first navlog entry if not already present.
        if (!string.IsNullOrEmpty(ofp.OriginIcao) &&
            (ofp.NavLog.Count == 0 ||
             !ofp.NavLog[0].Ident.Equals(ofp.OriginIcao, StringComparison.OrdinalIgnoreCase)))
        {
            ofp.NavLog.Insert(0, new SimBriefNavFix
            {
                Ident      = ofp.OriginIcao,
                VorName    = ofp.OriginName,
                Type       = "apt",
                IsSidStar  = false,
                AltitudeFt = ofp.OriginElevation,
                DistLeg    = "0",
                DistCum    = "0",
            });
        }

        // Remove any trailing origin ICAO that SimBrief sometimes appends at the end.
        while (!string.IsNullOrEmpty(ofp.OriginIcao) && ofp.NavLog.Count > 1 &&
               ofp.NavLog[^1].Ident.Equals(ofp.OriginIcao, StringComparison.OrdinalIgnoreCase))
            ofp.NavLog.RemoveAt(ofp.NavLog.Count - 1);

        // Append the destination airport as the last navlog entry if not already present.
        if (!string.IsNullOrEmpty(ofp.DestIcao) &&
            (ofp.NavLog.Count == 0 ||
             !ofp.NavLog[^1].Ident.Equals(ofp.DestIcao, StringComparison.OrdinalIgnoreCase)))
        {
            ofp.NavLog.Add(new SimBriefNavFix
            {
                Ident      = ofp.DestIcao,
                VorName    = ofp.DestName,
                Type       = "apt",
                IsSidStar  = false,
                AltitudeFt = ofp.DestElevation,
            });
        }

        // ── Diagnostics: capture first fix's XML element names ───────────────
        if (fixes != null && fixes.Count > 0)
        {
            var firstFix = fixes[0];
            ofp.NavLogFieldNames = string.Join(" ", firstFix?.ChildNodes
                .Cast<System.Xml.XmlNode>()
                .Where(n => n.NodeType == System.Xml.XmlNodeType.Element)
                .Select(n => n.Name) ?? Enumerable.Empty<string>());
        }

        // ── TLR text ──────────────────────────────────────────────────────────
        ofp.TlrText = ExtractTlrText(doc);

        return ofp;
    }
}

using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Service for analyzing cockpit displays using Google Gemini AI.
/// </summary>
public class GeminiService
{
    private static readonly HttpClient httpClient = new HttpClient();
    private const string API_BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent";
    private readonly string apiKey;

    static GeminiService()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public GeminiService()
    {
        apiKey = SettingsManager.Current.GeminiApiKey;
    }

    /// <summary>
    /// Display types that can be analyzed.
    /// </summary>
    public enum DisplayType
    {
        PFD,           // Primary Flight Display
        LowerECAM,     // Lower ECAM
        UpperECAM,     // Upper ECAM / Engine Warning Display
        ND,            // Navigation Display
        ISIS,          // Integrated Standby Instrument System
        EICAS,         // Engine Indicating and Crew Alerting System (Boeing 777)
        PFD777,        // Primary Flight Display (Boeing 777)
        ND777,         // Navigation Display (Boeing 777)
        ISFD           // Integrated Standby Flight Display (Boeing 777)
    }

    /// <summary>
    /// Analyzes the flight simulator scene using Gemini AI.
    /// Focuses on the visual experience - lighting, weather, terrain, and environment.
    /// </summary>
    /// <param name="imageBytes">Screenshot as PNG byte array</param>
    /// <returns>Text description of the scene</returns>
    public async Task<string> AnalyzeSceneAsync(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image data is empty or null.", nameof(imageBytes));
        }

        string prompt = GetScenePrompt();
        return await SendImageRequestAsync(prompt, imageBytes);
    }

    /// <summary>
    /// Analyzes a cockpit display screenshot using Gemini AI.
    /// </summary>
    /// <param name="imageBytes">Screenshot as PNG byte array</param>
    /// <param name="displayType">Type of display being analyzed</param>
    /// <returns>Text description of the display</returns>
    public async Task<string> AnalyzeDisplayAsync(byte[] imageBytes, DisplayType displayType)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image data is empty or null.", nameof(imageBytes));
        }

        string prompt = GetPromptForDisplay(displayType);
        return await SendImageRequestAsync(prompt, imageBytes);
    }

    /// <summary>
    /// Generates a prompt for scene description focused on the visual experience.
    /// </summary>
    private string GetScenePrompt()
    {
        return @"You are describing the visual flight simulator scene for a blind pilot.

Your goal is to help them experience what a sighted pilot would see when sitting back and enjoying the view.

Focus on these aspects in order of priority:

1. TIME OF DAY & LIGHTING:
   - Time of day (sunrise, golden hour, midday, sunset, dusk, night)
   - Quality and direction of light (harsh shadows, soft diffused light, dramatic lighting)
   - Sun position and appearance
   - Reflections on aircraft surfaces or water
   - Sky colors and gradients

2. WEATHER & ATMOSPHERE:
   - Cloud coverage (clear, scattered, overcast) and cloud types
   - Precipitation (rain, snow, fog) if visible
   - Visibility and atmospheric conditions
   - Weather mood (crisp clear day, moody overcast, dramatic storm)

3. TERRAIN & LANDSCAPE:
   - What's visible below and around (ocean, mountains, plains, urban, rural)
   - Notable landmarks or geographic features
   - Terrain textures and colors
   - Horizon line and how terrain meets sky
   - Other aircraft if visible

4. AIRPORT ENVIRONMENT (if at an airport):
   - Runway and taxiway layout
   - Terminal buildings and airport structures
   - Ground vehicles and activity
   - Airport lighting (if applicable)
   - Position on ground or in pattern

IMPORTANT GUIDELINES:
- De-emphasize instruments and cockpit displays (you may briefly mention them if they're prominent in the view, but they are NOT the focus)
- Focus on the OUTSIDE scene and what makes it visually interesting
- Use factual, direct descriptions of what is visible
- Quality assessments are welcome (beautiful, stunning, dramatic, impressive) - blind pilots need to know if scenery is worth sharing on social media
- AVOID metaphors, similes, and poetic comparisons (do NOT use phrases like ""as if"", ""like a"", or comparisons to abstract concepts)
- AVOID dreamy or flowery language - stick to observable facts about lighting, weather, and terrain
- Use line breaks to separate major scene elements for screen reader clarity
- Do not use markdown formatting
- Be vivid but concise - aim for a rich description in 150-300 words

Describe what you see directly and factually, helping someone understand the visual scene.";
    }

    /// <summary>
    /// Generates an appropriate prompt for each display type.
    /// </summary>
    private string GetPromptForDisplay(DisplayType displayType)
    {
        return displayType switch
        {
            DisplayType.PFD => @"You are reading the Primary Flight Display of an Airbus aircraft for a screen reader user.
The image may contain multiple displays. ONLY describe the Primary Flight Display (PFD). Ignore any other displays.
Be extremely concise and direct. Skip descriptions of tape layouts, scales, and visual positioning. Only report actual values, modes, states, and deviations.
Report colors for flight mode annunciators (FMA) and any warning/alert colors (amber, red). Skip colors for normal flight data displays (airspeed, altitude, vertical speed tapes).
Report: airspeed value, altitude value, vertical speed value, pitch/roll if significant, flight director status if active, lateral/vertical deviation if not centered, QNH, flight mode annunciators, any warnings.
Use line breaks to separate major values. Put airspeed, altitude, vertical speed, deviations, QNH, and flight mode annunciators each on their own line.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            DisplayType.LowerECAM => @"You are reading the Lower ECAM display for a screen reader user.
The image may contain multiple displays. ONLY describe the Lower ECAM (bottom center display). Ignore any other displays.
Be extremely concise and direct. Skip descriptions of layouts, visual positioning, and diagram explanations. Only report actual values, quantities, states, and modes.
Skip normal colors (green, white) - only mention warning/alert colors (amber, red).
Report: page name, all displayed values with units, system states (ON/OFF, OPEN/CLOSED, etc.), any warnings or cautions.
Use line breaks to separate parameters. Put the page name on the first line, then each parameter or system state on its own line.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            DisplayType.UpperECAM => @"You are reading the Upper ECAM display for a screen reader user.
The image may contain multiple displays. ONLY describe the Upper ECAM/EWD (top center display with engine parameters). Ignore any other displays.
Be extremely concise and direct. Skip descriptions of layouts, visual positioning, and gauge explanations. Only report actual values and states.
Skip normal colors (green, white) - only mention warning/alert colors (amber, red).
Report: engine parameters (N1, N2, EGT, fuel flow for each engine), flap position, slat position, any warnings or cautions with their colors.
Use line breaks to separate parameters. Put each engine on its own line, then each system state or configuration item on its own line.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            DisplayType.ND => @"You are reading the Navigation Display for a screen reader user.
The image may contain multiple displays. ONLY describe the Navigation Display (ND - the map display). Ignore any other displays.
Be extremely concise and direct. Skip descriptions of map layouts, visual positioning, and symbology explanations. Only report actual values, modes, and navigation data.
Skip normal colors (green, white, magenta) - only mention warning/alert colors (amber, red).
Report: display mode (ROSE NAV, ARC, PLAN), range setting, aircraft heading/track, active waypoints in sequence, distance/time to next waypoint, course deviation if present, weather radar returns if shown, TCAS traffic if present.
Use line breaks to separate information. Put mode and range on the first line, heading/track on the next line, then each waypoint on its own line.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            DisplayType.ISIS => @"You are reading the ISIS backup display for a screen reader user.
The image may contain multiple displays. ONLY describe the ISIS (center backup instrument). Ignore any other displays.
Be extremely concise and direct. Skip descriptions of instrument layout and visual positioning. Only report actual values.
Skip normal colors - only mention warning/alert colors (amber, red).
Report: airspeed value, altitude value, pitch/roll if significant, any warnings.
Use line breaks to separate values. Put airspeed, altitude, and attitude each on their own line.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            DisplayType.ISFD => @"You are reading the Integrated Standby Flight Display (ISFD) of a Boeing 777 for a screen reader user.
The image may contain multiple displays. ONLY describe the ISFD — the small square backup instrument located between the captain's displays and the EICAS. Ignore all other displays.
The ISFD is a compact display showing basic flight parameters as backup instruments.
Be extremely concise and direct. Skip descriptions of instrument layout and visual positioning. Only report actual values.
Report: airspeed in knots, altitude in feet, barometric setting, pitch and roll attitude if significant, any warnings or flags.
Skip normal colors - only mention warning/alert colors (amber, red).
Use line breaks to separate values. Put airspeed, altitude, baro setting, and attitude each on their own line.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            DisplayType.ND777 => @"You are reading the Navigation Display (ND) of a Boeing 777 for a screen reader user.
The image may contain multiple displays. ONLY describe the Navigation Display (ND) — the second display from the left showing the map/navigation information. Ignore PFD, EICAS, and any other displays.
Be extremely concise and direct. Skip descriptions of map layouts, visual positioning, and symbology explanations. Only report actual values, modes, and navigation data.

Report in this order:
Display mode: MAP, CTR MAP, PLAN, APP, or VOR.
Range setting.
Aircraft heading or track.
Active waypoint and next waypoints in sequence with distances and times if shown.
Course deviation if present.
Wind direction and speed if displayed.
True airspeed and ground speed if shown in the display corners.
Weather radar returns if shown (intensity and position relative to aircraft).
TCAS traffic if present (relative position, altitude, and trend).
Any terrain warnings or alerts.

Skip normal colors (green, white, magenta) - only mention warning/alert colors (amber, red).
Use line breaks to separate information. Put mode and range on the first line, heading/track on the next, then each waypoint on its own line.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            DisplayType.PFD777 => @"You are reading the Primary Flight Display (PFD) of a Boeing 777 for a screen reader user.
The image may contain multiple displays. ONLY describe the Primary Flight Display (PFD) — the leftmost display showing airspeed, altitude, and attitude. Ignore EICAS, ND, and any other displays.
Be extremely concise and direct. Skip descriptions of tape layouts, scales, and visual positioning. Only report actual values, modes, states, and deviations.

Report in this order:
Airspeed: current indicated airspeed in knots, any speed bugs or reference speeds shown (V1, VR, V2, Vref).
Mach number if displayed.
Altitude: current altitude, target/selected altitude if shown.
Vertical speed value.
Heading or track value.
Flight Mode Annunciators (FMA): report the active modes across the top of the PFD — autothrottle mode, roll mode, pitch mode, autopilot engagement status. Report colors for FMA (green = active, white = armed, amber = warning).
Flight director status if active.
Localizer and glideslope deviation if displayed and not centered.
Radio altitude if displayed.
QNH/altimeter setting.
Any warnings or alerts.

Skip normal colors (green, white) for flight data — only mention warning/alert colors (amber, red).
Use line breaks to separate major values. Put each item on its own line.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            DisplayType.EICAS => @"You are reading the EICAS display of a Boeing 777 for a screen reader user.
The image may contain multiple displays. ONLY describe the EICAS (the center display showing engine parameters and system information). Ignore PFD, ND, and any other displays.
The EICAS has two sections: the upper section shows primary engine parameters, and the lower section shows either a secondary engine display or a system synoptic page.

UPPER EICAS - Report these engine parameters for each engine (left engine and right engine):
N1 percentage, N2 percentage, EGT in degrees Celsius, fuel flow.
Also report: TAT (Total Air Temperature), SAT (Static Air Temperature), total fuel, flap position, gear status if displayed, and thrust mode if shown.

LOWER EICAS / SYSTEM PAGE - If a system page is displayed below the engine parameters, identify which page it is (ENG, ELEC, HYD, FUEL, AIR, DOOR, GEAR, FCTL, STAT, CHKL) and report all values, states, and parameters shown on that page. If a checklist is displayed, read the checklist title and all items with their status.

Report any caution or warning messages displayed in the crew alerting area.
Be extremely concise and direct. Skip descriptions of gauge layouts, arc positions, and visual formatting. Only report actual values and states.
Skip normal colors (green, white) - only mention warning/alert colors (amber, red).
Use line breaks to separate parameters. Put each engine on its own line, then system page data on separate lines.
Do not use markdown formatting. Do not explain what things mean. Just state the essential data.",

            _ => "Report what you see on this display in plain text. No markdown formatting. No explanations. Just the data."
        };
    }

    /// <summary>
    /// Generates a narrative route description from pre-extracted flight data.
    /// Optionally uses Google Search grounding to find current NOTAMs and real-time information.
    /// </summary>
    /// <param name="flightData">Pre-extracted flight data summary text</param>
    /// <returns>Text description of the route</returns>
    public async Task<string> DescribeRouteAsync(string flightData)
    {
        string prompt = GetRouteDescriptionPrompt(flightData);
        bool enableSearch = SettingsManager.Current.GeminiSearchGrounding;
        return await SendTextRequestAsync(prompt, enableSearch: enableSearch);
    }

    /// <summary>
    /// Sends a text-only request to Gemini and parses the response.
    /// Optionally enables Google Search grounding for real-time information like NOTAMs.
    /// </summary>
    private async Task<string> SendTextRequestAsync(string prompt, bool enableSearch = false)
    {
        var contents = new[]
        {
            new
            {
                parts = new object[]
                {
                    new { text = prompt }
                }
            }
        };

        object requestBody = enableSearch
            ? new
            {
                contents,
                tools = new object[]
                {
                    new { google_search = new { } }
                }
            }
            : new { contents };

        return await SendRequestAsync(requestBody);
    }

    /// <summary>
    /// Sends an image + text request to Gemini and parses the response.
    /// </summary>
    private async Task<string> SendImageRequestAsync(string prompt, byte[] imageBytes)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = "image/png",
                                data = Convert.ToBase64String(imageBytes)
                            }
                        }
                    }
                }
            }
        };

        return await SendRequestAsync(requestBody);
    }

    /// <summary>
    /// Sends a request to the Gemini API and returns the text response.
    /// </summary>
    private async Task<string> SendRequestAsync(object requestBody)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Please configure it in File > Gemini Settings.");
        }

        string jsonRequest = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

        string url = $"{API_BASE_URL}?key={apiKey}";
        HttpResponseMessage response = await httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Gemini API request failed with status {response.StatusCode}: {errorContent}");
        }

        string responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<GeminiResponse>(responseJson);

        if (result?.Candidates == null || result.Candidates.Length == 0)
        {
            throw new InvalidOperationException("Gemini API returned no candidates in response.");
        }

        var candidateContent = result.Candidates[0].Content;
        if (candidateContent?.Parts == null || candidateContent.Parts.Length == 0)
        {
            throw new InvalidOperationException("Gemini API returned no content in response.");
        }

        return candidateContent.Parts[0].Text ?? "No description available.";
    }

    /// <summary>
    /// Generates the prompt for route description.
    /// </summary>
    private string GetRouteDescriptionPrompt(string flightData)
    {
        return $@"You are writing a flight briefing for a blind flight simulator pilot. Based on the flight plan data below, write a narrative description of the route that helps the pilot understand what they will experience during this flight.

Cover the following topics, using descriptive section headings separated by blank lines:

1. FLIGHT OVERVIEW
   - Origin and destination cities/airports
   - Total distance and approximate flight time
   - Cruise altitude and general direction of flight
   - Countries or major regions traversed

2. DEPARTURE AND SID
   - Describe the area around the departure airport (city, terrain, water features, notable landmarks)
   - Terrain challenges on departure (mountains, obstacles, noise abatement areas)
   - What a pilot might see looking out the window during climb-out
   - Describe the filed SID procedure if present: its name, the waypoints it follows, and any notable routing (e.g. follows a river, turns toward the coast, etc.)
   - State the published top altitude for this SID based on your knowledge of the procedure (not from the flight plan data)

3. ENROUTE
   - Major cities, regions, or geographic features along the route
   - Mountain ranges, bodies of water, deserts, or other notable terrain below
   - Any interesting landmarks or geographic transitions (coastlines, borders, etc.)

4. ARRIVAL AND STAR
   - Describe the area around the destination airport (city, terrain, water features, notable landmarks)
   - Terrain challenges on arrival (mountains, obstacles, complex approaches)
   - What a pilot might see looking out the window during approach
   - Describe the filed STAR procedure if present: its name, the waypoints it follows, and any notable routing
   - State the published bottom altitude for this STAR based on your knowledge of the procedure (not from the flight plan data)

5. WEATHER
   - Summarize departure and arrival weather from the METAR data
   - Mention any SIGMETs or significant weather along the route
   - Note any weather that could affect the flight experience (turbulence, visibility, precipitation)

6. NOTAMS
   - Search for current NOTAMs for both the departure and arrival airports using their ICAO codes
   - Focus on operationally significant NOTAMs that would affect this flight, such as:
     - Closed or restricted runways
     - Inoperative ILS, VOR, or other navigation aids
     - Taxiway closures or restrictions
     - Airspace restrictions or temporary flight restrictions
     - Airport facility outages (lighting, PAPI, etc.)
   - Summarize each relevant NOTAM in plain language (not raw NOTAM code)
   - If no significant NOTAMs are found, state that no notable NOTAMs were found for these airports
   - Skip routine or minor NOTAMs (e.g. crane notifications, wildlife warnings) unless they affect runway operations

IMPORTANT GUIDELINES:
- Write in plain text with no markdown formatting
- Use line breaks between sections for screen reader clarity
- Use section headings in plain text (not with # or * symbols)
- Be factual and informative, drawing on your geographic knowledge
- Aim for 300 to 500 words
- Focus on helping the pilot build a mental picture of the journey
- If weather data is not available, note that and skip the weather section

FLIGHT PLAN DATA:
{flightData}";
    }

    #region Response Models

    private class GeminiResponse
    {
        [JsonProperty("candidates")]
        public Candidate[]? Candidates { get; set; }
    }

    private class Candidate
    {
        [JsonProperty("content")]
        public Content? Content { get; set; }
    }

    private class Content
    {
        [JsonProperty("parts")]
        public Part[]? Parts { get; set; }
    }

    private class Part
    {
        [JsonProperty("text")]
        public string? Text { get; set; }
    }

    #endregion
}

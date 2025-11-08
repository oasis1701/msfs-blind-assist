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
    private const string API_BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
    private readonly string apiKey;

    static GeminiService()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(30);
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
        ISIS           // Integrated Standby Instrument System
    }

    /// <summary>
    /// Analyzes the flight simulator scene using Gemini AI.
    /// Focuses on the visual experience - lighting, weather, terrain, and environment.
    /// </summary>
    /// <param name="imageBytes">Screenshot as PNG byte array</param>
    /// <returns>Text description of the scene</returns>
    public async Task<string> AnalyzeSceneAsync(byte[] imageBytes)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Please configure it in File > Gemini API Key Settings.");
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image data is empty or null.", nameof(imageBytes));
        }

        // Generate scene-focused prompt
        string prompt = GetScenePrompt();

        // Prepare the request
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

        string jsonRequest = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

        // Send request to Gemini API
        string url = $"{API_BASE_URL}?key={apiKey}";
        HttpResponseMessage response = await httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Gemini API request failed with status {response.StatusCode}: {errorContent}");
        }

        // Parse response
        string responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<GeminiResponse>(responseJson);

        if (result?.Candidates == null || result.Candidates.Length == 0)
        {
            throw new InvalidOperationException("Gemini API returned no candidates in response.");
        }

        if (result.Candidates[0].Content?.Parts == null || result.Candidates[0].Content.Parts.Length == 0)
        {
            throw new InvalidOperationException("Gemini API returned no content in response.");
        }

        return result.Candidates[0].Content.Parts[0].Text ?? "No description available.";
    }

    /// <summary>
    /// Analyzes a cockpit display screenshot using Gemini AI.
    /// </summary>
    /// <param name="imageBytes">Screenshot as PNG byte array</param>
    /// <param name="displayType">Type of display being analyzed</param>
    /// <returns>Text description of the display</returns>
    public async Task<string> AnalyzeDisplayAsync(byte[] imageBytes, DisplayType displayType)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Please configure it in File > Gemini API Key Settings.");
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image data is empty or null.", nameof(imageBytes));
        }

        // Generate prompt based on display type
        string prompt = GetPromptForDisplay(displayType);

        // Prepare the request
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

        string jsonRequest = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

        // Send request to Gemini API
        string url = $"{API_BASE_URL}?key={apiKey}";
        HttpResponseMessage response = await httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Gemini API request failed with status {response.StatusCode}: {errorContent}");
        }

        // Parse response
        string responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<GeminiResponse>(responseJson);

        if (result?.Candidates == null || result.Candidates.Length == 0)
        {
            throw new InvalidOperationException("Gemini API returned no candidates in response.");
        }

        if (result.Candidates[0].Content?.Parts == null || result.Candidates[0].Content.Parts.Length == 0)
        {
            throw new InvalidOperationException("Gemini API returned no content in response.");
        }

        return result.Candidates[0].Content.Parts[0].Text ?? "No description available.";
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

            _ => "Report what you see on this display in plain text. No markdown formatting. No explanations. Just the data."
        };
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

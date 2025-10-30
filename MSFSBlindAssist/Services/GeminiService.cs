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
    /// Generates an appropriate prompt for each display type.
    /// </summary>
    private string GetPromptForDisplay(DisplayType displayType)
    {
        return displayType switch
        {
            DisplayType.PFD => @"You are reading the Primary Flight Display for a screen reader user.
The image may contain multiple displays. ONLY describe the Primary Flight Display (PFD). Ignore any other displays.
Report all values, indicators, and messages you see in plain text.
Do not use markdown formatting. Do not explain what things mean. Just state the data and any message colors.",

            DisplayType.LowerECAM => @"You are reading the Lower ECAM display for a screen reader user.
The image may contain multiple displays. ONLY describe the Lower ECAM (bottom center display). Ignore any other displays.
Report the current page and all values shown in plain text.
Do not use markdown formatting. Do not explain what things mean. Just state the data and any message colors.",

            DisplayType.UpperECAM => @"You are reading the Upper ECAM display for a screen reader user.
The image may contain multiple displays. ONLY describe the Upper ECAM/EWD (top center display with engine parameters). Ignore any other displays.
Report everything you see on that display in plain text format.
Do not use markdown formatting. Do not explain what things mean. Just state what is displayed and the colors of any messages.",

            DisplayType.ND => @"You are reading the Navigation Display for a screen reader user.
The image may contain multiple displays. ONLY describe the Navigation Display (ND - the map display). Ignore any other displays.
Report the mode, range, waypoints, and everything visible in plain text.
Do not use markdown formatting. Do not explain what things mean. Just state the data.",

            DisplayType.ISIS => @"You are reading the ISIS backup display for a screen reader user.
The image may contain multiple displays. ONLY describe the ISIS (center backup instrument). Ignore any other displays.
Report all values shown in plain text.
Do not use markdown formatting. Do not explain what things mean. Just state the data.",

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

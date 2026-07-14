using System.Net.Http;
using System.Linq;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Newtonsoft.Json;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Claude (Anthropic) implementation of <see cref="IAiProvider"/>. Mirrors <see cref="GeminiService"/>:
/// vision for display/scene reads, text + optional web-search grounding for the route briefing, and a
/// live model list. Raw <see cref="HttpClient"/> + Newtonsoft to match the existing Gemini transport
/// (no SDK dependency). User-supplied API key only (<c>UserSettings.ClaudeApiKey</c>) — MSFSBA never
/// pays centrally. The prompts are shared with Gemini (the three <c>GeminiService.Get*Prompt</c>
/// builders are provider-neutral), so the wording stays in sync across providers.
/// </summary>
public class ClaudeService : IAiProvider
{
    private static readonly HttpClient httpClient = new HttpClient();

    private const string MESSAGES_URL = "https://api.anthropic.com/v1/messages";
    private const string MODELS_URL = "https://api.anthropic.com/v1/models";
    private const string ANTHROPIC_VERSION = "2023-06-01";

    // Concrete model required (Anthropic has no rolling "*-latest" alias). Default is the latest
    // flagship Opus; the Settings dialog's AI tab lets the user pick a cheaper model (Sonnet/Haiku).
    private const string DEFAULT_MODEL = "claude-opus-4-8";

    // web_search server tool for route-briefing NOTAM grounding (no beta header). The dated variant
    // is supported on current models (Opus 4.6+/Sonnet 4.6 — including the default Opus 4.8). If the
    // selected model rejects it (e.g. Haiku 4.5), DescribeRouteAsync degrades to an ungrounded briefing.
    private const string WEB_SEARCH_TOOL_TYPE = "web_search_20260209";

    // Cap searches per briefing. Without this the model can run 10+ searches for a multi-airport
    // NOTAM/weather briefing, which is slow (risking the HttpClient timeout) and costs $10/1000
    // searches. ~5 covers departure + arrival NOTAMs + weather + SIGMETs without runaway latency.
    private const int WEB_SEARCH_MAX_USES = 5;

    // The Messages API REQUIRES max_tokens (it cannot be omitted), so "no cap" means a value
    // the response never reaches: 16000 is ~30x a 300-500 word briefing, within every current
    // model's output limit, and the documented ceiling for non-streaming requests (larger
    // values need SSE streaming to avoid HTTP timeouts).
    private const int MAX_TOKENS = 16000;

    // Max long-edge pixels for a vision image; see ComputeScaledSize.
    private const int MAX_IMAGE_LONG_EDGE = 2576;

    // Route-briefing system prompt (text path only). With web_search enabled the model otherwise
    // emits process narration ("Let me search for NOTAMs… Now I'll compose the briefing.") before
    // the answer; this tells it to output only the briefing. ParseResponse also structurally drops
    // any narration that precedes the final answer (text after the last tool block).
    private const string ROUTE_SYSTEM_PROMPT =
        "You are a flight-briefing generator for a blind flight-simulator pilot. Output ONLY the " +
        "briefing itself, beginning directly with the first section heading. Never include any " +
        "preamble, sign-off, or commentary about searching, tools, or your own process.";

    private readonly string apiKey;

    static ClaudeService()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    public ClaudeService(string? apiKeyOverride = null)
    {
        apiKey = !string.IsNullOrWhiteSpace(apiKeyOverride)
            ? apiKeyOverride.Trim()
            : SettingsManager.Current.ClaudeApiKey;
    }

    public async Task<string> AnalyzeSceneAsync(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image data is empty or null.", nameof(imageBytes));
        }
        return await SendImageRequestAsync(GeminiService.GetScenePrompt(), imageBytes);
    }

    public async Task<string> AnalyzeDisplayAsync(byte[] imageBytes, GeminiService.DisplayType displayType)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image data is empty or null.", nameof(imageBytes));
        }
        return await SendImageRequestAsync(GeminiService.GetPromptForDisplay(displayType), imageBytes);
    }

    public async Task<string> DescribeRouteAsync(string flightData)
    {
        string prompt = GeminiService.GetRouteDescriptionPrompt(flightData);
        bool enableSearch = SettingsManager.Current.ClaudeWebSearch;
        try
        {
            return await SendTextRequestAsync(prompt, enableSearch);
        }
        catch (HttpRequestException ex) when (enableSearch &&
            ex.Message.Contains("web_search", StringComparison.OrdinalIgnoreCase))
        {
            // The selected model doesn't support the web_search tool (the API's 400 body names
            // the rejected tool type, so it always contains "web_search" — a broader match like
            // "tool" would swallow unrelated 400s). Degrade to an ungrounded briefing rather
            // than failing, but SAY SO up front: a blind pilot who asked for NOTAM grounding
            // must not silently receive an ungrounded briefing as if it were current.
            string briefing = await SendTextRequestAsync(prompt, false);
            return "Note: web search is not available for the selected Claude model, so this " +
                   "briefing is not grounded with current NOTAM or weather data.\n\n" + briefing;
        }
    }

    private static string ResolveModel()
    {
        string model = SettingsManager.Current.ClaudeModel;
        return string.IsNullOrWhiteSpace(model) ? DEFAULT_MODEL : model;
    }

    private async Task<string> SendImageRequestAsync(string prompt, byte[] imageBytes)
    {
        imageBytes = DownscaleImageIfNeeded(imageBytes);
        var requestBody = new
        {
            model = ResolveModel(),
            max_tokens = MAX_TOKENS,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = "image/png",
                                data = Convert.ToBase64String(imageBytes)
                            }
                        },
                        new { type = "text", text = prompt }
                    }
                }
            }
        };
        return await SendRequestAsync(requestBody);
    }

    private async Task<string> SendTextRequestAsync(string prompt, bool enableSearch)
    {
        object requestBody = enableSearch
            ? new
            {
                model = ResolveModel(),
                max_tokens = MAX_TOKENS,
                system = ROUTE_SYSTEM_PROMPT,
                messages = new[] { new { role = "user", content = prompt } },
                tools = new object[]
                {
                    new { type = WEB_SEARCH_TOOL_TYPE, name = "web_search", max_uses = WEB_SEARCH_MAX_USES }
                }
            }
            : new
            {
                model = ResolveModel(),
                max_tokens = MAX_TOKENS,
                system = ROUTE_SYSTEM_PROMPT,
                messages = new[] { new { role = "user", content = prompt } }
            };
        return await SendRequestAsync(requestBody);
    }

    /// <summary>
    /// POSTs to the Messages API and returns the joined text. Retries transient failures
    /// (429/5xx/timeout/connection) with backoff; fails fast on client errors (400/401/403/404).
    /// Headers are set per-request (the HttpClient is shared/static).
    /// </summary>
    private async Task<string> SendRequestAsync(object requestBody)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Claude API key is not configured. Please configure it in File > Settings > AI tab.");
        }

        string jsonRequest = JsonConvert.SerializeObject(requestBody);

        const int maxAttempts = 4; // 1 initial + 3 retries
        Exception? lastTransient = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, MESSAGES_URL);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", ANTHROPIC_VERSION);
            request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request);
            }
            catch (TaskCanceledException ex)
            {
                // 120 s HttpClient timeout (no caller token is ever passed). Transient — retry.
                lastTransient = new HttpRequestException("Claude API request timed out.", ex);
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds((int)Math.Pow(2, attempt + 1)));
                    continue;
                }
                break;
            }
            catch (HttpRequestException ex)
            {
                // Connection-level failure (DNS, reset, TLS). Transient — retry.
                lastTransient = ex;
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds((int)Math.Pow(2, attempt + 1)));
                    continue;
                }
                break;
            }

            // using guarantees disposal on every exit below — including a throw from
            // ReadAsStringAsync, which the old manual Dispose calls leaked on.
            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    string okJson = await response.Content.ReadAsStringAsync();
                    return ParseResponse(okJson);
                }

                var status = response.StatusCode;
                int code = (int)status;

                // Client errors won't be fixed by retrying — fail fast with the body.
                if (status == System.Net.HttpStatusCode.BadRequest ||      // 400
                    status == System.Net.HttpStatusCode.Unauthorized ||    // 401
                    status == System.Net.HttpStatusCode.Forbidden ||       // 403
                    status == System.Net.HttpStatusCode.NotFound)          // 404 (model unavailable)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    string message = status switch
                    {
                        System.Net.HttpStatusCode.NotFound =>
                            $"Claude model '{ResolveModel()}' is unavailable — choose a different model in File > Settings > AI tab. ({errorContent})",
                        System.Net.HttpStatusCode.Unauthorized =>
                            $"Claude API key was rejected (401). Check the key in File > Settings > AI tab. ({errorContent})",
                        _ => $"Claude API request failed with status {code}: {errorContent}"
                    };
                    throw new HttpRequestException(message);
                }

                // Transient server / rate-limit errors (429, 5xx) — retry with backoff.
                if (status == System.Net.HttpStatusCode.TooManyRequests || (code >= 500 && code <= 599))
                {
                    string busyBody = await response.Content.ReadAsStringAsync();
                    lastTransient = new HttpRequestException($"Claude transient error ({code}): {busyBody}");
                    int delaySeconds = GetRetryDelay(response, attempt);
                    if (attempt < maxAttempts - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        continue;
                    }
                    break;
                }

                // Any other unexpected status — fail with the body.
                string otherBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Claude API request failed with status {code}: {otherBody}");
            }
        }

        throw new HttpRequestException(
            "Claude is busy or unavailable — please try again in a moment.",
            lastTransient);
    }

    internal static string ParseResponse(string responseJson)
    {
        var result = JsonConvert.DeserializeObject<ClaudeResponse>(responseJson);
        if (result?.Content == null || result.Content.Length == 0)
        {
            throw new InvalidOperationException("Claude API returned no content in response.");
        }

        // With web_search grounding the model emits "process narration" text blocks BETWEEN the
        // search tool calls ("Let me search for NOTAMs…", "Now I'll compose the briefing.") — the
        // real answer is the text AFTER the last tool block (server_tool_use / web_search_tool_result).
        // Find the last non-text (tool) block and keep only text after it; a plain vision/text
        // response has no tool blocks (lastToolIndex stays -1), so every text block is kept.
        int lastToolIndex = -1;
        for (int i = 0; i < result.Content.Length; i++)
        {
            if (!string.Equals(result.Content[i].Type, "text", StringComparison.Ordinal))
                lastToolIndex = i;
        }

        string combined = string.Concat(result.Content
            .Skip(lastToolIndex + 1)
            .Where(b => b.Type == "text" && !string.IsNullOrEmpty(b.Text))
            .Select(b => b.Text));

        // Surface truncation to the (blind) user — they cannot visually notice a briefing that
        // just stops. "max_tokens" = output cap hit; "pause_turn" = a web-search turn was paused
        // server-side before the answer was composed. Any other stop_reason (end_turn,
        // stop_sequence, or absent) returns the text unchanged.
        bool incomplete = result.StopReason is "max_tokens" or "pause_turn";
        if (string.IsNullOrWhiteSpace(combined))
        {
            return incomplete
                ? "Claude stopped before completing a response. Please try again."
                : "No description available.";
        }
        return incomplete
            ? combined + "\n\n(Response may be incomplete — Claude stopped before finishing.)"
            : combined;
    }

    /// <summary>
    /// Target size for a screenshot sent to the vision API. Anthropic caps each image at 5 MB
    /// and current models accept at most <see cref="MAX_IMAGE_LONG_EDGE"/> px on the long edge
    /// (larger images are downscaled server-side anyway), so shrinking client-side loses no
    /// fidelity while keeping 4K screenshots under the byte cap. 1080p/1440p pass unchanged.
    /// </summary>
    internal static (int Width, int Height) ComputeScaledSize(int width, int height)
    {
        int longEdge = Math.Max(width, height);
        if (longEdge <= MAX_IMAGE_LONG_EDGE)
        {
            return (width, height);
        }
        double scale = (double)MAX_IMAGE_LONG_EDGE / longEdge;
        return (
            Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)));
    }

    /// <summary>
    /// Re-encodes a screenshot at the size <see cref="ComputeScaledSize"/> chose (no-op for
    /// 1080p/1440p). Any decode/encode failure falls back to the original bytes — a resize
    /// problem must never kill a display read; the API surfaces its own limit errors.
    /// </summary>
    private static byte[] DownscaleImageIfNeeded(byte[] imageBytes)
    {
        try
        {
            using var input = new MemoryStream(imageBytes);
            using var source = new Bitmap(input);
            var (targetWidth, targetHeight) = ComputeScaledSize(source.Width, source.Height);
            if (targetWidth == source.Width && targetHeight == source.Height)
            {
                return imageBytes;
            }
            using var scaled = new Bitmap(targetWidth, targetHeight);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, targetWidth, targetHeight);
            }
            using var output = new MemoryStream();
            scaled.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
        catch
        {
            return imageBytes;
        }
    }

    internal static int GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return Math.Min((int)delta.TotalSeconds, 30);
        }
        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            int seconds = (int)(date - DateTimeOffset.UtcNow).TotalSeconds;
            return Math.Clamp(seconds, 1, 30);
        }
        return (int)Math.Pow(2, attempt + 1); // 2s, 4s, 8s
    }

    /// <summary>
    /// Lists the account's Claude models for the settings dropdown. The Models API returns
    /// newest-first; we keep that order and filter to <c>claude-*</c> ids. Throws on HTTP/network
    /// failure (the dialog falls back to a curated list).
    /// </summary>
    public async Task<IReadOnlyList<AiModelInfo>> ListAvailableModelsAsync()
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Claude API key is not configured.");
        }

        var models = new List<AiModelInfo>();
        string? afterId = null;
        do
        {
            string url = MODELS_URL + "?limit=100"; // Anthropic list endpoints cap limit at 100
            if (!string.IsNullOrEmpty(afterId))
            {
                url += "&after_id=" + Uri.EscapeDataString(afterId);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", ANTHROPIC_VERSION);

            using var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            var page = JsonConvert.DeserializeObject<ModelListResponse>(json);

            if (page?.Data != null)
            {
                foreach (var m in page.Data)
                {
                    if (string.IsNullOrEmpty(m.Id)) continue;
                    if (!m.Id.StartsWith("claude", StringComparison.OrdinalIgnoreCase)) continue;
                    string display = string.IsNullOrWhiteSpace(m.DisplayName) ? m.Id : m.DisplayName!;
                    models.Add(new AiModelInfo(m.Id, display));
                }
            }
            afterId = (page != null && page.HasMore) ? page.LastId : null;
        } while (!string.IsNullOrEmpty(afterId));

        return models;
    }

    #region Response Models

    private class ModelListResponse
    {
        [JsonProperty("data")] public ModelEntry[]? Data { get; set; }
        [JsonProperty("has_more")] public bool HasMore { get; set; }
        [JsonProperty("last_id")] public string? LastId { get; set; }
    }

    private class ModelEntry
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("display_name")] public string? DisplayName { get; set; }
    }

    private class ClaudeResponse
    {
        [JsonProperty("content")] public ContentBlock[]? Content { get; set; }
        [JsonProperty("stop_reason")] public string? StopReason { get; set; }
    }

    private class ContentBlock
    {
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("text")] public string? Text { get; set; }
    }

    #endregion
}

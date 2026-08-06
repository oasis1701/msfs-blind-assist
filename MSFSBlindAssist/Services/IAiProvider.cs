namespace MSFSBlindAssist.Services;

/// <summary>
/// Cloud AI backend for MSFSBA's three AI features: cockpit-display reading (vision),
/// scene description (vision), and route briefing (text, optionally web-grounded for NOTAMs).
/// Implemented by <see cref="GeminiService"/> and <see cref="ClaudeService"/>; the active one is
/// chosen by <see cref="AiProviderFactory.Create"/> from <c>UserSettings.AiProvider</c>. Both
/// implementations are interchangeable behind this interface, so the three call sites
/// (MainForm scene, BaseAircraftDefinition display, ElectronicFlightBagForm route) never branch
/// on provider.
///
/// The display-type enum is shared (it lives on <see cref="GeminiService"/> to avoid churning the
/// five aircraft definitions that already reference <c>GeminiService.DisplayType</c>).
/// </summary>
public interface IAiProvider
{
    /// <summary>Read a cockpit display from a PNG screenshot into screen-reader-friendly text.</summary>
    Task<string> AnalyzeDisplayAsync(byte[] imageBytes, GeminiService.DisplayType displayType);

    /// <summary>Describe the outside-the-window scene from a PNG screenshot.</summary>
    Task<string> AnalyzeSceneAsync(byte[] imageBytes);

    /// <summary>Generate a narrative route briefing from extracted flight-plan text.</summary>
    Task<string> DescribeRouteAsync(string flightData);

    /// <summary>Fetch the account's available models for the settings dropdown (newest-first).</summary>
    Task<IReadOnlyList<AiModelInfo>> ListAvailableModelsAsync();
}

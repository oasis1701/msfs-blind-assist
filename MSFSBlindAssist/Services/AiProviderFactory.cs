using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Returns the AI provider the user selected in AI Settings. Every AI call site builds its
/// provider through this factory, so switching <c>UserSettings.AiProvider</c> routes ALL three
/// features (display reading, scene description, route briefing) through the chosen backend.
/// Hard switch by design: there is no silent cross-provider fallback.
/// </summary>
public static class AiProviderFactory
{
    public static IAiProvider Create()
    {
        return SettingsManager.Current.AiProvider == AiProvider.Claude
            ? new ClaudeService()
            : new GeminiService();
    }
}

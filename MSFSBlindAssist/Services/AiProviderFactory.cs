using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Returns the AI provider the user selected in the Settings dialog's AI tab. Every AI call site builds its
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

    /// <summary>Whether the selected provider has an API key — the same test its own request makes
    /// (string.IsNullOrEmpty), asked BEFORE any work is done for it.</summary>
    public static bool HasApiKey() => HasApiKey(SettingsManager.Current.AiProvider,
        SettingsManager.Current.GeminiApiKey, SettingsManager.Current.ClaudeApiKey);

    internal static bool HasApiKey(AiProvider provider, string? geminiKey, string? claudeKey) =>
        !string.IsNullOrEmpty(provider == AiProvider.Claude ? claudeKey : geminiKey);
}

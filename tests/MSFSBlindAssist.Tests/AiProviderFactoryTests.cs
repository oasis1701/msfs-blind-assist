using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class AiProviderFactoryTests
{
    [Theory]
    [InlineData(AiProvider.Gemini, "g-key", null, true)]
    [InlineData(AiProvider.Gemini, "", "c-key", false)]
    [InlineData(AiProvider.Gemini, null, "c-key", false)]
    [InlineData(AiProvider.Claude, null, "c-key", true)]
    [InlineData(AiProvider.Claude, "g-key", "", false)]
    public void The_key_checked_is_the_selected_provider_s(AiProvider provider, string? gemini, string? claude, bool expected)
        => Assert.Equal(expected, AiProviderFactory.HasApiKey(provider, gemini, claude));
}

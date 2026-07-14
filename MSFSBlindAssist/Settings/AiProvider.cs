namespace MSFSBlindAssist.Settings;

/// <summary>
/// Which cloud AI backend powers display reading, scene description, and route briefing.
/// Default is <see cref="Gemini"/> so existing installs behave exactly as before.
/// </summary>
public enum AiProvider
{
    Gemini = 0,
    Claude = 1
}

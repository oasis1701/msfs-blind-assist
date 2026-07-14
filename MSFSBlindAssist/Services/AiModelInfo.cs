namespace MSFSBlindAssist.Services;

/// <summary>
/// A model usable for the AI features (display reading, scene description, route briefing),
/// shared by every <see cref="IAiProvider"/>. Surfaced in the AI settings tab's model dropdown
/// (<see cref="Forms.Settings.AiSettingsPanel"/>). <see cref="ToString"/> is what the screen reader speaks.
/// </summary>
public sealed class AiModelInfo
{
    public string Id { get; }
    public string DisplayName { get; }

    public AiModelInfo(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}

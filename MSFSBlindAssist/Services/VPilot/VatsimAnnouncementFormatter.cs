using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services.VPilot;

/// <summary>Which VATSIM event types the pilot wants spoken. The MASTER switch and the
/// session mute are NOT here — those are lifecycle state owned by
/// <see cref="VatsimAnnouncementService"/>. This type carries only what changes the
/// text.</summary>
public sealed record VatsimAnnouncementOptions
{
    public bool AnnounceConnect { get; init; } = true;
    public bool AnnounceDisconnect { get; init; } = true;
    public bool AnnouncePrivateMessages { get; init; } = true;
    public bool AnnounceRadioMessages { get; init; } = true;
    public bool AnnounceSelcal { get; init; } = true;

    public static VatsimAnnouncementOptions From(UserSettings settings) => new()
    {
        AnnounceConnect = settings.VatsimAnnounceConnect,
        AnnounceDisconnect = settings.VatsimAnnounceDisconnect,
        AnnouncePrivateMessages = settings.VatsimAnnouncePrivateMessages,
        AnnounceRadioMessages = settings.VatsimAnnounceRadioMessages,
        AnnounceSelcal = settings.VatsimAnnounceSelcal,
    };
}

/// <summary>
/// Turns one wire message into the sentence the pilot hears, or <c>null</c> for silence.
/// Pure: no settings file, no pipe, no screen reader — which is the whole point, because
/// this is the part where a wording regression is invisible until someone is flying.
///
/// The wording is carried over verbatim from the standalone vPilot-to-TTS project; a
/// pilot migrating from it must hear the same phrases.
/// </summary>
public static class VatsimAnnouncementFormatter
{
    public static string? Format(string type, string from, string message, VatsimAnnouncementOptions options)
    {
        string sender = (from ?? "").Trim();
        string text = (message ?? "").Trim();

        switch (type)
        {
            case "connected":
                if (!options.AnnounceConnect) return null;
                return sender.Length > 0 ? $"Connected as {sender}" : "Connected to the network";

            case "disconnected":
                if (!options.AnnounceDisconnect) return null;
                return "Disconnected from network";

            case "private_message":
                if (!options.AnnouncePrivateMessages) return null;
                if (sender.Length == 0 && text.Length == 0) return null;
                if (sender.Length == 0) return $"Private message: {text}";
                // A PM whose body is empty is still worth hearing — someone messaged you.
                return text.Length == 0
                    ? $"Private message from {sender}"
                    : $"Private message from {sender}: {text}";

            case "radio_message":
                if (!options.AnnounceRadioMessages) return null;
                // No text means nothing was said. Unlike a PM there is no "someone
                // called you" fact left over, so stay silent.
                if (text.Length == 0) return null;
                return sender.Length == 0 ? text : $"{sender}: {text}";

            case "selcal":
                if (!options.AnnounceSelcal) return null;
                return sender.Length > 0 ? $"SELCAL alert from {sender}" : "SELCAL alert";

            default:
                // An event type this build does not know about. Silence, never raw text.
                return null;
        }
    }
}

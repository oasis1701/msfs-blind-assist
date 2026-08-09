using System.Text;

namespace MSFSBlindAssist.Services.VPilot;

/// <summary>A point-in-time snapshot of the vPilot chain, for the settings status field.
/// Deliberately a snapshot and not a live monitor — the settings dialog is a place you
/// look, not a dashboard that talks.</summary>
public sealed record VatsimStatus(
    bool Enabled,
    string? PluginsFolder,
    bool PluginInstalled,
    bool PluginCurrent,
    bool ClientConnected,
    bool Muted);

/// <summary>
/// Renders <see cref="VatsimStatus"/> as the lines shown in the VATSIM tab's read-only
/// text box. Every state must say something the pilot can act on: this field is the only
/// way to confirm the chain works without connecting to the network and waiting for
/// someone to talk.
/// </summary>
public static class VatsimStatusText
{
    public static string Compose(VatsimStatus status)
    {
        var sb = new StringBuilder();

        if (!status.Enabled)
            sb.AppendLine("VATSIM announcements are turned off.");

        if (status.PluginsFolder == null)
        {
            // vPilot has no portable install mode and always registers its own location,
            // so "not found" means it genuinely is not installed — there is nothing for
            // the pilot to point us at, and no Browse button to point it with.
            sb.AppendLine("vPilot was not found. Install vPilot, then re-open these settings.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"vPilot plugins folder: {status.PluginsFolder}");

        if (!status.PluginInstalled)
        {
            // With the feature off, ApplySettings returns before Install() ever runs
            // (see VatsimAnnouncementService.ApplySettings) — so "press OK" alone is a
            // lie for every new user, whose very first read of this box is with the
            // switch still off. Say what has to happen FIRST.
            sb.AppendLine(status.Enabled
                ? "The plugin is not installed. Press OK to install it."
                : "The plugin is not installed. Turn on the switch above, then press OK to install it.");
        }
        else if (!status.PluginCurrent)
        {
            sb.AppendLine(status.Enabled
                ? "An older plugin is installed. Press OK to update it."
                : "An older plugin is installed. Turn on the switch above, then press OK to update it.");
        }
        else
        {
            sb.AppendLine("The plugin is installed and up to date.");
        }

        // Only meaningful while the feature is on — with it off the pipe server is not
        // listening, so "not connected" would send the pilot to restart vPilot when the
        // real reason for silence is the switch above.
        if (status.Enabled && status.PluginInstalled)
        {
            sb.AppendLine(status.ClientConnected
                ? "vPilot is connected."
                : "vPilot is not connected. Start vPilot, or restart vPilot if you have just installed the plugin.");
        }

        if (status.Muted)
            sb.AppendLine("Announcements are muted for this session. Press ] then Alt+V to unmute.");

        return sb.ToString().TrimEnd();
    }
}

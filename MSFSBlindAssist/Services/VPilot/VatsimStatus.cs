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
            sb.AppendLine("vPilot was not found. Use Browse to select your vPilot folder.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"vPilot plugins folder: {status.PluginsFolder}");

        if (!status.PluginInstalled)
        {
            sb.AppendLine("The plugin is not installed. Press OK to install it.");
        }
        else if (!status.PluginCurrent)
        {
            sb.AppendLine("An older plugin is installed. Press OK to update it.");
        }
        else
        {
            sb.AppendLine("The plugin is installed and up to date.");
        }

        if (status.PluginInstalled)
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

using System.Windows.Forms;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>One settings section, hosted as a tab in SettingsForm. Panels never persist —
/// the dialog owns Save. LoadFrom populates on open; Validate gates OK; ApplyTo writes into
/// the shared UserSettings; OnLeaving stops transient resources (e.g. test tones).</summary>
public interface ISettingsPanel
{
    string TabTitle { get; }
    void LoadFrom(UserSettings settings);
    bool Validate(out string error, out Control? focus);
    void ApplyTo(UserSettings settings);
    void OnLeaving();
}

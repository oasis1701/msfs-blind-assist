// What MainForm does around a definition's ProcessSimVarUpdate for the Ctrl+M mute (the mute half
// of its Step 2.5), and an announcer that honours Suppressed the way the real one does. Shared by the
// tests that pin a definition's mutes through that wrap.

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// An announcer with the real one's gate: <c>Announce</c>, <c>AnnounceQueued</c> and
/// <c>AnnounceWithQueue</c> drop while <see cref="ScreenReaderAnnouncer.Suppressed"/> is set;
/// <c>AnnounceImmediate</c>, kept for hotkey readouts, does not.
/// </summary>
internal sealed class GatedSpeechCapture : ScreenReaderAnnouncer
{
    public GatedSpeechCapture() : base(IntPtr.Zero) { }

    /// <summary>Everything that got through, in order.</summary>
    public List<string> All { get; } = new();

    public override void Announce(string message) { if (!Suppressed) All.Add(message); }
    public override void AnnounceQueued(string message) { if (!Suppressed) All.Add(message); }
    public override void AnnounceWithQueue(string message) { if (!Suppressed) All.Add(message); }
    public override void AnnounceImmediate(string message) => All.Add(message);
}

internal static class MuteWrap
{
    /// <summary>
    /// One delivery as <c>MainForm.OnSimVarUpdated</c> makes it: the announcer is suppressed for the
    /// definition's processing when <see cref="DefAnnounceMuteSets.ShouldWrap"/> says so.
    /// </summary>
    public static bool Deliver(IAircraftDefinition definition, ScreenReaderAnnouncer announcer,
        UserSettings settings, string key, double value)
    {
        bool previous = announcer.Suppressed;
        if (DefAnnounceMuteSets.ShouldWrap(definition, key, settings)) announcer.Suppressed = true;
        try { return definition.ProcessSimVarUpdate(key, value, announcer); }
        finally { announcer.Suppressed = previous; }
    }
}

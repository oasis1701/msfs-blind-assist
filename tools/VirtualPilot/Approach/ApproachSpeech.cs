using MSFSBlindAssist.Accessibility;

namespace VirtualPilot.Approach;

/// <summary>
/// Captures what visual guidance would have spoken, stamped with simulated seconds, and whether
/// it would have interrupted (only AnnounceImmediate does).
/// </summary>
sealed class ApproachSpeech : ScreenReaderAnnouncer
{
    private readonly Func<double> _clockSeconds;

    public ApproachSpeech(Func<double> clockSeconds) : base(IntPtr.Zero) { _clockSeconds = clockSeconds; }

    public List<(double T, string Text, bool Immediate)> Lines { get; } = new();

    public override void Announce(string message) => Lines.Add((_clockSeconds(), message, false));
    public override void AnnounceImmediate(string message) => Lines.Add((_clockSeconds(), message, true));
    public override void AnnounceQueued(string message) => Lines.Add((_clockSeconds(), message, false));
    public override void AnnounceWithQueue(string message) => Lines.Add((_clockSeconds(), message, false));
}

namespace MSFSBlindAssist.Services;

/// <summary>
/// One AI capture at a time, across the whole app.
///
/// Both capture paths move or read the SIMULATOR's camera and then announce, so two of them in
/// flight together would each move the camera, each announce over the other, and the second
/// capture could land mid-switch. The camera is a property of the simulator, not of an aircraft
/// definition or a form, so the gate has to be shared by everything that captures:
/// <c>BaseAircraftDefinition.ReadDisplay</c> (every aircraft's display reads) and
/// <c>MainForm.DescribeSceneAsync</c> (the scene description). The scene path took no gate at
/// all, so pressing it during a display read captured that read's instrument view — or a frame
/// taken mid-switch — and called it "the scene".
///
/// The holder must release it before showing any MODAL dialog. It is released in a finally, but a
/// <c>MessageBox.Show</c> INSIDE the guarded region does not return until the dialog is dismissed,
/// so an error dialog left standing refused every later capture with "already in progress" —
/// untrue, and unactionable for a pilot who has not noticed the dialog.
///
/// An instance rather than a bare static so tests get their own; production shares
/// <see cref="Shared"/>.
/// </summary>
internal sealed class DisplayReadGate
{
    /// <summary>The one gate every production capture path takes.</summary>
    internal static readonly DisplayReadGate Shared = new();

    /// <summary>What a caller says when the gate is already held. One wording for both paths.</summary>
    internal const string BusyMessage = "A display read is already in progress.";

    private int _inFlight;

    /// <summary>True when this call took the gate and must release it; false when someone else holds it.</summary>
    internal bool TryEnter() => Interlocked.CompareExchange(ref _inFlight, 1, 0) == 0;

    /// <summary>Releases the gate. Safe to call when not held.</summary>
    internal void Exit() => Interlocked.Exchange(ref _inFlight, 0);

    /// <summary>For tests and diagnostics; never a substitute for <see cref="TryEnter"/>.</summary>
    internal bool IsHeld => Volatile.Read(ref _inFlight) != 0;
}

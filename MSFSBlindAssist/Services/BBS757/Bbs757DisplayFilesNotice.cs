namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The once-per-module-session latch for <see cref="Line"/> (S14), as the PMDG 737's <c>Pmdg737DisplayFilesNotice</c>:
/// one beside the 757's module session, given to every 757 display reader through its environment. The first reader
/// whose analysis gives no layout file and whose own announcements are not muted says the line; a muted reader leaves it
/// to the next, so with every reader muted nobody does. A new session makes a new notice. Called on the UI thread.
/// </summary>
public sealed class Bbs757DisplayFilesNotice
{
    public const string Line = "BlueBird display files could not be loaded; the display keys fall back to AI reading.";

    private bool _said;

    /// <summary>Says <see cref="Line"/> through <paramref name="speak"/> unless this notice said it already or the asking
    /// reader is <paramref name="muted"/>; a muted reader does not use it up.</summary>
    public void Say(bool muted, Action<string> speak)
    {
        ArgumentNullException.ThrowIfNull(speak);
        if (_said || muted) return;
        _said = true;
        speak(Line);
    }
}

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The once-per-module-session latch for <see cref="Pmdg737DisplaySpeech.FilesNotLoaded"/> (WASM-29). MainForm makes
/// one beside each module session (StartPmdg737Displays) and gives it to the four readers through their environments.
/// The first reader whose analysis gives no layout and whose own announcements are not muted says the line; a muted
/// reader leaves it to the next, so with all four muted nobody does. A flight reload keeps the session and this notice,
/// so a reader that finds no layout again says nothing; a new session (the 737 selected again, the app restarted) makes
/// a new notice. The readers call it on the UI thread.
/// </summary>
public sealed class Pmdg737DisplayFilesNotice
{
    private bool _said;

    /// <summary>Says the line through <paramref name="speak"/> unless it was said in this session or the asking reader's
    /// own Ctrl+M row is <paramref name="muted"/>; a muted reader does not use up the line.</summary>
    public void Say(bool muted, Action<string> speak)
    {
        ArgumentNullException.ThrowIfNull(speak);
        if (_said || muted) return;
        _said = true;
        speak(Pmdg737DisplaySpeech.FilesNotLoaded);
    }
}

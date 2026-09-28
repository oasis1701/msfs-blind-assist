namespace MSFSBlindAssist.Services;

/// <summary>
/// The flight bag's AI route description, kept for the whole app session. The flight bag (Shift+E) is disposed when
/// it closes, and a database switch or rebuild closes it too, so a description held in the window's own text box
/// was lost on every reopen -- an AI call plus up to ~25 s of taxi-route computation, gone. MainForm owns ONE of these
/// for the app's lifetime and hands it to every flight bag it opens.
/// Erased only by <see cref="Clear"/>, which Load SimBrief calls: a new plan makes the old briefing wrong. Memory
/// only -- closing the app forgets it. <see cref="Generation"/> is what lets a briefing still running when Load
/// SimBrief is pressed be discarded rather than stored over the new plan; <see cref="IsGenerating"/> keeps it to one
/// briefing at a time across flight-bag windows, since a briefing now finishes even after its window closed.
/// Used on the UI thread only.
/// </summary>
public sealed class RouteDescriptionSession
{
    /// <summary>The kept description; empty when there is none.</summary>
    public string Text { get; private set; } = "";

    /// <summary>A description is being prepared, by any flight-bag window.</summary>
    public bool IsGenerating { get; private set; }

    /// <summary>Moves on at every <see cref="Clear"/>. A briefing records it when it starts.</summary>
    public int Generation { get; private set; }

    /// <summary>Raised whenever <see cref="Text"/> or <see cref="IsGenerating"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Marks a briefing as running and returns the generation it must be stored under.</summary>
    public int BeginGenerating()
    {
        IsGenerating = true;
        OnChanged();
        return Generation;
    }

    public void EndGenerating()
    {
        IsGenerating = false;
        OnChanged();
    }

    /// <summary>
    /// Keeps <paramref name="text"/> unless Load SimBrief was pressed since the briefing started
    /// (<paramref name="generationAtStart"/> no longer current). Returns whether it was kept.
    /// </summary>
    public bool TryStore(int generationAtStart, string text)
    {
        if (generationAtStart != Generation) return false;
        Text = text;
        OnChanged();
        return true;
    }

    /// <summary>Erases the description and invalidates any briefing still running. Load SimBrief only.</summary>
    public void Clear()
    {
        Text = "";
        Generation++;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

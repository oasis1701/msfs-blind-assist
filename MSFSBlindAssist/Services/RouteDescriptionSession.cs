namespace MSFSBlindAssist.Services;

/// <summary>
/// The flight bag's AI route description, kept for the whole app session. The flight bag (Shift+E) is disposed when
/// it closes, and a database switch or rebuild closes it too, so a description held in the window's own text box
/// was lost on every reopen -- an AI call plus up to ~25 s of taxi-route computation, gone. MainForm owns ONE of these
/// for the app's lifetime and hands it to every flight bag it opens.
/// Erased only by <see cref="Clear"/>, which Load SimBrief calls when it replaces the plan: a new plan makes the old
/// briefing wrong. Memory only -- closing the app forgets it. <see cref="Generation"/> is what lets a briefing still
/// running when Load SimBrief is pressed (or the database is switched, <see cref="Abandon"/>) be discarded rather than
/// stored; <see cref="IsGenerating"/> keeps it to one briefing at a time across flight-bag windows, since a briefing
/// now finishes even after its window closed.
/// Used on the UI thread only.
/// </summary>
public sealed class RouteDescriptionSession
{
    private string _planKey = "";

    /// <summary>The kept description; empty when there is none.</summary>
    public string Text { get; private set; } = "";

    /// <summary>A description is being prepared, by any flight-bag window.</summary>
    public bool IsGenerating { get; private set; }

    /// <summary>Moves on at every <see cref="Clear"/> and <see cref="Abandon"/>. A briefing records it when it starts.</summary>
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

    /// <summary>
    /// Ends the briefing started under <paramref name="generationAtStart"/>. Ignored once the generation has moved on:
    /// <see cref="Clear"/> and <see cref="Abandon"/> already released the flag, and a newer briefing may be running.
    /// </summary>
    public void EndGenerating(int generationAtStart)
    {
        if (generationAtStart != Generation) return;
        IsGenerating = false;
        OnChanged();
    }

    /// <summary>
    /// Keeps <paramref name="text"/> unless Load SimBrief was pressed or the database switched since the briefing
    /// started (<paramref name="generationAtStart"/> no longer current). <paramref name="planKey"/> identifies the plan
    /// it describes (<see cref="IsFor"/>). Returns whether it was kept.
    /// </summary>
    public bool TryStore(int generationAtStart, string text, string planKey = "")
    {
        if (generationAtStart != Generation) return false;
        Text = text;
        _planKey = planKey;
        OnChanged();
        return true;
    }

    /// <summary>Whether the kept description was stored for the plan identified by <paramref name="planKey"/>.</summary>
    public bool IsFor(string planKey) => Text.Length > 0 && planKey.Length > 0 && planKey == _planKey;

    /// <summary>Erases the description and invalidates any briefing still running. Load SimBrief only.</summary>
    public void Clear()
    {
        Text = "";
        _planKey = "";
        Abandon();
    }

    /// <summary>
    /// Invalidates any briefing still running and releases <see cref="IsGenerating"/>, keeping the description.
    /// A database switch or rebuild calls it: a briefing computed against the old database is not stored.
    /// </summary>
    public void Abandon()
    {
        Generation++;
        IsGenerating = false;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

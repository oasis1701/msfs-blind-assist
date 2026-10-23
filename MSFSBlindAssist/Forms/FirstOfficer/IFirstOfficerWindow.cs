namespace MSFSBlindAssist.Forms.FirstOfficer;

/// <summary>
/// Non-generic view of <see cref="FirstOfficerForm{TExec,TState}"/>, so MainForm can hold the
/// current aircraft's First Officer window without knowing its closed generic type. MainForm
/// holds the ONE window <see cref="FirstOfficerWindows.Create"/> built (FO-24); cross-form
/// operations belong here.
/// </summary>
public interface IFirstOfficerWindow : IDisposable
{
    /// <summary>Re-reads the FO automation toggles from UserSettings (called after the
    /// Settings dialog's First Officer panel saves).</summary>
    void ApplySettings();

    /// <summary>Re-wires data-manager references after a SimConnect connect/reconnect.</summary>
    void OnSimConnectChanged();

    /// <summary>A flight was loaded in the simulator: the last flight's SimBrief plan and
    /// phase latches are forgotten, so the next flight never runs on them.</summary>
    void OnFlightLoaded();

    /// <summary>Shows the window (or brings it to the front) with its tab and selection as they were.</summary>
    void ShowForm();

    /// <summary>True once the window has been disposed (an aircraft swap disposes it).</summary>
    bool IsDisposed { get; }
}

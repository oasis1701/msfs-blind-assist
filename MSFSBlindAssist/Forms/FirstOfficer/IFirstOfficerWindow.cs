namespace MSFSBlindAssist.Forms.FirstOfficer;

/// <summary>
/// Non-generic view of <see cref="FirstOfficerForm{TExec,TState}"/> for MainForm code that
/// walks every open First Officer window without knowing the closed generic types
/// (each aircraft's FO form field is a distinct FirstOfficerForm&lt;TExec,TState&gt;).
/// Cross-form operations belong here, and MainForm.OpenFirstOfficerForms() is the ONE
/// place that enumerates the per-aircraft form fields — adding a new FO aircraft means
/// extending that enumerator, not every call site.
/// </summary>
public interface IFirstOfficerWindow
{
    /// <summary>Re-reads the FO automation toggles from UserSettings (called after the
    /// Settings dialog's First Officer panel saves).</summary>
    void ApplySettings();

    /// <summary>Re-wires data-manager references after a SimConnect connect/reconnect.</summary>
    void OnSimConnectChanged();
}

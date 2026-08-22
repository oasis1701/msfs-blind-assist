namespace MSFSBlindAssist.Forms;

/// <summary>Shared plumbing for the per-aircraft monitor-manager forms. The list itself is
/// built and populated by <see cref="MonitorManagerFormBase"/>; what remains here is the
/// close gate, which is not the base form's concern to define.</summary>
internal static class MonitorManagerShared
{
    /// <summary>Hide-on-close wiring that still lets the app EXIT: Application.Exit
    /// raises FormClosing on every OpenForms member (hidden forms included) and
    /// ABORTS the whole exit if any form cancels — an unconditional cancel left
    /// the auto-updater stalled against a still-running exe (PR #163 review).
    /// Real app/OS shutdown passes through; every other reason hides.</summary>
    public static void HideOnClose(Form form, Action? onHide = null)
    {
        form.FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.ApplicationExitCall
                or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
            {
                return;
            }
            e.Cancel = true;
            form.Hide();
            onHide?.Invoke();
        };
    }
}

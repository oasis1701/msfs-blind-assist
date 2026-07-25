namespace MSFSBlindAssist.Forms;

/// <summary>Shared plumbing for the per-aircraft monitor-manager forms (Fenix,
/// A32NX, A380, HS787, iFly; the PMDG form keeps its own filtered populate but
/// shares the close gate).</summary>
internal static class MonitorManagerShared
{
    /// <summary>Repopulates a monitor-manager CheckedListBox from the CURRENT
    /// persisted disabled set. The guard flag suppresses the ItemCheck handler
    /// while SetItemChecked runs — without it every populate fires a (no-op but
    /// file-writing) Save per row.</summary>
    public static void Populate(CheckedListBox listBox, IReadOnlyList<string> labels,
        IReadOnlyList<string> keys, ICollection<string> disabledVars, ref bool populating)
    {
        populating = true;
        try
        {
            listBox.BeginUpdate();
            listBox.Items.Clear();
            for (int i = 0; i < labels.Count; i++)
            {
                listBox.Items.Add(labels[i]);
                listBox.SetItemChecked(i, !disabledVars.Contains(keys[i])); // checked = announcing
            }
            listBox.EndUpdate();
        }
        finally
        {
            populating = false;
        }
    }

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

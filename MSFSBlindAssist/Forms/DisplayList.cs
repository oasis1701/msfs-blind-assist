using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MSFSBlindAssist.Forms
{
    /// <summary>
    /// Shared in-place updater for read-only, screen-reader-navigable status ListBoxes.
    /// Rewrites ONLY the rows whose text changed, grows/shrinks the tail IN PLACE (never
    /// <see cref="ListBox.ObjectCollection.Clear"/>, which would drop the selection and make NVDA
    /// re-read from the top), and restores the selection by ROW CONTENT — so a row inserted or
    /// removed ABOVE the user's cursor doesn't silently leave them reading a different parameter.
    /// Parallel to <see cref="DisplayText.SetPreserveCaret"/> (the TextBox equivalent); this is the
    /// single home for the list-update pattern hand-copied across the MCDU/CDU/DCDU display forms.
    /// </summary>
    public static class DisplayList
    {
        /// <summary>
        /// Reconcile <paramref name="lb"/>'s items to <paramref name="lines"/> in place. No-ops when
        /// the content is identical (no flicker, no re-read). Safe on a disposed control.
        /// </summary>
        public static void UpdateInPlace(ListBox lb, IReadOnlyList<string> lines)
        {
            if (lb == null || lb.IsDisposed || lines == null) return;
            int n = lines.Count;

            // First populate.
            if (lb.Items.Count == 0)
            {
                if (n == 0) return;
                lb.BeginUpdate();
                try { for (int i = 0; i < n; i++) lb.Items.Add(lines[i]); }
                finally { lb.EndUpdate(); }
                return;
            }

            // Fast path: same count AND identical text → don't touch the list at all.
            if (lb.Items.Count == n)
            {
                bool anyDiff = false;
                for (int i = 0; i < n; i++)
                    if (!string.Equals(lb.Items[i] as string, lines[i], StringComparison.Ordinal)) { anyDiff = true; break; }
                if (!anyDiff) return;
            }

            // Remember the selected row BY CONTENT (not just index) so we can follow it if the row
            // set shifts (e.g. a conditional status row appears/disappears above the cursor).
            int sel = lb.SelectedIndex;
            string? selText = (sel >= 0 && sel < lb.Items.Count) ? lb.Items[sel] as string : null;
            int top = lb.TopIndex;

            lb.BeginUpdate();
            try
            {
                // Grow/shrink the tail in place, then rewrite only the changed rows.
                while (lb.Items.Count > n) lb.Items.RemoveAt(lb.Items.Count - 1);
                while (lb.Items.Count < n) lb.Items.Add("");
                for (int i = 0; i < n; i++)
                    if (!string.Equals(lb.Items[i] as string, lines[i], StringComparison.Ordinal))
                        lb.Items[i] = lines[i];
            }
            finally
            {
                lb.EndUpdate();

                // Restore selection by CONTENT. If the row still at the old index holds the same
                // text, the list didn't shift under the cursor → keep it (no SelectedIndexChanged).
                // Otherwise follow that text to its new index; only when the text is gone entirely
                // (the selected value itself changed AND its row moved) fall back to the old index.
                int newSel = -1;
                if (selText != null)
                {
                    if (sel >= 0 && sel < n && string.Equals(lines[sel], selText, StringComparison.Ordinal))
                        newSel = sel;
                    else
                    {
                        for (int i = 0; i < n; i++)
                            if (string.Equals(lines[i], selText, StringComparison.Ordinal)) { newSel = i; break; }
                        if (newSel < 0 && sel >= 0 && sel < n) newSel = sel;
                    }
                }
                else if (sel >= 0 && sel < n)
                {
                    newSel = sel;
                }

                // SelectedIndex is range-checked, so the assignment can't throw; only set it when it
                // actually moves, so an undisturbed selection fires no SelectedIndexChanged.
                if (newSel >= 0 && newSel < lb.Items.Count && newSel != lb.SelectedIndex)
                    lb.SelectedIndex = newSel;
                if (top >= 0 && top < lb.Items.Count) lb.TopIndex = top;
            }
        }
    }
}

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
    /// Parallel to <see cref="DisplayText.SetPreserveCaret"/> (the TextBox equivalent, now used
    /// only by single-line/blob boxes). This IS the single home for the list-update pattern:
    /// MainForm's status display, every DisplayListBox window (E/WD, OANS, RMP, HS787 display +
    /// EICAS, GSX menu, Weather Radar), the ECL checklist, and all MCDU/CDU/DCDU forms
    /// reconcile through it.
    /// Per-form selection semantics (title/page force-select, positional index restore for CDU
    /// screens, the ECL's FWS cursor-follow) run CALLER-SIDE after this call and override the
    /// content-based restore below — keep it that way.
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

            // ⚠️ THE "Loading..." PLACEHOLDER IS NOT CONTENT, AND IT WAS SURVIVING. Panels
            // showed it as a real row ABOVE their values - Ice and Pitot, Standby
            // Instruments, Radios, Flight Controls and Fuel Failures all read
            // "Loading..." followed by live data in the pilot's dump of every panel. It is
            // added by the panel refresh when the list is empty so the first populate is
            // not silent, and it is only ever meant to exist until the first real row
            // arrives.
            //
            // Treating a list that holds NOTHING BUT the placeholder as EMPTY sends it down
            // the first-populate path, which writes the real rows and leaves nothing of it
            // behind - and it costs nothing on every other list, which never contains that
            // string. Defensive by design: the reconcile below already overwrites index 0
            // and should have removed it, so something about that sequence is not what it
            // looks like; this makes the outcome impossible to get wrong either way.
            if (lb.Items.Count == 1 && string.Equals(lb.Items[0] as string, "Loading...",
                    StringComparison.Ordinal) && n > 0)
            {
                lb.Items.Clear();
            }

            // First populate.
            if (lb.Items.Count == 0)
            {
                if (n == 0) return;
                lb.BeginUpdate();
                try { for (int i = 0; i < n; i++) lb.Items.Add(lines[i]); }
                finally { lb.EndUpdate(); }
                SelectFirstIfNothingSelected(lb);
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
                // Otherwise follow that text to the occurrence NEAREST the old index — status lists
                // legitimately contain duplicate rows (blank separators, repeated "--"), and a
                // first-match scan could teleport the cursor to an unrelated section. When the text
                // is gone entirely, CLAMP to the old index (or the new last row if the list shrank
                // past it) — never drop the selection to -1 and strand the reader at the top; the
                // old TextBox path clamped the caret the same way (DisplayText.SetPreserveCaret).
                int newSel = -1;
                if (selText != null)
                {
                    if (sel >= 0 && sel < n && string.Equals(lines[sel], selText, StringComparison.Ordinal))
                    {
                        newSel = sel;
                    }
                    else
                    {
                        for (int i = 0; i < n; i++)
                            if (string.Equals(lines[i], selText, StringComparison.Ordinal) &&
                                (newSel < 0 || Math.Abs(i - sel) < Math.Abs(newSel - sel)))
                                newSel = i;
                        if (newSel < 0 && sel >= 0) newSel = Math.Min(sel, n - 1);
                    }
                }
                else if (sel >= 0)
                {
                    newSel = Math.Min(sel, n - 1);
                }

                // Range-check newSel first — ListBox.SelectedIndex itself throws on an out-of-range
                // set, so this guard is what makes the assignment below safe. Only set it when it
                // actually moves, so an undisturbed selection fires no SelectedIndexChanged.
                if (newSel >= 0 && newSel < lb.Items.Count && newSel != lb.SelectedIndex)
                    lb.SelectedIndex = newSel;
                if (top >= 0 && top < lb.Items.Count) lb.TopIndex = top;
            }
        }

        /// <summary>
        /// ⚠️ A LIST AT -1 ANNOUNCES ITSELF AS EMPTY EVERY TIME IT REDRAWS.
        ///
        /// The restore above never DROPS a selection it had, but it could never CREATE one:
        /// `selText` is null when nothing was selected, and the `sel >= 0` fallback cannot run
        /// for a -1. The first-populate path returned without selecting anything at all, so a
        /// list started at -1 and stayed there - and every live redraw re-announced
        /// "List, nothing selected, 0 of 2" at a pilot who had touched nothing (live dump,
        /// five times in a row between two frequency read-backs).
        ///
        /// Same rule the Monitor Manager already carries: `Items.Clear()` drops SelectedIndex
        /// to -1 and NOTHING restores it - not adding items, not the control receiving focus -
        /// and at -1 a screen reader announces the list with no current item.
        ///
        /// Only ever fires when there is genuinely no selection, so a pilot's own position is
        /// never moved.
        ///
        /// ⚠️ CALLED ON FIRST POPULATE ONLY, NOT ON EVERY UPDATE. The update path is pinned by
        /// No_selection_before_the_update_means_no_selection_is_introduced, a deliberate
        /// decision that a redraw must not CREATE a selection the pilot never made - and
        /// introducing one there would move focus in a list somebody may have deliberately
        /// tabbed away from. A list that has never been populated has no such position to
        /// respect, and at -1 its first Enter or Space does nothing at all.
        /// </summary>
        private static void SelectFirstIfNothingSelected(ListBox lb)
        {
            if (lb.IsDisposed || lb.Items.Count == 0 || lb.SelectedIndex >= 0) return;
            try { lb.SelectedIndex = 0; } catch { }
        }
    }
}

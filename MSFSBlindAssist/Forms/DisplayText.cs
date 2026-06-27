using System;
using System.Windows.Forms;

namespace MSFSBlindAssist.Forms
{
    /// <summary>
    /// Updates read-only display text boxes (status / ECAM / ISIS / ND / SD / PFD pop-outs and
    /// in-panel boxes) on refresh WITHOUT throwing the screen-reader reading position back to the
    /// top.
    ///
    /// A full <c>TextBox.Text = …</c> reassignment makes NVDA/JAWS reset their review cursor to
    /// line 0 even if we restore the caret afterwards. To avoid that, <see cref="SetPreserveCaret"/>
    /// rewrites ONLY the characters that actually changed (keeping the common prefix + suffix), so
    /// the reader sees a small localized edit, and then puts the caret back on the SAME line the
    /// reader was on. It no-ops entirely when the content is unchanged, so an idle/auto refresh
    /// doesn't disturb the reader at all.
    /// </summary>
    public static class DisplayText
    {
        /// <summary>
        /// Set <paramref name="box"/> to <paramref name="text"/> in place, preserving the reading
        /// position (the line the caret is on). No-op when the content is unchanged.
        /// </summary>
        public static void SetPreserveCaret(TextBox? box, string? text)
        {
            if (box == null) return;
            string newText = text ?? "";
            string old = box.Text;
            if (old == newText) return;            // unchanged -> leave the reader untouched

            // Remember which line the reader is on; line structure is stable across a value-only
            // refresh, so restoring the same line index keeps their position even if chars shifted.
            int caretLine = 0;
            try { caretLine = box.GetLineFromCharIndex(box.SelectionStart); } catch { }

            try
            {
                // Replace only the differing span: keep the common prefix and the common suffix,
                // rewrite the middle. NVDA treats this as a localized edit, not a full replacement.
                int p = 0, minLen = Math.Min(old.Length, newText.Length);
                while (p < minLen && old[p] == newText[p]) p++;
                int s = 0;
                while (s < (minLen - p) && old[old.Length - 1 - s] == newText[newText.Length - 1 - s]) s++;

                int oldMidLen = old.Length - p - s;
                string newMid = newText.Substring(p, newText.Length - p - s);

                bool wasReadOnly = box.ReadOnly;
                if (wasReadOnly) box.ReadOnly = false; // read-only edit controls reject SelectedText
                box.Select(p, oldMidLen);
                box.SelectedText = newMid;
                if (wasReadOnly) box.ReadOnly = true;
            }
            catch
            {
                // Fallback: full replace (still better than leaving stale text on screen).
                try { box.Text = newText; } catch { return; }
            }

            // Put the caret back on the same line the reader was on.
            try
            {
                int lines = box.Lines.Length;
                int line = Math.Max(0, Math.Min(caretLine, lines - 1));
                int start = box.GetFirstCharIndexFromLine(line);
                if (start < 0) start = box.TextLength;
                box.SelectionStart = Math.Max(0, Math.Min(start, box.TextLength));
                box.SelectionLength = 0;
            }
            catch { }
        }
    }
}

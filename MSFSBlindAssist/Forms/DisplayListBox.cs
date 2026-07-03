using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace MSFSBlindAssist.Forms
{
    /// <summary>
    /// Standard read-only, screen-reader-navigable status list: the ListBox setup proven by the
    /// MainForm status display, packaged so every live display window shares one configuration
    /// and one reconcile path (<see cref="DisplayList.UpdateInPlace"/> — only changed rows are
    /// rewritten, so the NVDA cursor never jumps and only a changed row re-announces while
    /// focused). Windows swap their multiline TextBox for this and call
    /// <see cref="SetLines"/>/<see cref="SetText"/> instead of DisplayText.SetPreserveCaret.
    /// </summary>
    public class DisplayListBox : ListBox
    {
        public DisplayListBox()
        {
            SelectionMode = SelectionMode.One;
            IntegralHeight = false;
            HorizontalScrollbar = true;
            TabStop = true;
            Font = new Font("Consolas", 10f);
        }

        /// <summary>
        /// When true, character keys that no KeyPress handler consumed are marked handled so the
        /// native ListBox incremental type-ahead can never move the selection. Set this on
        /// displays whose character keys are INPUT (the RMP routes digits to the radio); leave
        /// false on read-only displays, where first-letter navigation is harmless and matches
        /// the MainForm status list.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool SuppressTypeAhead { get; set; }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e); // caller KeyPress handlers run first (RMP digit routing)
            if (SuppressTypeAhead && !e.Handled)
                e.Handled = true;
        }

        /// <summary>Reconcile the items to <paramref name="lines"/> in place. No-ops when unchanged.</summary>
        public void SetLines(IReadOnlyList<string> lines) => DisplayList.UpdateInPlace(this, lines);

        /// <summary>Split a joined multi-line string into rows and reconcile (same newline split
        /// as MainForm.UpdateDisplayText, so blank separator rows are preserved as items).</summary>
        public void SetText(string joined)
            => SetLines((joined ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
    }
}

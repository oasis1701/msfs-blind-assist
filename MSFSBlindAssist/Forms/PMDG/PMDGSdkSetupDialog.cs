using System.Diagnostics;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.PMDG;

/// <summary>
/// "PMDG 737 SDK setup" (or 777): shown when the pilot picks the aircraft from the Aircraft menu, or
/// starts MSFSBA with it as the selected profile ([PSDK-2]), while an installed variant's options
/// file lacks the <c>[SDK]</c> lines, or none exists yet.
/// MSFSBA never edits that file ([PSDK-1]): the dialog opens on its read-only instructions, which
/// the screen reader reads, puts the lines to paste in their own read-only box (Copy lines puts them
/// on the clipboard), lists each file to fix, and Open folder shows the selected file in Explorer.
/// Shown every time while anything is missing; a dismissal is never remembered.
/// </summary>
internal sealed class PMDGSdkSetupDialog : Form
{
    private readonly PMDGSdkSetupResult _result;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _linesToAdd;
    private TextBox _instructionsTextBox = null!;
    private ListBox? _filesListBox;

    internal PMDGSdkSetupDialog(PMDGSdkSetupResult result, ScreenReaderAnnouncer announcer)
    {
        _result = result;
        _announcer = announcer;
        _linesToAdd = string.Join("\r\n", PMDGOptionsIniFormat.SdkBlock(result.Family.RequiresCenterCdu));
        InitializeComponent();
        Shown += (_, _) => _instructionsTextBox.Focus();
    }

    private void InitializeComponent()
    {
        Text = $"{_result.Family.DisplayName} SDK setup";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        // Read-only TextBoxes, never Labels: focused first, the instructions are what the screen
        // reader reads, and the lines can be reviewed and selected like any text.
        _instructionsTextBox = new TextBox
        {
            Location = new Point(12, 12),
            Size = new Size(596, 180),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Instructions",
            Text = PMDGSdkSetupCheck.Instructions(_result),
            TabIndex = 0,
        };
        Controls.Add(_instructionsTextBox);

        Controls.Add(new TextBox
        {
            Location = new Point(12, 204),
            Size = new Size(596, 96),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Lines to add",
            Text = _linesToAdd,
            TabIndex = 1,
        });

        int buttonsTop = 312;
        if (_result.Problems.Count > 0)
        {
            // Each row reads as "737-800: no [SDK] section" (PMDGSdkFileProblem.ToString).
            _filesListBox = new ListBox
            {
                Location = new Point(12, 312),
                Size = new Size(596, 100),
                AccessibleName = "Files to fix",
                TabIndex = 2,
            };
            foreach (var problem in _result.Problems) _filesListBox.Items.Add(problem);
            _filesListBox.SelectedIndex = 0;
            Controls.Add(_filesListBox);
            buttonsTop = 424;
        }

        var buttonRow = new FlowLayoutPanel
        {
            Location = new Point(12, buttonsTop),
            Size = new Size(596, 40),
            FlowDirection = FlowDirection.LeftToRight,
            TabIndex = 3,
        };
        // Buttons carry no AccessibleName: the screen reader reads their Text.
        var copyButton = new Button { Text = "&Copy lines", AutoSize = true };
        copyButton.Click += OnCopyLinesClicked;
        buttonRow.Controls.Add(copyButton);
        if (_filesListBox != null)
        {
            var openFolderButton = new Button { Text = "&Open folder", AutoSize = true };
            openFolderButton.Click += OnOpenFolderClicked;
            buttonRow.Controls.Add(openFolderButton);
        }
        var closeButton = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonRow.Controls.Add(closeButton);
        Controls.Add(buttonRow);

        // Escape is Close. No AcceptButton: Enter in the instructions must not open Explorer.
        CancelButton = closeButton;
        ClientSize = new Size(620, buttonsTop + 52);
    }

    /// <summary>The screen reader cannot say that the clipboard now holds the lines, so this does.</summary>
    private void OnCopyLinesClicked(object? sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(_linesToAdd);
            _announcer.AnnounceImmediate("Lines copied.");
        }
        catch (Exception ex)
        {
            Log.Warn("PMDG", $"SDK setup: could not copy the lines: {ex.Message}");
            _announcer.AnnounceImmediate("Could not copy the lines. Select them in the Lines to add box and copy them instead.");
        }
    }

    /// <summary>Opens Explorer on the selected file's folder with the file selected, so Enter opens it.</summary>
    private void OnOpenFolderClicked(object? sender, EventArgs e)
    {
        if (_filesListBox?.SelectedItem is not PMDGSdkFileProblem problem) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{problem.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("PMDG", $"SDK setup: could not open the folder of {problem.Path}: {ex.Message}");
            _announcer.AnnounceImmediate($"Could not open the folder: {ex.Message}");
        }
    }
}

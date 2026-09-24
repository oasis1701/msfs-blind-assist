using System.Text.RegularExpressions;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// Synaptic A220 — FMS navigation database update (opened from the A220 EFB window).
///
/// Drives the aircraft's OWN updater: the captain MFW's Maintenance → Data Load →
/// "Load New Databases" page (see <see cref="A220NavdataState"/> for why it is not the
/// EFB's button). Opening the window switches the captain's upper MFW to the DATALOAD
/// format; closing puts back whatever format it showed before.
///
/// Screen-reader rules (CLAUDE.md): button presses and list moves are never
/// announced. Spoken: the Navigraph sign-in requirement appearing, load progress and
/// completion (background results the pilot cannot otherwise perceive), and errors.
/// </summary>
public sealed class A220NavdataForm : Form
{
    private readonly Func<string, Task<string?>> _agent;
    private readonly Func<double> _readSource;
    private readonly Action<int> _writeSource;
    private readonly ScreenReaderAnnouncer _announcer;

    private readonly ListBox _status;
    private readonly ListBox _packages;
    private readonly Button _signIn;
    private readonly Button _start;
    private readonly Button _useNavigraph;
    private readonly Button _useNative;
    private readonly System.Windows.Forms.Timer _poll;

    private A220NavdataState? _state;
    private int _previousFormat = -1;
    private bool _pollBusy;
    private bool _authAnnounced;
    private int _lastProgressBucket = -1;
    private string? _lastComplete;
    private bool _loadStarted;
    private bool _menuClickSent;
    private bool _listRetried;
    private DateTime? _emptySince;

    public A220NavdataForm(Func<string, Task<string?>> agent, Func<double> readSource,
        Action<int> writeSource, ScreenReaderAnnouncer announcer)
    {
        _agent = agent;
        _readSource = readSource;
        _writeSource = writeSource;
        _announcer = announcer;

        Text = "A220 FMS Navigation Database";
        Size = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var statusLabel = new Label { Text = "&Status", Location = new Point(12, 10), AutoSize = true };
        _status = new ListBox
        {
            Location = new Point(12, 30), Size = new Size(680, 110),
            IntegralHeight = false, AccessibleName = "Status"
        };
        var pkgLabel = new Label { Text = "Available &databases (Space or Enter selects for loading)", Location = new Point(12, 150), AutoSize = true };
        _packages = new ListBox
        {
            Location = new Point(12, 170), Size = new Size(680, 150),
            IntegralHeight = false, AccessibleName = "Available databases"
        };
        _packages.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) e.IsInputKey = true; };
        _packages.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) { _ = TogglePackageAsync(); e.Handled = true; e.SuppressKeyPress = true; }
        };

        _signIn = new Button { Text = "Open Navigraph &sign-in page in browser", Location = new Point(12, 335), Size = new Size(330, 32), Enabled = false };
        _signIn.Click += (_, _) => OpenSignIn();
        _start = new Button { Text = "Start &load", Location = new Point(352, 335), Size = new Size(160, 32), Enabled = false };
        _start.Click += (_, _) => _ = StartLoadAsync();
        _useNavigraph = new Button { Text = "Use &Navigraph database", Location = new Point(12, 377), Size = new Size(250, 32) };
        _useNavigraph.Click += (_, _) => _writeSource(1);
        _useNative = new Button { Text = "Use MSFS na&tive database", Location = new Point(272, 377), Size = new Size(250, 32) };
        _useNative.Click += (_, _) => _writeSource(0);
        var close = new Button { Text = "&Close", Location = new Point(12, 470), Size = new Size(120, 32), DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var hint = new Label
        {
            Location = new Point(12, 420), Size = new Size(680, 44),
            Text = "Loads Navigraph data into both FMSs through the aircraft's maintenance Data Load page. " +
                   "Sign in first if asked. A finished load switches the FMS to Navigraph by itself."
        };

        Controls.AddRange(new Control[] { statusLabel, _status, pkgLabel, _packages, _signIn, _start,
            _useNavigraph, _useNative, hint, close });
        CancelButton = close;

        _poll = new System.Windows.Forms.Timer { Interval = 1000 };
        _poll.Tick += async (_, _) => await PollAsync();

        FormClosed += async (_, _) =>
        {
            _poll.Stop();
            // Put the captain's MFW back to what it showed before we took it over.
            if (_previousFormat > 0 && _previousFormat != A220NavdataState.DataloadFormat)
                await _agent($"setCaptainMfwFormat({_previousFormat})");
        };
    }

    public async Task OpenAsync()
    {
        Show();
        Activate();
        _status.Items.Add("Opening the Data Load page…");
        _status.Focus();

        var first = A220NavdataState.Parse(await _agent("dataload()"));
        if (first == null)
        {
            Fail("The A220 displays are not reachable. Make sure the aircraft is loaded and powered, then try again.");
            return;
        }
        _previousFormat = first.Format;
        string? set = await _agent($"setCaptainMfwFormat({A220NavdataState.DataloadFormat})");
        if (set != "OK")
        {
            Fail("Could not switch the captain's display to the Data Load page (" + (set ?? "no answer") + ").");
            return;
        }
        await PollAsync();
        _poll.Start();
    }

    private void Fail(string message)
    {
        _status.Items.Clear();
        _status.Items.Add(message);
        _announcer.AnnounceImmediate(message);
    }

    private async Task PollAsync()
    {
        if (_pollBusy || IsDisposed) return;
        _pollBusy = true;
        try
        {
            var st = A220NavdataState.Parse(await _agent("dataload()"));
            if (st == null || IsDisposed) return;
            _state = st;

            // The format opens on its menu; step into Load New Databases once.
            if (st.Page == "menu" && !_menuClickSent && !st.AuthRequired)
            {
                _menuClickSent = true;
                await _agent("dataloadClick(\"LOAD NEW\")");
            }

            // The page asks Navigraph for its package list once, when it mounts; an
            // open that races the Navigraph session start stays EMPTY (live
            // 2026-09-24 — re-opening listed Navigraph_v2_2609 at once). Re-open the
            // page once after ~8 s of an empty list.
            if (st.Page == "databases" && st.Rows.Count == 0 && !st.AuthRequired && !_listRetried)
            {
                _emptySince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - _emptySince.Value > TimeSpan.FromSeconds(8))
                {
                    _listRetried = true;
                    await _agent($"setCaptainMfwFormat({A220NavdataState.FmsFormat})");
                    await Task.Delay(500);
                    await _agent($"setCaptainMfwFormat({A220NavdataState.DataloadFormat})");
                    await Task.Delay(500);
                    await _agent("dataloadClick(\"LOAD NEW\")");
                }
            }
            Render(st);
            AnnounceTransitions(st);
        }
        catch { /* next tick retries */ }
        finally { _pollBusy = false; }
    }

    private void Render(A220NavdataState st)
    {
        var lines = new List<string>
        {
            "Database in use: " + A220NavdataState.DescribeSource(_readSource())
        };
        if (st.AuthRequired)
            lines.Add(st.AuthCode != null
                ? $"Navigraph sign-in required. Code {SpellCode(st.AuthCode)}. Use the sign-in button, approve, then wait here."
                : "Navigraph sign-in required.");
        else if (st.Page == "databases")
        {
            if (st.Rows.Count == 0)
                lines.Add(_listRetried && _emptySince != null && DateTime.UtcNow - _emptySince.Value > TimeSpan.FromSeconds(20)
                    ? "No Navigraph databases are listed. Check your Navigraph subscription includes FMS data."
                    : "Asking Navigraph for the database list…");
            if (st.Progress is int p && st.Complete == null) lines.Add($"Loading: {p} percent.");
            if (st.Complete == "ok") lines.Add("Load complete.");
            if (st.Complete == "errors") lines.Add("Load complete with errors.");
        }
        else lines.Add("Waiting for the Data Load page…");

        ReplaceItems(_status, lines);
        ReplaceItems(_packages, st.Rows.Select(A220NavdataState.DescribeRow).ToList());
        _signIn.Enabled = st.AuthRequired && st.AuthCode != null;
        _start.Enabled = st.CanStart;
    }

    /// <summary>Update in place so the screen reader's position is not reset each poll.</summary>
    private static void ReplaceItems(ListBox box, IList<string> lines)
    {
        bool same = box.Items.Count == lines.Count;
        for (int i = 0; same && i < lines.Count; i++) same = (string)box.Items[i] == lines[i];
        if (same) return;
        int sel = box.SelectedIndex;
        box.BeginUpdate();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i < box.Items.Count) { if ((string)box.Items[i] != lines[i]) box.Items[i] = lines[i]; }
            else box.Items.Add(lines[i]);
        }
        while (box.Items.Count > lines.Count) box.Items.RemoveAt(box.Items.Count - 1);
        box.EndUpdate();
        if (box.Items.Count > 0) box.SelectedIndex = Math.Clamp(sel < 0 ? 0 : sel, 0, box.Items.Count - 1);
    }

    private void AnnounceTransitions(A220NavdataState st)
    {
        if (st.AuthRequired && !_authAnnounced && st.AuthCode != null)
        {
            _authAnnounced = true;
            _announcer.AnnounceImmediate($"Navigraph sign-in required. Code {SpellCode(st.AuthCode)}. " +
                                         "Press the sign-in button to open the page in your browser.");
        }
        if (!st.AuthRequired && _authAnnounced && st.Page != "none")
        {
            _authAnnounced = false;
            _announcer.AnnounceImmediate("Navigraph sign-in accepted.");
        }
        if (_loadStarted && st.Progress is int p && st.Complete == null)
        {
            int bucket = p / 25;
            if (bucket > _lastProgressBucket && bucket is > 0 and < 4)
            {
                _lastProgressBucket = bucket;
                _announcer.Announce($"Loading, {bucket * 25} percent.");
            }
        }
        if (st.Complete != null && st.Complete != _lastComplete && _loadStarted)
        {
            _lastComplete = st.Complete;
            _ = AnnounceCompletionAsync(st.Complete);
        }
    }

    private async Task AnnounceCompletionAsync(string complete)
    {
        await Task.Delay(1500); // the aircraft writes the source switch as the load ends
        if (IsDisposed) return;
        if (complete == "ok")
            _announcer.AnnounceImmediate("Database load complete. FMS database in use: " +
                                         A220NavdataState.DescribeSource(_readSource()) + ".");
        else
            _announcer.AnnounceImmediate("Database load finished with errors. The FMS kept its previous database.");
    }

    private async Task TogglePackageAsync()
    {
        var st = _state;
        int i = _packages.SelectedIndex;
        if (st == null || i < 0 || i >= st.Rows.Count) return;
        var row = st.Rows[i];
        if (row.Disabled)
        {
            _announcer.AnnounceImmediate(row.Status == "Complete" ? "Already loaded." : "Not selectable right now.");
            return;
        }
        string? r = await _agent($"dataloadClick({A220DisplaysClient.JsString(row.Name)})");
        if (r == null || !r.StartsWith("CLICKED", StringComparison.Ordinal))
            _announcer.AnnounceImmediate("Could not select that database.");
        await Task.Delay(300);
        await PollAsync();
    }

    private async Task StartLoadAsync()
    {
        _loadStarted = true;
        _lastProgressBucket = 0;
        _lastComplete = null;
        string? r = await _agent("dataloadClick(\"Start Load\")");
        if (r == null || !r.StartsWith("CLICKED", StringComparison.Ordinal))
        {
            _loadStarted = false;
            _announcer.AnnounceImmediate("Could not start the load.");
        }
    }

    private void OpenSignIn()
    {
        string? code = _state?.AuthCode;
        if (code == null || !Regex.IsMatch(code, "^[A-Z0-9]{6,10}$")) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                $"https://identity.api.navigraph.com/code/default.aspx?user_code={code}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _announcer.AnnounceImmediate("Could not open the browser: " + ex.Message +
                                         $". Go to navigraph.com/code and enter {SpellCode(code)}.");
        }
    }

    /// <summary>"ASSPWXLZ" → "A S S P W X L Z" so the reader spells it.</summary>
    private static string SpellCode(string code) => string.Join(" ", code.ToCharArray());

    protected override void Dispose(bool disposing)
    {
        if (disposing) _poll.Dispose();
        base.Dispose(disposing);
    }
}

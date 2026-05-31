using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// A380X ECAM System Display (SD) readout window.
///
/// PRIMARY source is now a LIVE SCRAPE of the real SD Coherent view
/// (A380X_SDv2) via <see cref="CoherentDisplayClient"/> + coherent-display-agent.js
/// — it surfaces exactly the DECODED values the crew sees (oxygen PSI, GW/FOB,
/// per-tank fuel, N1/EGT, temps, pressures, door states, …), reconstructed into
/// readable rows. A page selector drives A32NX_ECAM_SD_CURRENT_PAGE_INDEX so the
/// user can read ANY SD page (Engine / APU / Bleed / Cond / Press / Door / Elec /
/// Fuel / Wheel / Hyd / F-CTL / C-B / Video), including the ones the old
/// ARINC429-SimVar decode never covered.
///
/// FALLBACK: if the Coherent debugger / SD view is unreachable, the window falls
/// back to the legacy SimVar + ARINC429 decode (kept below) so it always shows
/// something. See tools/a380-sd-pages.md.
/// </summary>
public class FBWA380SystemDisplayForm : Form
{
    private enum VType { Plain, A429 }

    private sealed record Row(string Var, string Label, VType Type = VType.Plain, string Unit = "", string Format = "0");
    private sealed record Section(string Title, Row[] Rows);

    // SD page index -> name, from fbw-a380x SD/SystemDisplay.tsx PAGES map.
    private static readonly (int Index, string Name)[] SdPages =
    {
        (-1, "Default (current)"), (0, "Engine"), (1, "APU"), (2, "Bleed"), (3, "Air Cond"),
        (4, "Pressurization"), (5, "Doors"), (6, "Electrical AC"), (7, "Electrical DC"),
        (8, "Fuel"), (9, "Wheel"), (10, "Hydraulics"), (11, "Flight Controls"),
        (12, "Circuit Breakers"), (13, "Cruise"), (14, "Status"), (15, "Video"),
    };
    private const string SdPageVar = "A32NX_ECAM_SD_CURRENT_PAGE_INDEX";

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly Dictionary<string, double> _raw = new();
    private readonly CoherentDisplayClient _disp;
    private List<string> _scrapedRows = new();
    private bool _haveScrape;

    private TextBox _text = null!;
    private ComboBox _pageCombo = null!;
    private Label _status = null!;
    private Button _refresh = null!;

    // ---- the legacy SimVar SD layout (FALLBACK only) ----------------------
    private static readonly Section[] Sections = BuildSections();

    private static Section[] BuildSections()
    {
        var fuelTanks = new[] { "FEED_1", "FEED_2", "FEED_3", "FEED_4",
            "LEFT_OUTER", "LEFT_MID", "LEFT_INNER", "RIGHT_OUTER", "RIGHT_MID", "RIGHT_INNER", "TRIM" };
        var fuelRows = new List<Row>();
        foreach (var t in fuelTanks)
            fuelRows.Add(new Row($"A32NX_FQMS_{t}_TANK_QUANTITY", t.Replace("_", " ") + " tank", VType.A429, "kg"));
        fuelRows.Add(new Row("A32NX_FQMS_TOTAL_FUEL_ON_BOARD", "Fuel on board", VType.A429, "kg"));
        fuelRows.Add(new Row("A32NX_FQMS_GROSS_WEIGHT", "Gross weight", VType.A429, "kg"));
        fuelRows.Add(new Row("A32NX_FQMS_CENTER_OF_GRAVITY_MAC", "Center of gravity", VType.A429, "% MAC", "0.0"));
        fuelRows.Add(new Row("A32NX_TOTAL_FUEL_QUANTITY", "Total fuel (sim)", VType.Plain, "kg"));

        var engRows = new List<Row>();
        for (int e = 1; e <= 4; e++)
        {
            engRows.Add(new Row($"A32NX_ENGINE_N2:{e}", $"Engine {e} N2", VType.Plain, "%", "0.0"));
            engRows.Add(new Row($"A32NX_ENGINE_N3:{e}", $"Engine {e} N3", VType.Plain, "%", "0.0"));
            engRows.Add(new Row($"A32NX_ENGINE_FF:{e}", $"Engine {e} fuel flow", VType.Plain, "kg per hour"));
            engRows.Add(new Row($"A32NX_ENGINE_OIL_QTY:{e}", $"Engine {e} oil quantity", VType.Plain, "qt", "0.0"));
        }

        var pressRows = new[]
        {
            new Row("A32NX_PRESS_CABIN_ALTITUDE_B1", "Cabin altitude", VType.A429, "feet"),
            new Row("A32NX_PRESS_CABIN_VS_B1", "Cabin vertical speed", VType.A429, "feet per minute"),
            new Row("A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", "Differential pressure", VType.A429, "psi", "0.0"),
        };

        var apuRows = new[]
        {
            new Row("A32NX_APU_N", "APU N", VType.A429, "%", "0.0"),
            new Row("A32NX_APU_EGT", "APU EGT", VType.A429, "degrees"),
            new Row("A32NX_APU_FUEL_USED", "APU fuel used", VType.A429, "kg"),
        };

        var elecRows = new List<Row>();
        for (int n = 1; n <= 4; n++)
        {
            elecRows.Add(new Row($"A32NX_ELEC_ENG_GEN_{n}_POTENTIAL", $"Generator {n} voltage", VType.Plain, "volts"));
            elecRows.Add(new Row($"A32NX_ELEC_ENG_GEN_{n}_LOAD", $"Generator {n} load", VType.Plain, "%"));
        }
        elecRows.Add(new Row("A32NX_ELEC_APU_GEN_1_POTENTIAL", "APU generator 1 voltage", VType.Plain, "volts"));
        elecRows.Add(new Row("A32NX_ELEC_APU_GEN_2_POTENTIAL", "APU generator 2 voltage", VType.Plain, "volts"));
        foreach (var b in new[] { "1", "2", "3", "4", "ESS", "APU" })
            elecRows.Add(new Row($"A32NX_ELEC_BAT_{b}_POTENTIAL", $"Battery {b} voltage", VType.Plain, "volts", "0.0"));

        var hydRows = new[]
        {
            new Row("A32NX_HYD_GREEN_SYSTEM_1_SECTION_PRESSURE", "Green pressure", VType.Plain, "psi"),
            new Row("A32NX_HYD_GREEN_RESERVOIR_LEVEL", "Green reservoir", VType.Plain, "gallons", "0.0"),
            new Row("A32NX_HYD_YELLOW_SYSTEM_1_SECTION_PRESSURE", "Yellow pressure", VType.Plain, "psi"),
            new Row("A32NX_HYD_YELLOW_RESERVOIR_LEVEL", "Yellow reservoir", VType.Plain, "gallons", "0.0"),
        };

        var condRows = new List<Row> { new Row("A32NX_COND_CKPT_TEMP", "Cockpit temp", VType.Plain, "degrees", "0.0") };
        for (int z = 1; z <= 8; z++)
            condRows.Add(new Row($"A32NX_COND_MAIN_DECK_{z}_TEMP", $"Main deck zone {z} temp", VType.Plain, "degrees", "0.0"));
        for (int z = 1; z <= 7; z++)
            condRows.Add(new Row($"A32NX_COND_UPPER_DECK_{z}_TEMP", $"Upper deck zone {z} temp", VType.Plain, "degrees", "0.0"));

        return new[]
        {
            new Section("FUEL", fuelRows.ToArray()),
            new Section("ENGINE", engRows.ToArray()),
            new Section("PRESSURIZATION", pressRows),
            new Section("APU", apuRows),
            new Section("ELECTRICAL", elecRows.ToArray()),
            new Section("HYDRAULICS", hydRows),
            new Section("AIR CONDITIONING", condRows.ToArray()),
        };
    }

    /// <summary>All distinct L:var names the fallback reads (for registration).</summary>
    public static IEnumerable<string> AllVariableNames()
    {
        foreach (var s in Sections)
            foreach (var r in s.Rows)
                yield return r.Var;
    }

    public FBWA380SystemDisplayForm(ScreenReaderAnnouncer announcer, SimConnectManager sim)
    {
        _announcer = announcer;
        _sim = sim;
        _disp = new CoherentDisplayClient("A380X_SDv2");
        _disp.RowsUpdated += OnRowsUpdated;
        BuildUi();
        if (_sim != null) _sim.SimVarUpdated += OnSimVarUpdated;
        _disp.Start();
        Refresh_();
    }

    private void BuildUi()
    {
        Text = "A380 System Display";
        Size = new Size(820, 660);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        var pageLabel = new Label { Text = "SD &page:", Location = new Point(12, 15), Size = new Size(60, 22) };
        _pageCombo = new ComboBox
        {
            Location = new Point(76, 12),
            Size = new Size(200, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "System display page"
        };
        foreach (var p in SdPages) _pageCombo.Items.Add(p.Name);
        _pageCombo.SelectedIndex = 0;
        _pageCombo.SelectionChangeCommitted += (_, _) => OnPageSelected();

        _status = new Label
        {
            Location = new Point(290, 15),
            Size = new Size(510, 22),
            Text = "Connecting to System Display…",
            AccessibleName = "Status"
        };

        _text = new TextBox
        {
            Location = new Point(12, 44),
            Size = new Size(780, 530),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            AccessibleName = "System display readout",
            Text = "Loading…"
        };
        _refresh = new Button { Text = "&Refresh", Location = new Point(12, 582), Size = new Size(90, 30), AccessibleName = "Refresh" };
        _refresh.Click += (_, _) => { Refresh_(); };
        var close = new Button { Text = "&Close", Location = new Point(110, 582), Size = new Size(90, 30), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { pageLabel, _pageCombo, _status, _text, _refresh, close });
        CancelButton = close;
        AcceptButton = _refresh;
        Load += (_, _) => { _text.Focus(); };
    }

    private async void OnPageSelected()
    {
        int idx = _pageCombo.SelectedIndex;
        if (idx < 0 || idx >= SdPages.Length) return;
        int pageIndex = SdPages[idx].Index;
        // Drive the real SD to the chosen page (verified: writing the index switches
        // the rendered page), then re-scrape it. Calculator path = reliable FBW write.
        try { _sim?.ExecuteCalculatorCode($"{pageIndex} (>L:{SdPageVar})"); } catch { }
        await Task.Delay(450);
        var rows = await _disp.ScrapeNowAsync();
        ApplyRows(rows);
        _announcer?.Announce($"{SdPages[idx].Name} page");
    }

    private void OnRowsUpdated(List<string> rows) => ApplyRows(rows);

    private void ApplyRows(List<string> rows)
    {
        if (rows != null && rows.Count > 0)
        {
            _scrapedRows = rows;
            _haveScrape = true;
            _status.Text = "Live from System Display";
            _text.Text = RenderScrape();
        }
    }

    private string RenderScrape()
    {
        var sb = new StringBuilder();
        string page = _pageCombo.SelectedIndex >= 0 ? SdPages[_pageCombo.SelectedIndex].Name : "";
        sb.AppendLine($"A380 SYSTEM DISPLAY — {page}");
        sb.AppendLine(new string('=', 50));
        foreach (var r in _scrapedRows) sb.AppendLine(r);
        return sb.ToString();
    }

    private async void Refresh_()
    {
        // Try a live scrape first.
        var rows = await _disp.ScrapeNowAsync();
        if (rows != null && rows.Count > 0) { ApplyRows(rows); _announcer?.Announce("Refreshed"); return; }

        // Fallback: legacy SimVar + ARINC429 decode (Coherent SD view unreachable).
        if (!_haveScrape)
        {
            _status.Text = "System Display view not reachable — showing SimVar decode";
            foreach (var kvp in _sim.GetCachedVariableSnapshot(AllVariableNames().ToList()))
                _raw[kvp.Key] = kvp.Value;
            foreach (var v in AllVariableNames()) _sim.RequestVariable(v, forceUpdate: true);
            await Task.Delay(500);
            _text.Text = RenderSimVarFallback();
        }
        _announcer?.Announce("Refreshed");
    }

    private string RenderSimVarFallback()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A380 SYSTEM DISPLAY (SimVar fallback)");
        sb.AppendLine(new string('=', 50));
        foreach (var s in Sections)
        {
            sb.AppendLine();
            sb.AppendLine(s.Title);
            foreach (var r in s.Rows)
            {
                _raw.TryGetValue(r.Var, out double raw);
                string val = r.Type == VType.A429
                    ? new Arinc429Word(raw).ToReadout(r.Format, r.Unit)
                    : (string.IsNullOrEmpty(r.Unit)
                        ? raw.ToString(r.Format)
                        : $"{raw.ToString(r.Format)} {r.Unit}");
                sb.AppendLine($"  {r.Label}: {val}");
            }
        }
        return sb.ToString();
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.VarName)) _raw[e.VarName] = e.Value;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { Refresh_(); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_sim != null) _sim.SimVarUpdated -= OnSimVarUpdated;
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
}

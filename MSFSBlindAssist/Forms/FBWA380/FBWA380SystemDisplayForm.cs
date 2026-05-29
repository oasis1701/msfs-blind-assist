using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// On-demand A380X ECAM System Display (SD) readout window. Reads the FQMS /
/// PRESS / APU ARINC429 words (decoded via <see cref="Arinc429Word"/>) plus the
/// plain scalar SD L:vars, grouped by SD page (Fuel, Engine, Bleed, Hyd, Elec,
/// Press, Cond, APU). The generic per-panel display path can't decode ARINC429,
/// so this dedicated window exists. All names carry the A32NX_ prefix on the
/// A380X (legacy). See tools/a380-sd-pages.md.
/// </summary>
public class FBWA380SystemDisplayForm : Form
{
    private enum VType { Plain, A429 }

    private sealed record Row(string Var, string Label, VType Type = VType.Plain, string Unit = "", string Format = "0");
    private sealed record Section(string Title, Row[] Rows);

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly Dictionary<string, double> _raw = new();
    private TextBox _text = null!;
    private Button _refresh = null!;

    // ---- the SD layout ----------------------------------------------------
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
            engRows.Add(new Row($"A32NX_ENGINE_OIL_PRESSURE:{e}", $"Engine {e} oil pressure", VType.Plain, "psi"));
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

    /// <summary>All distinct L:var names this window reads (for registration).</summary>
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
        BuildUi();
        if (_sim != null) _sim.SimVarUpdated += OnSimVarUpdated;
        Refresh_();
    }

    private void BuildUi()
    {
        Text = "A380 System Display";
        Size = new Size(820, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        _text = new TextBox
        {
            Location = new Point(12, 12),
            Size = new Size(780, 540),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            AccessibleName = "System display readout",
            Text = "Loading…"
        };
        _refresh = new Button { Text = "&Refresh", Location = new Point(12, 560), Size = new Size(90, 30), AccessibleName = "Refresh" };
        _refresh.Click += (_, _) => { Refresh_(); _announcer?.Announce("Refreshed"); };
        var close = new Button { Text = "&Close", Location = new Point(110, 560), Size = new Size(90, 30), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _text, _refresh, close });
        CancelButton = close;
        AcceptButton = _refresh;
        Load += (_, _) => { _text.Focus(); };
    }

    private async void Refresh_()
    {
        // Seed from cache, then request fresh, then render.
        foreach (var kvp in _sim.GetCachedVariableSnapshot(AllVariableNames().ToList()))
            _raw[kvp.Key] = kvp.Value;
        foreach (var v in AllVariableNames()) _sim.RequestVariable(v, forceUpdate: true);
        await Task.Delay(500);
        _text.Text = Render();
    }

    private string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A380 SYSTEM DISPLAY");
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
        base.OnFormClosed(e);
    }
}

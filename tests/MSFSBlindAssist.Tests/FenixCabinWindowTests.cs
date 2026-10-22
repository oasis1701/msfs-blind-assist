// The Fenix "Cabin" window (plan 08 Task 7): its entry in the "Fenix windows" list, its shape, and the
// source facts that keep it honest. The data service answers on a pool thread ([FNX-2]), so the form
// may marshal only through its one try/catch SafeBeginInvoke ([A380C-6]); it writes only through the
// bundle builders (FenixCabinWrites, FenixCabinPram), never a raw typed write; and the app-wide call
// that the cabin is ready is FenixCabinReadyMonitor's alone (CORE-7). Those are source scans, because
// the data flow only shows in the live sim. The window itself is built, never shown: a TabControl, a
// ListBox and a TextBox construct headless in this xUnit host (FenixWindowsFormTests), and nothing that
// is built here touches the definition or the data service (both null). The Status page's render runs
// on values put straight into the window's cache, as the poll would apply them.

using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinWindowTests
{
    private const string Description =
        "Flight attendant panel: cabin ready, announcements and boarding music, lights, doors and slides, temperature, water and waste";

    private static string Root => ClaudeContextBudgetTests.RepoRoot();

    private static string AppPath(params string[] parts)
        => Path.Combine(new[] { Root, "MSFSBlindAssist" }.Concat(parts).ToArray());

    // Every FenixCabinForm partial; the main file must be there, so a scan never passes on no files.
    private static string[] CabinFormFiles()
    {
        string[] files = Directory.GetFiles(AppPath("Forms", "Fenix"), "FenixCabinForm*.cs");
        Assert.Contains(files, file => Path.GetFileName(file) == "FenixCabinForm.cs");
        return files;
    }

    private static string CabinFormText()
        => string.Join("\n", CabinFormFiles().Select(File.ReadAllText));

    private static FenixWindowContext Context(SpeechCapture speech) => new(null!, null!, null!, speech, null!);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }

    // ---- Registry -------------------------------------------------------------------------------

    [Fact]
    public void The_cabin_window_is_registered_once()
    {
        var entry = Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "Cabin");
        Assert.Equal(Description, entry.Description);
    }

    // ---- Source scans ---------------------------------------------------------------------------

    [Fact]
    public void Cabin_forms_marshal_only_through_SafeBeginInvoke()
    {
        string text = CabinFormText();

        Assert.Contains("catch (InvalidOperationException)", text);
        // The one call is the one inside SafeBeginInvoke; every other marshal goes through it.
        Assert.Single(Regex.Matches(text, @"(?<!Safe)BeginInvoke\("));
    }

    [Fact]
    public void Cabin_writes_go_through_the_bundle_builders()
    {
        string text = CabinFormText();

        foreach (string raw in new[] { "WriteBoolAsync(", "WriteIntAsync(", "WriteFloatAsync(", "WriteStringAsync(" })
            Assert.False(text.Contains(raw, StringComparison.Ordinal), $"A cabin form calls {raw} itself");

        // The audio state is the only query the window makes; a mutation goes through FenixCabinPram.SendAsync.
        int queries = Regex.Matches(text, @"QueryAsync\(").Count;
        int stateQueries = Regex.Matches(text, @"QueryAsync\(FenixCabinPram\.StateQuery").Count;
        Assert.True(stateQueries > 0, "The window never reads the audio state");
        Assert.Equal(stateQueries, queries);
    }

    [Fact]
    public void Cabin_code_never_names_localhost()
    {
        // The data service listens on IPv4 only ([A320-40]): every cabin read goes through FenixDataService.
        var files = Directory.GetFiles(AppPath("Services"), "FenixCabin*.cs")
            .Concat(Directory.GetFiles(AppPath("Forms", "Fenix"), "FenixCabin*.cs"))
            .ToList();
        Assert.Contains(files, file => Path.GetFileName(file) == "FenixCabinForm.cs");
        Assert.True(files.Count >= 8, "the seven cabin services and the form");

        foreach (string file in files)
            Assert.False(File.ReadAllText(file).Contains("localhost", StringComparison.OrdinalIgnoreCase),
                $"{Path.GetFileName(file)} names localhost");
    }

    [Fact]
    public void Only_the_monitor_speaks_cabin_ready()
    {
        // The exact C# literal, closing quote included: "Cabin ready is not available yet." and the
        // check box's "Cabin &ready" pass. Other aircraft are out of scope (the A380's "Cabin ready
        // signalled" is its own design).
        const string Literal = "\"Cabin ready\"";
        string monitor = AppPath("Services", "FenixCabinReadyMonitor.cs");
        Assert.Contains(Literal, File.ReadAllText(monitor));

        string app = AppPath();
        var files = Directory.GetFiles(app, "Fenix*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(AppPath("Forms", "Fenix"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(AppPath("FirstOfficer", "Fenix"), "*.cs", SearchOption.AllDirectories))
            .Select(Path.GetFullPath)
            .Where(file => !IsBuildOutput(app, file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(file => !string.Equals(file, Path.GetFullPath(monitor), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files.Where(file => File.ReadAllText(file).Contains(Literal, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(app, file))
            .ToList();
        Assert.True(offenders.Count == 0, "Only FenixCabinReadyMonitor may say it: " + string.Join(", ", offenders));
    }

    private static bool IsBuildOutput(string app, string file)
    {
        string first = Path.GetRelativePath(app, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("bin", StringComparison.OrdinalIgnoreCase) || first.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Shape ----------------------------------------------------------------------------------

    [Fact]
    public void The_window_has_a_status_box_five_pages_in_order_and_the_status_controls()
    {
        using var form = new FenixCabinForm(Context(new SpeechCapture()));

        Assert.Equal("Fenix cabin", form.Text);
        Assert.Equal("Fenix cabin", form.AccessibleName);
        Assert.Equal(new System.Drawing.Size(900, 620), form.Size);
        Assert.True(form.KeyPreview);

        // Status text is a read-only TextBox in the tab order, never a Label ([VAT-13]).
        var statusBox = Assert.Single(form.Controls.OfType<TextBox>());
        Assert.Equal("Cabin system status", statusBox.AccessibleName);
        Assert.True(statusBox.ReadOnly);
        Assert.True(statusBox.TabStop);

        var tabs = Assert.Single(form.Controls.OfType<TabControl>());
        Assert.Equal(new[] { "Status", "Audio", "Doors", "Temperature", "Water" },
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray());
        // Ruling R12: a tab page, a check box and a button are named by their text alone.
        Assert.All(tabs.TabPages.Cast<TabPage>(), page => Assert.Null(page.AccessibleName));

        TabPage status = tabs.TabPages[0];
        var list = Assert.Single(Descendants(status).OfType<ListBox>());
        Assert.Equal("Cabin status", list.AccessibleName);

        var cabinReady = Assert.Single(Descendants(status).OfType<CheckBox>());
        Assert.Equal("Cabin &ready", cabinReady.Text);
        Assert.Null(cabinReady.AccessibleName);

        var lights = Assert.Single(Descendants(status).OfType<ComboBox>());
        Assert.Equal("Cabin lights", lights.AccessibleName);
        Assert.Equal(ComboBoxStyle.DropDownList, lights.DropDownStyle);
        Assert.Equal(new object[] { "Bright", "Dim 1", "Dim 2", "Off" }, lights.Items.Cast<object>().ToArray());
    }

    // ---- Status page render ---------------------------------------------------------------------

    private static readonly FenixDataValue True = new(FenixValueKind.Bool, 0, true, null, "true");
    private static readonly FenixDataValue False = new(FenixValueKind.Bool, 0, false, null, "false");

    private static FenixDataValue Text(string text) => new(FenixValueKind.Text, 0, false, text, "\"" + text + "\"");

    private static TextBox StatusBox(Form form) => form.Controls.OfType<TextBox>().Single();
    private static TabPage StatusPage(Form form) => form.Controls.OfType<TabControl>().Single().TabPages[0];
    private static ListBox StatusList(Form form) => Descendants(StatusPage(form)).OfType<ListBox>().Single();
    private static CheckBox CabinReadyBox(Form form) => Descendants(StatusPage(form)).OfType<CheckBox>().Single();
    private static ComboBox LightsCombo(Form form) => Descendants(StatusPage(form)).OfType<ComboBox>().Single();

    [Fact]
    public void Before_any_answer_every_status_line_reads_no_data_and_nothing_is_spoken()
    {
        var speech = new SpeechCapture();
        using var form = new FenixCabinForm(Context(speech));

        form.Render();

        Assert.Equal("The Fenix is not answering. Load the Fenix and wait for it to start.", StatusBox(form).Text);
        string[] expected = FenixCabinLines
            .Build(new FenixCabinData(), null, FenixCabinLayout.Resolve(MSFSBlindAssist.Aircraft.FenixAircraftType.Unknown, null, null, null))
            .Where(line => line.Section == FenixCabinSection.Status)
            .Select(line => line.Text)
            .ToArray();
        Assert.Equal(expected, StatusList(form).Items.Cast<string>().ToArray());
        Assert.Equal("Cabin ready: no data", StatusList(form).Items[0]);
        // The list opens on its first line, so the reader has a current line ([MON-6]'s reasoning).
        Assert.Equal(0, StatusList(form).SelectedIndex);
        Assert.False(CabinReadyBox(form).Checked);
        Assert.Equal(-1, LightsCombo(form).SelectedIndex);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_status_controls_follow_the_Fenix_and_fall_back_when_it_stops_answering()
    {
        var speech = new SpeechCapture();
        using var form = new FenixCabinForm(Context(speech));
        form.Cache.Apply(FenixCabinNames.CabinReady, True);
        form.Cache.Apply(FenixCabinNames.CabinLights, Text("Dim1"));
        form.Cache.Apply(FenixCabinNames.FapOperational, True);
        form.Cache.Apply(FenixCabinNames.FapReady, True);
        form.Cache.Apply(FenixCabinNames.CidsReady, True);

        form.Render();

        Assert.Equal("Cabin system ready.", StatusBox(form).Text);
        Assert.Equal("Cabin ready: yes", StatusList(form).Items[0]);
        Assert.Equal("Cabin lights: Dim 1", StatusList(form).Items[1]);
        Assert.True(CabinReadyBox(form).Checked);
        Assert.Equal("Dim 1", LightsCombo(form).SelectedItem);
        // Set by the render, not the pilot: the check box's handler did not refuse or write.
        Assert.Empty(speech.All);

        // A level the Fenix never sends selects none, never a level ([FNX-1]).
        form.Cache.Apply(FenixCabinNames.CabinLights, Text("Medium"));
        form.Render();
        Assert.Equal(-1, LightsCombo(form).SelectedIndex);

        // A gateway that stops answering: the poll hands on every name as Unknown.
        foreach (string name in FenixCabinNames.All)
            form.Cache.Apply(name, FenixDataValue.Unknown);
        form.Render();

        Assert.Equal("The Fenix is not answering. Load the Fenix and wait for it to start.", StatusBox(form).Text);
        Assert.Equal("Cabin ready: no data", StatusList(form).Items[0]);
        Assert.False(CabinReadyBox(form).Checked);
        Assert.Equal(-1, LightsCombo(form).SelectedIndex);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Cabin_ready_refuses_and_restores_itself_until_the_Fenix_offers_it()
    {
        // The Fenix down first (R13), then the FAP's greyed button unless cabin ready is available (D5);
        // unknown is not available. Every press is the pilot's; nothing is written (the data service is null).
        var speech = new SpeechCapture();
        using var form = new FenixCabinForm(Context(speech));
        form.Render();

        // Nothing answered: the same reason every control gives while the Fenix is down.
        CabinReadyBox(form).Checked = true;

        Assert.False(CabinReadyBox(form).Checked);
        Assert.Equal(new[] { "The Fenix is not answering." }, speech.Interrupts);

        // The Fenix answering, availability not known: not available.
        form.Cache.Apply(FenixCabinNames.FapOperational, True);
        form.Render();
        CabinReadyBox(form).Checked = true;

        Assert.False(CabinReadyBox(form).Checked);
        Assert.Equal(new[] { "The Fenix is not answering.", "Cabin ready is not available yet." }, speech.Interrupts);

        // Known not available, cabin ready on: unchecking is refused too, and the box shows the Fenix's value.
        form.Cache.Apply(FenixCabinNames.CabinReady, True);
        form.Cache.Apply(FenixCabinNames.CabinReadyAvailable, False);
        form.Render();
        Assert.True(CabinReadyBox(form).Checked);

        CabinReadyBox(form).Checked = false;

        Assert.True(CabinReadyBox(form).Checked);
        Assert.Equal(
            new[] { "The Fenix is not answering.", "Cabin ready is not available yet.", "Cabin ready is not available yet." },
            speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);
    }

    [Fact]
    public void Building_and_closing_the_window_without_showing_it_speaks_nothing()
    {
        // The window says nothing on opening (CORE-7), and one never shown tears down without the
        // definition or the data service.
        var speech = new SpeechCapture();
        var form = new FenixCabinForm(Context(speech));
        form.Dispose();

        Assert.Empty(speech.All);
    }

    // ---- Audio state comparison -----------------------------------------------------------------

    private static FenixCabinPramState Pram(string channel = "Boarding Music", int volume = 5, string[]? memo = null)
        => new(
            MusicOn: false,
            Channels: new[] { new FenixCabinItem("c1", channel) },
            SelectedChannel: 0,
            Volume: volume,
            NowAnnouncing: null,
            Memo: (memo ?? Array.Empty<string>()).Select((name, i) => new FenixCabinItem("m" + i, name)).ToArray(),
            PlayingAllInMemo: false,
            Announcements: new[] { new FenixCabinItem("a1", "After Takeoff"), new FenixCabinItem("a2", "Safety Briefing") },
            PreferredVariant: 0,
            MaxVariant: 0);

    [Fact]
    public void Audio_states_compare_by_content_not_by_list_instance()
    {
        // Each read parses fresh lists, and the record compares lists by reference: by that, every 2 s
        // poll would look like a change.
        FenixCabinPramState first = Pram(), second = Pram();
        Assert.NotEqual(first, second);
        Assert.True(FenixCabinForm.SamePram(first, second));

        Assert.True(FenixCabinForm.SamePram(null, null));
        Assert.False(FenixCabinForm.SamePram(first, null));
        Assert.False(FenixCabinForm.SamePram(null, second));

        Assert.False(FenixCabinForm.SamePram(first, Pram(channel: "Jazz")));
        Assert.False(FenixCabinForm.SamePram(first, Pram(volume: 6)));
        Assert.False(FenixCabinForm.SamePram(first, Pram(memo: new[] { "After Takeoff" })));
        Assert.False(FenixCabinForm.SamePram(first, first with { NowAnnouncing = "Safety Briefing" }));
        Assert.False(FenixCabinForm.SamePram(first, first with { MaxVariant = 2 }));
    }
}

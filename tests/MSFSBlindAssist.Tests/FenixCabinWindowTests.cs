// The Fenix "Cabin" window (plan 08 Task 7): its entry in the "Fenix windows" list, its shape, and the
// source facts that keep it honest. The data service answers on a pool thread ([FNX-2]), so the form
// may marshal only through its one try/catch SafeBeginInvoke ([A380C-6]); it writes only through the
// bundle builders (FenixCabinWrites, FenixCabinPram), never a raw typed write; and the app-wide call
// that the cabin is ready is FenixCabinReadyMonitor's alone (CORE-7). Those are source scans, because
// the data flow only shows in the live sim. The window itself is built, never shown: a TabControl, a
// ListBox and a TextBox construct headless in this xUnit host (FenixWindowsFormTests), and nothing that
// is built here touches the definition or the data service (both null). The Status page's render runs
// on values put straight into the window's cache, as the poll would apply them. The Audio page's presses
// run against a real FenixDataService over a fake HttpMessageHandler (never the network), so a test sees
// the exact request each control sends and what the read-back then says.

using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

    private static FenixWindowContext Context(SpeechCapture speech, FenixDataService? data = null)
        => new(null!, data!, null!, speech, null!);

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

    // ---- Audio page -----------------------------------------------------------------------------

    private static TabPage AudioPage(Form form) => form.Controls.OfType<TabControl>().Single().TabPages[1];

    private static T AudioControl<T>(Form form, string accessibleName) where T : Control
        => Descendants(AudioPage(form)).OfType<T>().Single(control => control.AccessibleName == accessibleName);

    private static CheckBox MusicBox(Form form) => Descendants(AudioPage(form)).OfType<CheckBox>().Single();

    private static Button AudioButton(Form form, string text)
        => Descendants(AudioPage(form)).OfType<Button>().Single(button => button.Text == text);

    private static ComboBox ChannelCombo(Form form) => AudioControl<ComboBox>(form, "Music channel");
    private static ComboBox SetCombo(Form form) => AudioControl<ComboBox>(form, "Announcement set");
    private static ListBox AnnouncementsList(Form form) => AudioControl<ListBox>(form, "Prerecorded announcements");
    private static ListBox MemoList(Form form) => AudioControl<ListBox>(form, "Announcement memo");
    private static ListBox AudioStatusList(Form form) => AudioControl<ListBox>(form, "Audio status");

    private static string[] Sorted(IEnumerable<string> texts) => texts.OrderBy(text => text, StringComparer.Ordinal).ToArray();

    // A button press, without showing the window (PerformClick needs a visible button).
    private static void Click(Button button)
        => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, new object[] { EventArgs.Empty });

    // The pilot choosing an entry of a combo's list: the combo's own commit, which a program's own change never raises.
    private static void Commit(ComboBox combo, int index)
    {
        combo.SelectedIndex = index;
        typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(combo, new object[] { EventArgs.Empty });
    }

    // One tick of the window's render timer, which is never started in a test: renders when something
    // changed, then says what the read-backs have to say.
    private static void Tick(FenixCabinForm form)
        => typeof(FenixCabinForm).GetMethod("OnRenderTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, null);

    private static void WaitFor(Func<bool> condition, string what)
        => Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)), "Timed out waiting for " + what);

    private static FenixDataValue Fraction(double value)
        => new(FenixValueKind.Number, value, false, null, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static readonly FenixCabinItem AfterTakeoff = new("a1", "After Takeoff");
    private static readonly FenixCabinItem SafetyBriefing = new("a2", "Safety Briefing");

    // A FAP with two channels, a memo of both announcements, and three announcement sets (0 to 2).
    private static FenixCabinPramState FullPram() => Pram() with
    {
        Channels = new[] { new FenixCabinItem("c1", "Boarding Music"), new FenixCabinItem("c2", "Jazz") },
        Memo = new[] { AfterTakeoff, SafetyBriefing },
        MaxVariant = 2
    };

    // What the gateway answers: a whole audio state, and a success for every audio mutation and write, so
    // one answer serves a press and the read of the state that follows it.
    private static string AudioAnswer(FenixCabinPramState state)
    {
        static JArray Items(IEnumerable<FenixCabinItem> items)
            => new(items.Select(item => new JObject { ["id"] = item.Id, ["name"] = item.Name }));

        var pram = new JObject
        {
            ["state"] = new JObject
            {
                ["bgmOn"] = state.MusicOn,
                ["bgmChannels"] = Items(state.Channels),
                ["bgmSelectedChannel"] = state.SelectedChannel,
                ["volume"] = state.Volume,
                ["onAnnounce"] = state.NowAnnouncing == null ? null : new JObject { ["name"] = state.NowAnnouncing },
                ["announcementsMemo"] = Items(state.Memo),
                ["playingAllInMemo"] = state.PlayingAllInMemo,
                ["prerecordedAnnouncements"] = Items(state.Announcements)
            }
        };
        foreach (string field in new[]
                 {
                     "setBgmOn", "setVolume", "setBgmChannel", "addToMemo", "removeFromMemo", "clearMemo",
                     "directPlay", "playNext", "playAll", "stop"
                 })
            pram[field] = new JObject { ["success"] = true };

        var answer = new JObject
        {
            ["data"] = new JObject
            {
                ["fap"] = new JObject
                {
                    ["pram"] = pram,
                    ["announcementsVariant"] = new JObject
                    {
                        ["preferredVariant"] = state.PreferredVariant,
                        ["maxVariant"] = state.MaxVariant
                    },
                    ["setAnnouncementsVariant"] = new JObject { ["success"] = true }
                },
                ["dataRef"] = new JObject { ["writeFloat"] = true }
            }
        };
        return answer.ToString(Formatting.None);
    }

    private sealed class AudioGateway : HttpMessageHandler
    {
        private readonly List<JObject> _requests = new();

        public Func<string> Answer { get; set; } = () => "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            lock (_requests) _requests.Add(JObject.Parse(body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Answer()) };
        }

        private List<JObject> Where(Func<string, bool> query)
        {
            lock (_requests) return _requests.Where(request => query((string?)request["query"] ?? "")).ToList();
        }

        /// <summary>How many things a press sent: audio mutations and data-name writes (the window's own reads are not counted).</summary>
        public int Sent => Mutations.Count + Writes.Count;

        /// <summary>The FAP audio mutations sent, in order.</summary>
        public List<JObject> Mutations
            => Where(query => query.StartsWith("mutation", StringComparison.Ordinal) && query.Contains("{ fap {"));

        /// <summary>The data-name writes sent, in order.</summary>
        public List<JObject> Writes
            => Where(query => query.StartsWith("mutation", StringComparison.Ordinal) && query.Contains("dataRef"));
    }

    // The window over a fake gateway. Answering (a known value is in its cache) unless told not to.
    private sealed class AudioRig : IDisposable
    {
        public SpeechCapture Speech { get; } = new();
        public AudioGateway Gateway { get; } = new();
        public FenixDataService Data { get; }
        public FenixCabinForm Form { get; }

        // answer: the state the gateway reports once a press is accepted; by default the state the window holds.
        public AudioRig(FenixCabinPramState? pram, bool answering = true, FenixCabinPramState? answer = null)
        {
            Data = new FenixDataService(Gateway);
            Form = new FenixCabinForm(Context(Speech, Data));
            Gateway.Answer = () => AudioAnswer(answer ?? pram ?? Pram());
            if (answering) Form.Cache.Apply(FenixCabinNames.FapOperational, True);
            Form.SetAudioState(pram);
            Form.Render();
        }

        public void Dispose()
        {
            Form.Dispose();
            Data.Dispose();
        }
    }

    [Fact]
    public void The_audio_page_has_its_controls_named_for_the_reader()
    {
        using var form = new FenixCabinForm(Context(new SpeechCapture()));
        TabPage page = AudioPage(form);

        // Lists and combos carry their label as AccessibleName; the other controls are read by their text (R12).
        Assert.Equal(Sorted(new[] { "Audio status", "Prerecorded announcements", "Announcement memo" }),
            Sorted(Descendants(page).OfType<ListBox>().Select(list => list.AccessibleName!)));
        Assert.IsType<DisplayListBox>(AnnouncementsList(form));
        Assert.IsType<DisplayListBox>(MemoList(form));
        Assert.Equal(SelectionMode.One, AnnouncementsList(form).SelectionMode);

        Assert.Equal(Sorted(new[] { "Music channel", "Announcement set" }),
            Sorted(Descendants(page).OfType<ComboBox>().Select(combo => combo.AccessibleName!)));
        Assert.All(Descendants(page).OfType<ComboBox>(), combo => Assert.Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle));

        Assert.Equal("&Boarding music", MusicBox(form).Text);
        Assert.Equal(
            Sorted(new[]
            {
                "Volume &up", "Volume &down", "&Play now", "&Add to memo", "&Remove from memo", "Clear &memo",
                "Play &next", "Play a&ll", "&Stop", "FAP loudspeaker volume up", "FAP loudspeaker volume down"
            }),
            Sorted(Descendants(page).OfType<Button>().Select(button => button.Text)));
        Assert.Equal(Sorted(new[] { "Music &channel", "Announcement s&et" }),
            Sorted(Descendants(page).OfType<Label>().Select(label => label.Text)));

        // R12: no AccessibleName on a check box, a button or a label.
        Control[] named = Descendants(page).Where(control => control is CheckBox or Button or Label).ToArray();
        Assert.All(named, control => Assert.Null(control.AccessibleName));

        // Twelve mnemonics, none twice; the two loudspeaker buttons have none.
        char[] mnemonics = named.Select(control => control.Text).Where(text => text.Contains('&'))
            .Select(text => char.ToLowerInvariant(text[text.IndexOf('&') + 1])).ToArray();
        Assert.Equal(12, mnemonics.Length);
        Assert.Equal(mnemonics.Length, mnemonics.Distinct().Count());
        Assert.DoesNotContain('&', AudioButton(form, "FAP loudspeaker volume up").Text);
        Assert.DoesNotContain('&', AudioButton(form, "FAP loudspeaker volume down").Text);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_audio_controls_follow_the_audio_state(string culture)
        => TestCulture.Run(culture, () =>
        {
            var speech = new SpeechCapture();
            using var form = new FenixCabinForm(Context(speech));
            // The announcement set hides with its label, in one panel. Read it through a holder that is
            // visible itself: a control of a window that was never shown reads as not visible.
            ComboBox setCombo = SetCombo(form);
            Control setPanel = setCombo.Parent!;
            new Panel().Controls.Add(setPanel);

            // No audio state: nothing to pick, and the page says so.
            form.Render();
            Assert.Equal("Audio: no data from the Fenix", AudioStatusList(form).Items[0]);
            Assert.False(MusicBox(form).Checked);
            Assert.Empty(ChannelCombo(form).Items);
            Assert.Equal(-1, ChannelCombo(form).SelectedIndex);
            Assert.Empty(AnnouncementsList(form).Items);
            Assert.Empty(MemoList(form).Items);
            Assert.False(setPanel.Visible);

            // The state the live check expects (Task 8, Step 2): music off, one channel, volume 5.
            form.SetAudioState(Pram());
            form.Render();
            Assert.Equal(new[] { "Boarding music: off", "Music channel: Boarding Music, 1 of 1", "Volume: 5" },
                AudioStatusList(form).Items.Cast<string>().Take(3).ToArray());
            Assert.False(MusicBox(form).Checked);
            Assert.Equal(new object[] { "Boarding Music" }, ChannelCombo(form).Items.Cast<object>().ToArray());
            Assert.Equal(0, ChannelCombo(form).SelectedIndex);
            Assert.Equal(new object[] { "After Takeoff", "Safety Briefing" }, AnnouncementsList(form).Items.Cast<object>().ToArray());
            Assert.Empty(MemoList(form).Items);
            // One set only (MaxVariant 0): the control stays hidden.
            Assert.False(setPanel.Visible);

            // Music on, the second channel selected, a memo, three sets (0, 1, 2) with the second in effect.
            form.SetAudioState(FullPram() with { MusicOn = true, SelectedChannel = 1, PreferredVariant = 1 });
            form.Render();
            Assert.True(MusicBox(form).Checked);
            Assert.Equal(new object[] { "Boarding Music", "Jazz" }, ChannelCombo(form).Items.Cast<object>().ToArray());
            Assert.Equal(1, ChannelCombo(form).SelectedIndex);
            Assert.Equal(new object[] { "After Takeoff", "Safety Briefing" }, MemoList(form).Items.Cast<object>().ToArray());
            Assert.True(setPanel.Visible);
            Assert.Equal(new object[] { "0", "1", "2" }, setCombo.Items.Cast<object>().ToArray());
            Assert.Equal(1, setCombo.SelectedIndex);

            // An index the Fenix reports outside its lists selects none, never a neighbour.
            form.SetAudioState(FullPram() with { SelectedChannel = 5, PreferredVariant = 3 });
            form.Render();
            Assert.Equal(-1, ChannelCombo(form).SelectedIndex);
            Assert.Equal(-1, setCombo.SelectedIndex);

            // A Fenix with a single set again hides the control.
            form.SetAudioState(Pram());
            form.Render();
            Assert.False(setPanel.Visible);

            // Setting the controls is the render's doing, not the pilot's: nothing was said.
            Assert.Empty(speech.All);
            return 0;
        });

    [Fact]
    public void The_audio_lists_keep_the_pilots_selection_and_the_channel_items_change_only_with_the_names()
    {
        using var form = new FenixCabinForm(Context(new SpeechCapture()));
        // Distinct string instances of equal names, as two reads of the FAP parse them.
        static FenixCabinPramState WithNames(int volume, params string[] names)
            => Pram(volume: volume) with
            {
                Channels = names.Select((name, i) => new FenixCabinItem("c" + i, new string(name.ToCharArray()))).ToArray()
            };

        form.SetAudioState(WithNames(5, "Boarding Music"));
        form.Render();
        object first = ChannelCombo(form).Items[0]!;

        // The same names, another volume: the items stay the very same objects (not rebuilt).
        form.SetAudioState(WithNames(6, "Boarding Music"));
        form.Render();
        Assert.Same(first, ChannelCombo(form).Items[0]);
        Assert.Equal("Volume: 6", AudioStatusList(form).Items[2]);

        // New names: rebuilt.
        form.SetAudioState(WithNames(6, "Boarding Music", "Jazz"));
        form.Render();
        Assert.Equal(new object[] { "Boarding Music", "Jazz" }, ChannelCombo(form).Items.Cast<object>().ToArray());

        // The pilot's selection in each list survives a render that changes something else.
        form.SetAudioState(FullPram());
        form.Render();
        AnnouncementsList(form).SelectedIndex = 1;
        MemoList(form).SelectedIndex = 1;
        form.SetAudioState(FullPram() with { Volume = 7 });
        form.Render();
        Assert.Equal(1, AnnouncementsList(form).SelectedIndex);
        Assert.Equal(1, MemoList(form).SelectedIndex);

        // A list nobody selected from stays so: a press then asks for a selection.
        form.SetAudioState(Pram());
        form.Render();
        form.SetAudioState(FullPram());
        form.Render();
        Assert.Equal(-1, MemoList(form).SelectedIndex);
    }

    // Every control of the page, operated the way the pilot operates it. The combos need items to choose from.
    private static (string Name, Action Press)[] AudioPresses(FenixCabinForm form) => new (string, Action)[]
    {
        ("Boarding music", () => MusicBox(form).Checked = true),
        ("Music channel", () => Commit(ChannelCombo(form), 1)),
        ("Volume up", () => Click(AudioButton(form, "Volume &up"))),
        ("Volume down", () => Click(AudioButton(form, "Volume &down"))),
        ("Play now", () => Click(AudioButton(form, "&Play now"))),
        ("Add to memo", () => Click(AudioButton(form, "&Add to memo"))),
        ("Remove from memo", () => Click(AudioButton(form, "&Remove from memo"))),
        ("Clear memo", () => Click(AudioButton(form, "Clear &memo"))),
        ("Play next", () => Click(AudioButton(form, "Play &next"))),
        ("Play all", () => Click(AudioButton(form, "Play a&ll"))),
        ("Stop", () => Click(AudioButton(form, "&Stop"))),
        ("Announcement set", () => Commit(SetCombo(form), 2)),
        ("FAP loudspeaker volume up", () => Click(AudioButton(form, "FAP loudspeaker volume up"))),
        ("FAP loudspeaker volume down", () => Click(AudioButton(form, "FAP loudspeaker volume down")))
    };

    private const string NotAnsweringText = "The Fenix is not answering.";

    [Fact]
    public void Every_audio_press_says_the_Fenix_is_not_answering_first_and_sends_nothing()
    {
        // The Fenix down (R13): the same reason from every control, whatever else is wrong; a control the
        // pilot changed shows the Fenix's own value again. Volume 0 and nothing selected: the reasons that
        // would otherwise be given ("Volume is already 0.", "Select an announcement first.") come after.
        using var rig = new AudioRig(FullPram() with { Volume = 0 }, answering: false);
        var presses = AudioPresses(rig.Form);

        foreach (var press in presses) press.Press();

        Assert.Equal(Enumerable.Repeat(NotAnsweringText, presses.Length).ToArray(), rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(0, rig.Gateway.Sent);
        Assert.False(MusicBox(rig.Form).Checked);
        Assert.Equal(0, ChannelCombo(rig.Form).SelectedIndex);
        Assert.Equal(0, SetCombo(rig.Form).SelectedIndex);
    }

    [Fact]
    public void An_answering_Fenix_without_an_audio_state_refuses_every_audio_press_alike()
    {
        // The page then reads "no data from the Fenix"; a press is never sent on a state it lacks. The
        // loudspeaker volume is its own data name, so it is judged on its own (below). The combos have no
        // items to choose from.
        using var rig = new AudioRig(null);
        var presses = AudioPresses(rig.Form)
            .Where(p => !p.Name.StartsWith("FAP", StringComparison.Ordinal) && p.Name is not ("Music channel" or "Announcement set"))
            .ToArray();

        foreach (var press in presses) press.Press();

        Assert.Equal(Enumerable.Repeat(NotAnsweringText, presses.Length).ToArray(), rig.Speech.Interrupts);
        Assert.Equal(0, rig.Gateway.Sent);
        Assert.False(MusicBox(rig.Form).Checked);
    }

    [Fact]
    public void Audio_presses_with_nothing_to_act_on_say_so_and_send_nothing()
    {
        using var rig = new AudioRig(Pram(volume: 0));

        Click(AudioButton(rig.Form, "Volume &down"));
        Assert.Equal(new[] { "Volume is already 0." }, rig.Speech.Interrupts);

        // No announcement selected, no memo entry selected.
        Click(AudioButton(rig.Form, "&Play now"));
        Click(AudioButton(rig.Form, "&Add to memo"));
        Click(AudioButton(rig.Form, "&Remove from memo"));
        Assert.Equal(
            new[] { "Volume is already 0.", "Select an announcement first.", "Select an announcement first.", "Select a memo entry first." },
            rig.Speech.Interrupts);

        // A malformed entry without an id is never sent: it counts as no selection.
        rig.Form.SetAudioState(Pram(volume: 0) with { Announcements = new[] { new FenixCabinItem("", "No Id"), SafetyBriefing } });
        rig.Form.Render();
        AnnouncementsList(rig.Form).SelectedIndex = 0;
        Click(AudioButton(rig.Form, "&Play now"));
        Click(AudioButton(rig.Form, "&Add to memo"));

        Assert.Equal(6, rig.Speech.Interrupts.Count);
        Assert.Equal(new[] { "Select an announcement first.", "Select an announcement first." }, rig.Speech.Interrupts.TakeLast(2).ToArray());
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(0, rig.Gateway.Sent);
    }

    [Fact]
    public void The_loudspeaker_volume_is_not_stepped_when_unknown_or_at_a_limit()
    {
        // The Fenix answering without a loudspeaker volume: not stepped from the FAP's assumed 100 (R14).
        // The volume is its own data name, so no audio state is needed.
        using var rig = new AudioRig(null);
        Click(AudioButton(rig.Form, "FAP loudspeaker volume down"));
        Click(AudioButton(rig.Form, "FAP loudspeaker volume up"));
        rig.Form.Cache.Apply(FenixCabinNames.FapSpeakerVolume, FenixDataValue.Unknown);
        Click(AudioButton(rig.Form, "FAP loudspeaker volume up"));
        Assert.Equal(Enumerable.Repeat("FAP loudspeaker volume is not known.", 3).ToArray(), rig.Speech.Interrupts);

        // At the top and at the bottom of the page's own scale (a fraction that shows as 100 or 0 percent).
        rig.Form.Cache.Apply(FenixCabinNames.FapSpeakerVolume, Fraction(1.0));
        Click(AudioButton(rig.Form, "FAP loudspeaker volume up"));
        rig.Form.Cache.Apply(FenixCabinNames.FapSpeakerVolume, Fraction(0.98));
        Click(AudioButton(rig.Form, "FAP loudspeaker volume up"));
        rig.Form.Cache.Apply(FenixCabinNames.FapSpeakerVolume, Fraction(0.0));
        Click(AudioButton(rig.Form, "FAP loudspeaker volume down"));
        rig.Form.Cache.Apply(FenixCabinNames.FapSpeakerVolume, Fraction(0.02));
        Click(AudioButton(rig.Form, "FAP loudspeaker volume down"));

        Assert.Equal(
            new[]
            {
                "FAP loudspeaker volume is already at maximum.", "FAP loudspeaker volume is already at maximum.",
                "FAP loudspeaker volume is already at minimum.", "FAP loudspeaker volume is already at minimum."
            },
            rig.Speech.Interrupts.Skip(3).ToArray());
        Assert.Equal(0, rig.Gateway.Sent);

        // The Fenix not answering at all comes first.
        using var down = new AudioRig(null, answering: false);
        Click(AudioButton(down.Form, "FAP loudspeaker volume up"));
        Assert.Equal(new[] { NotAnsweringText }, down.Speech.Interrupts);
        Assert.Equal(0, down.Gateway.Sent);
    }

    // The request's variables as compact JSON, or null when it has none.
    private static string? VariablesOf(JObject request) => request["variables"]?.ToString(Formatting.None);

    private static string? VariablesOf(FenixCabinMutation mutation)
        => mutation.Variables == null ? null : JToken.FromObject(mutation.Variables).ToString(Formatting.None);

    [Fact]
    public void Each_audio_control_sends_its_own_mutation()
    {
        using var rig = new AudioRig(FullPram());
        FenixCabinForm form = rig.Form;

        void Sends(Action press, FenixCabinMutation expected)
        {
            int before = rig.Gateway.Mutations.Count;
            press();
            WaitFor(() => rig.Gateway.Mutations.Count == before + 1, expected.ResultField);
            JObject sent = rig.Gateway.Mutations[before];
            Assert.Equal(expected.Query, (string?)sent["query"]);
            Assert.Equal(VariablesOf(expected), VariablesOf(sent));
        }

        Sends(() => MusicBox(form).Checked = true, FenixCabinPram.SetMusicOn(true));
        Sends(() => MusicBox(form).Checked = false, FenixCabinPram.SetMusicOn(false));
        Sends(() => Commit(ChannelCombo(form), 1), FenixCabinPram.SetChannel(1));
        Sends(() => Click(AudioButton(form, "Volume &up")), FenixCabinPram.SetVolume(6));
        Sends(() => Click(AudioButton(form, "Volume &down")), FenixCabinPram.SetVolume(4));

        AnnouncementsList(form).SelectedIndex = 1;
        Sends(() => Click(AudioButton(form, "&Play now")), FenixCabinPram.DirectPlay("a2"));
        Sends(() => Click(AudioButton(form, "&Add to memo")), FenixCabinPram.AddToMemo("a2"));
        MemoList(form).SelectedIndex = 1;
        Sends(() => Click(AudioButton(form, "&Remove from memo")), FenixCabinPram.RemoveFromMemo(1));
        Sends(() => Click(AudioButton(form, "Clear &memo")), FenixCabinPram.ClearMemo());
        Sends(() => Click(AudioButton(form, "Play &next")), FenixCabinPram.PlayNext());
        Sends(() => Click(AudioButton(form, "Play a&ll")), FenixCabinPram.PlayAll());
        Sends(() => Click(AudioButton(form, "&Stop")), FenixCabinPram.Stop());
        Sends(() => Commit(SetCombo(form), 2), FenixCabinPram.SetAnnouncementsVariant(2));

        // Nothing but those thirteen, and none was refused or spoken.
        Assert.Equal(13, rig.Gateway.Mutations.Count);
        Assert.Empty(rig.Speech.Interrupts);
    }

    private static readonly FenixCabinItem Welcome = new("a3", "Welcome");

    [Fact]
    public void The_memo_list_follows_the_Fenix_while_the_controls_are_held()
    {
        // A check box change holds the controls for a few seconds, so the Fenix's value cannot flip them back.
        // The lists are not controls: a memo that changes meanwhile shows at once, or a Remove would send a
        // position of a memo the pilot no longer sees.
        var before = Pram() with { Memo = new[] { AfterTakeoff, SafetyBriefing, Welcome } };
        var after = Pram() with { Memo = new[] { AfterTakeoff, Welcome } };
        using var rig = new AudioRig(before, answer: after);
        MemoList(rig.Form).SelectedIndex = 1;

        MusicBox(rig.Form).Checked = true;
        WaitFor(() => FenixCabinForm.SamePram(rig.Form.AudioState, after), "the memo read");
        rig.Form.Render();

        Assert.Equal(new object[] { "After Takeoff", "Welcome" }, MemoList(rig.Form).Items.Cast<object>().ToArray());
        // The hold is still on: the check box keeps what the pilot chose, not the Fenix's "off".
        Assert.True(MusicBox(rig.Form).Checked);

        // A Remove now sends the position in the memo the pilot sees ("Welcome", second), not in the old one.
        Assert.Equal(1, MemoList(rig.Form).SelectedIndex);
        int sent = rig.Gateway.Mutations.Count;
        Click(AudioButton(rig.Form, "&Remove from memo"));
        WaitFor(() => rig.Gateway.Mutations.Count == sent + 1, "the remove");
        JObject remove = rig.Gateway.Mutations[sent];
        Assert.Equal(FenixCabinPram.RemoveFromMemo(1).Query, (string?)remove["query"]);
        Assert.Equal(VariablesOf(FenixCabinPram.RemoveFromMemo(1)), VariablesOf(remove));
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void Remove_from_memo_refuses_when_the_memo_changed_behind_the_shown_list()
    {
        var shown = Pram() with { Memo = new[] { AfterTakeoff, SafetyBriefing, Welcome } };
        var changed = Pram() with { Memo = new[] { AfterTakeoff, Welcome } };
        using var rig = new AudioRig(shown);
        FenixCabinForm form = rig.Form;
        MemoList(form).SelectedIndex = 1;
        Tick(form);   // renders, so nothing is waiting for a render

        // The memo changes after the last render, a poll's lag before the next one.
        BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FenixCabinForm).GetField("_pram", instance)!.SetValue(form, changed);
        FieldInfo dirty = typeof(FenixCabinForm).GetField("_dirty", instance)!;
        Assert.False((bool)dirty.GetValue(form)!);

        Click(AudioButton(form, "&Remove from memo"));

        Assert.Equal(new[] { "The memo has changed. Select the entry again." }, rig.Speech.Interrupts);
        Assert.Equal(0, rig.Gateway.Sent);
        // The next render shows the new memo.
        Assert.True((bool)dirty.GetValue(form)!);
        Tick(form);
        Assert.Equal(new object[] { "After Takeoff", "Welcome" }, MemoList(form).Items.Cast<object>().ToArray());

        // Asked again about the memo the pilot now sees, it sends that position.
        MemoList(form).SelectedIndex = 1;
        Click(AudioButton(form, "&Remove from memo"));
        WaitFor(() => rig.Gateway.Mutations.Count == 1, "the remove");
        Assert.Equal(VariablesOf(FenixCabinPram.RemoveFromMemo(1)), VariablesOf(rig.Gateway.Mutations[0]));
        Assert.Single(rig.Speech.Interrupts);
    }

    [Fact]
    public void The_loudspeaker_buttons_write_the_next_five_percent_step_of_what_the_page_shows()
    {
        using var rig = new AudioRig(Pram());
        // 75 percent, as the Fenix reports it.
        rig.Form.Cache.Apply(FenixCabinNames.FapSpeakerVolume, Fraction(0.75));
        rig.Form.Render();

        Click(AudioButton(rig.Form, "FAP loudspeaker volume up"));
        WaitFor(() => rig.Gateway.Writes.Count == 1, "the first write");
        Click(AudioButton(rig.Form, "FAP loudspeaker volume down"));
        WaitFor(() => rig.Gateway.Writes.Count == 2, "the second write");

        foreach ((JObject sent, double value) in rig.Gateway.Writes.Zip(new[] { 0.8, 0.7 }))
        {
            Assert.Contains("writeFloat", (string?)sent["query"]);
            Assert.Equal(FenixCabinNames.FapSpeakerVolume, (string?)sent["variables"]!["name"]);
            Assert.Equal(value, (double)sent["variables"]!["value"]!, 9);
        }
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void Each_audio_press_reads_back_its_own_line_and_the_other_controls_say_nothing()
    {
        // Per press: the state before it, the state the Fenix reports after it, and what is then said (the
        // screen reader says the rest).
        var withMemo = Pram() with { Memo = new[] { AfterTakeoff, SafetyBriefing } };
        var readBacks = new (string Press, FenixCabinPramState Before, FenixCabinPramState After, string Say)[]
        {
            ("Volume &up", Pram(), Pram(volume: 6), "Volume: 6"),
            ("Volume &down", Pram(), Pram(volume: 4), "Volume: 4"),
            ("&Play now", Pram(), Pram() with { NowAnnouncing = "After Takeoff" }, "Now announcing: After Takeoff"),
            ("&Add to memo", Pram(), Pram() with { Memo = new[] { AfterTakeoff } }, "Memo: After Takeoff"),
            ("&Remove from memo", withMemo, Pram() with { Memo = new[] { SafetyBriefing } }, "Memo: Safety Briefing"),
            ("Clear &memo", withMemo, Pram(), "Memo: empty"),
            ("Play &next", Pram(), Pram() with { NowAnnouncing = "After Takeoff" }, "Now announcing: After Takeoff"),
            // Playing the memo also changes "Playing all memo announcements", which a press does not read back.
            ("Play a&ll", Pram(), Pram() with { NowAnnouncing = "After Takeoff", PlayingAllInMemo = true }, "Now announcing: After Takeoff"),
            ("&Stop", Pram() with { NowAnnouncing = "After Takeoff" }, Pram(), "Now announcing: nothing")
        };

        foreach (var c in readBacks)
        {
            using var rig = new AudioRig(c.Before, answer: c.After);
            if (AnnouncementsList(rig.Form).Items.Count > 0) AnnouncementsList(rig.Form).SelectedIndex = 0;
            if (MemoList(rig.Form).Items.Count > 0) MemoList(rig.Form).SelectedIndex = 0;

            Click(AudioButton(rig.Form, c.Press));
            WaitFor(() => FenixCabinForm.SamePram(rig.Form.AudioState, c.After), c.Press);
            rig.Form.Render();
            Tick(rig.Form);

            Assert.True(new[] { c.Say }.SequenceEqual(rig.Speech.All), $"{c.Press} said [{string.Join(" | ", rig.Speech.All)}]");
            Assert.Empty(rig.Speech.Interrupts);
        }

        // The check box, the channel combo and the set combo are read by the reader: no read-back.
        var silent = new (string Control, Action<FenixCabinForm> Change, FenixCabinPramState After)[]
        {
            ("Boarding music", form => MusicBox(form).Checked = true, FullPram() with { MusicOn = true }),
            ("Music channel", form => Commit(ChannelCombo(form), 1), FullPram() with { SelectedChannel = 1 }),
            ("Announcement set", form => Commit(SetCombo(form), 2), FullPram() with { PreferredVariant = 2 })
        };
        foreach (var c in silent)
        {
            using var rig = new AudioRig(FullPram(), answer: c.After);

            c.Change(rig.Form);
            WaitFor(() => FenixCabinForm.SamePram(rig.Form.AudioState, c.After), c.Control);
            rig.Form.Render();
            Tick(rig.Form);

            Assert.Empty(rig.Speech.All);
        }
    }

    [Fact]
    public void A_loudspeaker_step_reads_the_new_percentage_back()
    {
        using var rig = new AudioRig(Pram());
        rig.Form.Cache.Apply(FenixCabinNames.FapSpeakerVolume, Fraction(0.75));
        rig.Form.Render();

        Click(AudioButton(rig.Form, "FAP loudspeaker volume up"));
        // The poll's next read shows the write.
        rig.Form.Cache.Apply(FenixCabinNames.FapSpeakerVolume, Fraction(0.8));
        rig.Form.Render();
        WaitFor(() =>
        {
            Tick(rig.Form);
            return rig.Speech.All.Count > 0;
        }, "the read-back");

        Assert.Equal(new[] { "FAP loudspeaker volume: 80 percent" }, rig.Speech.All);
        Assert.Empty(rig.Speech.Interrupts);
    }
}

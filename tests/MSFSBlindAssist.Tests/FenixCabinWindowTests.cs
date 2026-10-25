// The Fenix "Cabin" window: its entry in the "Fenix windows" list, its shape, and the
// source facts that keep it honest. The data service answers on a pool thread ([FNX-2]), so the form
// may marshal only through its one try/catch SafeBeginInvoke ([A380C-6]); it writes only through the
// bundle builders (FenixCabinWrites, FenixCabinPram), never a raw typed write; and the app-wide call
// that the cabin is ready is FenixCabinReadyMonitor's alone (CORE-7). Those are source scans, because
// the data flow only shows in the live sim. The window itself is built, never shown: a TabControl, a
// ListBox and a TextBox construct headless in this xUnit host (FenixWindowsFormTests). Building it and
// rendering it touch neither the definition nor the data service, so the tests that only build and render
// pass both as null; the Status page's render runs on values put straight into the window's cache, as the
// poll would apply them. The settings read that starts when the Fenix begins to answer belongs to the render
// tick, not to the render, and every test that ticks or presses, or reads the settings, runs against a real
// FenixDataService over a fake HttpMessageHandler (never the network), so it sees the exact request each
// control sends and what the read-back then says.

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
        // The house tab control, so the reader does not say "tab control" on every page change.
        Assert.IsType<MSFSBlindAssist.Controls.AccessibleTabControl>(tabs);
        Assert.Equal(new[] { "Status", "Audio", "Doors", "Temperature", "Water" },
            tabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray());
        // A tab page, a check box and a button are named by their text alone.
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
        // The Fenix down first, then the FAP's greyed button unless cabin ready is available;
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

    [Fact]
    public void The_layout_settings_are_read_by_the_render_tick_when_the_Fenix_starts_answering_never_by_a_render()
    {
        // The window may open before the Fenix has loaded: when it starts answering, the doors and lavatories
        // are read again. That read belongs to the render timer's tick; a render itself touches no data service.
        var gateway = new AudioGateway();
        using var data = new FenixDataService(gateway);
        using var form = new FenixCabinForm(Context(new SpeechCapture(), data));
        form.Cache.Apply(FenixCabinNames.FapOperational, True);

        form.Render();
        Thread.Sleep(300);
        Assert.Equal(0, gateway.Count(FenixDataQueries.SettingsQuery));

        Tick(form);
        WaitFor(() => gateway.Count(FenixDataQueries.SettingsQuery) == 1, "the settings read");

        // Once per start of answering, not on every tick.
        typeof(FenixCabinForm).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
        Tick(form);
        Thread.Sleep(300);
        Assert.Equal(1, gateway.Count(FenixDataQueries.SettingsQuery));
    }

    // The settings query's answer with the emergency-exits setting the FAP lays the doors out from.
    private static string SettingsAnswer(string exits)
        => new JObject
        {
            ["data"] = new JObject
            {
                ["settings"] = new JObject
                {
                    ["softwareVersion"] = "3.0.0.7426",
                    ["state"] = new JObject
                    {
                        ["settings"] = new JArray(new JObject
                        {
                            ["key"] = "emergency_exits_type",
                            ["category"] = "Aircraft",
                            ["value"] = exits
                        })
                    }
                }
            }
        }.ToString(Formatting.None);

    [Fact]
    public void A_variant_change_reads_the_layout_settings_again()
    {
        // Another airframe loaded into the same definition: its settings are read, so the window does not keep
        // the previous airframe's doors. The Unknown type takes the settings' exits.
        var gateway = new AudioGateway { Answer = () => SettingsAnswer("DoubleFullsize") };
        using var data = new FenixDataService(gateway);
        var context = new FenixWindowContext(new MSFSBlindAssist.Aircraft.FenixA320Definition(), data, null!, new SpeechCapture(), null!);
        using var form = new FenixCabinForm(context);
        BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo layout = typeof(FenixCabinForm).GetField("_layout", instance)!;
        MethodInfo loadSettings = typeof(FenixCabinForm).GetMethod("LoadSettingsAsync", instance)!;
        FenixExitsType Exits() => ((FenixCabinLayout)layout.GetValue(form)!).Exits;

        // The previous airframe's settings: every door full size (the Unknown type alone would be overwing).
        Assert.Equal(FenixExitsType.DoubleOverwing, Exits());
        loadSettings.Invoke(form, null);
        WaitFor(() => Exits() == FenixExitsType.DoubleFullsize, "the first airframe's exits");

        // The new airframe has overwing exits.
        gateway.Answer = () => SettingsAnswer("DoubleOverwing");
        typeof(FenixCabinForm).GetMethod("OnVariantChanged", instance)!
            .Invoke(form, new object?[] { null, MSFSBlindAssist.Aircraft.FenixVariant.Unknown });

        WaitFor(() => Exits() == FenixExitsType.DoubleOverwing, "the new airframe's exits");
        Assert.Equal(2, gateway.Count(FenixDataQueries.SettingsQuery));
    }

    [Fact]
    public void Each_combo_asks_for_a_render_when_its_list_closes()
    {
        // A value that changed while the pilot had the list open (the render leaves an open list alone) is shown
        // once it closes: the lights combo as the audio combos.
        using var form = new FenixCabinForm(Context(new SpeechCapture()));
        FieldInfo dirty = typeof(FenixCabinForm).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo closed = typeof(ComboBox).GetMethod("OnDropDownClosed", BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (ComboBox combo in new[] { LightsCombo(form), ChannelCombo(form), SetCombo(form) })
        {
            Tick(form);
            Assert.False((bool)dirty.GetValue(form)!);

            closed.Invoke(combo, new object[] { EventArgs.Empty });

            Assert.True((bool)dirty.GetValue(form)!, combo.AccessibleName);
        }
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

        /// <summary>How many requests carried exactly this query (the window's settings read, say).</summary>
        public int Count(string query) => Where(sent => string.Equals(sent, query, StringComparison.Ordinal)).Count;

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

        // Lists and combos carry their label as AccessibleName; the other controls are read by their text.
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

        // No AccessibleName on a check box, a button or a label.
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

            // The state the live check expects: music off, one channel, volume 5.
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
        // The Fenix down: the same reason from every control, whatever else is wrong; a control the
        // pilot changed shows the Fenix's own value again. Volume 0, nothing selected, an empty memo and
        // nothing playing: the reasons that would otherwise be given ("Volume is already 0.", "Select an
        // announcement first.", "The memo is empty.", "Nothing is playing.") come after.
        using var rig = new AudioRig(FullPram() with { Volume = 0, Memo = Array.Empty<FenixCabinItem>() }, answering: false);
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
        // The page then reads "no data from the Fenix", and so does every press: the Fenix answers, only the
        // audio state is missing. A press is never sent on a state it lacks. The loudspeaker volume is its own
        // data name, so it is judged on its own (below). The combos have no items to choose from.
        using var rig = new AudioRig(null);
        var presses = AudioPresses(rig.Form)
            .Where(p => !p.Name.StartsWith("FAP", StringComparison.Ordinal) && p.Name is not ("Music channel" or "Announcement set"))
            .ToArray();

        foreach (var press in presses) press.Press();

        Assert.Equal(Enumerable.Repeat("No audio data from the Fenix.", presses.Length).ToArray(), rig.Speech.Interrupts);
        Assert.Equal(0, rig.Gateway.Sent);
        Assert.False(MusicBox(rig.Form).Checked);
    }

    [Fact]
    public void An_answering_Fenix_without_an_audio_state_restores_the_combos_with_the_audio_message()
    {
        // A combo the pilot changes while the audio state is missing shows none again; the reason is the
        // missing audio data, not the Fenix being down. The combos are filled from a state, then it is lost.
        using var rig = new AudioRig(FullPram());
        rig.Form.SetAudioState(null);

        Commit(ChannelCombo(rig.Form), 1);
        Commit(SetCombo(rig.Form), 2);

        Assert.Equal(new[] { "No audio data from the Fenix.", "No audio data from the Fenix." }, rig.Speech.Interrupts);
        Assert.Equal(-1, ChannelCombo(rig.Form).SelectedIndex);
        Assert.Equal(-1, SetCombo(rig.Form).SelectedIndex);
        Assert.Equal(0, rig.Gateway.Sent);
    }

    [Fact]
    public void Memo_and_stop_presses_that_would_change_nothing_say_so_and_send_nothing()
    {
        // An empty memo, nothing announcing and the memo not being played (CORE-7's validation messages).
        using var rig = new AudioRig(Pram());

        Click(AudioButton(rig.Form, "Clear &memo"));
        Click(AudioButton(rig.Form, "Play &next"));
        Click(AudioButton(rig.Form, "Play a&ll"));
        Click(AudioButton(rig.Form, "&Stop"));

        Assert.Equal(
            new[] { "The memo is already empty.", "The memo is empty.", "The memo is empty.", "Nothing is playing." },
            rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(0, rig.Gateway.Sent);

        // Something to stop: the memo being played, or one announcement.
        rig.Form.SetAudioState(Pram() with { PlayingAllInMemo = true });
        rig.Form.Render();
        Click(AudioButton(rig.Form, "&Stop"));
        WaitFor(() => rig.Gateway.Mutations.Count == 1, "the stop while the memo plays");
        rig.Form.SetAudioState(Pram() with { NowAnnouncing = "After Takeoff" });
        rig.Form.Render();
        Click(AudioButton(rig.Form, "&Stop"));
        WaitFor(() => rig.Gateway.Mutations.Count == 2, "the stop while an announcement plays");
        Assert.All(rig.Gateway.Mutations, sent => Assert.Equal(FenixCabinPram.Stop().Query, (string?)sent["query"]));
        Assert.Equal(4, rig.Speech.Interrupts.Count);
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
        // The Fenix answering without a loudspeaker volume: not stepped from the FAP's assumed 100.
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
        // An announcement playing, so Stop has something to stop.
        using var rig = new AudioRig(FullPram() with { NowAnnouncing = "After Takeoff" });
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
            ("Play &next", withMemo, withMemo with { NowAnnouncing = "After Takeoff" }, "Now announcing: After Takeoff"),
            // Playing the memo also changes "Playing all memo announcements", which a press does not read back.
            ("Play a&ll", withMemo, withMemo with { NowAnnouncing = "After Takeoff", PlayingAllInMemo = true }, "Now announcing: After Takeoff"),
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

    // ---- Doors, Temperature and Water pages -----------------------------------------------------

    private static TabPage PageAt(Form form, int index) => form.Controls.OfType<TabControl>().Single().TabPages[index];
    private static TabPage DoorsPage(Form form) => PageAt(form, 2);
    private static TabPage TemperaturePage(Form form) => PageAt(form, 3);
    private static TabPage WaterPage(Form form) => PageAt(form, 4);

    private static ListBox PageList(TabPage page) => Descendants(page).OfType<ListBox>().Single();
    private static ListBox DoorsList(Form form) => PageList(DoorsPage(form));
    private static ListBox TemperatureList(Form form) => PageList(TemperaturePage(form));
    private static ListBox WaterList(Form form) => PageList(WaterPage(form));
    private static Button DoorButton(Form form) => Descendants(DoorsPage(form)).OfType<Button>().Single();

    // The Doors button, moved with its row into a holder that is visible itself, so that its Visible reads the
    // button's own flag: a control of a window that was never shown reads as not visible. The window keeps
    // driving it; take it once per form, after which DoorButton no longer finds it on the page.
    private static Button DetachedDoorButton(Form form)
    {
        Button button = DoorButton(form);
        new Panel().Controls.Add(button.Parent!);
        return button;
    }
    private static Button TemperatureButton(Form form, string text)
        => Descendants(TemperaturePage(form)).OfType<Button>().Single(button => button.Text == text);
    private static Button WaterButton(Form form, string text)
        => Descendants(WaterPage(form)).OfType<Button>().Single(button => button.Text == text);

    private const string ForwardWarmer = "&Forward warmer";
    private const string ForwardCooler = "Forward &cooler";
    private const string AftWarmer = "&Aft warmer";
    private const string AftCooler = "Aft c&ooler";
    private const string ResetTemperatures = "&Reset to the cockpit temperatures";
    private const string PreselectUp = "Preselect &up";
    private const string PreselectDown = "Preselect &down";
    private const string NotAcceptedText = "The Fenix did not accept that.";

    private static FenixDataValue Num(double value)
        => new(FenixValueKind.Number, value, false, null, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    // The A320 the window starts from, before any setting says otherwise.
    private static FenixCabinLayout DefaultLayout()
        => FenixCabinLayout.Resolve(MSFSBlindAssist.Aircraft.FenixAircraftType.Unknown, null, null, null);

    // Puts values in the window's cache, as the poll would, and renders.
    private static void Set(FenixCabinForm form, params (string Name, FenixDataValue Value)[] values)
    {
        foreach ((string name, FenixDataValue value) in values) form.Cache.Apply(name, value);
        form.Render();
    }

    private static void Zones(FenixCabinForm form, double forwardSelected, double forwardOffset, double aftSelected, double aftOffset)
        => Set(form,
            (FenixCabinNames.TempFwdAir, Num(24)), (FenixCabinNames.TempFwdSelected, Num(forwardSelected)), (FenixCabinNames.TempFwdOffset, Num(forwardOffset)),
            (FenixCabinNames.TempAftAir, Num(24)), (FenixCabinNames.TempAftSelected, Num(aftSelected)), (FenixCabinNames.TempAftOffset, Num(aftOffset)));

    // Selects the Doors line with this key, from the lines the window shows for the layout.
    private static void SelectDoorLine(FenixCabinForm form, FenixCabinLayout layout, string key)
    {
        int index = FenixCabinLines.Build(form.Cache, null, layout)
            .Where(line => line.Section == FenixCabinSection.Doors)
            .Select(line => line.Key)
            .ToList()
            .IndexOf(key);
        Assert.True(index >= 0, "no Doors line " + key);
        DoorsList(form).SelectedIndex = index;
    }

    // What the gateway answers a data-name write: accepted or refused, for each of the four kinds.
    private static string WriteAnswer(bool accepted)
        => new JObject
        {
            ["data"] = new JObject
            {
                ["dataRef"] = new JObject
                {
                    ["writeInt"] = accepted,
                    ["writeBool"] = accepted,
                    ["writeFloat"] = accepted,
                    ["writeString"] = accepted
                }
            }
        }.ToString(Formatting.None);

    // The window over a fake gateway that accepts every write. Answering unless told not to; an answering
    // Fenix stands on the ground (0 ft above it), where the EFB offers its door and cargo toggles.
    private sealed class ControlsRig : IDisposable
    {
        public SpeechCapture Speech { get; } = new();
        public AudioGateway Gateway { get; } = new();
        public FenixDataService Data { get; }
        public FenixCabinForm Form { get; }

        // layout: the doors the window lists; the A320 by default.
        public ControlsRig(bool answering = true, FenixCabinLayout? layout = null)
        {
            Data = new FenixDataService(Gateway);
            Form = new FenixCabinForm(Context(Speech, Data));
            Gateway.Answer = () => WriteAnswer(accepted: true);
            if (layout != null)
                typeof(FenixCabinForm).GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Form, layout);
            if (answering)
            {
                Form.Cache.Apply(FenixCabinNames.FapOperational, True);
                Form.Cache.Apply(FenixCabinNames.AltitudeAboveGround, Num(0));
            }
            Form.Render();
        }

        public void Dispose()
        {
            Form.Dispose();
            Data.Dispose();
        }
    }

    // Each press that was accepted leaves a read-back expectation in the window: its line, the press-time
    // text and state, how long to wait, whether to say "unchanged" and whether it waits for the line's state
    // rather than its text. Read from the window's own read-back object. A snapshot taken while an
    // accepted press is being recorded on another thread is retried by the caller.
    private static (string Key, string? PressText, string? PressState, TimeSpan Window, bool SpeakIfUnchanged, bool ByState)[] Expectations(FenixCabinForm form)
    {
        BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        object readback = typeof(FenixCabinForm).GetField("_readback", instance)!.GetValue(form)!;
        var pending = (System.Collections.IEnumerable)typeof(FenixCabinReadback).GetField("_pending", instance)!.GetValue(readback)!;
        try
        {
            return pending.Cast<object>()
                .Select(item => (
                    Key: (string)item.GetType().GetProperty("Key")!.GetValue(item)!,
                    PressText: (string?)item.GetType().GetProperty("PressText")!.GetValue(item),
                    PressState: (string?)item.GetType().GetProperty("PressState")!.GetValue(item),
                    Window: (TimeSpan)item.GetType().GetProperty("Window")!.GetValue(item)!,
                    SpeakIfUnchanged: (bool)item.GetType().GetProperty("SpeakIfUnchanged")!.GetValue(item)!,
                    ByState: (bool)item.GetType().GetProperty("ByState")!.GetValue(item)!))
                .ToArray();
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<(string, string?, string?, TimeSpan, bool, bool)>();
        }
    }

    // A write the gateway received, against the write the press should have made: its kind (the mutation's
    // name), the data name and the value.
    private static void AssertSent(JObject sent, FenixCabinWrite expected)
    {
        Assert.Contains($"write{expected.Kind}(", (string?)sent["query"]);
        Assert.Equal(expected.Name, (string?)sent["variables"]!["name"]);
        JToken value = sent["variables"]!["value"]!;
        Assert.True(JToken.DeepEquals(JToken.FromObject(expected.Value), value),
            $"{expected.Name}: sent {value.ToString(Formatting.None)}, expected {expected.Value}");
    }

    private static FenixCabinWrite Float(string name, double value) => new(FenixCabinWriteKind.Float, name, value);

    // The text of the line with this key, as the window renders it for the A320.
    private static string? LineTextOf(FenixCabinForm form, string key)
        => FenixCabinLines.Build(form.Cache, null, DefaultLayout()).FirstOrDefault(line => line.Key == key)?.Text;

    // The state of the line with this key (the part a press changes), as the window builds it for the A320.
    private static string? LineStateOf(FenixCabinForm form, string key)
        => FenixCabinLines.Build(form.Cache, null, DefaultLayout()).FirstOrDefault(line => line.Key == key)?.State;

    [Fact]
    public void The_doors_temperature_and_water_pages_have_their_controls_named_for_the_reader()
    {
        using var form = new FenixCabinForm(Context(new SpeechCapture()));

        // The Doors button has no text until a line with a door control is selected, and is hidden until then.
        var pages = new (TabPage Page, string List, string[] Buttons)[]
        {
            (DoorsPage(form), "Doors and slides", new[] { "" }),
            (TemperaturePage(form), "Cabin temperatures", new[] { ForwardWarmer, ForwardCooler, AftWarmer, AftCooler, ResetTemperatures }),
            (WaterPage(form), "Water and waste", new[] { PreselectUp, PreselectDown })
        };
        foreach ((TabPage page, string listName, string[] buttonTexts) in pages)
        {
            // A DisplayListBox with the label as its AccessibleName; the buttons are read by their text.
            var list = Assert.Single(Descendants(page).OfType<ListBox>());
            Assert.IsType<DisplayListBox>(list);
            Assert.Equal(listName, list.AccessibleName);
            Assert.Equal(0, list.TabIndex);

            Button[] buttons = Descendants(page).OfType<Button>().ToArray();
            Assert.Equal(buttonTexts, buttons.Select(button => button.Text).ToArray());
            Assert.All(buttons, button => Assert.Null(button.AccessibleName));
            // The buttons follow the list in the tab order, in the order they are listed here.
            Assert.Equal(Enumerable.Range(0, buttons.Length).ToArray(), buttons.Select(button => button.TabIndex).ToArray());
            Assert.Equal(1, buttons[0].Parent!.TabIndex);

            // No mnemonic twice on a page.
            char[] mnemonics = buttons.Select(button => button.Text).Where(text => text.Contains('&'))
                .Select(text => char.ToLowerInvariant(text[text.IndexOf('&') + 1])).ToArray();
            Assert.Equal(mnemonics.Length, mnemonics.Distinct().Count());
        }
        Assert.Empty(Descendants(DoorsPage(form)).OfType<CheckBox>());
        // Nothing selected yet: no door to press, so no button in the tab order.
        Assert.False(DetachedDoorButton(form).Visible);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_doors_temperature_and_water_lists_show_their_lines(string culture)
        => TestCulture.Run(culture, () =>
        {
            var speech = new SpeechCapture();
            using var form = new FenixCabinForm(Context(speech));
            form.Render();

            // Before any answer every value reads "no data", and each list opens on its first line.
            Assert.Equal("Forward zone: no data", TemperatureList(form).Items[0]);
            Assert.Equal("Water: no data", WaterList(form).Items[0]);
            Assert.Equal("Door 1 left: no status", DoorsList(form).Items[0]);
            Assert.All(new[] { DoorsList(form), TemperatureList(form), WaterList(form) }, list => Assert.Equal(0, list.SelectedIndex));

            Set(form,
                (FenixCabinNames.FapOperational, True),
                (FenixCabinNames.TempFwdAir, Num(24)), (FenixCabinNames.TempFwdSelected, Num(24)), (FenixCabinNames.TempFwdOffset, Num(0.5)),
                (FenixCabinNames.WaterTank, Num(85)), (FenixCabinNames.WaterPreselect, Num(100)),
                (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.Slide("d1l"), True));

            FenixCabinLayout layout = DefaultLayout();
            FenixCabinLine[] lines = FenixCabinLines.Build(form.Cache, null, layout).ToArray();
            foreach ((FenixCabinSection section, ListBox list) in new[]
                     {
                         (FenixCabinSection.Doors, DoorsList(form)),
                         (FenixCabinSection.Temperature, TemperatureList(form)),
                         (FenixCabinSection.Water, WaterList(form))
                     })
                Assert.Equal(lines.Where(line => line.Section == section).Select(line => line.Text).ToArray(),
                    list.Items.Cast<string>().ToArray());

            Assert.Equal("Door 1 left: closed, slide armed", DoorsList(form).Items[0]);
            Assert.Equal("Forward zone: 24.0 degrees, target 24.5 degrees, offset plus 0.5", TemperatureList(form).Items[0]);
            Assert.Equal("Water: 85 percent", WaterList(form).Items[0]);
            Assert.Equal("Water preselect: 100 percent", WaterList(form).Items[1]);
            Assert.Empty(speech.All);
            return 0;
        });

    [Fact]
    public void The_doors_button_says_what_the_selected_line_can_do_and_is_hidden_where_there_is_no_control()
    {
        using var rig = new ControlsRig();
        FenixCabinForm form = rig.Form;
        FenixCabinLayout layout = DefaultLayout();
        Button button = DetachedDoorButton(form);
        string ButtonText() => button.Text;

        // The A320 lists its four corner doors, the overwing slides between them, and three cargo doors.
        Assert.Equal(
            new[] { "Door 1 left", "Door 1 right", "Overwing slide left", "Overwing slide right", "Door 4 left", "Door 4 right" },
            DoorsList(form).Items.Cast<string>().Take(6).Select(text => text[..text.IndexOf(':')]).ToArray());
        Assert.Equal(9, DoorsList(form).Items.Count);

        // The list opens on Door 1 left, whose control the Fenix has not said.
        Assert.Equal(0, DoorsList(form).SelectedIndex);
        Assert.True(button.Visible);
        Assert.Equal("&Open door 1 left", ButtonText());

        // Open (2) reads Close; armed (0) and disarmed (1) read Open; so does a control that stops being known.
        Set(form, (FenixCabinNames.DoorControl("d1l"), Num(2)));
        Assert.Equal("&Close door 1 left", ButtonText());
        Set(form, (FenixCabinNames.DoorControl("d1l"), Num(1)));
        Assert.Equal("&Open door 1 left", ButtonText());
        Set(form, (FenixCabinNames.DoorControl("d1l"), Num(0)));
        Assert.Equal("&Open door 1 left", ButtonText());
        Set(form, (FenixCabinNames.DoorControl("d1l"), Num(2)));
        Set(form, (FenixCabinNames.DoorControl("d1l"), FenixDataValue.Unknown));
        Assert.Equal("&Open door 1 left", ButtonText());

        // A new selection changes the button at once, before any render.
        form.Cache.Apply(FenixCabinNames.DoorControl("d4r"), Num(2));
        SelectDoorLine(form, layout, "door.d4r");
        Assert.Equal("&Close door 4 right", ButtonText());

        // An overwing slide has no control to press: the button leaves the tab order, on every render too.
        SelectDoorLine(form, layout, "slide.owl");
        Assert.False(button.Visible);
        form.Render();
        Assert.False(button.Visible);
        SelectDoorLine(form, layout, "slide.owr");
        Assert.False(button.Visible);

        // A cargo door: Open until the Fenix says it is open; a moving door keeps the word of its flag.
        SelectDoorLine(form, layout, "cargo.forward");
        Assert.True(button.Visible);
        Assert.Equal("&Open the forward cargo door", ButtonText());
        Set(form, (FenixCabinNames.CargoForward, True));
        Assert.Equal("&Close the forward cargo door", ButtonText());
        Set(form, (FenixCabinNames.CargoFwdMoving, True));
        Assert.Equal("&Close the forward cargo door", ButtonText());
        Set(form, (FenixCabinNames.CargoForward, False));
        Assert.Equal("&Open the forward cargo door", ButtonText());

        SelectDoorLine(form, layout, "cargo.aft");
        Assert.Equal("&Open the aft cargo door", ButtonText());
        Set(form, (FenixCabinNames.CargoAft, True));
        Assert.Equal("&Close the aft cargo door", ButtonText());

        SelectDoorLine(form, layout, "cargo.bulk");
        Assert.Equal("&Open the bulk cargo door", ButtonText());
        Set(form, (FenixCabinNames.CargoBulk, True));
        Assert.Equal("&Close the bulk cargo door", ButtonText());
        Assert.True(button.Visible);

        // No selection: nothing to press.
        DoorsList(form).SelectedIndex = -1;
        Assert.False(button.Visible);

        // Back on a door: shown again with its own word.
        SelectDoorLine(form, layout, "door.d4r");
        Assert.True(button.Visible);
        Assert.Equal("&Close door 4 right", ButtonText());

        // Setting the button is the render's doing, not the pilot's: nothing was said or sent.
        Assert.Empty(rig.Speech.All);
        Assert.Equal(0, rig.Gateway.Sent);
    }

    [Fact]
    public void The_doors_button_follows_the_door_on_every_render_also_while_the_controls_are_held()
    {
        // A check box change holds the controls for a few seconds. The Doors list line follows the Fenix
        // meanwhile, and so must the button: a press closes the door it reads "Close" for.
        using var rig = new ControlsRig();
        FenixCabinForm form = rig.Form;
        Set(form,
            (FenixCabinNames.CabinReadyAvailable, True), (FenixCabinNames.CabinReady, False),
            (FenixCabinNames.DoorControl("d1l"), Num(0)));
        Assert.Equal("&Open door 1 left", DoorButton(form).Text);

        CabinReadyBox(rig.Form).Checked = true;
        WaitFor(() => rig.Gateway.Writes.Count == 1, "the cabin ready write");

        Set(form, (FenixCabinNames.Door("d1l"), True), (FenixCabinNames.DoorControl("d1l"), Num(2)));

        // The hold is still on: the check box keeps the pilot's choice though the Fenix still says "no".
        Assert.True(CabinReadyBox(form).Checked);
        Assert.StartsWith("Door 1 left: open", (string)DoorsList(form).Items[0]!);
        Assert.Equal("&Close door 1 left", DoorButton(form).Text);

        Click(DoorButton(form));
        WaitFor(() => rig.Gateway.Writes.Count == 2, "the door write");
        AssertSent(rig.Gateway.Writes[1], new FenixCabinWrite(FenixCabinWriteKind.Int, FenixCabinNames.DoorControl("d1l"), 1));
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void A_door_the_EFB_does_not_list_has_no_control_and_the_A321_has_all_eight()
    {
        // An A320 whose exits setting is double full-size has doors 2 and 3 in the cabin, but the EFB's A320
        // ground service lists only the four corner doors.
        FenixCabinLayout a320 = FenixCabinLayout.Resolve(MSFSBlindAssist.Aircraft.FenixAircraftType.A320, "DoubleFullsize", null, null);
        using (var rig = new ControlsRig(layout: a320))
        {
            Button button = DetachedDoorButton(rig.Form);
            Set(rig.Form, (FenixCabinNames.DoorControl("d2l"), Num(2)), (FenixCabinNames.DoorControl("d1l"), Num(2)));
            SelectDoorLine(rig.Form, a320, "door.d1l");
            Assert.True(button.Visible);
            Assert.Equal("&Close door 1 left", button.Text);
            SelectDoorLine(rig.Form, a320, "door.d2l");

            // Hidden, so no key, click or mnemonic reaches it. A press forced on it anyway says and sends nothing.
            Assert.False(button.Visible);
            Click(button);
            Assert.Empty(rig.Speech.All);
            Assert.Equal(0, rig.Gateway.Sent);
        }

        FenixCabinLayout a321 = FenixCabinLayout.Resolve(MSFSBlindAssist.Aircraft.FenixAircraftType.A321, null, null, null);
        using (var rig = new ControlsRig(layout: a321))
        {
            Button button = DetachedDoorButton(rig.Form);
            Set(rig.Form, (FenixCabinNames.DoorControl("d2l"), Num(2)), (FenixCabinNames.DoorControl("d3r"), Num(0)));
            SelectDoorLine(rig.Form, a321, "door.d2l");
            Assert.True(button.Visible);
            Assert.Equal("&Close door 2 left", button.Text);
            SelectDoorLine(rig.Form, a321, "door.d3r");
            Assert.True(button.Visible);
            Assert.Equal("&Open door 3 right", button.Text);
        }
    }

    [Fact]
    public void A_layout_change_shows_or_hides_the_doors_button_before_the_next_render()
    {
        // The A321's door 2 left has a control; the airframe changes to an A320, which has no door 2: the button
        // hides as the layout changes, not a render later, so a press never reaches a line it has no control for.
        var gateway = new AudioGateway();
        using var data = new FenixDataService(gateway);
        var context = new FenixWindowContext(new MSFSBlindAssist.Aircraft.FenixA320Definition(), data, null!, new SpeechCapture(), null!);
        using var form = new FenixCabinForm(context);
        BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        FenixCabinLayout a321 = FenixCabinLayout.Resolve(MSFSBlindAssist.Aircraft.FenixAircraftType.A321, null, null, null);
        typeof(FenixCabinForm).GetField("_layout", instance)!.SetValue(form, a321);
        form.Render();
        Button button = DetachedDoorButton(form);
        SelectDoorLine(form, a321, "door.d2l");
        Assert.True(button.Visible);

        // The definition's variant is still Unknown, which lays the cabin out as an A320.
        typeof(FenixCabinForm).GetMethod("OnVariantChanged", instance)!
            .Invoke(form, new object?[] { null, MSFSBlindAssist.Aircraft.FenixVariant.Unknown });

        Assert.False(button.Visible);
    }

    [Fact]
    public void Every_press_on_the_new_pages_says_the_Fenix_is_not_answering_first_and_sends_nothing()
    {
        // The Fenix down: the same reason from every button, whatever else is wrong; the Doors list is
        // on a door whose height and control are not known either, which would otherwise say so.
        using var rig = new ControlsRig(answering: false);
        SelectDoorLine(rig.Form, DefaultLayout(), "door.d1l");
        Button[] buttons = new[] { DoorsPage(rig.Form), TemperaturePage(rig.Form), WaterPage(rig.Form) }
            .SelectMany(page => Descendants(page).OfType<Button>())
            .ToArray();
        Assert.Equal(8, buttons.Length);

        foreach (Button button in buttons) Click(button);

        Assert.Equal(Enumerable.Repeat(NotAnsweringText, buttons.Length).ToArray(), rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(0, rig.Gateway.Sent);
    }

    [Fact]
    public void Temperature_presses_refuse_for_an_unknown_value_or_an_unavailable_page_before_any_limit()
    {
        using var rig = new ControlsRig();
        FenixCabinForm form = rig.Form;
        Button[] steps =
        {
            TemperatureButton(form, ForwardWarmer), TemperatureButton(form, ForwardCooler),
            TemperatureButton(form, AftWarmer), TemperatureButton(form, AftCooler)
        };

        // The Fenix answers, but no zone value is known.
        foreach (Button step in steps) Click(step);
        Assert.Equal(Enumerable.Repeat("Temperature control is not available.", 4).ToArray(), rig.Speech.Interrupts);

        // One zone known, the other not: only the unknown one refuses.
        Zones(form, 24, 0, 24, 0);
        form.Cache.Apply(FenixCabinNames.TempAftOffset, FenixDataValue.Unknown);
        form.Render();
        Click(TemperatureButton(form, AftWarmer));
        Click(TemperatureButton(form, AftCooler));
        Assert.Equal(Enumerable.Repeat("Temperature control is not available.", 6).ToArray(), rig.Speech.Interrupts);
        Assert.Equal(0, rig.Gateway.Sent);

        // Each of the three flags known false makes the page unavailable, even at a limit (an unknown flag does
        // not: the limits test below refuses with the zone's own words).
        Zones(form, 30, 0, 18, -2.5);
        int refusals = rig.Speech.Interrupts.Count;
        foreach (string flag in new[]
                 {
                     FenixCabinNames.ZoneControllerOperational, FenixCabinNames.LavGalleyFanRunning, FenixCabinNames.CidsOperational
                 })
        {
            Set(form, (flag, False));
            Click(TemperatureButton(form, ForwardWarmer));
            Click(TemperatureButton(form, AftCooler));
            Click(TemperatureButton(form, ResetTemperatures));
            Set(form, (flag, True));
        }
        Assert.Equal(Enumerable.Repeat("Temperature control is not available.", 9).ToArray(), rig.Speech.Interrupts.Skip(refusals).ToArray());
        Assert.Equal(0, rig.Gateway.Sent);
    }

    [Fact]
    public void Temperature_presses_at_a_limit_say_so_for_their_zone_and_send_nothing()
    {
        using var rig = new ControlsRig();
        FenixCabinForm form = rig.Form;

        // Forward at 30 degrees (the target), aft at the lowest offset: warmest forward, coolest aft.
        Zones(form, 30, 0, 24, -2.5);
        Click(TemperatureButton(form, ForwardWarmer));
        Click(TemperatureButton(form, AftCooler));
        // Forward at the highest offset, aft at 18 degrees: the other limits of each rule.
        Zones(form, 24, 2.5, 18, 0);
        Click(TemperatureButton(form, ForwardWarmer));
        Click(TemperatureButton(form, AftCooler));
        // Forward at the lowest offset, aft at 30: coolest forward, warmest aft.
        Zones(form, 24, -2.5, 30, 0);
        Click(TemperatureButton(form, ForwardCooler));
        Click(TemperatureButton(form, AftWarmer));
        // Forward at 18 degrees, aft at the highest offset.
        Zones(form, 18, 0, 24, 2.5);
        Click(TemperatureButton(form, ForwardCooler));
        Click(TemperatureButton(form, AftWarmer));

        Assert.Equal(
            new[]
            {
                "Forward zone is at its warmest setting.", "Aft zone is at its coolest setting.",
                "Forward zone is at its warmest setting.", "Aft zone is at its coolest setting.",
                "Forward zone is at its coolest setting.", "Aft zone is at its warmest setting.",
                "Forward zone is at its coolest setting.", "Aft zone is at its warmest setting."
            },
            rig.Speech.Interrupts);
        Assert.Equal(0, rig.Gateway.Sent);
    }

    [Fact]
    public void The_water_preselect_is_not_stepped_when_unknown_or_at_a_limit()
    {
        using var rig = new ControlsRig();
        FenixCabinForm form = rig.Form;

        // Answering without a preselect: not stepped from the FAP's assumed 100.
        Click(WaterButton(form, PreselectDown));
        Click(WaterButton(form, PreselectUp));
        Assert.Equal(Enumerable.Repeat("Water preselect is not known.", 2).ToArray(), rig.Speech.Interrupts);

        Set(form, (FenixCabinNames.WaterPreselect, Num(100)));
        Click(WaterButton(form, PreselectUp));
        Set(form, (FenixCabinNames.WaterPreselect, Num(25)));
        Click(WaterButton(form, PreselectDown));

        Assert.Equal(
            new[] { "Water preselect is already 100 percent.", "Water preselect is already 25 percent." },
            rig.Speech.Interrupts.Skip(2).ToArray());
        Assert.Equal(0, rig.Gateway.Sent);
    }

    [Fact]
    public void Door_presses_refuse_an_unknown_state_and_a_moving_cargo_door()
    {
        using var rig = new ControlsRig();
        FenixCabinForm form = rig.Form;
        FenixCabinLayout layout = DefaultLayout();
        Button button = DetachedDoorButton(form);
        var expected = new List<string>();
        void Presses(string line, string refusal)
        {
            SelectDoorLine(form, layout, line);
            Assert.True(button.Visible, line);
            Click(button);
            expected.Add(refusal);
            Assert.Equal(expected, rig.Speech.Interrupts);
        }

        // The Fenix answers without a control value for the door, or a state for the cargo door.
        Presses("door.d1l", "Door 1 left state is not known.");
        Presses("door.d4r", "Door 4 right state is not known.");
        Presses("cargo.forward", "The forward cargo door state is not known.");
        Presses("cargo.aft", "The aft cargo door state is not known.");
        Presses("cargo.bulk", "The bulk cargo door state is not known.");

        // A cargo door that moves, open or closed: the EFB greys its toggle.
        Set(form, (FenixCabinNames.CargoForward, False), (FenixCabinNames.CargoFwdMoving, True));
        Presses("cargo.forward", "The forward cargo door is moving.");
        Set(form, (FenixCabinNames.CargoAft, True), (FenixCabinNames.CargoAftMoving, True));
        Presses("cargo.aft", "The aft cargo door is moving.");

        // An unknown state comes before the movement flag (the order is unknown, then the limits).
        Set(form, (FenixCabinNames.CargoAft, FenixDataValue.Unknown));
        Presses("cargo.aft", "The aft cargo door state is not known.");

        // An overwing slide and no selection at all: the button is hidden, and a press forced on it anyway
        // says nothing.
        foreach (string line in new[] { "slide.owl", "slide.owr" })
        {
            SelectDoorLine(form, layout, line);
            Assert.False(button.Visible, line);
            Click(button);
        }
        DoorsList(form).SelectedIndex = -1;
        Assert.False(button.Visible);
        Click(button);
        Assert.Equal(expected, rig.Speech.Interrupts);

        Assert.Equal(0, rig.Gateway.Sent);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
    }

    private const string OnlyOnTheGround = "Doors can only be opened or closed on the ground.";
    private const string HeightNotKnown = "Height above ground is not known.";

    [Fact]
    public void Door_and_cargo_presses_refuse_above_100_feet_or_with_the_height_unknown()
    {
        // The EFB shows its door and cargo toggles only while the height above ground is at most
        // 100 ft. The check comes after the Fenix not answering and before an unknown or moving door.
        using var rig = new ControlsRig();
        FenixCabinForm form = rig.Form;
        FenixCabinLayout layout = DefaultLayout();
        Button button = DetachedDoorButton(form);
        Set(form,
            (FenixCabinNames.DoorControl("d1l"), Num(0)),
            (FenixCabinNames.CargoForward, False), (FenixCabinNames.CargoFwdMoving, False),
            (FenixCabinNames.CargoAft, False), (FenixCabinNames.CargoAftMoving, True));
        var expected = new List<string>();
        void Presses(string line, string refusal)
        {
            SelectDoorLine(form, layout, line);
            // In flight the button stays offered on a door or cargo line: the press says why it cannot act.
            Assert.True(button.Visible, line);
            Click(button);
            expected.Add(refusal);
            Assert.Equal(expected, rig.Speech.Interrupts);
        }

        // 101 ft: a door, a cargo door, a door whose control is not known and a moving cargo door all refuse.
        Set(form, (FenixCabinNames.AltitudeAboveGround, Num(101)));
        SelectDoorLine(form, layout, "door.d1l");
        // The button text does not change in flight.
        Assert.Equal("&Open door 1 left", button.Text);
        Presses("door.d1l", OnlyOnTheGround);
        Presses("cargo.forward", OnlyOnTheGround);
        Presses("door.d4r", OnlyOnTheGround);
        Presses("cargo.aft", OnlyOnTheGround);
        // A line with no control has no button, in flight as on the ground.
        SelectDoorLine(form, layout, "slide.owl");
        Assert.False(button.Visible);

        // The Fenix answering without a height: not known, not assumed on the ground.
        Set(form, (FenixCabinNames.AltitudeAboveGround, FenixDataValue.Unknown));
        Presses("door.d1l", HeightNotKnown);
        Presses("cargo.forward", HeightNotKnown);
        Assert.Equal(0, rig.Gateway.Sent);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);

        // 100 ft is the EFB's ground: the door and the cargo door write.
        Set(form, (FenixCabinNames.AltitudeAboveGround, Num(100)));
        SelectDoorLine(form, layout, "door.d1l");
        Click(button);
        WaitFor(() => rig.Gateway.Writes.Count == 1, "the door write");
        AssertSent(rig.Gateway.Writes[0], new FenixCabinWrite(FenixCabinWriteKind.Int, FenixCabinNames.DoorControl("d1l"), 2));
        SelectDoorLine(form, layout, "cargo.forward");
        Click(button);
        WaitFor(() => rig.Gateway.Writes.Count == 2, "the cargo write");
        AssertSent(rig.Gateway.Writes[1], new FenixCabinWrite(FenixCabinWriteKind.Bool, FenixCabinNames.CargoForward, true));
        Assert.Equal(expected, rig.Speech.Interrupts);
    }

    [Fact]
    public void A_door_press_says_the_Fenix_is_not_answering_before_the_height()
    {
        // Nothing answered, so the height is not known either: the not-answering reason comes first. The
        // button is offered all the same on a door or cargo line, so the pilot hears why.
        using var rig = new ControlsRig(answering: false);
        Button button = DetachedDoorButton(rig.Form);
        SelectDoorLine(rig.Form, DefaultLayout(), "door.d1l");
        Assert.True(button.Visible);
        Assert.Equal("&Open door 1 left", button.Text);

        Click(button);
        SelectDoorLine(rig.Form, DefaultLayout(), "cargo.forward");
        Assert.True(button.Visible);
        Assert.Equal("&Open the forward cargo door", button.Text);
        Click(button);

        Assert.Equal(new[] { NotAnsweringText, NotAnsweringText }, rig.Speech.Interrupts);
        Assert.Equal(0, rig.Gateway.Sent);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Each_temperature_and_water_button_sends_its_own_write(string culture)
        => TestCulture.Run(culture, () =>
        {
            using var rig = new ControlsRig();
            FenixCabinForm form = rig.Form;
            // Forward: selected 24, offset plus 0.5. Aft: selected 22, offset minus 1.0. Water preselect 75.
            Zones(form, 24, 0.5, 22, -1.0);
            Set(form, (FenixCabinNames.WaterPreselect, Num(75)));

            var presses = new (Button Button, FenixCabinWrite Write)[]
            {
                (TemperatureButton(form, ForwardWarmer), Float(FenixCabinNames.TempFwdOffset, 1.0)),
                (TemperatureButton(form, ForwardCooler), Float(FenixCabinNames.TempFwdOffset, 0.0)),
                (TemperatureButton(form, AftWarmer), Float(FenixCabinNames.TempAftOffset, -0.5)),
                (TemperatureButton(form, AftCooler), Float(FenixCabinNames.TempAftOffset, -1.5)),
                (WaterButton(form, PreselectUp), Float(FenixCabinNames.WaterPreselect, 100)),
                (WaterButton(form, PreselectDown), Float(FenixCabinNames.WaterPreselect, 50))
            };
            for (int i = 0; i < presses.Length; i++)
            {
                Click(presses[i].Button);
                int count = i + 1;
                WaitFor(() => rig.Gateway.Writes.Count == count, presses[i].Button.Text);
                AssertSent(rig.Gateway.Writes[i], presses[i].Write);
            }

            // Reset: both offsets to zero, forward first, whatever the zones read.
            Click(TemperatureButton(form, ResetTemperatures));
            WaitFor(() => rig.Gateway.Writes.Count == presses.Length + 2, "the reset");
            AssertSent(rig.Gateway.Writes[presses.Length], Float(FenixCabinNames.TempFwdOffset, 0.0));
            AssertSent(rig.Gateway.Writes[presses.Length + 1], Float(FenixCabinNames.TempAftOffset, 0.0));

            // Accepted, so nothing is said until the lines change.
            Assert.Empty(rig.Speech.All);
            return 0;
        });

    [Fact]
    public void The_reset_says_so_when_both_offsets_are_already_zero_and_sends_nothing()
    {
        // The FAP greys its Reset button then (offsetsReset$: every zone's selectedOffset is 0).
        using var rig = new ControlsRig();
        Zones(rig.Form, 24, 0, 22, 0);

        Click(TemperatureButton(rig.Form, ResetTemperatures));

        Assert.Equal(new[] { "Both zones already have no offset." }, rig.Speech.Interrupts);
        Assert.Equal(0, rig.Gateway.Sent);

        // One offset not known: not proven reset, so the reset is sent (forward first).
        Set(rig.Form, (FenixCabinNames.TempAftOffset, FenixDataValue.Unknown));
        Click(TemperatureButton(rig.Form, ResetTemperatures));
        WaitFor(() => rig.Gateway.Writes.Count == 2, "the reset");
        AssertSent(rig.Gateway.Writes[0], Float(FenixCabinNames.TempFwdOffset, 0.0));
        AssertSent(rig.Gateway.Writes[1], Float(FenixCabinNames.TempAftOffset, 0.0));

        // One offset not zero: sent too.
        Set(rig.Form, (FenixCabinNames.TempAftOffset, Num(-0.5)));
        Click(TemperatureButton(rig.Form, ResetTemperatures));
        WaitFor(() => rig.Gateway.Writes.Count == 4, "the second reset");
        Assert.Single(rig.Speech.Interrupts);
    }

    [Fact]
    public void The_reset_works_without_any_zone_value_known()
    {
        // Only the Fenix has to answer: the reset writes zeros and needs no value to step from.
        using var rig = new ControlsRig();

        Click(TemperatureButton(rig.Form, ResetTemperatures));

        WaitFor(() => rig.Gateway.Writes.Count == 2, "the reset");
        AssertSent(rig.Gateway.Writes[0], Float(FenixCabinNames.TempFwdOffset, 0.0));
        AssertSent(rig.Gateway.Writes[1], Float(FenixCabinNames.TempAftOffset, 0.0));
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void Each_door_press_writes_the_toggle_of_the_selected_line()
    {
        using var rig = new ControlsRig();
        FenixCabinForm form = rig.Form;
        FenixCabinLayout layout = DefaultLayout();
        Set(form,
            (FenixCabinNames.DoorControl("d1l"), Num(2)), (FenixCabinNames.DoorControl("d1r"), Num(0)),
            (FenixCabinNames.DoorControl("d4l"), Num(1)),
            (FenixCabinNames.CargoForward, False), (FenixCabinNames.CargoFwdMoving, False),
            (FenixCabinNames.CargoAft, True), (FenixCabinNames.CargoAftMoving, False),
            (FenixCabinNames.CargoBulk, False));

        // An open door is disarmed (1), a door armed (0) or disarmed (1) is opened (2): the EFB's own toggle.
        // A cargo door writes the opposite of its flag.
        var presses = new (string Line, FenixCabinWrite Write)[]
        {
            ("door.d1l", new FenixCabinWrite(FenixCabinWriteKind.Int, FenixCabinNames.DoorControl("d1l"), 1)),
            ("door.d1r", new FenixCabinWrite(FenixCabinWriteKind.Int, FenixCabinNames.DoorControl("d1r"), 2)),
            ("door.d4l", new FenixCabinWrite(FenixCabinWriteKind.Int, FenixCabinNames.DoorControl("d4l"), 2)),
            ("cargo.forward", new FenixCabinWrite(FenixCabinWriteKind.Bool, FenixCabinNames.CargoForward, true)),
            ("cargo.aft", new FenixCabinWrite(FenixCabinWriteKind.Bool, FenixCabinNames.CargoAft, false)),
            ("cargo.bulk", new FenixCabinWrite(FenixCabinWriteKind.Bool, FenixCabinNames.CargoBulk, true))
        };
        for (int i = 0; i < presses.Length; i++)
        {
            SelectDoorLine(form, layout, presses[i].Line);
            Click(DoorButton(form));
            int count = i + 1;
            WaitFor(() => rig.Gateway.Writes.Count == count, presses[i].Line);
            AssertSent(rig.Gateway.Writes[i], presses[i].Write);
        }

        // The button acts on the Fenix's value of the moment, not on the text it had: the door that was open
        // is now armed, so the same line is opened.
        SelectDoorLine(form, layout, "door.d1l");
        Assert.Equal("&Close door 1 left", DoorButton(form).Text);
        form.Cache.Apply(FenixCabinNames.DoorControl("d1l"), Num(0));
        Click(DoorButton(form));
        WaitFor(() => rig.Gateway.Writes.Count == presses.Length + 1, "the second press");
        AssertSent(rig.Gateway.Writes[presses.Length], new FenixCabinWrite(FenixCabinWriteKind.Int, FenixCabinNames.DoorControl("d1l"), 2));

        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void A_door_press_marks_its_line_as_the_pilots_own_for_the_read_back_window()
    {
        // While a door or cargo press waits for its read-back, the app-wide door announcement leaves that
        // door alone, so the change is said once. A press the Fenix refuses withdraws its mark at once.
        foreach (string line in new[] { "door.d1l", "cargo.forward" })
        {
            using var rig = new ControlsRig();
            var presses = new FenixCabinOwnPresses();
            rig.Form.OwnPresses = presses;
            Set(rig.Form, (FenixCabinNames.DoorControl("d1l"), Num(0)), (FenixCabinNames.CargoForward, False), (FenixCabinNames.CargoFwdMoving, False));
            SelectDoorLine(rig.Form, DefaultLayout(), line);

            DateTime before = DateTime.UtcNow;
            Click(DoorButton(rig.Form));
            WaitFor(() => Expectations(rig.Form).Length == 1, line);

            // While the read-back waits; its 30 s plus the 2 s grace is only the ceiling, the read-back's end
            // ends it sooner (the tests below).
            Assert.True(presses.IsPending(line, DateTime.UtcNow), line);
            Assert.True(presses.IsPending(line, before + TimeSpan.FromSeconds(29)), line);
            Assert.False(presses.IsPending(line, DateTime.UtcNow + TimeSpan.FromSeconds(33)), line);
            Assert.False(presses.IsPending(line == "door.d1l" ? "cargo.forward" : "door.d1l", DateTime.UtcNow));
        }

        using (var rig = new ControlsRig())
        {
            var presses = new FenixCabinOwnPresses();
            rig.Form.OwnPresses = presses;
            Set(rig.Form, (FenixCabinNames.DoorControl("d1l"), Num(0)));
            rig.Gateway.Answer = () => WriteAnswer(accepted: false);

            Click(DoorButton(rig.Form));

            WaitFor(() => rig.Speech.Interrupts.Count == 1, "the refusal");
            Assert.Equal(new[] { NotAcceptedText }, rig.Speech.Interrupts);
            // The withdrawal follows the refusal on the press's own continuation.
            WaitFor(() => !presses.IsPending("door.d1l", DateTime.UtcNow), "the mark withdrawn");
        }

        // A press refused before anything is sent marks nothing.
        using (var rig = new ControlsRig())
        {
            var presses = new FenixCabinOwnPresses();
            rig.Form.OwnPresses = presses;
            Click(DoorButton(rig.Form));
            Assert.Equal(new[] { "Door 1 left state is not known." }, rig.Speech.Interrupts);
            Assert.False(presses.IsPending("door.d1l", DateTime.UtcNow));
        }

        // Unless a test gives it its own, the window marks the registry the app-wide announcements read.
        using (var form = new FenixCabinForm(Context(new SpeechCapture())))
            Assert.Same(FenixCabinOwnPresses.Shared, form.OwnPresses);
    }

    // ---- The own-press hold follows the read-back -----------------------------------------------

    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    // The app-wide announcements on the same registry as the window, fed whole polls by hand; inline speech.
    private sealed class Background : IDisposable
    {
        private Action<string, FenixDataValue>? _onValue;

        public List<string> Spoken { get; } = new();
        public DateTime Now { get; set; } = T0;
        public FenixCabinReadyMonitor Monitor { get; }

        public Background(FenixCabinOwnPresses presses)
            => Monitor = new FenixCabinReadyMonitor(
                onValue => { _onValue = onValue; return new Stop(); },
                null, Spoken.Add, ownPresses: presses, clock: () => Now);

        // One poll: every cabin name in order, these values, Unknown for the rest.
        public void Poll(params (string Name, FenixDataValue Value)[] values)
        {
            var given = values.ToDictionary(v => v.Name, v => v.Value);
            foreach (string name in FenixCabinNames.All)
                _onValue!(name, given.TryGetValue(name, out FenixDataValue value) ? value : FenixDataValue.Unknown);
        }

        public void Dispose() => Monitor.Dispose();

        private sealed class Stop : IDisposable
        {
            public void Dispose() { }
        }
    }

    // A rig whose window keeps the read-back and its marks by the given clock, on its own registry.
    private static (ControlsRig Rig, FenixCabinOwnPresses Presses) OwnPressRig(Func<DateTime> clock)
    {
        var rig = new ControlsRig();
        var presses = new FenixCabinOwnPresses();
        rig.Form.OwnPresses = presses;
        rig.Form.Clock = clock;
        Set(rig.Form, (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.Slide("d1l"), True), (FenixCabinNames.DoorControl("d1l"), Num(0)));
        return (rig, presses);
    }

    [Fact]
    public void Closing_the_window_before_its_read_back_speaks_hands_the_door_back_to_the_app_wide_announcement()
    {
        DateTime now = T0;
        (ControlsRig rig, FenixCabinOwnPresses presses) = OwnPressRig(() => now);
        using var background = new Background(presses);
        background.Poll((FenixCabinNames.Door("d1l"), False));

        Click(DoorButton(rig.Form));
        WaitFor(() => Expectations(rig.Form).Length == 1, "the door press");
        Assert.True(presses.IsPending("door.d1l", now));

        // The pilot closes the window before the door has moved: its read-back can no longer speak.
        rig.Dispose();
        Assert.False(presses.IsPending("door.d1l", now));

        // The door opens a few seconds later: the app-wide announcement says it.
        now = background.Now = T0 + TimeSpan.FromSeconds(5);
        background.Poll((FenixCabinNames.Door("d1l"), True));
        Assert.Equal(new[] { "Door 1 left open" }, background.Spoken);
    }

    [Fact]
    public void A_read_back_that_spoke_holds_its_door_only_for_the_grace()
    {
        DateTime now = T0;
        (ControlsRig rig, FenixCabinOwnPresses presses) = OwnPressRig(() => now);
        using (rig)
        using (var background = new Background(presses))
        {
            background.Poll((FenixCabinNames.Door("d1l"), False));
            Click(DoorButton(rig.Form));
            WaitFor(() => Expectations(rig.Form).Length == 1, "the door press");

            // The door opens at 4 s: the app-wide poll sees it first and leaves it to the read-back, which says it.
            now = background.Now = T0 + TimeSpan.FromSeconds(4);
            background.Poll((FenixCabinNames.Door("d1l"), True));
            Set(rig.Form, (FenixCabinNames.Door("d1l"), True), (FenixCabinNames.DoorControl("d1l"), Num(2)));
            Tick(rig.Form);
            Assert.Equal(new[] { "Door 1 left: open, slide armed" }, rig.Speech.All);
            Assert.Empty(background.Spoken);

            // Held for a poll's lag only: about 2 s, not the rest of the 30 s.
            Assert.True(presses.IsPending("door.d1l", now + TimeSpan.FromSeconds(1.9)));
            Assert.False(presses.IsPending("door.d1l", now + TimeSpan.FromSeconds(2)));

            // Another source closes it 3 s later: said by the app-wide announcement, the opening not again.
            background.Now = now + TimeSpan.FromSeconds(3);
            background.Poll((FenixCabinNames.Door("d1l"), False));
            Assert.Equal(new[] { "Door 1 left closed" }, background.Spoken);
        }
    }

    [Fact]
    public void A_read_back_that_timed_out_silently_withdraws_its_mark_at_once()
    {
        DateTime now = T0;
        (ControlsRig rig, FenixCabinOwnPresses presses) = OwnPressRig(() => now);
        using (rig)
        {
            Click(DoorButton(rig.Form));
            WaitFor(() => Expectations(rig.Form).Length == 1, "the door press");

            // The door never moves: at the window's end the read-back ends silently and so does the mark,
            // before its 32 s ceiling.
            now = T0 + TimeSpan.FromSeconds(29);
            Tick(rig.Form);
            Assert.True(presses.IsPending("door.d1l", now));
            now = T0 + TimeSpan.FromSeconds(30);
            Tick(rig.Form);
            Assert.Empty(Expectations(rig.Form));
            Assert.False(presses.IsPending("door.d1l", now));
            Assert.Empty(rig.Speech.All);
        }
    }

    [Fact]
    public void A_door_press_whose_send_path_throws_withdraws_its_mark()
    {
        // No announcer: the refusal of the refused write throws inside the send path. The mark must not outlive it.
        var gateway = new AudioGateway { Answer = () => WriteAnswer(accepted: false) };
        using var data = new FenixDataService(gateway);
        using var form = new FenixCabinForm(new FenixWindowContext(null!, data, null!, null!, null!));
        var presses = new FenixCabinOwnPresses();
        form.OwnPresses = presses;
        Set(form, (FenixCabinNames.FapOperational, True), (FenixCabinNames.AltitudeAboveGround, Num(0)), (FenixCabinNames.DoorControl("d1l"), Num(0)));

        Click(DoorButton(form));

        WaitFor(() => gateway.Writes.Count == 1, "the door write");
        WaitFor(() => !presses.IsPending("door.d1l", DateTime.UtcNow), "the mark withdrawn");
    }

    [Fact]
    public void The_windows_read_back_is_never_muted_by_a_monitor_manager_row()
    {
        // CORE-7: the window confirms the pilot's own press once; the Ctrl+M cabin rows mute only the app-wide
        // announcements, so no cabin form reads the mute list.
        string text = CabinFormText();
        Assert.DoesNotContain("DisabledMonitorVariables", text);
        Assert.DoesNotContain("FenixCabinAnnouncements", text);
    }

    [Fact]
    public void A_bulk_cargo_door_has_no_movement_flag_to_wait_for()
    {
        using var rig = new ControlsRig();
        FenixCabinLayout layout = DefaultLayout();
        // Another door's movement does not stop the bulk door.
        Set(rig.Form, (FenixCabinNames.CargoBulk, False), (FenixCabinNames.CargoFwdMoving, True), (FenixCabinNames.CargoAftMoving, True));
        SelectDoorLine(rig.Form, layout, "cargo.bulk");

        Click(DoorButton(rig.Form));

        WaitFor(() => rig.Gateway.Writes.Count == 1, "the bulk door write");
        AssertSent(rig.Gateway.Writes[0], new FenixCabinWrite(FenixCabinWriteKind.Bool, FenixCabinNames.CargoBulk, true));
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void Each_press_waits_for_its_own_line_for_its_own_time()
    {
        // The read-back key and window of every press (and whether it says "unchanged"): 5 s and a spoken
        // "unchanged" for the numbers, 30 s and silence for a door. The reset waits on both zones. A door, a
        // cargo door and a temperature wait for the part of the line they change; the preselect for its text.
        var five = TimeSpan.FromSeconds(5);
        var thirty = TimeSpan.FromSeconds(30);
        FenixCabinLayout layout = DefaultLayout();

        var cases = new (string Name, Action<FenixCabinForm> Press, (string Key, bool Unchanged, TimeSpan Window, bool ByState)[] Waits)[]
        {
            ("Forward warmer", form => Click(TemperatureButton(form, ForwardWarmer)), new[] { ("temp.forward", true, five, true) }),
            ("Forward cooler", form => Click(TemperatureButton(form, ForwardCooler)), new[] { ("temp.forward", true, five, true) }),
            ("Aft warmer", form => Click(TemperatureButton(form, AftWarmer)), new[] { ("temp.aft", true, five, true) }),
            ("Aft cooler", form => Click(TemperatureButton(form, AftCooler)), new[] { ("temp.aft", true, five, true) }),
            ("Reset", form => Click(TemperatureButton(form, ResetTemperatures)), new[] { ("temp.forward", true, five, true), ("temp.aft", true, five, true) }),
            ("Preselect up", form => Click(WaterButton(form, PreselectUp)), new[] { ("water.preselect", true, five, false) }),
            ("Preselect down", form => Click(WaterButton(form, PreselectDown)), new[] { ("water.preselect", true, five, false) }),
            ("Door", form => { SelectDoorLine(form, layout, "door.d1l"); Click(DoorButton(form)); }, new[] { ("door.d1l", false, thirty, true) }),
            ("Forward cargo", form => { SelectDoorLine(form, layout, "cargo.forward"); Click(DoorButton(form)); }, new[] { ("cargo.forward", false, thirty, true) }),
            ("Aft cargo", form => { SelectDoorLine(form, layout, "cargo.aft"); Click(DoorButton(form)); }, new[] { ("cargo.aft", false, thirty, true) }),
            ("Bulk cargo", form => { SelectDoorLine(form, layout, "cargo.bulk"); Click(DoorButton(form)); }, new[] { ("cargo.bulk", false, thirty, true) })
        };

        foreach (var c in cases)
        {
            using var rig = new ControlsRig();
            // An offset forward, so the reset has something to reset.
            Zones(rig.Form, 24, 0.5, 24, 0);
            Set(rig.Form,
                (FenixCabinNames.WaterPreselect, Num(75)),
                (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.DoorControl("d1l"), Num(0)),
                (FenixCabinNames.CargoForward, False), (FenixCabinNames.CargoAft, False), (FenixCabinNames.CargoBulk, False));
            string?[] before = c.Waits.Select(wait => LineTextOf(rig.Form, wait.Key)).ToArray();
            string?[] beforeState = c.Waits.Select(wait => LineStateOf(rig.Form, wait.Key)).ToArray();

            c.Press(rig.Form);

            WaitFor(() => Expectations(rig.Form).Length == c.Waits.Length, c.Name);
            var waiting = Expectations(rig.Form);
            Assert.True(c.Waits.Select(wait => wait.Key).SequenceEqual(waiting.Select(w => w.Key)), $"{c.Name} waits on [{string.Join(", ", waiting.Select(w => w.Key))}]");
            for (int i = 0; i < waiting.Length; i++)
            {
                Assert.Equal(c.Waits[i].Window, waiting[i].Window);
                Assert.Equal(c.Waits[i].Unchanged, waiting[i].SpeakIfUnchanged);
                Assert.Equal(c.Waits[i].ByState, waiting[i].ByState);
                // The text and state of the line as the pilot had it at the press.
                Assert.Equal(before[i], waiting[i].PressText);
                Assert.NotNull(waiting[i].PressText);
                Assert.Equal(beforeState[i], waiting[i].PressState);
                if (c.Waits[i].ByState) Assert.NotNull(waiting[i].PressState);
            }
            Assert.Empty(rig.Speech.All);
        }
    }

    [Fact]
    public void A_press_takes_its_line_from_the_values_of_the_moment_not_the_last_render()
    {
        // The poll has applied a new offset that no render has shown yet (renders run every 300 ms): the press
        // steps from it, so its read-back starts from it too, or the pending render would read as its answer.
        using var rig = new ControlsRig();
        Zones(rig.Form, 24, 0, 24, 0);
        rig.Form.Cache.Apply(FenixCabinNames.TempFwdOffset, Num(0.5));

        Click(TemperatureButton(rig.Form, ForwardWarmer));

        WaitFor(() => rig.Gateway.Writes.Count == 1 && Expectations(rig.Form).Length == 1, "the forward press");
        AssertSent(rig.Gateway.Writes[0], Float(FenixCabinNames.TempFwdOffset, 1.0));
        var waiting = Assert.Single(Expectations(rig.Form));
        Assert.Equal("Forward zone: 24.0 degrees, target 24.5 degrees, offset plus 0.5", waiting.PressText);
        Assert.Equal("24.5|0.5", waiting.PressState);

        // The render that shows the 0.5 the press started from is not its answer; the 1.0 is.
        rig.Form.Render();
        Tick(rig.Form);
        Assert.Empty(rig.Speech.All);
        Set(rig.Form, (FenixCabinNames.TempFwdOffset, Num(1.0)));
        Tick(rig.Form);
        Assert.Equal(new[] { "Forward zone: 24.0 degrees, target 25.0 degrees, offset plus 1.0" }, rig.Speech.All);
    }

    [Fact]
    public void A_door_press_waits_for_the_door_not_its_slide()
    {
        // The slide disarms a poll before the door opens: that is not the press's answer, the door opening is,
        // and the whole line is said then.
        using var rig = new ControlsRig();
        Set(rig.Form, (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.Slide("d1l"), True), (FenixCabinNames.DoorControl("d1l"), Num(0)));
        Click(DoorButton(rig.Form));
        WaitFor(() => Expectations(rig.Form).Length == 1, "the door press");

        Set(rig.Form, (FenixCabinNames.Slide("d1l"), False), (FenixCabinNames.DoorControl("d1l"), Num(1)));
        Tick(rig.Form);
        Assert.Empty(rig.Speech.All);

        Set(rig.Form, (FenixCabinNames.Door("d1l"), True), (FenixCabinNames.DoorControl("d1l"), Num(2)));
        Tick(rig.Form);
        Assert.Equal(new[] { "Door 1 left: open, slide disarmed" }, rig.Speech.All);
        Assert.Empty(Expectations(rig.Form));
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void A_cargo_press_says_the_open_line_never_moving()
    {
        using var rig = new ControlsRig();
        Set(rig.Form, (FenixCabinNames.CargoForward, False), (FenixCabinNames.CargoFwdMoving, False));
        SelectDoorLine(rig.Form, DefaultLayout(), "cargo.forward");
        Click(DoorButton(rig.Form));
        WaitFor(() => Expectations(rig.Form).Length == 1, "the cargo press");

        // Moving, and the open flag turning while it moves: still waiting.
        Set(rig.Form, (FenixCabinNames.CargoFwdMoving, True));
        Tick(rig.Form);
        Set(rig.Form, (FenixCabinNames.CargoForward, True));
        Tick(rig.Form);
        Assert.Empty(rig.Speech.All);

        Set(rig.Form, (FenixCabinNames.CargoFwdMoving, False));
        Tick(rig.Form);
        Assert.Equal(new[] { "Forward cargo door: open" }, rig.Speech.All);
        Assert.Empty(Expectations(rig.Form));
    }

    [Fact]
    public void A_temperature_press_waits_for_the_target_not_the_air_temperature()
    {
        using var rig = new ControlsRig();
        Zones(rig.Form, 24, 0, 24, 0);
        Click(TemperatureButton(rig.Form, ForwardWarmer));
        WaitFor(() => Expectations(rig.Form).Length == 1, "the forward press");

        // The measured air temperature drifts: not the press's answer.
        Set(rig.Form, (FenixCabinNames.TempFwdAir, Num(24.3)));
        Tick(rig.Form);
        Assert.Empty(rig.Speech.All);

        Set(rig.Form, (FenixCabinNames.TempFwdOffset, Num(0.5)));
        Tick(rig.Form);
        Assert.Equal(new[] { "Forward zone: 24.3 degrees, target 24.5 degrees, offset plus 0.5" }, rig.Speech.All);
        Assert.Empty(Expectations(rig.Form));
    }

    [Fact]
    public void A_temperature_press_reads_the_zone_line_back_and_a_door_press_reads_the_door_line()
    {
        // Forward warmer: the poll's next read shows the new offset, and the zone's line is what is said.
        using (var rig = new ControlsRig())
        {
            Zones(rig.Form, 24, 0, 24, 0);
            Click(TemperatureButton(rig.Form, ForwardWarmer));
            WaitFor(() => Expectations(rig.Form).Length == 1, "the forward press");

            Set(rig.Form, (FenixCabinNames.TempFwdOffset, Num(0.5)));
            Tick(rig.Form);

            Assert.Equal(new[] { "Forward zone: 24.0 degrees, target 24.5 degrees, offset plus 0.5" }, rig.Speech.All);
            Assert.Empty(rig.Speech.Interrupts);
            Assert.Empty(Expectations(rig.Form));
        }

        // Preselect down: "Water preselect: 50 percent".
        using (var rig = new ControlsRig())
        {
            Set(rig.Form, (FenixCabinNames.WaterPreselect, Num(75)));
            Click(WaterButton(rig.Form, PreselectDown));
            WaitFor(() => Expectations(rig.Form).Length == 1, "the preselect press");

            Set(rig.Form, (FenixCabinNames.WaterPreselect, Num(50)));
            Tick(rig.Form);

            Assert.Equal(new[] { "Water preselect: 50 percent" }, rig.Speech.All);
        }

        // Opening door 1 left: its line says open; the slide is not part of what changed, but of the line.
        using (var rig = new ControlsRig())
        {
            Set(rig.Form, (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.Slide("d1l"), True), (FenixCabinNames.DoorControl("d1l"), Num(0)));
            Click(DoorButton(rig.Form));
            WaitFor(() => Expectations(rig.Form).Length == 1, "the door press");

            Set(rig.Form, (FenixCabinNames.Door("d1l"), True), (FenixCabinNames.DoorControl("d1l"), Num(2)));
            Tick(rig.Form);

            Assert.Equal(new[] { "Door 1 left: open, slide armed" }, rig.Speech.All);
            Assert.Equal("&Close door 1 left", DoorButton(rig.Form).Text);
        }
    }

    [Fact]
    public void The_reset_stops_at_the_first_write_the_Fenix_refuses()
    {
        // The first refused: said once, the aft write never sent, nothing awaited.
        using (var rig = new ControlsRig())
        {
            rig.Gateway.Answer = () => WriteAnswer(accepted: false);

            Click(TemperatureButton(rig.Form, ResetTemperatures));

            WaitFor(() => rig.Speech.Interrupts.Count == 1, "the refusal");
            Thread.Sleep(300);
            Assert.Equal(new[] { NotAcceptedText }, rig.Speech.Interrupts);
            Assert.Single(rig.Gateway.Writes);
            Assert.Empty(Expectations(rig.Form));
        }

        // The second refused: the forward zone is still read back, the failure is said once.
        using (var rig = new ControlsRig())
        {
            rig.Gateway.Answer = () => WriteAnswer(accepted: rig.Gateway.Writes.Count < 2);

            Click(TemperatureButton(rig.Form, ResetTemperatures));

            WaitFor(() => rig.Speech.Interrupts.Count == 1, "the refusal");
            Assert.Equal(new[] { NotAcceptedText }, rig.Speech.Interrupts);
            Assert.Equal(2, rig.Gateway.Writes.Count);
            Assert.Equal(new[] { "temp.forward" }, Expectations(rig.Form).Select(w => w.Key).ToArray());
        }
    }

    [Fact]
    public void A_write_the_Fenix_refuses_is_said_and_nothing_is_read_back()
    {
        var presses = new (string Name, Action<FenixCabinForm> Press)[]
        {
            ("Forward warmer", form => Click(TemperatureButton(form, ForwardWarmer))),
            ("Aft cooler", form => Click(TemperatureButton(form, AftCooler))),
            ("Preselect up", form => Click(WaterButton(form, PreselectUp))),
            ("Door", form => Click(DoorButton(form)))
        };
        foreach (var c in presses)
        {
            using var rig = new ControlsRig();
            Zones(rig.Form, 24, 0, 24, 0);
            Set(rig.Form, (FenixCabinNames.WaterPreselect, Num(75)), (FenixCabinNames.DoorControl("d1l"), Num(0)));
            rig.Gateway.Answer = () => WriteAnswer(accepted: false);

            c.Press(rig.Form);

            WaitFor(() => rig.Speech.Interrupts.Count == 1, c.Name);
            Assert.Equal(new[] { NotAcceptedText }, rig.Speech.Interrupts);
            Assert.Single(rig.Gateway.Writes);
            Assert.Empty(Expectations(rig.Form));
        }
    }
}

// Characterization tests for the Fenix E/WD's window: FenixChannelEwdForm (Forms/Fenix) and the definition's factory for
// it (FenixA320Definition.CreateChannelEwdForm, Aircraft/FenixA320Definition.ChannelEwd.cs). The window's ECAM keys are
// FenixChannelEwdKeysTests'; these tests give the form a press that does nothing. No sim and no socket: a
// monitor that is never started trusts nothing, and one the test feeds by hand trusts what the gate lets it. The window is
// built, never shown (StartLive runs the first open's work without putting anything on screen), as the other display
// windows are tested (FenixChannelPfdWindowTests). The rows themselves are FenixChannelEwdRowsTests'.
//
// The contract: the window shows the rows FenixChannelDisplayForm.RowsFor gives for the E/WD's snapshot with the E/WD's
// composer. While the E/WD is not trusted that is the one status row, "E/WD: not available, <reason>", and the composer
// is not asked; a composer that cannot read the display gives its problem in the same row. The window is titled
// "Fenix E/WD" and its list is named "E/WD display". It is a class of its own, since Fenix windows are tracked one for
// each type. The engine family is not in the channel: the window asks for it at each refresh, on the UI thread, and the
// definition's factory answers with the loaded Fenix's. A problem the composer reports goes to the channel's log under
// the display's name, "E/WD", as one it throws does, once for each distinct line. The window is in the Fenix windows list
// once, as "E/WD", and Alt+E reaches it only through OpenChannelWindow, which opens it while the E/WD is trusted at the
// press, or dark (on its one status row), and otherwise leaves the key to its AI read; the key's seam is a partial
// method a test cannot reach, so its source is read.

using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelEwdWindowTests
{
    private const FenixEngineFamily Iae = FenixEngineFamily.IaeV2500;

    // ---- The rows ---------------------------------------------------------------------------------

    // One engine with the five values the E/WD draws.
    private static FenixChannelProtoWriter Engine(double n1, double n2, double ff, double epr, double egt)
        => new FenixChannelProtoWriter().Double(2, n1).Double(3, n2).Double(4, ff).Double(5, epr).Double(6, egt);

    // The cruise as RemoteData holds it: both engines, the thrust limit "CLB 1.618" (three elements, the last one empty)
    // and the weight unit in kilograms. The values are the ones FenixChannelEwdRowsTests reads.
    private static FenixChannelProtoWriter CruiseRemote()
    {
        var engines = new FenixChannelProtoWriter()
            .Message(1, Engine(81.0801, 86.1461, 1320, 1.4082, 376.7972))
            .Message(2, Engine(81.2561, 86.3052, 1320, 1.407, 378.4421))
            .Message(8, new FenixChannelProtoWriter().String(1, "cCLB"))
            .Message(8, new FenixChannelProtoWriter().String(1, "g1.618"))
            .Message(8, new FenixChannelProtoWriter());

        return new FenixChannelProtoWriter().Message(129, engines).Bool(154, true);
    }

    // A snapshot of the E/WD as the monitor would hand it with this verdict: the RemoteData message, and the frame's FOB
    // line and right upper ECAM line as measured.
    private static FenixChannelSnapshot SnapshotOf(FenixChannelProtoWriter remote, FenixDisplayVerdict verdict)
    {
        FenixChannelPush push = FenixChannelPush.From(remote.ToArray())
            ?? throw new InvalidOperationException("the test's RemoteData is not valid protobuf");
        var frame = new FenixChannelFrame(
            false, [], FenixFdState.Default, [], [], ["gCOMPANY MSG"], 0, 0, 0, "wFOB:   g8140 cs KG")
        {
            EwdLive = true
        };

        return new FenixChannelSnapshot(verdict, frame, push, null, 0, 1);
    }

    private static FenixDisplayVerdict Trusted => new(true, "trusted");

    // What follows the engine block in the cruise: the thrust limit, the FOB line and the right upper ECAM line.
    private static readonly string[] CruiseText =
        ["CLB cyan, 1.618 green", "FOB: white, 8140 green, KG cyan", "COMPANY MSG, green"];

    [Fact]
    public void A_trusted_ewd_shows_the_composed_rows()
    {
        // The window's rows are the composer's, in its order, for the engine family it is given: EPR first on the IAE and
        // N1 first on the CFM.
        FenixChannelSnapshot snapshot = SnapshotOf(CruiseRemote(), Trusted);

        IReadOnlyList<string> iae = FenixChannelDisplayForm.RowsFor(
            FenixChannelEwdRows.DisplayName, snapshot, s => FenixChannelEwdRows.Compose(s, Iae));
        IReadOnlyList<string> cfm = FenixChannelDisplayForm.RowsFor(
            FenixChannelEwdRows.DisplayName, snapshot, s => FenixChannelEwdRows.Compose(s, FenixEngineFamily.Cfm56));

        Assert.Equal(
            new[] { "EPR: 1.408, 1.407", "FF KG/H: 1320, 1320", "EGT °C: 377, 378", "N2 %: 86.1, 86.3", "N1 %: 81.0, 81.2" }
                .Concat(CruiseText),
            iae);
        Assert.Equal(
            new[] { "N1 %: 81.0, 81.2", "EGT °C: 377, 378", "N2 %: 86.1, 86.3", "FF KG/H: 1320, 1320" }.Concat(CruiseText),
            cfm);
    }

    [Fact]
    public void An_untrusted_ewd_shows_one_status_row()
    {
        // The data is all there, and the E/WD is dark: the composer is not asked, so none of it can reach the list.
        FenixChannelSnapshot snapshot = SnapshotOf(CruiseRemote(), new FenixDisplayVerdict(false, "display off"));
        int asked = 0;

        IReadOnlyList<string> rows = FenixChannelDisplayForm.RowsFor(
            FenixChannelEwdRows.DisplayName,
            snapshot,
            s =>
            {
                asked++;
                return FenixChannelEwdRows.Compose(s, Iae);
            });

        Assert.Equal(new[] { "E/WD: not available, display off" }, rows);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void A_wrong_shape_shows_one_status_row()
    {
        // The engine message (129) arrives as a number instead of a nested message. The window shows the composer's
        // problem and the composer reports it, once.
        FenixChannelSnapshot snapshot = SnapshotOf(new FenixChannelProtoWriter().Varint(129, 1), Trusted);
        var reported = new List<string>();

        IReadOnlyList<string> rows = FenixChannelDisplayForm.RowsFor(
            FenixChannelEwdRows.DisplayName, snapshot, s => FenixChannelEwdRows.Compose(s, Iae, reported.Add));

        Assert.Equal(new[] { "E/WD: not available, field 129 has the wrong shape" }, rows);
        Assert.Equal(new[] { "field 129 has the wrong shape" }, reported);
    }

    // ---- The window -------------------------------------------------------------------------------

    private static DisplayListBox ListOf(Control form) => form.Controls.OfType<DisplayListBox>().Single();

    private static string[] ItemsOf(Control form) => ListOf(form).Items.Cast<string>().ToArray();

    // A whole display push that passes the gate (FenixChannelProtoWriter.GoodPush) with the E/WD's fields in the same
    // RemoteData message: the push of a real Fenix, which has both. GoodPush carries the FOB line the rows read.
    private static byte[] PushWith(FenixChannelProtoWriter ewdFields)
    {
        byte[] good = FenixChannelProtoWriter.GoodPush();
        int at = 1;
        while ((good[at] & 0x80) != 0) at++;
        at++;

        byte[] remote = [.. good[at..], .. ewdFields.ToArray()];
        return new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().Raw(remote)).ToArray();
    }

    // A monitor the E/WD trusts: the data call is connected, the build is checked and one good push, with the E/WD's
    // fields, has arrived.
    private static FenixChannelMonitor TrustedEwdMonitor(MonitorTestKit.Lines lines, FenixChannelProtoWriter ewdFields)
    {
        FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.True(
            SpinWait.SpinUntil(
                () => lines.All.Contains($"build {MonitorTestKit.CheckedBuild}: checked"), MonitorTestKit.Generous),
            "the build was never checked. The log: " + lines);

        monitor.OnDataMessage(PushWith(ewdFields));
        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), monitor.VerdictFor(FenixChannelDisplay.Ewd));
        return monitor;
    }

    [Fact]
    public void The_window_is_the_ewds_and_reads_the_engine_family_at_each_refresh()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedEwdMonitor(lines, CruiseRemote());
        FenixEngineFamily family = Iae;
        int reads = 0;
        using var form = new FenixChannelEwdForm(monitor, () =>
        {
            reads++;
            return family;
        }, _ => { });

        Assert.Equal("Fenix E/WD", form.Text);
        Assert.Equal("E/WD display", ListOf(form).AccessibleName);

        form.StartLive();

        string[] rows = ItemsOf(form);
        Assert.Equal("EPR: 1.408, 1.407", rows[0]);
        Assert.Contains("CLB cyan, 1.618 green", rows);
        Assert.Equal(1, reads);

        // The family is the loaded Fenix's, and it can be read after the window opened.
        family = FenixEngineFamily.Cfm56;
        form.RefreshNow();

        Assert.Equal("N1 %: 81.0, 81.2", ItemsOf(form)[0]);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void The_window_shows_one_status_row_while_the_ewd_is_not_trusted()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        int reads = 0;
        using var form = new FenixChannelEwdForm(neverStarted, () =>
        {
            reads++;
            return Iae;
        }, _ => { });

        form.StartLive();

        Assert.Equal(
            new[] { $"E/WD: not available, {neverStarted.VerdictFor(FenixChannelDisplay.Ewd).Reason}" },
            ItemsOf(form));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void A_window_problem_is_logged_under_the_display_name_once()
    {
        // "x" is a format letter the app does not know. The gate does not look in the flap text, so the E/WD stays
        // trusted and the composer is the one that finds it: the window shows the problem and the monitor logs it once
        // however often the window refreshes.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedEwdMonitor(lines, CruiseRemote().String(152, "xCONF"));
        using var form = new FenixChannelEwdForm(monitor, () => Iae, _ => { });

        form.StartLive();
        form.RefreshNow();

        Assert.Equal(new[] { "E/WD: not available, unknown code 'x' in the flap text" }, ItemsOf(form));
        Assert.Single(lines.All, line => line == "E/WD: unknown code 'x' in the flap text");
    }

    [Fact]
    public void The_window_needs_an_engine_family_to_ask()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());

        Assert.Throws<ArgumentNullException>(() => new FenixChannelEwdForm(neverStarted, null!, _ => { }));
    }

    [Fact]
    public void The_window_is_its_own_class_so_the_ewd_has_a_window_of_its_own()
    {
        // Fenix windows are tracked one for each type: a display that shared the base class would share its window.
        Assert.NotEqual(typeof(FenixChannelDisplayForm), typeof(FenixChannelEwdForm));
        Assert.True(typeof(FenixChannelEwdForm).IsSubclassOf(typeof(FenixChannelDisplayForm)));
    }

    // ---- The definition's factory -----------------------------------------------------------------

    [Fact]
    public void The_definition_makes_the_ewds_window_on_the_loaded_fenixs_engines()
    {
        // No Fenix has published its config here, so the family is not read: the window shows its status row in the
        // engine block's place and the rest as it is, which is what a family read from Variant gives.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedEwdMonitor(lines, CruiseRemote());
        var definition = new FenixA320Definition();
        Assert.Equal(FenixEngineFamily.Unknown, definition.Variant.Engine);

        using FenixChannelEwdForm form = definition.CreateChannelEwdForm(monitor, null!, null!);
        form.StartLive();

        Assert.Equal("Fenix E/WD", form.Text);
        Assert.Equal(
            new[] { "Engines: not available, engine type not read yet", "CLB cyan, 1.618 green" },
            ItemsOf(form).Take(2));
    }

    // ---- The way in -------------------------------------------------------------------------------

    [Fact]
    public void The_ewd_window_is_listed_once()
    {
        // The Fenix windows list speaks "Name. Description": one E/WD entry, named for the display as the others are.
        FenixWindowEntry entry = Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "E/WD");

        Assert.Equal(
            "Engines, thrust limit, fuel on board, flaps, warnings and memos, as the Fenix's E/WD shows them",
            entry.Description);
        Assert.NotNull(entry.Open);
    }

    // Alt+E has two ways to answer and only one may open the window. The key opens it when the E/WD is trusted at the
    // press, or dark, and otherwise leaves the key to its AI read, with no extra words: that is OpenChannelWindow's work
    // (judged on the monitor's snapshot, both hotkey modes left first), so the seam may do nothing but hand it the
    // window. The seam is a partial method nothing in a test can reach, so its source is read.
    [Fact]
    public void The_ewd_key_opens_its_window_only_through_OpenChannelWindow()
    {
        string source = Comment.Replace(
            File.ReadAllText(Path.Combine(
                ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.ChannelEwd.cs")),
            "");

        Match key = Regex.Match(
            source,
            @"partial\s+void\s+ChannelEwdKey\s*\(\s*Hotkeys\.HotkeyAction\s+action\s*,\s*ChannelKeyContext\s+ctx\s*,"
            + @"\s*ref\s+bool\s+handled\s*\)\s*(?:=>\s*(?<body>[^;]*;)|\{\s*(?<body>[^;]*;)\s*\})");
        Assert.True(key.Success, "FenixA320Definition.ChannelEwd.cs implements no ChannelEwdKey of the seam's shape");

        // The body is one assignment, and it names the E/WD's verdict and its name. Compared without whitespace, so that
        // where the line breaks is no part of it.
        Assert.Equal(
            WithoutWhitespace(
                "handled = OpenChannelWindow(ctx, Services.FenixChannelDisplay.Ewd, Services.FenixChannelEwdRows.DisplayName,"
                + " monitor => CreateChannelEwdForm(monitor, ctx.SimConnect, ctx.Announcer));"),
            WithoutWhitespace(key.Groups["body"].Value));
    }

    private static string WithoutWhitespace(string text) => Regex.Replace(text, @"\s+", "");

    // A line comment or a block comment: a call named in a comment is no call.
    private static readonly Regex Comment = new(
        @"//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);
}

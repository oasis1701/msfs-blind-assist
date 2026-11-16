using System.Globalization;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The bbs757 layout records serialise and load back unchanged through the generic layout file format,
/// byte-identical under any culture (WASM-21), and a damaged file is refused (WASM-26).</summary>
public class Bbs757LayoutFileTests
{
    private static readonly ModuleDescription Module = Bbs757Module.Description;
    private const string Hash0 = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string Hash1 = "1111111111111111111111111111111111111111111111111111111111111111";

    /// <summary>The sample FMA and EFB layouts, for the tests that build a whole 757 file.</summary>
    public static Bbs757FmaLayout SampleFma() => SampleLayout();
    public static Bbs757EfbMemLayout SampleEfb() => SampleEfbLayout();

    private static Bbs757FmaLayout SampleLayout() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        PfdVptr: 0x1406c,
        PfdToAfds: 612,
        PfdToTmc: 616,
        Pitch: 11924,
        PitchArmed: 11944,
        Roll: 11932,
        RollArmed: 11940,
        AtMode: 444,
        AtWords: new Bbs757WordTable(new Dictionary<int, string>
        {
            [1] = "SPD", [2] = "EPR", [3] = "THR HLD", [4] = "IDLE", [5] = "FLCH", [6] = "GA",
            [7] = "FLAP LIM", [8] = "ALPHA", [9] = "SPD LIM",
        }),
        PitchWords: new Bbs757WordTable(new Dictionary<int, string>
        {
            [1] = "TO", [2] = "ALT HOLD", [3] = "V/S", [4] = "SPD", [6] = "VNAV PTH", [7] = "VNAV SPD",
            [9] = "G/S", [10] = "FLARE", [11] = "ALT CAP", [12] = "GA", [13] = "SPD LIM", [14] = "FLAP LIM",
        }),
        PitchArmedWords: new Bbs757WordTable(new Dictionary<int, string> { [5] = "VNAV", [9] = "G/S", [10] = "FLARE" }),
        RollWords: new Bbs757WordTable(new Dictionary<int, string>
        {
            [1] = "HDG HOLD", [2] = "HDG SEL", [3] = "LOC", [4] = "ROLLOUT", [5] = "LNAV", [6] = "B/CRS",
            [8] = "TO", [9] = "GA",
        }),
        RollArmedWords: new Bbs757WordTable(new Dictionary<int, string> { [3] = "LNAV", [4] = "LOC", [5] = "B/CRS", [6] = "ROLLOUT" }),
        Status: 11948,
        StatusWords: new Bbs757WordTable(new Dictionary<int, string> { [1] = "CMD" }),
        FdWord: "FD",
        FdCaptain: 11957,
        FdFirstOfficer: 11958,
        AfdsToMcp: 11848,
        McpAltitude: 856,
        McpHeading: 848,
        McpValueType: Bbs757McpValueType.F64);

    private static Bbs757EfbMemLayout SampleEfbLayout() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        EfbVptr: 0x13128,
        EfbToBbos: 132,
        BbosToAppMap: 0x238,
        AppFront: 0x140,
        AppPage: 0x144,
        AppRoots: 0x17c,
        PageChildren: 0x4c,
        CtrlName: 0x1c,
        CtrlRect: 0x38,
        CtrlVisible: 0x8c,
        CtrlChildren: 0xf4,
        CtrlParent: 0xe8,
        CtrlLabelText: 0x128,
        CtrlButtonText: 0x144,
        ScrollboxOffset: 0x134,
        SliderKnob: 0x184,
        // The phase 2 fields, measured live 2026-11-07.
        TextboxLabel: 0x128,
        MultilineText: 0x128,
        PulldownFace: 0x128,
        PulldownList: 0x130,
        PulldownEntries: 0x138,
        PulldownEntryText: 0x0c,
        PulldownOpen: 0x154,
        ButtonSelectable: 0x140,
        ButtonUnselected: 0x143,
        BbosModalOpen: 0x1d8,
        BbosNotices: 0x9334,
        NoticeStride: 0x3c,
        NoticeId: 0x00,
        NoticeTitle: 0x04,
        NoticeText: 0x10,
        NoticeTime: 0x1c,
        NoticeUnread: 0x34,
        // The top bar app's notification panel, measured live 2026-11-07 (the evidence's "The top bar").
        OverlayPanelShown: 0x1fc,
        OverlayListOpen: 0x1fd,
        OverlayPanelRect: 0x234,
        OverlayPanelHeight: 0x240,
        // The top bar's flight-time text (a std::string, "DEP 00:28"), measured live 2026-11-07.
        OverlayFlightTimeText: 0x1b4,
        // The top bar's boarding box and BBOS's loading state, decoded from BBS752.wasm and read live 2026-11-07.
        OverlayBoardingRect: 0x214,
        BbosLoadState: 0x9160,
        BbosLoadDeboarding: 0x9164,
        BbosLoadFlown: 0x9167,
        BbosLoadRemaining: 0x9278,
        // The keyboard app's state and a text box's caret, maximum length and keypad flag, measured live 2026-11-07.
        KeyboardShown: 0x1ac,
        KeyboardReady: 0x1ad,
        KeyboardKeypad: 0x1ae,
        KeyboardTarget: 0x1ec,
        KeypadRect: 0x1d8,
        KeyboardNumeric: 0x1e8,
        KeyboardShift: 0x1e9,
        KeyboardCaps: 0x1ea,
        TextboxCaret: 0x1ac,
        TextboxMaxLength: 0x1bc,
        TextboxKeypad: 0x1cc,
        // A toggle control's own byte: 1 on, 0 off (measured live 2026-11-07).
        ToggleOn: 0x188,
        ClassVptrs: new[]
        {
            new Bbs757EfbClassVptr("scrollbox", 0x30400),
            new Bbs757EfbClassVptr("button", 0x30164), new Bbs757EfbClassVptr("label", 0x30cd4),
            new Bbs757EfbClassVptr("toggle", 0x30e7c), new Bbs757EfbClassVptr("slider", 0x30c48),
            new Bbs757EfbClassVptr("grid", 0x30320),
            new Bbs757EfbClassVptr("textbox", 0x30dec), new Bbs757EfbClassVptr("multiline", 0x305e4),
            new Bbs757EfbClassVptr("pulldown", 0x307a0), new Bbs757EfbClassVptr("image", 0x303ac),
            new Bbs757EfbClassVptr("HOME", 0x27bdc), new Bbs757EfbClassVptr("SETTINGS", 0x2c860),
            new Bbs757EfbClassVptr("overlay", 0x28c84), new Bbs757EfbClassVptr("modal", 0x27df0),
            new Bbs757EfbClassVptr("keyboard", 0x26f18),
        });

    /// <summary>The phase 2 fields as the file writes them (camelCase name, decimal value), in the record's order.</summary>
    private static readonly (string Name, int Value)[] Phase2Fields =
    {
        ("textboxLabel", 0x128), ("multilineText", 0x128), ("pulldownFace", 0x128), ("pulldownList", 0x130),
        ("pulldownEntries", 0x138), ("pulldownEntryText", 0x0c), ("pulldownOpen", 0x154),
        ("buttonSelectable", 0x140), ("buttonUnselected", 0x143), ("bbosModalOpen", 0x1d8), ("bbosNotices", 0x9334),
        ("noticeStride", 0x3c), ("noticeId", 0x00), ("noticeTitle", 0x04), ("noticeText", 0x10), ("noticeTime", 0x1c),
        ("noticeUnread", 0x34),
    };

    /// <summary>The notification panel's fields as the file writes them, in the record's order.</summary>
    private static readonly (string Name, int Value)[] PanelFields =
    {
        ("overlayPanelShown", 0x1fc), ("overlayListOpen", 0x1fd), ("overlayPanelRect", 0x234), ("overlayPanelHeight", 0x240),
        ("overlayFlightTimeText", 0x1b4), ("overlayBoardingRect", 0x214), ("bbosLoadState", 0x9160), ("bbosLoadDeboarding", 0x9164),
        ("bbosLoadFlown", 0x9167), ("bbosLoadRemaining", 0x9278),
    };

    /// <summary>The keyboard app's fields and a text box's typing fields as the file writes them, in the record's order.</summary>
    private static readonly (string Name, int Value)[] KeyboardFields =
    {
        ("keyboardShown", 0x1ac), ("keyboardReady", 0x1ad), ("keyboardKeypad", 0x1ae), ("keyboardTarget", 0x1ec),
        ("keypadRect", 0x1d8), ("keyboardNumeric", 0x1e8), ("keyboardShift", 0x1e9), ("keyboardCaps", 0x1ea),
        ("textboxCaret", 0x1ac), ("textboxMaxLength", 0x1bc), ("textboxKeypad", 0x1cc),
        ("toggleOn", 0x188),
    };

    private static string WithoutField(string text, string name, int value) =>
        System.Text.RegularExpressions.Regex.Replace(text, $"\\s*\"{name}\": {value.ToString(CultureInfo.InvariantCulture)},?", "");

    private static LayoutFile FileWith(Bbs757FmaLayout layout) => FileWith(layout, SampleEfbLayout());

    private static LayoutFile FileWith(Bbs757FmaLayout fma, Bbs757EfbMemLayout efb) => new(
        GeneratorVersion: 1,
        CodeHash: Hash0,
        DataRanges: new[] { new DataRange(0x400, 64) },
        DataHash: Hash1,
        Searches: Array.Empty<DataSearch>(),
        Readers: new[]
        {
            new ReaderResult(Bbs757Module.Fma, fma, null), new ReaderResult(Bbs757Module.Efb, efb, null),
            new ReaderResult(Bbs757Module.LowerEicas, Bbs757LowerEicasLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.UpperEicas, Bbs757UpperEicasLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.Ehsi, Bbs757EhsiLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.Adi, Bbs757AdiLayoutTests.Sample(), null),
        });

    [Fact]
    public void A_fma_layout_round_trips_through_the_format()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757FmaLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Fma).Layout!;
        Assert.Equal(SampleLayout() with { SourceFile = "" }, loaded);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
    }

    [Fact]
    public void An_efb_layout_round_trips_through_the_format()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(SampleEfbLayout() with { SourceFile = "" }, loaded);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
    }

    [Fact]
    public void The_efb_layout_with_the_scroll_offset_round_trips()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        // The content offset (+0x134, B757-26), never the scrollbar thumb at +0x158.
        Assert.Contains("\"scrollboxOffset\": 308", text);
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(0x134, loaded.ScrollboxOffset);
        Assert.Equal("scrollbox", loaded.KindFor(0x30400));
    }

    [Fact]
    public void A_layout_without_the_scroll_offset_reads_as_missing()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        string damaged = System.Text.RegularExpressions.Regex.Replace(text, "\\s*\"scrollboxOffset\": 308,?", "");
        Assert.NotEqual(text, damaged);
        Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }

    [Fact]
    public void The_efb_layout_with_the_slider_knob_round_trips()
    {
        // A slider keeps its knob's rectangle at +0x184 (four floats, measured live 2026-11-07).
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        Assert.Contains("\"sliderKnob\": 388", text);
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(0x184, loaded.SliderKnob);
        Assert.NotEqual(SampleEfbLayout(), SampleEfbLayout() with { SliderKnob = 0x188 });
        Assert.Contains(Bbs757LayoutDescriptions.DescribeEfb(loaded), line => line.Contains("sliderKnob 388", StringComparison.Ordinal));
    }

    [Fact]
    public void A_layout_without_the_slider_knob_reads_as_missing()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        string damaged = System.Text.RegularExpressions.Regex.Replace(text, "\\s*\"sliderKnob\": 388,?", "");
        Assert.NotEqual(text, damaged);
        Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }

    [Fact]
    public void The_efb_layout_with_the_phase_2_fields_round_trips()
    {
        // Text boxes, drop-downs, the selected entry, the dialog flag and the notification records (measured live 2026-11-07).
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        foreach (var (name, value) in Phase2Fields)
            Assert.Contains($"\"{name}\": {value.ToString(CultureInfo.InvariantCulture)}", text);
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(SampleEfbLayout() with { SourceFile = "" }, loaded);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));

        Assert.Equal(0x128, loaded.TextboxLabel);
        Assert.Equal(0x128, loaded.MultilineText);
        Assert.Equal(0x128, loaded.PulldownFace);
        Assert.Equal(0x130, loaded.PulldownList);
        Assert.Equal(0x138, loaded.PulldownEntries);
        Assert.Equal(0x0c, loaded.PulldownEntryText);
        Assert.Equal(0x154, loaded.PulldownOpen);
        Assert.Equal(0x140, loaded.ButtonSelectable);
        Assert.Equal(0x143, loaded.ButtonUnselected);
        Assert.Equal(0x1d8, loaded.BbosModalOpen);
        Assert.Equal(0x9334, loaded.BbosNotices);
        Assert.Equal(0x3c, loaded.NoticeStride);
        Assert.Equal(0x00, loaded.NoticeId);
        Assert.Equal(0x04, loaded.NoticeTitle);
        Assert.Equal(0x10, loaded.NoticeText);
        Assert.Equal(0x1c, loaded.NoticeTime);
        Assert.Equal(0x34, loaded.NoticeUnread);
        foreach (var (kind, vptr) in new[] { ("textbox", 0x30decu), ("multiline", 0x305e4u), ("pulldown", 0x307a0u),
                     ("image", 0x303acu), ("overlay", 0x28c84u), ("modal", 0x27df0u) })
            Assert.Equal(kind, loaded.KindFor(vptr));

        // Equality and the hash see every phase 2 field.
        var sample = SampleEfbLayout();
        foreach (var changed in new[]
                 {
                     sample with { TextboxLabel = 1 }, sample with { MultilineText = 1 }, sample with { PulldownFace = 1 },
                     sample with { PulldownList = 1 }, sample with { PulldownEntries = 1 }, sample with { PulldownEntryText = 1 },
                     sample with { PulldownOpen = 1 }, sample with { ButtonSelectable = 1 }, sample with { ButtonUnselected = 1 },
                     sample with { BbosModalOpen = 1 }, sample with { BbosNotices = 1 }, sample with { NoticeStride = 1 },
                     sample with { NoticeId = 1 }, sample with { NoticeTitle = 1 }, sample with { NoticeText = 1 },
                     sample with { NoticeTime = 1 }, sample with { NoticeUnread = 1 },
                 })
        {
            Assert.NotEqual(sample, changed);
            Assert.NotEqual(sample.GetHashCode(), changed.GetHashCode());
        }

        string described = string.Join("\n", Bbs757LayoutDescriptions.DescribeEfb(loaded));
        foreach (string part in new[] { "textboxLabel 296", "multilineText 296", "pulldownFace 296", "pulldownList 304",
                     "pulldownEntries 312", "pulldownEntryText 12", "pulldownOpen 340", "buttonSelectable 320",
                     "buttonUnselected 323", "bbosModalOpen 472", "bbosNotices 37684", "noticeStride 60", "noticeId 0",
                     "noticeTitle 4", "noticeText 16", "noticeTime 28", "noticeUnread 52", "overlay=0x28c84", "modal=0x27df0" })
            Assert.Contains(part, described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_layout_without_the_phase_2_fields_reads_as_missing()
    {
        // A file plan 20's generator wrote has none of them: it is refused, never read with zeros (WASM-26).
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        string old = text;
        foreach (var (name, value) in Phase2Fields) old = WithoutField(old, name, value);
        Assert.DoesNotContain("noticeUnread", old, StringComparison.Ordinal);
        Assert.Contains("\"sliderKnob\": 388", old, StringComparison.Ordinal);
        Assert.False(LayoutFileFormat.TryRead(Module, old, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));

        // Nor is a file that lacks any one of them.
        foreach (var (name, value) in Phase2Fields)
        {
            string damaged = WithoutField(text, name, value);
            Assert.NotEqual(text, damaged);
            Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? one), name);
            Assert.False(string.IsNullOrEmpty(one), name);
        }
    }

    [Fact]
    public void The_efb_layout_with_the_notification_panel_round_trips()
    {
        // BlueBird draws its notification pop-up and list from the top bar app's own fields (measured live 2026-11-07): the shown byte,
        // the list byte, the panel's rectangle and its height.
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        foreach (var (name, value) in PanelFields)
            Assert.Contains($"\"{name}\": {value.ToString(CultureInfo.InvariantCulture)}", text);
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(SampleEfbLayout() with { SourceFile = "" }, loaded);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
        Assert.Equal((0x1fc, 0x1fd, 0x234, 0x240, 0x1b4), (loaded.OverlayPanelShown, loaded.OverlayListOpen, loaded.OverlayPanelRect, loaded.OverlayPanelHeight, loaded.OverlayFlightTimeText));
        Assert.Equal((0x214, 0x9160, 0x9164, 0x9167, 0x9278),
            (loaded.OverlayBoardingRect, loaded.BbosLoadState, loaded.BbosLoadDeboarding, loaded.BbosLoadFlown, loaded.BbosLoadRemaining));
        Assert.True(text.IndexOf("\"overlayPanelHeight\"", StringComparison.Ordinal) < text.IndexOf("\"overlayFlightTimeText\"", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\"overlayFlightTimeText\"", StringComparison.Ordinal) < text.IndexOf("\"overlayBoardingRect\"", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\"bbosLoadFlown\"", StringComparison.Ordinal) < text.IndexOf("\"bbosLoadRemaining\"", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\"noticeUnread\"", StringComparison.Ordinal) < text.IndexOf("\"overlayPanelShown\"", StringComparison.Ordinal));

        var sample = SampleEfbLayout();
        foreach (var changed in new[]
                 {
                     sample with { OverlayPanelShown = 1 }, sample with { OverlayListOpen = 1 }, sample with { OverlayPanelRect = 1 },
                     sample with { OverlayPanelHeight = 1 }, sample with { OverlayFlightTimeText = 1 },
                     sample with { OverlayBoardingRect = 1 }, sample with { BbosLoadState = 1 }, sample with { BbosLoadDeboarding = 1 },
                     sample with { BbosLoadFlown = 1 }, sample with { BbosLoadRemaining = 1 },
                 })
        {
            Assert.NotEqual(sample, changed);
            Assert.NotEqual(sample.GetHashCode(), changed.GetHashCode());
        }

        string described = string.Join("\n", Bbs757LayoutDescriptions.DescribeEfb(loaded));
        foreach (string part in new[] { "overlayPanelShown 508", "overlayListOpen 509", "overlayPanelRect 564", "overlayPanelHeight 576", "overlayFlightTimeText 436",
                     "overlayBoardingRect 532", "bbosLoadState 37216", "bbosLoadDeboarding 37220", "bbosLoadFlown 37223", "bbosLoadRemaining 37496" })
            Assert.Contains(part, described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_layout_without_the_notification_panel_reads_as_missing()
    {
        // The v3 file (plan 21 before the final review) has none of them: refused, never read with zeros (WASM-26); each alone too.
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        string old = text;
        foreach (var (name, value) in PanelFields) old = WithoutField(old, name, value);
        Assert.Contains("\"noticeUnread\": 52", old, StringComparison.Ordinal);
        Assert.False(LayoutFileFormat.TryRead(Module, old, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
        foreach (var (name, value) in PanelFields)
        {
            string damaged = WithoutField(text, name, value);
            Assert.NotEqual(text, damaged);
            Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? one), name);
            Assert.False(string.IsNullOrEmpty(one), name);
        }
    }

    [Fact]
    public void The_efb_layout_with_the_keyboard_fields_round_trips()
    {
        // The keyboard app's state (shown, ready, keypad, target, the keypad's rectangle, its three layer bytes) and a text box's
        // caret, maximum length and keypad flag, measured live 2026-11-07; the keyboard app is
        // a class of its own (N3bbs12app_keyboardE).
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        foreach (var (name, value) in KeyboardFields)
            Assert.Contains($"\"{name}\": {value.ToString(CultureInfo.InvariantCulture)}", text);
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(SampleEfbLayout() with { SourceFile = "" }, loaded);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
        Assert.Equal((0x1ac, 0x1ad, 0x1ae, 0x1ec, 0x1d8, 0x1e8, 0x1e9, 0x1ea),
            (loaded.KeyboardShown, loaded.KeyboardReady, loaded.KeyboardKeypad, loaded.KeyboardTarget, loaded.KeypadRect,
                loaded.KeyboardNumeric, loaded.KeyboardShift, loaded.KeyboardCaps));
        Assert.Equal((0x1ac, 0x1bc, 0x1cc), (loaded.TextboxCaret, loaded.TextboxMaxLength, loaded.TextboxKeypad));
        Assert.Equal(0x188, loaded.ToggleOn); // a toggle's own byte, after TextboxKeypad
        Assert.Equal("keyboard", loaded.KindFor(0x26f18));
        Assert.Equal("keyboard", loaded.ClassVptrs[^1].Kind); // after modal
        Assert.Equal("modal", loaded.ClassVptrs[^2].Kind);
        // In the record's order, after BBOS's loading record.
        Assert.True(text.IndexOf("\"bbosLoadRemaining\"", StringComparison.Ordinal) < text.IndexOf("\"keyboardShown\"", StringComparison.Ordinal));
        for (int i = 1; i < KeyboardFields.Length; i++)
            Assert.True(text.IndexOf($"\"{KeyboardFields[i - 1].Name}\"", StringComparison.Ordinal)
                < text.IndexOf($"\"{KeyboardFields[i].Name}\"", StringComparison.Ordinal), KeyboardFields[i].Name);
        Assert.True(text.IndexOf("\"textboxKeypad\"", StringComparison.Ordinal) < text.IndexOf("\"classVptrs\"", StringComparison.Ordinal));

        // Equality and the hash see every keyboard field.
        var sample = SampleEfbLayout();
        foreach (var changed in new[]
                 {
                     sample with { KeyboardShown = 1 }, sample with { KeyboardReady = 1 }, sample with { KeyboardKeypad = 1 },
                     sample with { KeyboardTarget = 1 }, sample with { KeypadRect = 1 }, sample with { KeyboardNumeric = 1 },
                     sample with { KeyboardShift = 1 }, sample with { KeyboardCaps = 1 }, sample with { TextboxCaret = 1 },
                     sample with { TextboxMaxLength = 1 }, sample with { TextboxKeypad = 1 },
                     sample with { ToggleOn = 1 },
                 })
        {
            Assert.NotEqual(sample, changed);
            Assert.NotEqual(sample.GetHashCode(), changed.GetHashCode());
        }

        var line = Assert.Single(Bbs757LayoutDescriptions.DescribeEfb(loaded), l => l.Contains("EFB keyboard:", StringComparison.Ordinal));
        foreach (string part in new[] { "keyboardShown 428", "keyboardReady 429", "keyboardKeypad 430", "keyboardTarget 492", "keypadRect 472",
                     "keyboardNumeric 488", "keyboardShift 489", "keyboardCaps 490", "textboxCaret 428", "textboxMaxLength 444", "textboxKeypad 460", "toggleOn 392" })
            Assert.Contains(part, line, StringComparison.Ordinal);
        Assert.Contains("keyboard=0x26f18", string.Join("\n", Bbs757LayoutDescriptions.DescribeEfb(loaded)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_layout_without_the_keyboard_fields_reads_as_missing()
    {
        // The file plan 21's generator wrote has none of them: refused, never read with zeros (WASM-26); each alone too.
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        string old = text;
        foreach (var (name, value) in KeyboardFields) old = WithoutField(old, name, value);
        Assert.DoesNotContain("\"keyboardShown\"", old, StringComparison.Ordinal);
        Assert.DoesNotContain("\"textboxKeypad\"", old, StringComparison.Ordinal);
        Assert.Contains("\"bbosLoadRemaining\": 37496", old, StringComparison.Ordinal);
        Assert.False(LayoutFileFormat.TryRead(Module, old, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
        foreach (var (name, value) in KeyboardFields)
        {
            string damaged = WithoutField(text, name, value);
            Assert.NotEqual(text, damaged);
            Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? one), name);
            Assert.False(string.IsNullOrEmpty(one), name);
        }
    }

    [Fact]
    public void A_layout_with_the_gate_fields_round_trips()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757FmaLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Fma).Layout!;
        Assert.Equal(SampleLayout() with { SourceFile = "" }, loaded);
        Assert.Equal("CMD", loaded.StatusWords.Lookup(1));
        Assert.Equal("FD", loaded.FdWord);
        Assert.Equal(Bbs757McpValueType.F64, loaded.McpValueType);
        Assert.NotEqual(SampleLayout(), SampleLayout() with { McpValueType = Bbs757McpValueType.I32 });
        Assert.NotEqual(SampleLayout(), SampleLayout() with { FdWord = "F/D" });
        Assert.NotEqual(SampleLayout(), SampleLayout() with
        {
            StatusWords = new Bbs757WordTable(new Dictionary<int, string> { [1] = "CWS" }),
        });
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            Assert.Equal(text, TestCulture.Run(culture, () => LayoutFileFormat.Write(Module, FileWith(SampleLayout()))));
        Assert.Contains(Bbs757LayoutDescriptions.Describe(loaded), line => line.Contains("afdsToMcp 11848", StringComparison.Ordinal));
    }

    [Fact]
    public void A_layout_without_the_fma_gate_fields_reads_as_missing()
    {
        // The generator-3 text: a file written before the gate fields has none of them, so it is refused whole for
        // every 757 reader, the EFB's included, never read with zeros (WASM-26). The members are removed from the parsed
        // file, so the text stays JSON and is refused for the missing member, never for its syntax.
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        string[] gateFields = { "status", "statusWords", "fdWord", "fdCaptain", "fdFirstOfficer", "afdsToMcp", "mcpAltitude", "mcpHeading", "mcpValueType" };
        string old = WithoutFmaMembers(text, gateFields);
        Assert.DoesNotContain("\"statusWords\"", old, StringComparison.Ordinal);
        // Not the whole text: the EHSI's layout has an mcpHeading member of its own.
        Assert.False(System.Text.Json.Nodes.JsonNode.Parse(old)!["readers"]![Bbs757Module.Fma]!["layout"]!.AsObject().ContainsKey("mcpHeading"));
        Assert.Contains("\"rollArmedWords\"", old, StringComparison.Ordinal);
        AssertRefusedForAMissingMember(old, gateFields);
        // Each alone too.
        foreach (string name in gateFields)
        {
            string damaged = WithoutFmaMembers(text, name);
            Assert.NotEqual(text, damaged);
            AssertRefusedForAMissingMember(damaged, name);
        }
    }

    /// <summary>The file with <paramref name="names"/> removed from the FMA's layout object, still well-formed JSON.</summary>
    private static string WithoutFmaMembers(string text, params string[] names)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        var layout = root["readers"]![Bbs757Module.Fma]!["layout"]!.AsObject();
        foreach (string name in names) Assert.True(layout.Remove(name), name);
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The text parses as JSON, and the format refuses it naming one of <paramref name="names"/> as missing.</summary>
    private static void AssertRefusedForAMissingMember(string text, params string[] names)
    {
        using (System.Text.Json.JsonDocument.Parse(text)) { }   // still JSON: not refused for its syntax
        Assert.False(LayoutFileFormat.TryRead(Module, text, out _, out string? failure));
        Assert.DoesNotContain("not a layout file", failure, StringComparison.Ordinal);
        Assert.True(names.Any(n => failure.Contains($"'{n}'", StringComparison.Ordinal)),
            $"the failure names none of {string.Join(", ", names)}: {failure}");
    }

    [Fact]
    public void The_text_is_the_same_under_sv_SE_and_de_DE()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "sv-SE", "de-DE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.Equal(text, LayoutFileFormat.Write(Module, FileWith(SampleLayout())));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void A_missing_member_is_refused()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        // Drop the atMode field: the loader must refuse rather than read it as 0 (WASM-26).
        string damaged = text.Replace("\"atMode\": 444,\n", "");
        Assert.NotEqual(text, damaged);
        Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }
}

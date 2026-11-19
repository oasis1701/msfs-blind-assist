using System.Buffers.Binary;
using System.Text;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The EFB memory reader walks a synthetic BBOS image — gauge manager, EFB, BBOS app map, one front app
/// with a page — and returns the visible control tree with libc++ std::string text and rectangles.</summary>
public class Bbs757EfbMemoryTests
{
    private const ulong B = 0x40000000;
    private const uint Manager = 0x1000, Efb = 0x2000, Bbos = 0x3000, MapNode = 0x3400, AppVec = 0x3480,
        App = 0x4000, RootsVec = 0x4400, Page = 0x5000, PageVec = 0x5400,
        Grid = 0x6000, GridChildrenVec = 0x6400, Button = 0x7000, ValueLabel = 0x8000, HiddenBtn = 0x9000;
    private const uint EfbVptr = 0x13128, ButtonVptr = 0x30164, LabelVptr = 0x30cd4, GridVptr = 0x30320, ScrollboxVptr = 0x30400,
        SliderVptr = 0x30c48;
    // The fake's own vptrs for plan 21's kinds; a stack is no kind the layout names, so it reads "other".
    private const uint TextboxVptr = 0x30d00, MultilineVptr = 0x30500, PulldownVptr = 0x30700, ImageVptr = 0x30300,
        OverlayVptr = 0x28c00, ModalVptr = 0x27d00, StackVptr = 0x30d60, KeyboardVptr = 0x26f00, ToggleVptr = 0x30e00;
    // The keyboard app (key 2) beside the page's app in the map, with its kb_grid and one key; an object whose fields past +0x1a0 lie
    // past the fake's memory (a torn read).
    private const uint KbNode = 0x15000, KbAppVec = 0x15080, KbApp = 0x15100, KbRoots = 0x15400, KbGrid = 0x15600, KbGridVec = 0x15800,
        KbKey = 0x15A00, EdgeObject = 0x1B0000 - 0x1a0;
    private const uint Outside = 0xA000, InnerVec = 0xA200, Inner = 0xA300;
    // A text box's label, a drop-down's face, list, stack and entries, the entry buttons and records.
    private const uint TextLabel = 0xD000, Face = 0xD200, List = 0xD400, ListVec = 0xD600, Stack = 0xD700, StackVec = 0xD900,
        EntriesVec = 0xDA00, Items = 0x10000, Records = 0x11000;
    // The top bar's app (key 1) and the dialog app (key 3), beside the page's app in the map.
    private const uint BarNode = 0xE000, BarAppVec = 0xE080, BarApp = 0xE100, BarRoots = 0xE400, BarGrid = 0x12000, BarGridVec = 0x12200,
        TimeLabel = 0x12400, ResetButton = 0x12600;
    private const uint DialogNode = 0xE800, DialogAppVec = 0xE880, DialogApp = 0xE900, DialogPage = 0x13000, DialogPageVec = 0x13400,
        DialogGrid = 0x13600, DialogGridVec = 0x13800, Prompt = 0x13A00, YesButton = 0x13C00;
    private const uint NoticeRecords = 0x14000, Heap = 0x18000, HeapEnd = 0x40000;
    // A text past the long cap (1 MiB and a byte); then a wide or deep tree of distinct controls.
    private const uint BigText = 0x40000, TreeBase = 0x150000;

    private static readonly Bbs757EfbMemLayout Layout = new(
        SourceFile: "", AnchorAddress: 0x400, Anchor: new byte[] { 1 },
        ManagerBase: Manager, ManagerSpan: 0x100, EfbVptr: EfbVptr, EfbToBbos: 132, BbosToAppMap: 0x238,
        AppFront: 0x140, AppPage: 0x144, AppRoots: 0x17c, PageChildren: 0x4c,
        CtrlName: 0x1c, CtrlRect: 0x38, CtrlVisible: 0x8c, CtrlChildren: 0xf4, CtrlParent: 0xe8,
        CtrlLabelText: 0x128, CtrlButtonText: 0x144, ScrollboxOffset: 0x134, SliderKnob: 0x184,
        TextboxLabel: 0x128, MultilineText: 0x128, PulldownFace: 0x128, PulldownList: 0x130, PulldownEntries: 0x138,
        PulldownEntryText: 0x0c, PulldownOpen: 0x154, ButtonSelectable: 0x140, ButtonUnselected: 0x143,
        BbosModalOpen: 0x1d8, BbosNotices: 0x9334, NoticeStride: 0x3c, NoticeId: 0x00, NoticeTitle: 0x04,
        NoticeText: 0x10, NoticeTime: 0x1c, NoticeUnread: 0x34,
        OverlayPanelShown: 0x1fc, OverlayListOpen: 0x1fd, OverlayPanelRect: 0x234, OverlayPanelHeight: 0x240, OverlayFlightTimeText: 0x1b4,
        OverlayBoardingRect: 0x214, BbosLoadState: 0x9160, BbosLoadDeboarding: 0x9164, BbosLoadFlown: 0x9167, BbosLoadRemaining: 0x9278,
        KeyboardShown: 0x1ac, KeyboardReady: 0x1ad, KeyboardKeypad: 0x1ae, KeyboardTarget: 0x1ec, KeypadRect: 0x1d8,
        KeyboardNumeric: 0x1e8, KeyboardShift: 0x1e9, KeyboardCaps: 0x1ea, TextboxCaret: 0x1ac, TextboxMaxLength: 0x1bc, TextboxKeypad: 0x1cc,
        ToggleOn: 0x188,
        ClassVptrs: new[]
        {
            new Bbs757EfbClassVptr("button", ButtonVptr), new Bbs757EfbClassVptr("label", LabelVptr),
            new Bbs757EfbClassVptr("grid", GridVptr), new Bbs757EfbClassVptr("scrollbox", ScrollboxVptr),
            new Bbs757EfbClassVptr("slider", SliderVptr),
            new Bbs757EfbClassVptr("textbox", TextboxVptr), new Bbs757EfbClassVptr("multiline", MultilineVptr),
            new Bbs757EfbClassVptr("pulldown", PulldownVptr), new Bbs757EfbClassVptr("image", ImageVptr),
            new Bbs757EfbClassVptr("overlay", OverlayVptr), new Bbs757EfbClassVptr("modal", ModalVptr),
            new Bbs757EfbClassVptr("keyboard", KeyboardVptr), new Bbs757EfbClassVptr("toggle", ToggleVptr),
        });

    private readonly byte[] _b = new byte[0x1B0000];
    private uint _heap = Heap;
    private void Ptr(uint at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(_b.AsSpan((int)at), v);
    private void F32(uint at, float v) => BinaryPrimitives.WriteSingleLittleEndian(_b.AsSpan((int)at), v);
    private void ShortStr(uint at, string s) { Encoding.ASCII.GetBytes(s).CopyTo(_b.AsSpan((int)at)); _b[at + 11] = (byte)s.Length; }

    /// <summary>A libc++ long-form string: its characters on the fake's heap, the pointer, the size and the capacity with the long flag.</summary>
    private void LongStr(uint at, string s)
    {
        uint data = _heap;
        Assert.True(data + (uint)s.Length + 1 <= HeapEnd, "the fake's heap is full");
        Encoding.ASCII.GetBytes(s).CopyTo(_b.AsSpan((int)data));
        _heap += (uint)s.Length + 1;
        LongHeader(at, data, s.Length);
    }

    /// <summary>A long-form string's 12 bytes alone: the pointer, the size word and the capacity with the long flag.</summary>
    private void LongHeader(uint at, uint data, int size) { Ptr(at, data); Ptr(at + 4, (uint)size); Ptr(at + 8, 0x80000000u | (uint)(size + 1)); }

    /// <summary>A std::string in the form libc++ keeps it: short up to 10 characters, long past that.</summary>
    private void Str(uint at, string s) { if (s.Length <= 10) ShortStr(at, s); else LongStr(at, s); }
    private void Rect(uint ctrl, float x, float y, float w, float h) { F32(ctrl + 0x38, x); F32(ctrl + 0x3c, y); F32(ctrl + 0x40, w); F32(ctrl + 0x44, h); }
    private void Control(uint ctrl, uint vptr, string name, bool visible) { Ptr(ctrl, vptr); Str(ctrl + 0x1c, name); _b[ctrl + 0x8c] = (byte)(visible ? 1 : 0); }

    /// <summary>A control's children vector (8-byte slots, as libc++ keeps BlueBird's).</summary>
    private void Children(uint ctrl, uint vec, params uint[] kids)
    {
        Ptr(ctrl + 0xf4, vec); Ptr(ctrl + 0xf8, vec + 8 * (uint)kids.Length);
        for (int i = 0; i < kids.Length; i++) Ptr(vec + 8 * (uint)i, kids[i]);
    }

    private Bbs757EfbMemory Build(Action? mutate = null)
    {
        Ptr(Manager + 0x70, Efb);           // efb in the manager
        Ptr(Efb, EfbVptr);
        Ptr(Efb + 132, Bbos);               // efb -> bbos
        // app map header at bbos+0x238: [begin, root, size]
        Ptr(Bbos + 0x238 + 4, MapNode);
        Ptr(Bbos + 0x238 + 8, 1);
        Ptr(MapNode + 0x10, 7);             // key
        Ptr(MapNode + 0x14, AppVec); Ptr(MapNode + 0x18, AppVec + 8); // value vector [one app*]
        Ptr(AppVec, App);
        Ptr(App, GridVptr);                 // app "class" (any vptr; mapped to grid here, harmless)
        Ptr(App + 0x140, 1);                // front
        Ptr(App + 0x144, Page);             // current page
        Ptr(App + 0x17c, RootsVec); Ptr(App + 0x180, RootsVec); // empty roots vector
        Ptr(Page + 0x4c, PageVec); Ptr(Page + 0x50, PageVec + 8); // page children [grid]
        Ptr(PageVec, Grid);
        Control(Grid, GridVptr, "page_grid", true); Rect(Grid, 0, 32, 1397, 1016);
        Ptr(Grid + 0xf4, GridChildrenVec); Ptr(Grid + 0xf8, GridChildrenVec + 24); // 3 children
        Ptr(GridChildrenVec, Button); Ptr(GridChildrenVec + 8, ValueLabel); Ptr(GridChildrenVec + 16, HiddenBtn);
        Control(Button, ButtonVptr, "btn_fuel", true); Rect(Button, 0, 202, 250, 60); ShortStr(Button + 0x144, "FUEL");
        Control(ValueLabel, LabelVptr, "tiller_value", true); Rect(ValueLabel, 1197, 422, 200, 60); ShortStr(ValueLabel + 0x128, "RUDDER");
        Control(HiddenBtn, ButtonVptr, "btn_hidden", false); Rect(HiddenBtn, 0, 300, 250, 60); ShortStr(HiddenBtn + 0x144, "HIDDEN");
        mutate?.Invoke();

        var src = new FakeProcessMemory().Add(new MemoryRegion(B, B, (ulong)_b.Length, true, true, true), _b);
        return new Bbs757EfbMemory(new ReadBudget(new ModuleEfbMemory(new ModuleMemory(src, B)), MaxReads), Layout);
    }

    /// <summary>More reads than any test's walk needs (the node budget's test needs about 300,000): a walk past it did not end.</summary>
    private const long MaxReads = 2_000_000;

    /// <summary>The fake's memory with a read budget, so a walk that would not end fails the test instead of hanging the run.</summary>
    private sealed class ReadBudget : IEfbMemory
    {
        private readonly IEfbMemory _m;
        private long _left;
        public ReadBudget(IEfbMemory m, long reads) { _m = m; _left = reads; }
        private void Spend() { if (--_left < 0) throw new InvalidOperationException("the walk read past its budget: it would not have ended"); }
        public bool TryReadInt(uint a, out int v) { Spend(); return _m.TryReadInt(a, out v); }
        public bool TryReadByte(uint a, out byte v) { Spend(); return _m.TryReadByte(a, out v); }
        public float ReadFloat(uint a) { Spend(); return _m.ReadFloat(a); }
        public bool TryReadPtr(uint a, out uint v) { Spend(); return _m.TryReadPtr(a, out v); }
        public string? ReadStdString(uint a, int maxLength = Bbs757EfbMemory.TextCap) { Spend(); return _m.ReadStdString(a, maxLength); }
    }

    private static int Count(IReadOnlyList<Bbs757EfbMemory.Node> ns) => ns.Count + ns.Sum(n => Count(n.Children));

    private static IEnumerable<Bbs757EfbMemory.Node> Flat(IEnumerable<Bbs757EfbMemory.Node> ns) =>
        ns.SelectMany(n => new[] { n }.Concat(Flat(n.Children)));

    [Fact]
    public void Walks_the_front_app_page_tree_with_text_and_rects()
    {
        var apps = Build().ReadFrontApps();
        var app = Assert.Single(apps);
        Assert.Equal(7, app.Key);
        var grid = Assert.Single(app.Controls);
        Assert.Equal("page_grid", grid.Name);
        // the hidden button is dropped; the button and value label remain, with their text
        Assert.Equal(2, grid.Children.Count);
        var fuel = grid.Children[0];
        Assert.Equal("button", fuel.Kind);
        Assert.Equal("FUEL", fuel.Text);
        Assert.Equal((0f, 202f, 250f, 60f), (fuel.X, fuel.Y, fuel.W, fuel.H));
        var value = grid.Children[1];
        Assert.Equal("label", value.Kind);
        Assert.Equal("RUDDER", value.Text);
    }

    /// <summary>The grid made a scrollbox scrolled by <paramref name="offset"/>, written where the layout keeps a scrollbox's content
    /// offset (B757-26: never the scrollbar thumb).</summary>
    private void ScrolledGrid(float offset)
    {
        Ptr(Grid, ScrollboxVptr); F32(Grid + (uint)Layout.ScrollboxOffset, offset);
        Rect(Button, 0, 1202, 250, 60);
    }

    /// <summary>Settings, Aircraft at the end of its scroll: its stackbox's 1,180 less its scrollbox's 966 (measured at +0x134,
    /// 2026-11-07).</summary>
    private const float SettingsAircraftEnd = 214f;

    [Fact]
    public void A_scrolled_scrollbox_moves_its_controls_up_by_its_offset()
    {
        var apps = Build(() =>
        {
            ScrolledGrid(SettingsAircraftEnd);
            // a second page child, outside the scrollbox
            Ptr(Page + 0x50, PageVec + 16); Ptr(PageVec + 8, Outside);
            Control(Outside, LabelVptr, "outside", true); Rect(Outside, 0, 1202, 100, 20); ShortStr(Outside + 0x128, "TOP");
        }).ReadFrontApps();
        var controls = Assert.Single(apps).Controls;
        var grid = controls[0];
        Assert.Equal("scrollbox", grid.Kind);
        Assert.Equal(SettingsAircraftEnd, grid.Scroll);
        Assert.Equal(32f, grid.Y);                                          // the scrollbox itself is not moved
        Assert.Equal(1202f - SettingsAircraftEnd, grid.Children[0].Y, 0.01); // the row reads where it is drawn
        Assert.Equal(1202f, controls[1].Y);                          // outside the scrollbox: unmoved
        Assert.Equal(0f, controls[1].Scroll);
    }

    [Fact]
    public void A_nested_scrollbox_adds_its_offset()
    {
        var apps = Build(() =>
        {
            ScrolledGrid(100f);
            Ptr(Button, ScrollboxVptr); F32(Button + (uint)Layout.ScrollboxOffset, 50f);
            Ptr(Button + 0xf4, InnerVec); Ptr(Button + 0xf8, InnerVec + 8); Ptr(InnerVec, Inner);
            Control(Inner, LabelVptr, "inner", true); Rect(Inner, 0, 500, 100, 20); ShortStr(Inner + 0x128, "IN");
        }).ReadFrontApps();
        var button = Assert.Single(apps).Controls[0].Children[0];
        Assert.Equal(1102f, button.Y);
        Assert.Equal(350f, Assert.Single(button.Children).Y);
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(-5f, 0f)]
    [InlineData(1e9f, 100000f)]
    public void An_unreadable_offset_reads_as_zero(float stored, float expected)
    {
        var grid = Assert.Single(Assert.Single(Build(() => ScrolledGrid(stored)).ReadFrontApps()).Controls);
        Assert.Equal(expected, grid.Scroll);
        Assert.Equal(1202f - expected, grid.Children[0].Y);
    }

    // ---- a slider's knob ----

    /// <summary>The grid's first child made a slider at <paramref name="x"/>,<paramref name="y"/> <paramref name="w"/>x<paramref name="h"/>
    /// with its knob's rectangle written where the layout keeps it, in the slider rectangle's own coordinates.</summary>
    private void Slider(float x, float y, float w, float h, float kx, float ky, float kw, float kh)
    {
        Ptr(Button, SliderVptr); Rect(Button, x, y, w, h);
        uint knob = Button + (uint)Layout.SliderKnob;
        F32(knob, kx); F32(knob + 4, ky); F32(knob + 8, kw); F32(knob + 12, kh);
    }

    private Bbs757EfbMemory.Node FirstChild(Action mutate) =>
        Assert.Single(Assert.Single(Build(mutate).ReadFrontApps()).Controls).Children[0];

    [Fact]
    public void An_across_sliders_place_is_its_knobs_left_along_the_track()
    {
        // Weight & balance's forward cargo, measured 2026-11-07: 59.8 % of the way, the 12 px knob's left at X + 0.598 (W - 12). Its y
        // is off the slider's middle, so a place taken from y (an across slider read as upright) would differ.
        var cargo = FirstChild(() => Slider(1162.5f, 462, 188, 18, 1162.5f + 0.598f * 176, 463, 12, 12));

        Assert.Equal("slider", cargo.Kind);
        Assert.Equal(0.598f, cargo.Knob!.Value, 3);
        Assert.Equal(12f, cargo.KnobSpan);
    }

    [Fact]
    public void An_upright_sliders_place_counts_from_its_bottom()
    {
        // The left hydraulic reservoir, measured 2026-11-07: 78.1 %, the knob's top at Y + (1 - 0.781) (H - 12); bottom is 0.
        var reservoir = FirstChild(() => Slider(423, 279, 24, 190, 429, 279 + 0.219f * 178, 12, 12));

        Assert.Equal(0.781f, reservoir.Knob!.Value, 3);
        Assert.Equal(12f, reservoir.KnobSpan);
        Assert.Equal(0f, FirstChild(() => Slider(423, 279, 24, 190, 429, 279 + 178, 12, 12)).Knob);   // at the bottom
        Assert.Equal(1f, FirstChild(() => Slider(423, 279, 24, 190, 429, 279, 12, 12)).Knob);         // at the top
    }

    [Fact]
    public void An_upright_sliders_span_is_its_knobs_height()
    {
        // A knob wider than tall on an upright slider: its height is what runs along the track (a width taken as the span reads 0.77).
        var reservoir = FirstChild(() => Slider(423, 279, 24, 190, 425, 279 + 0.219f * 178, 20, 12));
        Assert.Equal(0.781f, reservoir.Knob!.Value, 3);
        Assert.Equal(12f, reservoir.KnobSpan);

        // A knob as tall as its upright slider has no track to run along.
        var tall = FirstChild(() => Slider(423, 279, 24, 190, 429, 279, 12, 190));
        Assert.Null(tall.Knob);
        Assert.Equal(0f, tall.KnobSpan);
    }

    [Fact]
    public void A_scroll_moves_a_slider_but_not_its_place()
    {
        // Settings, Aircraft scrolled, measured 2026-11-07: Boarding & load rate's knob y 864.5 is its rectangle's 857 + 7.5 while the
        // slider is drawn 113 px higher. The knob is in the rectangle's own coordinates, so its place is the same. The knob's y here is
        // off the slider's middle (861): an across slider's place never comes from it.
        var rate = FirstChild(() =>
        {
            ScrolledGrid(113);
            Slider(897, 857, 300, 30, 897 + 285, 861, 15, 15);
        });

        Assert.Equal(857f - 113f, rate.Y);
        Assert.Equal(1f, rate.Knob);
        Assert.Equal(15f, rate.KnobSpan);
    }

    [Fact]
    public void A_scroll_moves_an_upright_slider_but_not_its_place()
    {
        // The review's I1: the left reservoir under a scroll of 113. Its place comes from the knob against the rectangle as stored (279),
        // not as drawn (166): against the drawn one it would read 0.146, inside the range, and list as 15.
        var reservoir = FirstChild(() =>
        {
            ScrolledGrid(113);
            Slider(423, 279, 24, 190, 429, 279 + 0.219f * 178, 12, 12);
        });

        Assert.Equal(166f, reservoir.Y);
        Assert.Equal(0.781f, reservoir.Knob!.Value, 3);
        Assert.Equal(12f, reservoir.KnobSpan);
    }

    [Theory]
    [InlineData(float.NaN, 465f, 12f, 12f)]                  // a float that is no number
    [InlineData(1200f, float.PositiveInfinity, 12f, 12f)]
    [InlineData(1200f, 465f, 188f, 12f)]                     // the knob as wide as the slider along its track
    [InlineData(1200f, 465f, -12f, 12f)]                     // a knob of negative size
    [InlineData(1162.5f + 1.02f * 176, 465f, 12f, 12f)]      // past the track's end
    [InlineData(1162.5f - 0.02f * 176, 465f, 12f, 12f)]      // before its start
    public void An_unreadable_knob_gives_no_place(float kx, float ky, float kw, float kh)
    {
        var cargo = FirstChild(() => Slider(1162.5f, 462, 188, 18, kx, ky, kw, kh));
        Assert.Null(cargo.Knob);
        Assert.Equal(0f, cargo.KnobSpan);
    }

    [Theory]
    [InlineData(1.005f, 1f)]
    [InlineData(-0.005f, 0f)]
    public void A_place_just_past_an_end_is_that_end(float place, float expected)
    {
        Assert.Equal(expected, FirstChild(() => Slider(1162.5f, 462, 188, 18, 1162.5f + place * 176, 465, 12, 12)).Knob);
    }

    [Fact]
    public void Only_a_slider_has_a_knob()
    {
        var button = FirstChild(() =>
        {
            uint knob = Button + (uint)Layout.SliderKnob;
            F32(knob, 100); F32(knob + 4, 210); F32(knob + 8, 12); F32(knob + 12, 12);
        });
        Assert.Equal("button", button.Kind);
        Assert.Null(button.Knob);
    }

    // ---- text boxes, drop-downs and the selected entry (plan 21, measured 2026-11-07) ----

    [Fact]
    public void A_text_box_reads_its_text()
    {
        // PERF calc's airport box: its text is its text_label's, a label in no children vector that the box points at; 2 of 6 boxes
        // had theirs allocated apart, so the label sits far from the box here. The box's own button-text field holds "FUEL".
        var box = FirstChild(() =>
        {
            Ptr(Button, TextboxVptr); Ptr(Button + (uint)Layout.TextboxLabel, TextLabel);
            Control(TextLabel, LabelVptr, "text_label", true); Rect(TextLabel, 1, 203, 248, 58);
            ShortStr(TextLabel + (uint)Layout.CtrlLabelText, "KPHX");
        });
        Assert.Equal("textbox", box.Kind);
        Assert.Equal("KPHX", box.Text);
        Assert.Empty(box.Children);

        // No label: no text, never the box's other fields.
        var bare = FirstChild(() => { Ptr(Button, TextboxVptr); Ptr(Button + (uint)Layout.TextboxLabel, 0); });
        Assert.Equal("", bare.Text);
    }

    // ---- a text box's caret, maximum length and keypad flag (plan 22, measured 2026-11-07) ----

    /// <summary>The grid's first child made a text box holding <paramref name="text"/> (its text label's), with its caret, maximum length
    /// and keypad ints where the layout keeps them.</summary>
    private void TextBox(string text, int caret, int maxLength, int keypad)
    {
        Ptr(Button, TextboxVptr); Ptr(Button + (uint)Layout.TextboxLabel, TextLabel);
        Control(TextLabel, LabelVptr, "text_label", true); Rect(TextLabel, 1, 203, 248, 58);
        Str(TextLabel + (uint)Layout.CtrlLabelText, text);
        Ptr(Button + (uint)Layout.TextboxCaret, (uint)caret);
        Ptr(Button + (uint)Layout.TextboxMaxLength, (uint)maxLength);
        Ptr(Button + (uint)Layout.TextboxKeypad, (uint)keypad);
    }

    [Fact]
    public void A_text_box_reads_its_caret_maximum_length_and_keypad_flag()
    {
        // PERF calc's Airport box "EDDF", tapped at x 360: the caret 2 (+0x1ac), its maximum length 4 (+0x1bc), the QWERTY keyboard
        // (+0x1cc 0); the fuel dialog's box, pre-filled, its maximum length 8, opens the numeric keypad (+0x1cc 1).
        var airport = FirstChild(() => TextBox("EDDF", caret: 2, maxLength: 4, keypad: 0));
        Assert.Equal(("textbox", "EDDF"), (airport.Kind, airport.Text));
        Assert.Equal(((int?)2, (int?)4, (bool?)false), (airport.Caret, airport.MaxLength, airport.Keypad));
        var fuel = FirstChild(() => TextBox("2176", caret: 4, maxLength: 8, keypad: 1));
        Assert.Equal(((int?)4, (int?)8, (bool?)true), (fuel.Caret, fuel.MaxLength, fuel.Keypad));

        // The caret at either end of the text, an empty box's at 0; the maximum length 1 to 1,000 (SayIntentions' 64 is the most measured).
        Assert.Equal(0, FirstChild(() => TextBox("EDDF", 0, 4, 0)).Caret);
        Assert.Equal(0, FirstChild(() => TextBox("", 0, 64, 0)).Caret);
        Assert.Equal(1, FirstChild(() => TextBox("", 0, 1, 0)).MaxLength);
        Assert.Equal(1000, FirstChild(() => TextBox("", 0, 1000, 0)).MaxLength);
    }

    [Theory]
    [InlineData(5, 4)]                // past the end of "EDDF"
    [InlineData(int.MaxValue, 4)]
    [InlineData(-1, 0)]
    public void A_caret_outside_the_text_is_kept_inside_it(int caret, int kept)
    {
        // The final review's M4: a text BlueBird sets itself (PERF calc's lookups, a saved ID) may leave the caret where an older, longer
        // text had it, and dropping the box's state for it made a text box a line in the window. The caret is a boundary of the text, so
        // it is kept to 0 to the text's length (the planner clamps it too); the box stays a text box, and the other fields read.
        var box = FirstChild(() => TextBox("EDDF", caret, 4, 1));
        Assert.Equal(((int?)kept, (int?)4, (bool?)true), (box.Caret, box.MaxLength, box.Keypad));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    [InlineData(1001)]
    public void A_maximum_length_outside_1_to_1000_reads_none(int maxLength)
    {
        var box = FirstChild(() => TextBox("EDDF", 4, maxLength, 0));
        Assert.Null(box.MaxLength);
        Assert.Equal(((int?)4, (bool?)false), (box.Caret, box.Keypad));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(0x100)]
    public void A_keypad_flag_neither_0_nor_1_reads_none(int keypad)
    {
        // The int is 0 or 1 (measured on eleven boxes); any other word is a torn read, never a guess either way.
        var box = FirstChild(() => TextBox("EDDF", 4, 4, keypad));
        Assert.Null(box.Keypad);
        Assert.Equal(((int?)4, (int?)4), (box.Caret, box.MaxLength));
    }

    [Fact]
    public void Only_a_text_box_has_a_caret()
    {
        // A button with the same ints at those offsets: none, nor for a label.
        void Ints() { Ptr(Button + (uint)Layout.TextboxCaret, 2); Ptr(Button + (uint)Layout.TextboxMaxLength, 4); Ptr(Button + (uint)Layout.TextboxKeypad, 1); }
        var button = FirstChild(Ints);
        Assert.Equal("button", button.Kind);
        Assert.Equal(((int?)null, (int?)null, (bool?)null), (button.Caret, button.MaxLength, button.Keypad));
        var label = FirstChild(() => { Ints(); Ptr(Button, LabelVptr); });
        Assert.Equal(((int?)null, (int?)null, (bool?)null), (label.Caret, label.MaxLength, label.Keypad));
    }

    [Fact]
    public void A_text_box_whose_ints_cannot_be_read_has_none()
    {
        // A text box at the end of the module's memory: its name, rectangle and label read, its ints (from +0x1ac) lie past the end.
        var box = FirstChild(() =>
        {
            Children(Grid, GridChildrenVec, EdgeObject);
            Control(EdgeObject, TextboxVptr, "edge_textbox", true); Rect(EdgeObject, 300, 200, 150, 34);
            Ptr(EdgeObject + (uint)Layout.TextboxLabel, TextLabel);
            Control(TextLabel, LabelVptr, "text_label", true); Str(TextLabel + (uint)Layout.CtrlLabelText, "EDDF");
        });
        Assert.Equal(("textbox", "edge_textbox", "EDDF"), (box.Kind, box.Name, box.Text));
        Assert.Equal(((int?)null, (int?)null, (bool?)null), (box.Caret, box.MaxLength, box.Keypad));
    }

    [Fact]
    public void The_tree_format_carries_a_text_boxs_typing_fields()
    {
        // Optional, as every field added after the first trees: written only when the walk read them, so a node without them (and
        // every committed tree) writes as before.
        var apps = Build(() => TextBox("EDDF", 2, 4, 1)).ReadFrontApps();
        string json = Bbs757EfbTreeJson.Write(apps);
        var box = Assert.Single(Bbs757EfbTreeJson.Read(json)).Controls[0].Children[0];
        Assert.Equal(((int?)2, (int?)4, (bool?)true), (box.Caret, box.MaxLength, box.Keypad));

        var torn = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(Build(() => TextBox("EDDF", 2, 0, 0)).ReadFrontApps()));
        var tornBox = Assert.Single(torn).Controls[0].Children[0];
        Assert.Equal(((int?)2, (int?)null, (bool?)false), (tornBox.Caret, tornBox.MaxLength, tornBox.Keypad));

        string bare = Bbs757EfbTreeJson.Write(Build().ReadFrontApps());
        foreach (string field in new[] { "caret", "maxLength", "keypad" })
            Assert.DoesNotContain($"\"{field}\"", bare, StringComparison.Ordinal);
    }

    // ---- a toggle's own byte (plan 22, measured 2026-11-07) ----

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(0x0101, true)]      // +0x189 reads 1 on every toggle: only the low bit of +0x188 is the state
    [InlineData(0x0100, false)]
    [InlineData(0x80, false)]       // a high bit alone is not on
    [InlineData(0xff, true)]
    public void A_toggle_reads_its_own_on_byte(int word, bool expected)
    {
        var toggle = FirstChild(() => { Ptr(Button, ToggleVptr); Ptr(Button + (uint)Layout.ToggleOn, (uint)word); });
        Assert.Equal("toggle", toggle.Kind);
        Assert.Equal((bool?)expected, toggle.On);
    }

    [Fact]
    public void Only_a_toggle_has_an_on_state_and_an_unreadable_one_is_none()
    {
        // A button, a label and a text box with the byte set: none. A toggle at the end of the module's memory: its byte lies past it.
        void On() => Ptr(Button + (uint)Layout.ToggleOn, 1);
        Assert.Null(FirstChild(On).On);
        Assert.Null(FirstChild(() => { On(); Ptr(Button, LabelVptr); }).On);
        Assert.Null(FirstChild(() => { On(); Ptr(Button, TextboxVptr); }).On);
        var torn = FirstChild(() =>
        {
            // The toggle's byte (+0x188) is the first one past the memory's end.
            uint edge = 0x1B0000 - 0x188;
            Children(Grid, GridChildrenVec, edge);
            Control(edge, ToggleVptr, "edge_toggle", true); Rect(edge, 300, 200, 150, 34);
        });
        Assert.Equal(("toggle", "edge_toggle"), (torn.Kind, torn.Name));
        Assert.Null(torn.On);
    }

    [Fact]
    public void The_tree_format_carries_a_toggles_on_state()
    {
        // Optional like the typing fields: written only when set, so every node without it (and every committed tree) writes as before.
        var on = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(Build(() => { Ptr(Button, ToggleVptr); Ptr(Button + (uint)Layout.ToggleOn, 1); }).ReadFrontApps()));
        Assert.True(Assert.Single(on).Controls[0].Children[0].On);
        var off = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(Build(() => { Ptr(Button, ToggleVptr); Ptr(Button + (uint)Layout.ToggleOn, 0); }).ReadFrontApps()));
        Assert.False(Assert.Single(off).Controls[0].Children[0].On);
        string bare = Bbs757EfbTreeJson.Write(Build().ReadFrontApps());
        Assert.DoesNotContain("\"on\"", bare, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multiline_box_reads_every_line()
    {
        // The Operating FP: 91,869 characters with '\n' between lines, past the 4,096 every other string is capped at.
        string plan = string.Join("\n", Enumerable.Range(1, 150).Select(i => FormattableString.Invariant($"{i:000} WPT{i:000} N47 26.9 W122 18.5 FL350")));
        Assert.True(plan.Length > 4096);
        var fp = FirstChild(() => { Ptr(Button, MultilineVptr); LongStr(Button + (uint)Layout.MultilineText, plan); });
        Assert.Equal("multiline", fp.Kind);
        Assert.Equal(plan, fp.Text);

        // A label keeps the 4,096 cap: past it, no text.
        var label = FirstChild(() =>
        {
            Ptr(Button, LabelVptr); ShortStr(Button + (uint)Layout.CtrlButtonText, "");
            LongStr(Button + (uint)Layout.CtrlLabelText, plan);
        });
        Assert.Equal("", label.Text);
    }

    [Fact]
    public void A_multiline_box_reads_the_operating_fps_length()
    {
        // The Operating FP measured 91,869 characters: 100,000 read whole.
        string text = string.Concat(Enumerable.Repeat("KSEA DCT BTG DCT KPDX FL350 0:35 120000\n", 2500));
        Assert.Equal(100_000, text.Length);
        var fp = FirstChild(() => { Ptr(Button, MultilineVptr); LongStr(Button + (uint)Layout.MultilineText, text); });
        Assert.Equal(text, fp.Text);
    }

    [Fact]
    public void A_multiline_text_past_the_long_cap_reads_none()
    {
        // The cap is 1 MiB: a size word of exactly that reads whole; one more reads "", though every byte of it is there to read.
        // Numbers, not the constant, so a cap moved either way fails here.
        uint at = Button + (uint)Layout.MultilineText;
        Assert.Equal(1_048_576, Big(1_048_576).Length);
        Assert.Equal("", Big(1_048_577));

        string Big(int size) => FirstChild(() =>
        {
            Ptr(Button, MultilineVptr);
            _b.AsSpan((int)BigText, 1_048_577).Fill((byte)'A');
            LongHeader(at, BigText, size);
        }).Text;
    }

    /// <summary>The grid's first child made a drop-down, as PERF calc's runway one was read: its face (a button showing
    /// <paramref name="face"/>), its entry records, and its list (a scrollbox below the face holding a stack of one button per
    /// entry, 38 px apart while open, [0,0 50x38] while closed), scrolled by <paramref name="scroll"/>.</summary>
    private void DropDown(string face, string[] entries, bool open = false, float scroll = 0)
    {
        Ptr(Button, PulldownVptr); Rect(Button, 519.1f, 156, 150, 34);
        Ptr(Button + (uint)Layout.PulldownFace, Face);
        Control(Face, ButtonVptr, "main_button", true); Rect(Face, 519.1f, 156, 150, 34); Str(Face + (uint)Layout.CtrlButtonText, face);
        Ptr(Button + (uint)Layout.PulldownList, List);
        Control(List, ScrollboxVptr, "item_scrollbox", true); Rect(List, 519.1f, 189, 150, 180);
        F32(List + (uint)Layout.ScrollboxOffset, scroll);
        Children(List, ListVec, Stack);
        Control(Stack, StackVptr, "item_stack", true); Rect(Stack, 519.1f, 189, 150, 38 * entries.Length);
        var items = Enumerable.Range(0, entries.Length).Select(i => Items + (uint)i * 0x200).ToArray();
        Children(Stack, StackVec, items);
        uint vec = Button + (uint)Layout.PulldownEntries;
        Ptr(vec, EntriesVec); Ptr(vec + 4, EntriesVec + 8 * (uint)entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            Control(items[i], ButtonVptr, "item_btn_" + i, true);
            if (open) Rect(items[i], 519.1f, 189 + 38 * i, 150, 38); else Rect(items[i], 0, 0, 50, 38);
            Str(items[i] + (uint)Layout.CtrlButtonText, entries[i]);
            uint record = Records + (uint)i * 0x40;
            Ptr(EntriesVec + 8 * (uint)i, record);
            Str(record + (uint)Layout.PulldownEntryText, entries[i]);
        }
        _b[Button + (uint)Layout.PulldownOpen] = (byte)(open ? 1 : 0);
    }

    [Fact]
    public void A_drop_down_reads_its_entries_and_current_one()
    {
        // PERF calc's flap drop-down: no index field; the current entry is the one whose text is the face's.
        var flap = FirstChild(() => DropDown("Optimum", new[] { "15", "20", "5", "Optimum" }));
        Assert.Equal("pulldown", flap.Kind);
        Assert.Equal("Optimum", flap.Text);
        Assert.Equal("Optimum", flap.Value);
        Assert.Equal(new[] { "15", "20", "5", "Optimum" }, flap.Entries);
        Assert.False(flap.Open);
        Assert.Null(flap.Rows);
        Assert.Null(flap.OpenList);
        Assert.Empty(flap.Children); // the face and the list are in no children vector
    }

    [Fact]
    public void A_drop_down_whose_face_matches_no_entry_has_no_value()
    {
        // Systems' pb_menu1 always shows SELECT.
        var menu = FirstChild(() => DropDown("SELECT", new[] { "24 - ELECTRICAL", "28 - FUEL", "29 - HYDRAULICS", "36 - PNEUMATICS" }));
        Assert.Equal("SELECT", menu.Text);
        Assert.Equal("", menu.Value);
        Assert.Equal(new[] { "24 - ELECTRICAL", "28 - FUEL", "29 - HYDRAULICS", "36 - PNEUMATICS" }, menu.Entries);
    }

    [Fact]
    public void An_open_drop_down_reads_its_rows()
    {
        // The runway drop-down tapped open: its +0x154 byte 1, the entry buttons laid out 38 px apart from the list's top. The list
        // (180 px tall for six 38 px rows) is scrolled by one row here, so each row is drawn 38 px higher than its rectangle.
        string[] runways = { "RW07L", "RW07R", "RW08", "RW25L", "RW25R", "RW26" };
        var runway = FirstChild(() => DropDown("RW07L", runways, open: true, scroll: 38));
        Assert.True(runway.Open);
        Assert.Equal("RW07L", runway.Value);
        var rows = runway.Rows!;
        Assert.Equal(runways, rows.Select(r => r.Text));
        Assert.All(rows, r => Assert.Equal("button", r.Kind));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => 189f + 38 * i - 38), rows.Select(r => r.Y));
        Assert.Equal((519.1f, 150f, 38f), (rows[0].X, rows[0].W, rows[0].H));
        Assert.Empty(runway.Children);

        // The list's own viewport, for a choice to tell a row in view from one scrolled out of it: within its 189 to 369, row 0
        // (drawn at 151) is scrolled out above and row 5 (341 to 379) runs past its bottom.
        var list = runway.OpenList!;
        Assert.Equal(("scrollbox", "item_scrollbox", 519.1f, 189f, 150f, 180f, 38f), (list.Kind, list.Name, list.X, list.Y, list.W, list.H, list.Scroll));
        Assert.Empty(list.Children); // its rows are Rows
    }

    [Fact]
    public void An_open_drop_down_under_a_scrolled_page_takes_the_pages_scroll_off_its_rows()
    {
        // Logic only, not measured: no drop-down read so far sits in a scrollbox. The list is drawn under its drop-down, so it is
        // read as any control inside the page's scrollbox: it and its rows drawn the page's offset higher, the rows its own too.
        string[] runways = { "RW07L", "RW07R", "RW08", "RW25L", "RW25R", "RW26" };
        var runway = FirstChild(() => { ScrolledGrid(100); DropDown("RW07L", runways, open: true, scroll: 38); });
        Assert.Equal(156f - 100, runway.Y);
        Assert.Equal((189f - 100, 38f), (runway.OpenList!.Y, runway.OpenList.Scroll));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => 189f + 38 * i - 38 - 100), runway.Rows!.Select(r => r.Y));
    }

    private bool? Selection(byte selectable, byte unselected) => FirstChild(() =>
    {
        _b[Button + (uint)Layout.ButtonSelectable] = selectable; _b[Button + (uint)Layout.ButtonUnselected] = unselected;
    }).Selected;

    [Fact]
    public void A_selected_button_reads_selected()
    {
        // Side menus and tab rows: the current entry is the button whose +0x140 byte is 1 and +0x143 byte 0.
        Assert.True(Selection(selectable: 1, unselected: 0));
        Assert.False(Selection(selectable: 1, unselected: 1)); // another entry of the menu
        Assert.Null(Selection(selectable: 0, unselected: 0));  // a tile that takes no tap (Ground services' chocks_service_box)

        var label = FirstChild(() =>
        {
            Ptr(Button, LabelVptr);
            _b[Button + (uint)Layout.ButtonSelectable] = 1; _b[Button + (uint)Layout.ButtonUnselected] = 0;
        });
        Assert.Null(label.Selected); // only a button is selected or not
    }

    // ---- notifications ----

    /// <summary>BBOS's notification vector holding <paramref name="records"/>, each <c>NoticeStride</c> bytes, capacity 64.</summary>
    private void Notices(params (int Id, string Title, string Text, string Time, bool Unread)[] records)
    {
        uint vec = Bbos + (uint)Layout.BbosNotices, stride = (uint)Layout.NoticeStride;
        Ptr(vec, NoticeRecords); Ptr(vec + 4, NoticeRecords + stride * (uint)records.Length); Ptr(vec + 8, NoticeRecords + stride * 64);
        for (int i = 0; i < records.Length; i++)
        {
            uint r = NoticeRecords + stride * (uint)i;
            Ptr(r + (uint)Layout.NoticeId, (uint)records[i].Id);
            Str(r + (uint)Layout.NoticeTitle, records[i].Title);
            Str(r + (uint)Layout.NoticeText, records[i].Text);
            Str(r + (uint)Layout.NoticeTime, records[i].Time);
            _b[r + (uint)Layout.NoticeUnread] = (byte)(records[i].Unread ? 1 : 0);
        }
    }

    [Fact]
    public void The_notifications_read_text_and_time()
    {
        // Measured: id 45 read, the AFT cargo door's id 50 new and unread, an autobrake one with a title.
        var reader = Build(() => Notices((45, "", "CHOCKS REMOVED.", "21:37Z", false), (50, "", "AFT CARGO DOOR OPENED.", "03:15Z", true),
            (52, "AUTOBRAKE 1", "TARGET 4.5 | ACTUAL 4.5 ft/s2", "03:20Z", true)));
        Assert.Equal(new[]
        {
            new Bbs757EfbNoticeRecord(45, "", "CHOCKS REMOVED.", "21:37Z", false),
            new Bbs757EfbNoticeRecord(50, "", "AFT CARGO DOOR OPENED.", "03:15Z", true),
            new Bbs757EfbNoticeRecord(52, "AUTOBRAKE 1", "TARGET 4.5 | ACTUAL 4.5 ft/s2", "03:20Z", true),
        }, reader.ReadNotices());
    }

    [Fact]
    public void No_notification_reads_an_empty_list()
    {
        // A whole vector with no record, and one never allocated (begin and end both 0, as a fresh BBOS starts): read, and empty.
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Bbs757EfbNoticeRecord>>(Build(() => Notices()).ReadNotices()));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Bbs757EfbNoticeRecord>>(Build().ReadNotices()));
    }

    [Theory]
    [InlineData(NoticeRecords, NoticeRecords - 0x3c)]             // its end before its start
    [InlineData(NoticeRecords, NoticeRecords + 0x3c + 7)]         // not a whole record
    [InlineData(0u, 0x3cu)]                                      // no buffer
    [InlineData(NoticeRecords, NoticeRecords + 257 * 0x3c)]       // more records than any BlueBird keeps
    public void A_torn_notices_vector_is_not_read(uint begin, uint end)
    {
        // Not read (null), never an empty list: the window tells "BlueBird has none" from "none could be read".
        var reader = Build(() =>
        {
            Notices((45, "", "CHOCKS REMOVED.", "21:37Z", false), (50, "", "AFT CARGO DOOR OPENED.", "03:15Z", true));
            Ptr(Bbos + (uint)Layout.BbosNotices, begin); Ptr(Bbos + (uint)Layout.BbosNotices + 4, end);
        });
        Assert.Null(reader.ReadNotices());
    }

    [Fact]
    public void Notifications_are_not_read_while_the_module_is_not_reachable()
    {
        var reader = Build(() => Ptr(Efb, 0));   // the gauge manager's object is no EFB: BBOS not found
        Assert.Null(reader.ReadNotices());
    }

    // ---- the top bar and the dialog: apps found by their class ----

    /// <summary>The map gains the top bar's app (key 1, its front flag 0, as measured) and the dialog app (key 3, its front flag 18 and
    /// its page set, as BlueBird leaves them after a dialog closes), with BBOS's dialog byte <paramref name="dialogOpen"/>.</summary>
    private void TopBarAndDialog(bool dialogOpen)
    {
        Ptr(Bbos + 0x238 + 8, 3);
        Ptr(MapNode, BarNode); Ptr(MapNode + 4, DialogNode);
        Ptr(BarNode + 0x10, 1); Ptr(BarNode + 0x14, BarAppVec); Ptr(BarNode + 0x18, BarAppVec + 8); Ptr(BarAppVec, BarApp);
        Ptr(BarApp, OverlayVptr); Ptr(BarApp + 0x140, 0); Ptr(BarApp + 0x144, 0);
        Ptr(BarApp + 0x17c, BarRoots); Ptr(BarApp + 0x180, BarRoots + 16); Ptr(BarRoots, ResetButton); Ptr(BarRoots + 8, BarGrid);
        Control(ResetButton, ButtonVptr, "reset_points_button", false);
        Control(BarGrid, GridVptr, "overlay_grid", true); Rect(BarGrid, 0, 0, 1397, 32);
        Children(BarGrid, BarGridVec, TimeLabel);
        Control(TimeLabel, LabelVptr, "time_label", true); Rect(TimeLabel, 15, 0, 1382, 32);
        Str(TimeLabel + (uint)Layout.CtrlLabelText, "Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C");

        Ptr(DialogNode + 0x10, 3); Ptr(DialogNode + 0x14, DialogAppVec); Ptr(DialogNode + 0x18, DialogAppVec + 8); Ptr(DialogAppVec, DialogApp);
        Ptr(DialogApp, ModalVptr); Ptr(DialogApp + 0x140, 18); Ptr(DialogApp + 0x144, DialogPage);
        Ptr(DialogApp + 0x17c, 0); Ptr(DialogApp + 0x180, 0);
        Ptr(DialogPage + 0x4c, DialogPageVec); Ptr(DialogPage + 0x50, DialogPageVec + 8); Ptr(DialogPageVec, DialogGrid);
        Control(DialogGrid, GridVptr, "dialog_grid", true); Rect(DialogGrid, 334.5f, 431, 728, 218);
        Children(DialogGrid, DialogGridVec, Prompt, YesButton);
        Control(Prompt, LabelVptr, "text_label", true); Rect(Prompt, 435.5f, 436, 622, 148);
        Str(Prompt + (uint)Layout.CtrlLabelText, "Power off the EFB?");
        Control(YesButton, ButtonVptr, "yes_btn", true); Rect(YesButton, 334.5f, 589, 180, 60); Str(YesButton + (uint)Layout.CtrlButtonText, "YES");

        _b[Bbos + (uint)Layout.BbosModalOpen] = (byte)(dialogOpen ? 1 : 0);
    }

    [Fact]
    public void The_top_bar_app_is_read_though_not_front()
    {
        var apps = Build(() => TopBarAndDialog(dialogOpen: false)).ReadFrontApps();
        var bar = Assert.Single(apps, a => a.Class == Bbs757EfbMemory.TopBarClass);
        Assert.Equal(1, bar.Key);
        var grid = Assert.Single(bar.Controls); // its hidden reset_points_button dropped
        Assert.Equal("overlay_grid", grid.Name);
        Assert.Equal("Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C", Assert.Single(grid.Children).Text);
    }

    [Fact]
    public void The_top_bar_app_reads_its_notification_panel()
    {
        // Measured 2026-11-07: under a new notification's pop-up the overlay's byte +0x1fc is 1 (+0x1fd, the full list, 0), its panel's x,
        // y and width the floats at +0x234 and its height the float at +0x240 (118). BlueBird draws the panel from these fields, not as a
        // control: only the top bar's app carries one.
        var apps = Build(() =>
        {
            TopBarAndDialog(dialogOpen: true);
            _b[BarApp + (uint)Layout.OverlayPanelShown] = 1;
            uint rect = BarApp + (uint)Layout.OverlayPanelRect;
            F32(rect, 1039.75f); F32(rect + 4, 38); F32(rect + 8, 349.25f); F32(BarApp + (uint)Layout.OverlayPanelHeight, 118);
        }).ReadFrontApps();
        var bar = Assert.Single(apps, a => a.Class == Bbs757EfbMemory.TopBarClass);
        Assert.Equal(new Bbs757EfbMemory.NoticePanel(true, false, 1039.75f, 38, 349.25f, 118), bar.Panel);
        Assert.All(apps.Where(a => a.Class != Bbs757EfbMemory.TopBarClass), a => Assert.Null(a.Panel));

        // The list open: both bytes 1, 480 tall (the fake's image is the same one, its rectangle kept). Closed: both 0, no height.
        var listed = Build(() =>
        {
            TopBarAndDialog(dialogOpen: false);
            _b[BarApp + (uint)Layout.OverlayListOpen] = 1;
            F32(BarApp + (uint)Layout.OverlayPanelHeight, 480);
        }).ReadFrontApps();
        Assert.Equal(new Bbs757EfbMemory.NoticePanel(true, true, 1039.75f, 38, 349.25f, 480), listed.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Panel);
        var closed = Build(() =>
        {
            TopBarAndDialog(dialogOpen: false);
            _b[BarApp + (uint)Layout.OverlayPanelShown] = 0;
            _b[BarApp + (uint)Layout.OverlayListOpen] = 0;
            F32(BarApp + (uint)Layout.OverlayPanelHeight, 0);
        }).ReadFrontApps();
        Assert.Equal(new Bbs757EfbMemory.NoticePanel(false, false, 1039.75f, 38, 349.25f, 0), closed.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Panel);
    }

    [Fact]
    public void The_top_bar_app_reads_its_flight_time_text()
    {
        // Measured 2026-11-07: the departure countdown is a std::string at +0x1b4 of the top bar's app ("DEP 00:28"), no label; empty
        // after a flight. Only the top bar's app carries one.
        var apps = Build(() => { TopBarAndDialog(dialogOpen: false); Str(BarApp + (uint)Layout.OverlayFlightTimeText, "DEP 00:28"); }).ReadFrontApps();
        Assert.Equal("DEP 00:28", Assert.Single(apps, a => a.Class == Bbs757EfbMemory.TopBarClass).FlightTime);
        Assert.All(apps.Where(a => a.Class != Bbs757EfbMemory.TopBarClass), a => Assert.Equal("", a.FlightTime));
        // (the fake's image is the same one: the string cleared, as size 0 over the stale bytes of "ETA 0000Z")
        var empty = Build(() =>
        {
            TopBarAndDialog(dialogOpen: false);
            Str(BarApp + (uint)Layout.OverlayFlightTimeText, "ETA 0000Z");
            _b[BarApp + (uint)Layout.OverlayFlightTimeText + 11] = 0;
        }).ReadFrontApps();
        Assert.Equal("", empty.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).FlightTime);

        // The tree format carries it (optional: a tree without it reads as "").
        var back = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(apps));
        Assert.Equal("DEP 00:28", back.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).FlightTime);
        Assert.DoesNotContain("flightTime", Bbs757EfbTreeJson.Write(empty), StringComparison.Ordinal);
    }

    private void I64(uint at, long v) => BinaryPrimitives.WriteInt64LittleEndian(_b.AsSpan((int)at), v);

    /// <summary>The boarding box as BlueBird's draw leaves it: its rectangle in the top bar's app, BBOS's loading state, its two bytes and
    /// its three remaining times (seconds).</summary>
    private void BoardingBox(float w, int state, byte deboarding, byte flown, long r0, long r1, long r2)
    {
        uint rect = BarApp + (uint)Layout.OverlayBoardingRect;
        F32(rect, 1144); F32(rect + 4, 4); F32(rect + 8, w); F32(rect + 12, w > 0 ? 24 : 0);
        Ptr(Bbos + (uint)Layout.BbosLoadState, (uint)state);
        _b[Bbos + (uint)Layout.BbosLoadDeboarding] = deboarding;
        _b[Bbos + (uint)Layout.BbosLoadFlown] = flown;
        I64(Bbos + (uint)Layout.BbosLoadRemaining, r0); I64(Bbos + (uint)Layout.BbosLoadRemaining + 8, r1); I64(Bbos + (uint)Layout.BbosLoadRemaining + 16, r2);
    }

    [Fact]
    public void The_top_bar_app_reads_its_boarding_box()
    {
        // Decoded from BBS752.wasm and read live 2026-11-07: the top bar's draw writes the box's rectangle at +0x214 of its app on every
        // frame it draws the box and zeros it otherwise; the words come from BBOS's loading state (+0x9160), its deboarding and flown bytes
        // (+0x9164, +0x9167) and the largest of its three remaining times (+0x9278, 64-bit seconds). Only the top bar's app carries one.
        var apps = Build(() => { TopBarAndDialog(dialogOpen: false); BoardingBox(176, 0, 0, 0, 0, 0, 0); }).ReadFrontApps();
        Assert.Equal(new Bbs757EfbMemory.BoardingBox(1144, 4, 176, 24, 0, false, false, 0),
            Assert.Single(apps, a => a.Class == Bbs757EfbMemory.TopBarClass).Boarding);
        Assert.All(apps.Where(a => a.Class != Bbs757EfbMemory.TopBarClass), a => Assert.Null(a.Boarding));

        // Boarding: the largest remaining time; a byte's low bit is its flag (BlueBird tests it so).
        var running = Build(() => { TopBarAndDialog(dialogOpen: false); BoardingBox(176, 2, 0x02, 0x03, 540, 961, 120); }).ReadFrontApps();
        Assert.Equal(new Bbs757EfbMemory.BoardingBox(1144, 4, 176, 24, 2, false, true, 961), running.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Boarding);

        // A remaining time past a day or below 0 is a torn word, none; the others still count.
        var torn = Build(() => { TopBarAndDialog(dialogOpen: false); BoardingBox(176, 1, 1, 0, -5, 86_401, 300); }).ReadFrontApps();
        Assert.Equal(new Bbs757EfbMemory.BoardingBox(1144, 4, 176, 24, 1, true, false, 300), torn.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Boarding);

        // No box drawn: the rectangle zeroed, so none is read whatever the state says.
        var none = Build(() => { TopBarAndDialog(dialogOpen: false); BoardingBox(0, 4, 0, 0, 0, 0, 0); }).ReadFrontApps();
        Assert.Null(none.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Boarding);

        // The tree format carries it (optional: a tree without it reads none).
        var back = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(running));
        Assert.Equal(new Bbs757EfbMemory.BoardingBox(1144, 4, 176, 24, 2, false, true, 961), back.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Boarding);
        Assert.DoesNotContain("\"boarding\"", Bbs757EfbTreeJson.Write(none), StringComparison.Ordinal);
    }

    // ---- the keyboard app (plan 22, measured 2026-11-07) ----

    /// <summary>The map gains the keyboard app at <paramref name="app"/> (key 2, BlueBird's app_keyboard: its front flag 1, as measured),
    /// its roots holding <c>kb_grid</c> with one key.</summary>
    private void KeyboardApp(uint app = KbApp)
    {
        Ptr(Bbos + 0x238 + 8, 2);
        Ptr(MapNode, KbNode);
        Ptr(KbNode + 0x10, 2); Ptr(KbNode + 0x14, KbAppVec); Ptr(KbNode + 0x18, KbAppVec + 8); Ptr(KbAppVec, app);
        Ptr(app, KeyboardVptr); Ptr(app + 0x140, 1); Ptr(app + 0x144, 0);
        Ptr(app + 0x17c, KbRoots); Ptr(app + 0x180, KbRoots + 8); Ptr(KbRoots, KbGrid);
        Control(KbGrid, GridVptr, "kb_grid", true); Rect(KbGrid, 0, 648, 1397, 400);
        Children(KbGrid, KbGridVec, KbKey);
        Control(KbKey, ButtonVptr, "char_btn_q|Q|1", true); Rect(KbKey, 150, 720, 106, 60); Str(KbKey + (uint)Layout.CtrlButtonText, "q|Q|1");
    }

    /// <summary>The keyboard app's state as BlueBird keeps it: its shown, ready and keypad bytes, its pointer to the text box typed into,
    /// the keypad's rectangle (under the fuel input dialog, as measured) and its layer bytes.</summary>
    private void KeyboardState(byte shown, byte ready, byte keypad, uint target, byte numeric = 0, byte shift = 0, byte caps = 0)
    {
        _b[KbApp + (uint)Layout.KeyboardShown] = shown;
        _b[KbApp + (uint)Layout.KeyboardReady] = ready;
        _b[KbApp + (uint)Layout.KeyboardKeypad] = keypad;
        Ptr(KbApp + (uint)Layout.KeyboardTarget, target);
        uint rect = KbApp + (uint)Layout.KeypadRect;
        F32(rect, 595.5f); F32(rect + 4, 348); F32(rect + 8, 360); F32(rect + 12, 430);
        _b[KbApp + (uint)Layout.KeyboardNumeric] = numeric;
        _b[KbApp + (uint)Layout.KeyboardShift] = shift;
        _b[KbApp + (uint)Layout.KeyboardCaps] = caps;
    }

    /// <summary>The page's first control made PERF calc's Airport box, the box the keyboard types into.</summary>
    private void AirportBox() => Control(Button, TextboxVptr, "planning_perf_calc_airport_input", true);

    private static Bbs757EfbMemory.App KeyboardOf(IReadOnlyList<Bbs757EfbMemory.App> apps) =>
        Assert.Single(apps, a => a.Class == Bbs757EfbMemory.KeyboardClass);

    [Fact]
    public void The_keyboard_app_reads_its_state()
    {
        // The Airport box tapped and the keyboard settled: the keyboard app's +0x1ac and +0x1ad are 1, +0x1ec points at the box (its name
        // read), the keypad's rectangle is at +0x1d8; the one-shot shift (+0x1e9) on. The keyboard app is a class of its own now, no
        // longer the fallback "app", and its front flag set keeps it drawn: its kb_grid reads as before. Only it carries a state.
        var apps = Build(() => { AirportBox(); KeyboardApp(); KeyboardState(1, 1, 0, Button, shift: 1); }).ReadFrontApps();
        var kb = KeyboardOf(apps);
        Assert.Equal(2, kb.Key);
        Assert.Equal(new Bbs757EfbMemory.KeyboardState(true, true, false, "planning_perf_calc_airport_input", false, true, false, 595.5f, 348, 360, 430),
            kb.Keyboard);
        Assert.Equal("kb_grid", Assert.Single(kb.Controls).Name);
        Assert.Equal("char_btn_q|Q|1", Assert.Single(kb.Controls[0].Children).Name);
        Assert.All(apps.Where(a => a.Class != Bbs757EfbMemory.KeyboardClass), a => Assert.Null(a.Keyboard));
        Assert.DoesNotContain(apps, a => a.Class == "app");

        // Up, not yet settled (a key tapped now lands on the page); the numeric keypad for a keypad box; the third layer and caps lock.
        Assert.Equal((true, false), Shown(KeyboardOf(Build(() => { AirportBox(); KeyboardApp(); KeyboardState(1, 0, 0, Button); }).ReadFrontApps())));
        var keypad = KeyboardOf(Build(() => { AirportBox(); KeyboardApp(); KeyboardState(1, 1, 1, Button, numeric: 1, caps: 1); }).ReadFrontApps()).Keyboard!;
        Assert.Equal((true, true, false, true), (keypad.Keypad, keypad.Numeric, keypad.Shift, keypad.Caps));

        static (bool, bool) Shown(Bbs757EfbMemory.App app) => (app.Keyboard!.Shown, app.Keyboard.Ready);
    }

    [Fact]
    public void The_keyboard_down_reads_no_target()
    {
        // Down: its bytes 0 and its target pointer 0, so no box is typed into (""), never a stale name.
        var down = KeyboardOf(Build(() => { AirportBox(); KeyboardApp(); KeyboardState(0, 0, 0, 0); }).ReadFrontApps()).Keyboard;
        Assert.Equal(new Bbs757EfbMemory.KeyboardState(false, false, false, "", false, false, false, 595.5f, 348, 360, 430), down);

        // A target whose name is not a readable string, or that lies outside the module: "" as well, the state still read.
        Assert.Equal("", KeyboardOf(Build(() => { KeyboardApp(); KeyboardState(1, 1, 0, 0x0100_0000); }).ReadFrontApps()).Keyboard!.Target);
        var garbled = KeyboardOf(Build(() =>
        {
            AirportBox();
            _b.AsSpan((int)Button + 0x1c, 12).Fill(0x01);   // its name's bytes no printable string
            KeyboardApp(); KeyboardState(1, 1, 0, Button);
        }).ReadFrontApps()).Keyboard!;
        Assert.Equal(("", true), (garbled.Target, garbled.Shown));
    }

    [Fact]
    public void A_keyboard_byte_is_its_low_bit()
    {
        // As BlueBird's other flags: 0x02 is off, 0x03 on.
        var kb = KeyboardOf(Build(() => { AirportBox(); KeyboardApp(); KeyboardState(0x03, 0x02, 0x02, Button, 0x03, 0x02, 0x03); }).ReadFrontApps()).Keyboard!;
        Assert.Equal((true, false, false, true, false, true), (kb.Shown, kb.Ready, kb.Keypad, kb.Numeric, kb.Shift, kb.Caps));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_keypad_float_that_is_no_number_reads_the_whole_rectangle_as_0(int which)
    {
        // A float that is no number is a torn read: the four read as 0 together, never a rectangle made of the others (a made-up
        // 0, 348, 360, 0 would be a keypad that is not there), never NaN into the model or the tree format.
        var kb = KeyboardOf(Build(() =>
        {
            KeyboardApp(); KeyboardState(1, 1, 1, 0);
            F32(KbApp + (uint)Layout.KeypadRect + 4 * (uint)which, which % 2 == 0 ? float.NaN : float.PositiveInfinity);
        }).ReadFrontApps()).Keyboard!;
        Assert.Equal((0f, 0f, 0f, 0f), (kb.KeypadX, kb.KeypadY, kb.KeypadW, kb.KeypadH));
    }

    [Fact]
    public void A_keyboard_app_whose_bytes_cannot_be_read_has_no_state()
    {
        // The keyboard app at the end of the module's memory: its class, front flag and roots read (its kb_grid among them), its state's
        // bytes (from +0x1ac) lie past the end: no state, never one of zeros.
        var kb = KeyboardOf(Build(() => KeyboardApp(EdgeObject)).ReadFrontApps());
        Assert.Null(kb.Keyboard);
        Assert.Equal("kb_grid", Assert.Single(kb.Controls).Name);
    }

    [Fact]
    public void The_tree_format_carries_the_keyboards_state()
    {
        var apps = Build(() => { AirportBox(); KeyboardApp(); KeyboardState(1, 1, 1, Button, numeric: 1); }).ReadFrontApps();
        var back = Bbs757EfbTreeJson.Read(Bbs757EfbTreeJson.Write(apps));
        Assert.Equal(new Bbs757EfbMemory.KeyboardState(true, true, true, "planning_perf_calc_airport_input", true, false, false, 595.5f, 348, 360, 430),
            KeyboardOf(back).Keyboard);

        // Optional: an app without one writes none, and reads none.
        var none = Build(() => KeyboardApp(EdgeObject)).ReadFrontApps();
        string json = Bbs757EfbTreeJson.Write(none);
        Assert.DoesNotContain("\"keyboard\":", json, StringComparison.Ordinal);
        Assert.Null(KeyboardOf(Bbs757EfbTreeJson.Read(json)).Keyboard);
    }

    [Fact]
    public void A_closed_dialog_is_not_read()
    {
        // BlueBird left an answered defuel question "front" for hours, its page set; only BBOS's dialog byte said it was gone.
        var apps = Build(() => TopBarAndDialog(dialogOpen: false)).ReadFrontApps();
        Assert.DoesNotContain(apps, a => a.Class == Bbs757EfbMemory.DialogClass || a.Key == 3);
        Assert.Contains(apps, a => a.Key == 7); // the page's app as before
    }

    [Fact]
    public void An_open_dialog_is_read()
    {
        var apps = Build(() => TopBarAndDialog(dialogOpen: true)).ReadFrontApps();
        var dialog = Assert.Single(apps, a => a.Class == Bbs757EfbMemory.DialogClass);
        Assert.Equal(3, dialog.Key);
        var grid = Assert.Single(dialog.Controls);
        Assert.Equal(new[] { "Power off the EFB?", "YES" }, grid.Children.Select(c => c.Text));
        Assert.Equal(3, apps.Count);
    }

    // ---- a torn tree: each control read once, a node budget per reading, a depth guard ----

    [Fact]
    public void A_control_listing_itself_twice_is_read_once()
    {
        // The review's case: with only the depth guard, two back-edges made about 2^65 walks.
        var apps = Build(() => Children(Grid, GridChildrenVec, Grid, Grid)).ReadFrontApps();
        var grid = Assert.Single(Assert.Single(apps).Controls);
        Assert.Equal("page_grid", grid.Name);
        Assert.Empty(grid.Children);
    }

    [Fact]
    public void A_two_control_cycle_reads_each_once()
    {
        var apps = Build(() => { Children(Grid, GridChildrenVec, Button); Children(Button, InnerVec, Grid); }).ReadFrontApps();
        Assert.Equal(new[] { "page_grid", "btn_fuel" }, Flat(Assert.Single(apps).Controls).Select(n => n.Name));
    }

    [Fact]
    public void A_drop_down_whose_list_points_back_up_the_tree_reads_no_rows()
    {
        var apps = Build(() => { DropDown("A", new[] { "A" }, open: true); Ptr(Button + (uint)Layout.PulldownList, Grid); }).ReadFrontApps();
        var grid = Assert.Single(Assert.Single(apps).Controls);
        var dropDown = grid.Children[0];
        Assert.True(dropDown.Open);
        Assert.Empty(dropDown.Rows!);
        Assert.Null(dropDown.OpenList);
        Assert.Single(Flat(new[] { grid }), n => n.Name == "page_grid");
    }

    [Fact]
    public void A_reading_walks_at_most_its_node_budget()
    {
        // Eleven grids of 2,048 distinct controls each (a children vector's cap) under the page: 22,539 controls, past the budget.
        const int Parents = 11, Each = 2048;
        var apps = Build(() =>
        {
            uint wide = TreeBase, vecs = TreeBase + 0x18000, parents = TreeBase + 0x46000, page = TreeBase + 0x48000;
            // Controls 4 bytes apart in bytes of 0x01: each visible, of no known kind, with no name, text or children.
            _b.AsSpan((int)wide, Parents * Each * 4 + 0x150).Fill(1);
            for (int k = 0; k < Parents; k++)
            {
                uint parent = parents + (uint)k * 0x200, vec = vecs + (uint)k * 0x4000;
                Control(parent, GridVptr, "p" + k, true);
                Ptr(parent + 0xf4, vec); Ptr(parent + 0xf8, vec + 8 * Each);
                for (int j = 0; j < Each; j++) Ptr(vec + 8 * (uint)j, wide + 4 * (uint)(k * Each + j));
                Ptr(page + 8 * (uint)k, parent);
            }
            Ptr(Page + 0x4c, page); Ptr(Page + 0x50, page + 8 * Parents);
        }).ReadFrontApps();
        Assert.Equal(Bbs757EfbMemory.MaxNodes, Count(Assert.Single(apps).Controls));
    }

    [Fact]
    public void A_chain_deeper_than_the_depth_guard_stops_there()
    {
        // Seventy distinct controls each inside the one before: the walk reads the first MaxDepth + 1 levels.
        const int Links = 70;
        var apps = Build(() =>
        {
            for (int i = 0; i < Links; i++)
            {
                uint link = TreeBase + (uint)i * 0x200;
                Control(link, GridVptr, "c" + i, true);
                if (i + 1 < Links) Children(link, link + 0x100, link + 0x200);
            }
            Ptr(PageVec, TreeBase);
        }).ReadFrontApps();
        int depth = 0;
        for (var level = Assert.Single(apps).Controls; level.Count > 0; level = level[0].Children) depth++;
        Assert.Equal(Bbs757EfbMemory.MaxDepth + 1, depth);
    }

    [Fact]
    public void A_missing_efb_vptr_reads_no_apps()
    {
        var reader = Build();
        _b[Efb] = 0; _b[Efb + 1] = 0; _b[Efb + 2] = 0; _b[Efb + 3] = 0; // clobber the efb vptr
        var src = new FakeProcessMemory().Add(new MemoryRegion(B, B, (ulong)_b.Length, true, true, true), _b);
        var r = new Bbs757EfbMemory(new ModuleEfbMemory(new ModuleMemory(src, B)), Layout);
        Assert.False(r.TryEfb(out _));
        Assert.Empty(r.ReadFrontApps());
    }
}

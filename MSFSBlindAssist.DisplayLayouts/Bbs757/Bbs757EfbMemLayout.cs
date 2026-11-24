namespace MSFSBlindAssist.Services.BBS757;

/// <summary>A control class the EFB reader recognises, with the runtime vtable pointer its objects carry.</summary>
public sealed record Bbs757EfbClassVptr(string Kind, uint Vptr);

/// <summary>
/// Where one installed BBS752.wasm keeps the EFB tablet's control tree, all from the layout file (BBS757-D2).
/// The EFB object is the gauge-manager pointer carrying <see cref="EfbVptr"/>; it holds the BBOS root at
/// <see cref="EfbToBbos"/>; BBOS holds the app map at <see cref="BbosToAppMap"/> (a <c>std::map&lt;int, vector&gt;</c>
/// whose node and vector layout are libc++ ABI, known to the reader). Each app has a front flag, a current page
/// and a roots vector; each control a name, rectangle, visible byte, children vector, parent, and the label or
/// button text. A control's rectangle never moves when its page scrolls: a <c>scrollbox</c> control keeps the
/// scroll, its content offset in tablet pixels, as a float at <see cref="ScrollboxOffset"/> and draws its controls that much
/// higher (never the scrollbar thumb, which stops short of the content's end: B757-26). A <c>slider</c> keeps its knob's
/// rectangle at <see cref="SliderKnob"/> (x, y, width, height: four floats, in the slider rectangle's own coordinates, which
/// a scroll does not move). The reader tells a control's kind from its vtable (<see cref="ClassVptrs"/>). Equality compares
/// the byte and class lists by content, ignoring <see cref="SourceFile"/>.
/// <para>The phase 2 fields (measured live 2026-11-07; docs/b757.md, "The tablet window") follow <see cref="SliderKnob"/>:
/// a <c>textbox</c>'s text label and a <c>multiline</c> box's text; a <c>pulldown</c>'s face, list, entries and open byte (it has
/// no index: its current entry is the one whose text is its face's); a button's two bytes that mark the current side-menu entry
/// or tab; BBOS's dialog byte; and BBOS's vector of notification records with each record's fields. Every std::string is
/// libc++'s, every vector (begin, end, capacity).</para>
/// <para>Then the top bar app's notification panel (measured live 2026-11-07; the same section's Notifications): BlueBird draws its
/// notification pop-up and its notification list from the overlay app's own fields, not as controls, over the top right of the page:
/// the byte that says one shows, the byte that says the full list is open, and the panel's rectangle.</para>
/// <para>Then the keyboard's state and a text box's typing fields (plan 22, measured live 2026-11-07;
/// docs/b757.md, "Pressing", Typing): the keyboard app (its class <c>keyboard</c>) keeps whether the keyboard is up and settled, whether
/// the numeric keypad shows instead and where, which text box it types into, and its layer bytes; a text box keeps its caret, its
/// maximum length and whether it opens the keypad.</para>
/// </summary>
/// <param name="TextboxLabel">A text box's pointer to its <c>text_label</c>, a label outside every children vector whose
/// <see cref="CtrlLabelText"/> is the box's text (0x128 in this build).</param>
/// <param name="MultilineText">A multi-line text box's whole text, a std::string with '\n' between lines (0x128); the Operating
/// FP's runs to about 92,000 characters.</param>
/// <param name="PulldownFace">A drop-down's pointer to its face, a button whose <see cref="CtrlButtonText"/> is the current
/// choice and whose rectangle is the drop-down's (0x128).</param>
/// <param name="PulldownList">A drop-down's pointer to its list, a scrollbox holding a stack of one button per entry (0x130);
/// open, those buttons are laid out 38 px apart from the scrollbox's top.</param>
/// <param name="PulldownEntries">A drop-down's std::vector of entry-record pointers in 8-byte slots, as
/// <see cref="CtrlChildren"/> (0x138), in the list's drawn order.</param>
/// <param name="PulldownEntryText">An entry record's text, a std::string (0x0c).</param>
/// <param name="PulldownOpen">A drop-down's byte, 1 while its list is open (0x154).</param>
/// <param name="ButtonSelectable">A button's byte, 1 on the entries of a side menu or tab row (0x140); 0 on a tile that takes
/// no tap and on a hidden button.</param>
/// <param name="ButtonUnselected">A button's byte, 0 on the current side-menu entry or tab, 1 on the others (0x143); the
/// current one is the button with <see cref="ButtonSelectable"/> 1 and this 0.</param>
/// <param name="BbosModalOpen">BBOS's byte, 1 while a modal dialog (a question) is up (0x1d8); the dialog app's own front flag
/// and page stay set after the dialog is closed.</param>
/// <param name="BbosNotices">BBOS's std::vector of notification records (begin, end, capacity), an offset into BBOS like
/// <see cref="BbosToAppMap"/> (0x9334): a member of the BBOS heap object, not a static address.</param>
/// <param name="NoticeStride">A notification record's size (0x3c).</param>
/// <param name="NoticeId">A record's id, an int rising with each new notification (0x00).</param>
/// <param name="NoticeTitle">A record's title, a std::string, empty but for a few (0x04).</param>
/// <param name="NoticeText">A record's text, a std::string (0x10).</param>
/// <param name="NoticeTime">A record's time, a std::string such as "21:37Z" (0x1c).</param>
/// <param name="NoticeUnread">A record's byte, 1 until the pilot has opened the notification list (0x34).</param>
/// <param name="OverlayPanelShown">The top bar app's byte, 1 while BlueBird's notification pop-up or its notification list shows
/// (0x1fc): a new notification's pop-up sets it for about 10 s, and opening the list sets it too.</param>
/// <param name="OverlayListOpen">The top bar app's byte, 1 while the full notification list is open (0x1fd); 0 under a pop-up.</param>
/// <param name="OverlayPanelRect">The top bar app's panel rectangle, the x, y and width floats (0x234), in tablet pixels: [1039.75,38
/// 349.25 wide] for both the pop-up and the list.</param>
/// <param name="OverlayPanelHeight">The panel's height, a float (0x240, the rectangle's fourth float in this build): 118 under the
/// pop-up, 480 with the list open, 0 closed.</param>
/// <param name="OverlayFlightTimeText">The top bar's flight-time text, a std::string in the top bar app (0x1b4): "DEP ±hh:mm" around
/// departure ("DEP 00:28" at 14:16L, counting up from the scheduled departure); "ETA hhmmZ" seen in its stale bytes after a flight,
/// empty (size 0) when it shows nothing.</param>
/// <param name="OverlayBoardingRect">The top bar's boarding box, x, y, width and height floats in the top bar app (0x214): BlueBird's
/// top bar draw writes the box's rectangle there on every frame it draws the box ([1144,4 176x24] offering START BOARDING) and zeros it
/// on every frame it does not (decoded from BBS752.wasm, 2026-11-07), so a width above 0 is the box showing.</param>
/// <param name="BbosLoadState">BBOS's loading state, an int (0x9160): 0 idle, 1 just started, 2 running, 3 paused, 4 done (its
/// writers in BBS752.wasm: start 1, the tick 2 and 4, pause 3, resume 2, cancel 0).</param>
/// <param name="BbosLoadDeboarding">BBOS's byte, 1 while the loading started is a deboarding (0x9164): START DEBOARDING sets it, START
/// BOARDING and the end of a deboarding clear it.</param>
/// <param name="BbosLoadFlown">BBOS's byte, 1 once the aircraft has flown since its boarding (0x9167), cleared when a deboarding ends:
/// the top bar offers START DEBOARDING rather than START BOARDING while it is set.</param>
/// <param name="BbosLoadRemaining">BBOS's three remaining loading times, 64-bit seconds 8 bytes apart (0x9278): the box reads
/// "BOARDING... N MIN" with N the largest rounded up to whole minutes, and "BOARDING..." when none is above 0.</param>
/// <param name="KeyboardShown">The keyboard app's byte (BlueBird's <c>app_keyboard</c>, BBOS app map key 2), 1 from a text box's tap
/// until the keyboard goes down (<c>btn_keyboard_down</c>), 0 down (0x1ac; measured live 2026-11-07).
/// The keyboard's controls read visible either way, so this byte is what says it is up.</param>
/// <param name="KeyboardReady">The keyboard app's byte, 1 once the keyboard has settled, 1.12 to 1.16 s after the tap, and 0 at once on
/// down (0x1ad): a key tapped before it is 1 lands on the page beneath, the box losing focus.</param>
/// <param name="KeyboardKeypad">The keyboard app's byte, 1 when the numeric keypad shows instead of the QWERTY keyboard (0x1ae).</param>
/// <param name="KeyboardTarget">The keyboard app's pointer to the text box being typed into, 0 while the keyboard is down (0x1ec).</param>
/// <param name="KeypadRect">The keyboard app's four floats, the numeric keypad's x, y, width and height in tablet pixels (0x1d8): [595.5,348
/// 360x430] under the fuel input dialog. The keypad has no controls in any tree; its keys are placed from this rectangle.</param>
/// <param name="KeyboardNumeric">The keyboard app's byte, 1 while the third layer (digits and marks) shows (0x1e8);
/// <c>btn_numeric_switch</c> toggles it, and it holds.</param>
/// <param name="KeyboardShift">The keyboard app's byte, the one-shot shift (0x1e9): set by <c>btn_shift</c>, cleared by the next character
/// key or backspace; caps lock sets it too.</param>
/// <param name="KeyboardCaps">The keyboard app's byte, caps lock (0x1ea): <c>btn_double_shift</c> sets it until tapped again.</param>
/// <param name="TextboxCaret">A text box's int, its caret: the character boundary a typed character goes in at, 0 to the text's length
/// (0x1ac; +0x1b0 and +0x1b4 always equal it). A tap puts it at the nearest boundary; a typed character then moves it to the end.</param>
/// <param name="TextboxMaxLength">A text box's int, the most characters it takes (0x1bc): Airport 4, the fuel dialog 8, Hoppie 24,
/// SayIntentions 64; a key tapped on a full box is dropped.</param>
/// <param name="TextboxKeypad">A text box's int, 1 when it opens the numeric keypad, 0 the QWERTY keyboard (0x1cc).</param>
/// <param name="ToggleOn">A toggle control's (<c>bbos_control_toggle</c>, a settings switch) own byte, 1 on and 0 off (0x188; measured
/// live 2026-11-07): the only word that changed when TOD pause was turned on and off, and it matched
/// all 16 of Settings &gt; Aircraft's toggles. Only its low bit is the state.</param>
public sealed record Bbs757EfbMemLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    uint ManagerBase,
    uint ManagerSpan,
    uint EfbVptr,
    int EfbToBbos,
    int BbosToAppMap,
    int AppFront,
    int AppPage,
    int AppRoots,
    int PageChildren,
    int CtrlName,
    int CtrlRect,
    int CtrlVisible,
    int CtrlChildren,
    int CtrlParent,
    int CtrlLabelText,
    int CtrlButtonText,
    int ScrollboxOffset,
    int SliderKnob,
    int TextboxLabel,
    int MultilineText,
    int PulldownFace,
    int PulldownList,
    int PulldownEntries,
    int PulldownEntryText,
    int PulldownOpen,
    int ButtonSelectable,
    int ButtonUnselected,
    int BbosModalOpen,
    int BbosNotices,
    int NoticeStride,
    int NoticeId,
    int NoticeTitle,
    int NoticeText,
    int NoticeTime,
    int NoticeUnread,
    int OverlayPanelShown,
    int OverlayListOpen,
    int OverlayPanelRect,
    int OverlayPanelHeight,
    int OverlayFlightTimeText,
    int OverlayBoardingRect,
    int BbosLoadState,
    int BbosLoadDeboarding,
    int BbosLoadFlown,
    int BbosLoadRemaining,
    int KeyboardShown,
    int KeyboardReady,
    int KeyboardKeypad,
    int KeyboardTarget,
    int KeypadRect,
    int KeyboardNumeric,
    int KeyboardShift,
    int KeyboardCaps,
    int TextboxCaret,
    int TextboxMaxLength,
    int TextboxKeypad,
    int ToggleOn,
    IReadOnlyList<Bbs757EfbClassVptr> ClassVptrs)
{
    public bool Equals(Bbs757EfbMemLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && ManagerBase == other.ManagerBase && ManagerSpan == other.ManagerSpan
        && EfbVptr == other.EfbVptr && EfbToBbos == other.EfbToBbos && BbosToAppMap == other.BbosToAppMap
        && AppFront == other.AppFront && AppPage == other.AppPage && AppRoots == other.AppRoots
        && PageChildren == other.PageChildren
        && CtrlName == other.CtrlName && CtrlRect == other.CtrlRect && CtrlVisible == other.CtrlVisible
        && CtrlChildren == other.CtrlChildren && CtrlParent == other.CtrlParent
        && CtrlLabelText == other.CtrlLabelText && CtrlButtonText == other.CtrlButtonText
        && ScrollboxOffset == other.ScrollboxOffset && SliderKnob == other.SliderKnob
        && TextboxLabel == other.TextboxLabel && MultilineText == other.MultilineText
        && PulldownFace == other.PulldownFace && PulldownList == other.PulldownList
        && PulldownEntries == other.PulldownEntries && PulldownEntryText == other.PulldownEntryText
        && PulldownOpen == other.PulldownOpen
        && ButtonSelectable == other.ButtonSelectable && ButtonUnselected == other.ButtonUnselected
        && BbosModalOpen == other.BbosModalOpen && BbosNotices == other.BbosNotices
        && NoticeStride == other.NoticeStride && NoticeId == other.NoticeId && NoticeTitle == other.NoticeTitle
        && NoticeText == other.NoticeText && NoticeTime == other.NoticeTime && NoticeUnread == other.NoticeUnread
        && OverlayPanelShown == other.OverlayPanelShown && OverlayListOpen == other.OverlayListOpen
        && OverlayPanelRect == other.OverlayPanelRect && OverlayPanelHeight == other.OverlayPanelHeight
        && OverlayFlightTimeText == other.OverlayFlightTimeText
        && OverlayBoardingRect == other.OverlayBoardingRect && BbosLoadState == other.BbosLoadState
        && BbosLoadDeboarding == other.BbosLoadDeboarding && BbosLoadFlown == other.BbosLoadFlown
        && BbosLoadRemaining == other.BbosLoadRemaining
        && KeyboardShown == other.KeyboardShown && KeyboardReady == other.KeyboardReady && KeyboardKeypad == other.KeyboardKeypad
        && KeyboardTarget == other.KeyboardTarget && KeypadRect == other.KeypadRect && KeyboardNumeric == other.KeyboardNumeric
        && KeyboardShift == other.KeyboardShift && KeyboardCaps == other.KeyboardCaps
        && TextboxCaret == other.TextboxCaret && TextboxMaxLength == other.TextboxMaxLength && TextboxKeypad == other.TextboxKeypad
        && ToggleOn == other.ToggleOn
        && ClassVptrs.SequenceEqual(other.ClassVptrs);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(ManagerBase); h.Add(ManagerSpan); h.Add(EfbVptr); h.Add(EfbToBbos); h.Add(BbosToAppMap);
        h.Add(AppFront); h.Add(AppPage); h.Add(AppRoots); h.Add(PageChildren);
        h.Add(CtrlName); h.Add(CtrlRect); h.Add(CtrlVisible); h.Add(CtrlChildren); h.Add(CtrlParent);
        h.Add(CtrlLabelText); h.Add(CtrlButtonText); h.Add(ScrollboxOffset); h.Add(SliderKnob);
        h.Add(TextboxLabel); h.Add(MultilineText);
        h.Add(PulldownFace); h.Add(PulldownList); h.Add(PulldownEntries); h.Add(PulldownEntryText); h.Add(PulldownOpen);
        h.Add(ButtonSelectable); h.Add(ButtonUnselected); h.Add(BbosModalOpen); h.Add(BbosNotices);
        h.Add(NoticeStride); h.Add(NoticeId); h.Add(NoticeTitle); h.Add(NoticeText); h.Add(NoticeTime); h.Add(NoticeUnread);
        h.Add(OverlayPanelShown); h.Add(OverlayListOpen); h.Add(OverlayPanelRect); h.Add(OverlayPanelHeight);
        h.Add(OverlayFlightTimeText);
        h.Add(OverlayBoardingRect); h.Add(BbosLoadState); h.Add(BbosLoadDeboarding); h.Add(BbosLoadFlown); h.Add(BbosLoadRemaining);
        h.Add(KeyboardShown); h.Add(KeyboardReady); h.Add(KeyboardKeypad); h.Add(KeyboardTarget); h.Add(KeypadRect);
        h.Add(KeyboardNumeric); h.Add(KeyboardShift); h.Add(KeyboardCaps);
        h.Add(TextboxCaret); h.Add(TextboxMaxLength); h.Add(TextboxKeypad); h.Add(ToggleOn);
        foreach (var c in ClassVptrs) h.Add(c);
        return h.ToHashCode();
    }

    /// <summary>The kind for a control whose first word is <paramref name="vptr"/>; null when unknown.</summary>
    public string? KindFor(uint vptr)
    {
        foreach (var c in ClassVptrs) if (c.Vptr == vptr) return c.Kind;
        return null;
    }
}

/// <summary>The EFB reader's result in a layout file: a layout, or the detailed reason there is none.</summary>
public sealed record Bbs757EfbMemDerivation(Bbs757EfbMemLayout? Layout, string? Failure);

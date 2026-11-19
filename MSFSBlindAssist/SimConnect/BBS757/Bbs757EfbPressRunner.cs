using System.Drawing;
using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What a press on the tablet window needs, as delegates: the bridge builds it over the EFB agent (the tap), its own
/// memory re-read of the tablet, and the app's seams; the tests stand a fake tablet, file and clock behind it. Every member is
/// called on the bridge's loop, one press at a time; the bridge marshals to the UI thread the ones that need it (the echoes, the
/// hook). The reader reads the tablet from the module's memory, so a press asks "where is the control now?" and "did the page
/// change?" by re-reading the model, never by comparing pictures.
/// </summary>
public sealed class Bbs757EfbPressIo
{
    /// <summary>Reads the tablet now from memory, as the bridge's loop reads it, and pushes it to the window when it says something
    /// new; the page read, or null when nothing could be read (the window then shows why).</summary>
    public required Func<CancellationToken, Task<Bbs757EfbScreenModel?>> Read { get; init; }

    /// <summary>One drag at a tablet x from y0 to y1 (the page scrolls on a drag): true when it went out, false when nothing was
    /// dragged.</summary>
    public required Func<int, int, int, CancellationToken, Task<bool>> Drag { get; init; }

    /// <summary>One tap at a tablet point: true when it went out (it may have landed), false when nothing was tapped.</summary>
    public required Func<Point, CancellationToken, Task<bool>> Tap { get; init; }

    /// <summary>A run of taps typing on BlueBird's keyboard (<see cref="Bbs757EfbTyping.Runs"/>: at most
    /// <see cref="Bbs757EfbTyping.MaxTapsPerRun"/>), the given milliseconds after each, as one agent run: true when every tap of it went
    /// out, false when it did not run or not all of it did.</summary>
    public required Func<IReadOnlyList<Point>, int, CancellationToken, Task<bool>> TapRun { get; init; }

    /// <summary>The nav data cycle BlueBird has installed now (<c>NavigationData\cycle.json</c>), read before a nav data press's tap;
    /// null when none is installed or it cannot be read.</summary>
    public required Func<int?> ReadInstalledCycle { get; init; }

    /// <summary>A BlueBird L:var read now through the EFB agent; null when unread.</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadVar { get; init; }

    /// <summary>A variable the definition registers, read fresh (the ground power's connection); null when unread.</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadRegistered { get; init; }

    /// <summary>A setting read now from BlueBird's file; null when it cannot be read.</summary>
    public required Func<Bbs757Setting, CancellationToken, Task<Bbs757Value?>> ReadSetting { get; init; }

    /// <summary>Says a text as the window's alert (spoken at once, once).</summary>
    public required Action<string> Alert { get; init; }

    /// <summary>An awaitable wait.</summary>
    public required Func<int, CancellationToken, Task> Delay { get; init; }

    /// <summary>The clock in milliseconds.</summary>
    public required Func<long> NowMs { get; init; }

    /// <summary>Opens and ends the definition's echo of a ground service's row around the tap on its tile.</summary>
    public required Action<Bbs757GroundService> ArmServiceEcho { get; init; }

    public required Action<Bbs757GroundService, double?> SettleServiceEcho { get; init; }

    /// <summary>Opens and ends plan 07's ground power echo around the tap on the GPU tile.</summary>
    public required Action ArmGroundPowerEcho { get; init; }

    public required Action<double?> SettleGroundPowerEcho { get; init; }

    /// <summary>A hook the press started (<see cref="Bbs757EfbExtras.HookFor"/>), right after its tap went out, with the nav data
    /// cycle installed before that tap for UPDATE or REMOVE (null for any other hook, or when none was installed).</summary>
    public required Action<Bbs757EfbHook, int?> Hooked { get; init; }
}

/// <summary>
/// A press the pilot made in the tablet window: the tablet control (<paramref name="Item"/>) and the page the window showed when it
/// was pressed (<paramref name="Page"/>). The control is found again in a fresh memory read before any tap, so a page that moved
/// under the pilot is handled from the page it was pressed on.
/// </summary>
public sealed record Bbs757EfbPressed(Bbs757EfbItem Item, Bbs757EfbScreenModel Page);

/// <summary>
/// The tablet window's presses (the owner's ruling of 2026-10-29: nothing refuses a press), on the bridge's loop, one at a time:
/// <list type="number">
/// <item>Where: the tablet is read again from memory and the control found in it (<see cref="Bbs757EfbPress.Relocate"/>, from the
/// page the pilot pressed on); a control of the scrollbox that scrolls, outside its view, is dragged into view first (at most
/// <see cref="MaxScrollSteps"/> drags, each followed by a read), and it is tapped where it is drawn now
/// (<see cref="Bbs757EfbPress.TapPoint"/>). Gone, turned into words that are no control (START BOARDING become
/// "Boarding… 10 min"), or an answer whose question was answered meanwhile: nothing tapped, "{label} is no longer on the tablet."
/// Under a question that came up meanwhile, an answer to another question included (every question's YES is keyed alike): nothing
/// tapped and nothing said. A side-menu entry or a home-screen app whose page the tablet shows already (pressed twice), and the
/// window's own Home on the home screen: nothing tapped, nothing said. A page control BlueBird's notification pop-up or list covers
/// (the tap would land on the notification): nothing tapped, "{name} is under a BlueBird notification, not pressed." A page control
/// another drop-down's open list covers (the tap would choose one of its rows): nothing tapped, "{name} is under the open {list} list,
/// not pressed."</item>
/// <item>Whether: a switch or check box only when its state read differs from the wish (<see cref="Bbs757EfbPress.NeedsTap"/>), a
/// slider only when it does not read the choice asked for.</item>
/// <item>The tap, and right after it the press's hook (<see cref="Bbs757EfbExtras.HookFor"/>); then the page re-read until it changes
/// (at most <see cref="ChangeMaxMs"/>).</item>
/// <item>What it came to: a navigation (Home, a side-menu entry, a home-screen app) is read again until the page name is the one
/// tapped (at most <see cref="NavigateMaxMs"/>); a tab that became the current one says "{name} (current)"
/// (<see cref="Bbs757EfbPress.TabOutcome"/>); any other button says its new words when the read after the tap finds it with
/// other words (<see cref="Bbs757EfbPress.ButtonOutcome"/>), and that it changed nothing when nothing on the page did; a Settings
/// switch or slider is judged by BlueBird's file (at most <see cref="FileMaxMs"/>), and anything with no setting behind it by the
/// page (a switch within <see cref="FollowMaxMs"/>); a callout the press switched off with it is named. A switch of two choices the
/// window shows as a choice list says nothing once it landed (the screen reader said the word chosen); one it shows as a check box
/// says its new word once (the screen reader can say only checked or not checked of it). A drop-down's entry is chosen by
/// <see cref="ChooseAsync"/>, its list opened and its row tapped, and says nothing once it landed either.</item>
/// </list>
/// A tile of a ground service or the GPU opens the definition's echo for its row before the tap and ends it with the state read
/// after (CORE-7). A field's text is typed on BlueBird's own keyboard or keypad and read back by <see cref="TypeAsync"/> (plan 22).
/// Everything said is an alert, in plain words that no Windows culture changes.
/// </summary>
public sealed class Bbs757EfbPressRunner
{
    /// <summary>After a tap, the longest wait for the page to change. Measured 2026-10-27: a read 700 ms after a tap caught the old
    /// page on 4 of 5 app taps.</summary>
    public const int ChangeMaxMs = 3000;

    /// <summary>How long a navigation is read again for: measured 2026-10-27, the page follows the icon's press highlight 0.4-2.4 s
    /// after the first change.</summary>
    public const int NavigateMaxMs = 4000;

    /// <summary>How long a switch or check box with no setting behind it is read again for, from its tap.</summary>
    public const int FollowMaxMs = 4000;

    /// <summary>How long BlueBird's file is read for a Settings press. Measured 2026-10-27: its taps were in the file 1.3 s later.</summary>
    public const int FileMaxMs = 2000;

    /// <summary>How many drags a press makes at most to bring its control into view.</summary>
    public const int MaxScrollSteps = 8;

    private const int ChangePollMs = 100, NavigatePollMs = 500, FollowPollMs = 300, FilePollMs = 250;

    /// <summary>How far a slider with no choices may sit from the percent asked for and still be set: by its knob's place when the read
    /// has BlueBird's knob, else by the value it shows.</summary>
    private const double PercentSlack = 10;

    /// <summary>The window's own Home button (<see cref="Bbs757EfbElements"/>), on every app page.</summary>
    private const string HomeKey = "btn:HOME";

    private readonly Bbs757EfbPressIo _io;

    public Bbs757EfbPressRunner(Bbs757EfbPressIo io) => _io = io;

    // ------------------------------------------------------------------ buttons, tiles, menus

    /// <summary>
    /// A button, tile, side-menu entry, tab, top bar control, value box or question answer pressed. A switch or check box clicked is set
    /// to the other state (<see cref="SwitchAsync"/>); a slider, a drop-down or a text box clicked does nothing (the window sets it: a
    /// drop-down's choice is <see cref="ChooseAsync"/>, a text box's text <see cref="TypeAsync"/>). A value box
    /// (<see cref="Bbs757EfbItem.ValueBox"/>) is BlueBird's button and is tapped as one: BlueBird opens its dialog over the page, which the
    /// read that finds it says.
    /// </summary>
    public async Task ClickAsync(Bbs757EfbPressed pressed, CancellationToken ct)
    {
        switch (pressed.Item.Kind)
        {
            case Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox:
                await SwitchAsync(pressed, pressed.Item.On != true, ct);
                return;
            // A text box is typed into (TypeAsync): a tap alone would bring the keyboard up with nothing to type.
            case Bbs757EfbKind.Slider or Bbs757EfbKind.Choice:
            case Bbs757EfbKind.Field when !pressed.Item.ValueBox:
                return;
        }

        var target = await LocateAsync(pressed, ct);
        if (target == null) return;
        var item = target.Item;

        // A tile's words from its key: "btn:|FUEL TRUCK@56,28" is FUEL TRUCK, the state word left out; "" for any other control.
        string words = Bbs757EfbScreen.KeyWords(item.Key);
        var service = Bbs757GroundServices.ForTile(words);
        bool gpu = service == null && words.StartsWith("GPU", StringComparison.Ordinal);
        if (service != null) _io.ArmServiceEcho(service);
        if (gpu) _io.ArmGroundPowerEcho();
        bool tapped = false;
        try
        {
            tapped = await TapAsync(item, target, ct);
            if (!tapped) return;
            var after = await WaitForChangeAsync(target.Page, ct);
            if (after == null) return;   // nothing read: the window says why

            if (NavigationTarget(item) is { } page)
                await ArriveAsync(after, page, item, target.Page, ct);
            // A tab that became the current one says so; any other control its new words, or that the press changed nothing: said
            // here, once, since the window says neither.
            else if ((Bbs757EfbPress.TabOutcome(target.Page, after, item) ?? Bbs757EfbPress.ButtonOutcome(target.Page, after, item)) is { } outcome)
                _io.Alert(outcome);
        }
        finally
        {
            // The state read after the tap is the row's again: the echo ends with it (with nothing read when nothing was tapped).
            if (service != null)
                _io.SettleServiceEcho(service, tapped && !ct.IsCancellationRequested ? await _io.ReadVar(service.StateVar, ct) : null);
            if (gpu)
                _io.SettleGroundPowerEcho(tapped && !ct.IsCancellationRequested ? await _io.ReadRegistered(Bbs757SharedKeys.GpuConnected, ct) : null);
        }
    }

    /// <summary>The page a navigation tap asks for: Home for the window's Home button, the entry's words for a side-menu entry or a
    /// home-screen app; null for any other control (a tab changes the list under it, not the page's name).</summary>
    private static string? NavigationTarget(Bbs757EfbItem item) =>
        item.Key == HomeKey ? "Home"
        : item.Kind == Bbs757EfbKind.Menu && (item.Key.StartsWith("menu:", StringComparison.Ordinal) || item.Key.StartsWith("app:", StringComparison.Ordinal))
            ? item.Text
            : null;

    /// <summary>Reads the tablet again until it shows <paramref name="page"/>, for <see cref="NavigateMaxMs"/>, and says so when it
    /// never does.</summary>
    private async Task ArriveAsync(Bbs757EfbScreenModel after, string page, Bbs757EfbItem item, Bbs757EfbScreenModel pressedOn, CancellationToken ct)
    {
        long until = _io.NowMs() + NavigateMaxMs;
        var now = after;
        while (!PageIs(now, page) && _io.NowMs() < until)
        {
            await _io.Delay(NavigatePollMs, ct);
            if (await _io.Read(ct) is not { } next) return;   // nothing read: the window says why
            now = next;
        }
        if (PageIs(now, page)) return;
        // Only a read that NAMES another app or section than the tap asked for is reported; an app or section that was
        // not read says nothing (plan 19: do not call a page missing when only its section was not read).
        if (!ShowsOtherPage(now, item, pressedOn))
        {
            Log.Debug("BBS757", $"EFB reader: tapped '{page}', page reads '{now.PageName}'; app/section not read, nothing said");
            return;
        }
        Log.Debug("BBS757", $"EFB reader: tapped '{page}', page reads '{now.PageName}'");
        _io.Alert(NotShownText(page));
    }

    /// <summary>Whether the read names another place than the tap asked for: another app than Home, the home-screen app or the
    /// pressed page's app, or in that app a section that is read and is not the entry's. An unread app or section says nothing.</summary>
    internal static bool ShowsOtherPage(Bbs757EfbScreenModel now, Bbs757EfbItem pressed, Bbs757EfbScreenModel pressedOn)
    {
        if (string.IsNullOrWhiteSpace(now.App)) return false;
        if (pressed.Key == HomeKey) return !now.App.Equals("HOME", StringComparison.OrdinalIgnoreCase);
        if (pressed.Key.StartsWith("app:", StringComparison.Ordinal))
            return !now.App.Equals(pressed.Key["app:".Length..], StringComparison.OrdinalIgnoreCase);
        if (!now.App.Equals(pressedOn.App, StringComparison.OrdinalIgnoreCase)) return true;
        return !string.IsNullOrWhiteSpace(now.Section) && !PageIs(now, pressed.Text);
    }

    /// <summary>Whether the page read is the one a navigation tap asked for (an app's or a section's name).</summary>
    public static bool PageIs(Bbs757EfbScreenModel? m, string expect)
    {
        if (m == null) return false;
        string want = expect.Replace(" (current page)", "").Trim();
        return m.PageName.Equals(want, StringComparison.OrdinalIgnoreCase)
            || m.PageName.StartsWith(want + ",", StringComparison.OrdinalIgnoreCase)
            || m.PageName.EndsWith(", " + want, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A navigation the tablet never showed.</summary>
    public static string NotShownText(string page) => "The tablet did not show " + page + ". Try again, or press F5 to read the tablet again.";

    // ------------------------------------------------------------------ switches and check boxes

    /// <summary>
    /// A switch or check box set to <paramref name="wantOn"/>: tapped only when the tablet does not show the wish already. A row of
    /// the Settings pages that is one of BlueBird's settings (<see cref="Bbs757EfbSettingsMatch.SettingFor"/>) is judged by its file:
    /// a switch must store its wished state; a setting of two choices (the CPDLC network, the units, the EHSI up reference) the
    /// choice that was not set before the tap; the TOD pause distance switched off its empty box. A row that is no setting, or a
    /// file that cannot be read, is judged by the page. Not followed: "{label} did not change." Followed, a switch or check box says
    /// nothing, since the screen reader said its new state; a switch of two words the window shows as a check box (a row that is no
    /// setting, or a two-choice setting whose word was not read) says its new word once. A two-choice row the window shows as a choice
    /// list is set by <see cref="ChoiceSwitchAsync"/>, and says nothing either.
    /// </summary>
    public Task SwitchAsync(Bbs757EfbPressed pressed, bool wantOn, CancellationToken ct) =>
        SwitchCoreAsync(pressed,
            (target, _) => Task.FromResult<bool?>(Bbs757EfbPress.NeedsTap(target.Item, wantOn) ? wantOn : null),
            (_, found) => found.On == wantOn, ct);

    /// <summary>
    /// A switch of two choices the window shows as a choice list ("Nosewheel steering": Realistic or Rudder, D4) set to its choice
    /// <paramref name="wanted"/> (the index of its word among the catalog's two, <see cref="Bbs757EfbSettingsMatch.ChoiceIndex"/>).
    /// Whether it needs its tap is decided on the press's own fresh read (the final review's I5, B757-18): the word the relocated row
    /// shows now, else the choice BlueBird's file holds; when that is the wish, nothing is tapped (B757-19's precision check). Else one
    /// tap, judged as <see cref="SwitchAsync"/> judges a setting, the page following when the row shows the wished word. Followed, it
    /// says nothing: the screen reader said the chosen word as the pilot picked it (CORE-7, the owner's ruling of 2026-11-07, which
    /// replaced the 2026-10-30 "says its new choice once"); only "{label} did not change." is said.
    /// </summary>
    public Task ChoiceSwitchAsync(Bbs757EfbPressed pressed, int wanted, CancellationToken ct) =>
        SwitchCoreAsync(pressed,
            async (target, c) => await ShownChoiceAsync(target, c) == wanted ? null : target.Item.On != true,
            (target, found) => Bbs757EfbSettingsMatch.SettingFor(target.Page.PageName, found) is { } s
                && Bbs757EfbSettingsMatch.ChoiceIndex(s, found.Value) == wanted, ct);

    /// <summary>The choice a two-choice row shows on the press's fresh read: its word, else the choice BlueBird's file holds; -1 when
    /// neither tells.</summary>
    private async Task<int> ShownChoiceAsync(Target target, CancellationToken ct)
    {
        if (Bbs757EfbSettingsMatch.SettingFor(target.Page.PageName, target.Item) is not { } setting) return -1;
        int shown = Bbs757EfbSettingsMatch.ChoiceIndex(setting, target.Item.Value);
        if (shown < 0 && await _io.ReadSetting(setting, ct) is { } stored)
            shown = setting.Choices.ToList().FindIndex(c => c.Stored == stored);
        return shown;
    }

    /// <param name="wantFor">The state the switch is tapped toward, from the press's fresh read; null when the read shows the wish
    /// already, so a tap would undo it.</param>
    /// <param name="follows">Whether the switch, found again on a later read, shows the wish (the page's judgment).</param>
    private async Task SwitchCoreAsync(Bbs757EfbPressed pressed, Func<Target, CancellationToken, Task<bool?>> wantFor,
        Func<Target, Bbs757EfbItem, bool> follows, CancellationToken ct)
    {
        if (pressed.Item.Kind is not (Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox)) return;
        var target = await LocateAsync(pressed, ct);
        if (target == null) return;
        var item = target.Item;
        if (await wantFor(target, ct) is not bool wantOn) return;   // it shows the wish already: a tap would undo it

        var setting = Bbs757EfbSettingsMatch.SettingFor(target.Page.PageName, item);
        var wish = setting == null ? null : await FileWishAsync(setting, item, wantOn, ct);
        IReadOnlyList<Bbs757Setting> onBefore = setting != null && wantOn ? await GroupOnAsync(setting, target.Page, ct) : Array.Empty<Bbs757Setting>();

        if (!await TapAsync(item, target, ct)) return;
        long tappedAt = _io.NowMs();
        var changed = await WaitForChangeAsync(target.Page, ct);
        if (changed is not { } after) return;   // nothing read: the window says why

        var verdict = setting != null && wish != null ? await FileHoldsAsync(setting, wish.Accepts, ct) : Verdict.Unread;
        Bbs757EfbItem? shown = null;   // the switch as the page shows it once it followed
        if (verdict == Verdict.Unread) (verdict, shown) = await PageFollowsAsync(target, after, found => follows(target, found), tappedAt, ct);
        if (verdict == Verdict.Differs)
        {
            _io.Alert(Bbs757EfbPress.NotChangedText(Bbs757EfbPress.SpokenName(target.Page, item)));
            return;
        }
        if (verdict != Verdict.Holds) return;

        // A row the window shows as a choice list: the screen reader said its new word as the pilot chose it (item 18, CORE-7).
        if (Bbs757EfbElements.ShowsAsChoiceList(pressed.Item, pressed.Page.PageName)) return;
        // A callout is an On/Off switch, so a switch of two choices never has a callout to name: one alert at most.
        if (NewChoice(item, setting, wish, shown) is { } choice)
            _io.Alert(Bbs757EfbPress.ChoiceText(Bbs757EfbPress.SpokenName(target.Page, item), choice));
        else if (onBefore.Count > 0 && await TurnedOffAsync(onBefore, after, ct) is { } turnedOff)
            _io.Alert(turnedOff);
    }

    /// <summary>The new choice a switch of two choices says once its tap landed; null for any other switch.</summary>
    private static string? NewChoice(Bbs757EfbItem item, Bbs757Setting? setting, Wish? wish, Bbs757EfbItem? shown)
    {
        if (item.Kind != Bbs757EfbKind.Toggle) return null;
        if (wish?.Word is { } word) return word;
        return setting is null or { Kind: Bbs757SettingKind.Choice } ? Bbs757EfbPress.NewChoiceWord(item.Value, shown?.Value) : null;
    }

    /// <summary>What a switch's tap must leave in BlueBird's file.</summary>
    private sealed record Wish(Func<Bbs757Value, bool> Accepts, string? Word = null);

    /// <summary>What BlueBird's file must hold once a switch's tap landed; null when the setting names no value to judge by.</summary>
    private async Task<Wish?> FileWishAsync(Bbs757Setting setting, Bbs757EfbItem row, bool wantOn, CancellationToken ct)
    {
        switch (setting.Kind)
        {
            case Bbs757SettingKind.OnOff:
                return Bbs757EfbSettingsMatch.StoredFor(setting, wantOn, null) is { } on ? new Wish(v => v == on) : null;
            case Bbs757SettingKind.Choice when setting.Choices.Count == 2:
            {
                int before = Bbs757EfbSettingsMatch.ChoiceIndex(setting, row.Value);
                if (before < 0 && await _io.ReadSetting(setting, ct) is { } stored)
                    before = setting.Choices.ToList().FindIndex(c => c.Stored == stored);
                if (before < 0) return null;
                var other = setting.Choices[1 - before];
                return new Wish(v => v == other.Stored, other.Word);
            }
            case Bbs757SettingKind.Number when setting.EmptyStored is { } empty:
                return new Wish(wantOn ? v => v != empty : v => v == empty);
            default:
                return null;
        }
    }

    /// <summary>The other members of the setting's exclusive group that read on before the tap.</summary>
    private async Task<IReadOnlyList<Bbs757Setting>> GroupOnAsync(Bbs757Setting setting, Bbs757EfbScreenModel page, CancellationToken ct)
    {
        var on = new List<Bbs757Setting>();
        foreach (var other in Bbs757EfbSettingsMatch.SwitchedOffWith(setting))
            if (await IsOnAsync(other, page, ct) == true) on.Add(other);
        return on;
    }

    /// <summary>"2500 turned off" for each of <paramref name="onBefore"/> that is off now; null when none is.</summary>
    private async Task<string?> TurnedOffAsync(IReadOnlyList<Bbs757Setting> onBefore, Bbs757EfbScreenModel after, CancellationToken ct)
    {
        var off = new List<string>();
        foreach (var other in onBefore)
            if (await IsOnAsync(other, after, ct) == false) off.Add(Bbs757EfbSettingsMatch.TurnedOffText(other));
        return off.Count == 0 ? null : string.Join(", ", off);
    }

    /// <summary>Whether a switch setting is on: from BlueBird's file, else from its row on <paramref name="page"/>; null when
    /// neither tells.</summary>
    private async Task<bool?> IsOnAsync(Bbs757Setting setting, Bbs757EfbScreenModel page, CancellationToken ct)
    {
        if (await _io.ReadSetting(setting, ct) is { Type: Bbs757ValueType.Bool } stored) return stored.Bool;
        var row = page.Items.FirstOrDefault(i => Bbs757EfbSettingsMatch.SettingFor(page.PageName, i)?.Path == setting.Path);
        return row?.On;
    }

    private enum Verdict { Holds, Differs, Unread }

    /// <summary>BlueBird's file read for <see cref="FileMaxMs"/> until it holds the wish.</summary>
    private async Task<Verdict> FileHoldsAsync(Bbs757Setting setting, Func<Bbs757Value, bool> wish, CancellationToken ct)
    {
        long until = _io.NowMs() + FileMaxMs;
        bool read = false;
        while (true)
        {
            if (await _io.ReadSetting(setting, ct) is { } stored)
            {
                if (wish(stored)) return Verdict.Holds;
                read = true;
            }
            if (_io.NowMs() >= until) return read ? Verdict.Differs : Verdict.Unread;
            await _io.Delay(FilePollMs, ct);
        }
    }

    /// <summary>The page read again until the switch shows the wish (<paramref name="follows"/>), for <see cref="FollowMaxMs"/> from its
    /// tap.</summary>
    private async Task<(Verdict Verdict, Bbs757EfbItem? Shown)> PageFollowsAsync(Target target, Bbs757EfbScreenModel after,
        Func<Bbs757EfbItem, bool> follows, long tappedAt, CancellationToken ct)
    {
        long until = tappedAt + FollowMaxMs;
        var now = after;
        while (true)
        {
            if (Bbs757EfbPress.Relocate(target.Page, now, target.Item) is { } found && follows(found)) return (Verdict.Holds, found);
            if (_io.NowMs() >= until) return (Verdict.Differs, null);
            await _io.Delay(FollowPollMs, ct);
            if (await _io.Read(ct) is not { } next) return (Verdict.Unread, null);
            now = next;
        }
    }

    // ------------------------------------------------------------------ sliders

    /// <summary>
    /// A slider set to <paramref name="value"/>: one of its choices (<see cref="Bbs757EfbElements.OptionsFor"/>'s words), or else a
    /// percent along its track. A choice: the choice's place tapped (<see cref="Bbs757EfbPress.ChoiceX"/>), read back, and stepped
    /// toward it, at most <see cref="Bbs757EfbPress.MaxSliderTaps"/> taps, none once a question came up over the page (then nothing more
    /// is said); then judged by BlueBird's file when the row is one of its
    /// settings, else by the page. A percent: one tap at its place (along a vertical track for a slider taller than wide; along the
    /// range of the knob's center for a slider whose knob BlueBird's memory gives, <see cref="Bbs757EfbSlider.KnobRead"/>), judged by
    /// where that knob is after the tap when the fresh read has it, else by the part of the track its value shows ("50%", "8/16") when
    /// it shows one, else by whether the tap changed the page or the slider's words. Not set: "{label} could not be set to {value}."
    /// </summary>
    public async Task SliderAsync(Bbs757EfbPressed pressed, string value, CancellationToken ct)
    {
        if (pressed.Item.Kind != Bbs757EfbKind.Slider) return;
        var options = Bbs757EfbElements.OptionsFor(pressed.Item, pressed.Page.PageName) ?? Array.Empty<string>();
        int wanted = -1;
        for (int index = 0; index < options.Count && wanted < 0; index++)
            if (string.Equals(options[index], value, StringComparison.OrdinalIgnoreCase)) wanted = index;
        if (wanted >= 0)
            await ChoiceAsync(pressed, options, wanted, ct);
        else
            await PercentAsync(pressed, value, ct);
    }

    private async Task ChoiceAsync(Bbs757EfbPressed pressed, IReadOnlyList<string> options, int wanted, CancellationToken ct)
    {
        var target = await LocateAsync(pressed, ct);
        if (target?.Item.Slider is not { } track) return;
        var row = target.Item;
        var page = target.Page;
        int read = Bbs757EfbElements.CurrentChoice(row, page.PageName, options);
        if (read == wanted) return;   // it shows the wish already

        int x = Bbs757EfbPress.ChoiceX(track, wanted, options.Count);
        int y = target.At.Y;
        for (int taps = 1; ; taps++)
        {
            if (!await TapAsync(row, target, ct, new Point(x, y), on: page)) return;
            if (await WaitForChangeAsync(page, ct) is not { } after) return;   // nothing read: the window says why
            // A question that came up since takes no tap on the page under it (B757-18): no next step, and nothing more said (the read
            // that found the question said it).
            if (after.Question != null && !Bbs757EfbPress.SameQuestion(page.Question, after.Question)) return;
            var now = Bbs757EfbPress.Relocate(page, after, row);
            read = now == null ? -1 : Bbs757EfbElements.CurrentChoice(now, after.PageName, options);
            if (read == wanted || Bbs757EfbPress.NextChoiceX(track, x, wanted, read, options.Count, taps) is not int next) break;
            x = next;
            page = after;
            row = now!;
            y = Bbs757EfbPress.TapPoint(page, row)?.Y ?? y;
        }

        var setting = Bbs757EfbSettingsMatch.SettingFor(target.Page.PageName, target.Item);
        var verdict = setting != null && Bbs757EfbSettingsMatch.StoredFor(setting, null, options[wanted]) is { } stored
            ? await FileHoldsAsync(setting, v => v == stored, ct)
            : Verdict.Unread;
        if (verdict == Verdict.Holds || (verdict == Verdict.Unread && read == wanted)) return;
        _io.Alert(Bbs757EfbPress.CouldNotSetText(Bbs757EfbPress.SpokenName(target.Page, target.Item), options[wanted]));
    }

    private async Task PercentAsync(Bbs757EfbPressed pressed, string value, CancellationToken ct)
    {
        // The window's range control writes the number, invariant. Anything else is no place on the track: nothing is tapped.
        if (!NumberParsing.TryParseExternal(value, out double percent) || !double.IsFinite(percent))
        {
            _io.Alert(Bbs757EfbPress.CouldNotSetText(Bbs757EfbPress.SpokenName(pressed.Page, pressed.Item), value));
            return;
        }
        percent = Math.Clamp(percent, 0, 100);

        var target = await LocateAsync(pressed, ct);
        if (target?.Item.Slider is not { } track) return;
        if (!await TapAsync(target.Item, target, ct, PercentPoint(target, track, percent))) return;
        if (await WaitForChangeAsync(target.Page, ct) is not { } after) return;   // nothing read: the window says why

        // Judged by where BlueBird's knob is now when the fresh read has it (within the slack: the weight beside a cargo slider may change
        // while its knob stays); else by the value the slider shows now when it shows a part of its track (a percent, a count of its
        // most: within the slack); else by whether the tap changed anything: the page (the weight beside a cargo slider), or the
        // slider's own words. A value that shows nowhere ("") never judges it alone: it stays "" whatever the tap did.
        if (Bbs757EfbPress.Relocate(target.Page, after, target.Item) is { } now
            && (now.Slider is { KnobRead: true } knob
                ? Math.Abs(knob.PercentOf(knob.KnobX) - percent) <= PercentSlack
                : Bbs757EfbSlider.PercentShown(now.Value) is double shown
                ? Math.Abs(shown - percent) <= PercentSlack
                : !Bbs757EfbPress.PageUnchanged(target.Page, after)
                  || !string.Equals(now.Value, target.Item.Value, StringComparison.Ordinal)
                  || !string.Equals(now.Text, target.Item.Text, StringComparison.Ordinal)))
            return;
        _io.Alert(Bbs757EfbPress.CouldNotSetText(Bbs757EfbPress.SpokenName(target.Page, target.Item),
            percent.ToString("0.#", CultureInfo.InvariantCulture) + " percent"));
    }

    /// <summary>Where a set to <paramref name="percent"/> taps a slider: along a horizontal track on the row's tap point, along a
    /// vertical one at its x middle, kept inside the part of its box that shows, so no tap lands outside the control's clip. With
    /// BlueBird's knob read the track is its center's range, inside the box, so the tap is where the knob's center is to go.</summary>
    private static Point PercentPoint(Target target, Bbs757EfbSlider track, double percent)
    {
        if (!track.Vertical) return new Point(track.At(percent), target.At.Y);
        var box = target.Item.Box;
        var clip = Bbs757EfbPress.ClipOf(target.Item);
        int top = Math.Max(box.Top, clip.Top), bottom = Math.Min(box.Bottom, clip.Bottom) - 1;
        return new Point(track.AcrossX, Math.Clamp(track.At(percent), top, Math.Max(top, bottom)));
    }

    // ------------------------------------------------------------------ drop-downs (plan 21, Task 7)

    /// <summary>
    /// A drop-down set to its entry <paramref name="entry"/> (the words the window's choice list sends), on the tablet as a sighted pilot
    /// chooses (B757-27), through the runner's discipline (B757-18: a fresh read, the control relocated and brought into view, a tap or
    /// nothing). The fresh read shows it with the entry already: nothing tapped, nothing said; but with its list open (the pilot's own mouse
    /// left it so, or a choice that failed), the entry's row is tapped, which closes the list with nothing changed (measured on the runway
    /// list, 2026-11-07), and judged as a choice is (Task 7's concern 2, the final review's I2). An entry it does not list: nothing tapped,
    /// "{name} could not be set to {entry}." Closed, its face is tapped and the tablet read until its list is open with its rows laid out
    /// (at most <see cref="ChangeMaxMs"/>); open already, it is never tapped open again. The entry's row is
    /// the fresh read's (<see cref="Bbs757EfbPress.RowOf"/>), tapped in the middle of the part of it the list shows when at least
    /// <see cref="Bbs757EfbPress.MinShownPx"/> does (<see cref="Bbs757EfbPress.RowTapPoint"/>). It is never dragged into view: an open list
    /// scrolls by no input (measured live 2026-11-07 on PERF calc's Surface list), so a row the list does not show, or shows less of, is
    /// "{name} could not be set to {entry}: the tablet's list does not show it." at once (<see cref="Bbs757EfbPress.NotShownInListText"/>, the
    /// final review's M3), the row not tapped. After the tap the tablet is read until the list is closed and the face
    /// shows the entry (at most <see cref="ChangeMaxMs"/>): then nothing is said (the owner's ruling of 2026-11-07 for choice lists: the screen
    /// reader said the entry as the pilot picked it, CORE-7, as B757-20's two-choice rows); otherwise "{name} could not be set to {entry}."
    /// once. A list the runner opened itself and then could not choose from is closed again after that one alert, by its current entry's row
    /// when that row shows (<see cref="CloseListAsync"/>), so no page control is left under it (the final review's I2). A drop-down whose face
    /// is a prompt (it matches none of its entries: Systems' SELECT) is judged by its list closing alone: its face never shows a choice, and
    /// the page shown already, chosen again, changes nothing; it has no current entry, so its list is never closed by one. A question up over
    /// the page, or one raised by a tap, holds the choice back with nothing more tapped and nothing said (the read that found it said it). True
    /// when the tablet shows the entry chosen at the end (or showed it already); false otherwise, the window then shown what the tablet shows.
    /// </summary>
    public async Task<bool> ChooseAsync(Bbs757EfbPressed pressed, string entry, CancellationToken ct)
    {
        if (pressed.Item.Kind != Bbs757EfbKind.Choice) return false;
        var target = await LocateAsync(pressed, ct);
        if (target == null) return false;
        var page = target.Page;
        var dropDown = target.Item;
        string name = Bbs757EfbPress.SpokenName(page, dropDown);
        bool prompt = dropDown.Value.Length == 0;   // its face shows none of its entries: the face cannot tell the choice
        if (string.Equals(dropDown.Value, entry, StringComparison.Ordinal))
            // It shows the wish already; with its list open, the entry's row closes it with nothing changed.
            return !dropDown.Open || prompt || await CloseOnShownEntryAsync(page, dropDown, name, entry, ct);
        if (dropDown.Entries?.Contains(entry, StringComparer.Ordinal) != true) return CouldNotChoose(name, entry);

        bool openedHere = !dropDown.Open;   // the runner opens the list itself: one it then cannot choose from, it closes again
        if (openedHere && !await TapAsync(dropDown, target, ct)) return false;
        if (!ShowsRows(dropDown))
        {
            // Read until the list is open with its rows laid out (closed, BlueBird leaves them at [0,0 50x38]), at most ChangeMaxMs.
            var (seen, opened, found) = await WatchAsync(page, dropDown, (_, shownNow) => ShowsRows(shownNow), ct);
            if (seen is Seen.Unread or Seen.Asked) return false;   // the window says why; the question was said by the read that found it
            // Not open in time (or its rows not read): the face's tap did not open it, and nothing more is tapped (no row laid out to
            // close it by either).
            if (seen != Seen.Holds) return CouldNotChoose(name, entry);
            page = opened;
            dropDown = found!;
        }

        // The entry's row where the open list draws it, tapped in the part of it that shows. Never dragged into view: an open list scrolls
        // by no input (measured live 2026-11-07), and a drag's release over a row is a press nobody asked for. A row that shows less than
        // MinShownPx cannot be chosen on the tablet: it is not tapped, and a list the runner opened is closed again.
        if (Bbs757EfbPress.ListViewOf(dropDown) is not { } view || Bbs757EfbPress.RowOf(dropDown, entry) is not { } row
            || Bbs757EfbPress.RowTapPoint(view, row.Box) is not { } at)
        {
            _io.Alert(Bbs757EfbPress.NotShownInListText(name, entry));
            if (openedHere) await CloseListAsync(page, dropDown, ct);
            return false;
        }

        if (!await TapAsync(dropDown, new Target(dropDown, page, at), ct, at)) return false;
        var (outcome, after, left) = await WatchAsync(page, dropDown, (_, found) => Chose(found, entry, prompt), ct);
        if (outcome is Seen.Unread or Seen.Asked) return false;   // a question raised by the tap holds back any further step
        if (outcome == Seen.Holds) return true;
        CouldNotChoose(name, entry);
        if (openedHere) await CloseListAsync(after, left, ct);   // the row's tap left the list the runner opened open
        return false;
    }

    /// <summary>The entry the drop-down shows already picked while its list is open (<paramref name="dropDown"/>, the fresh read's): its row
    /// tapped (<see cref="CurrentRowPoint"/>), which closes the list with nothing changed (measured), and the tablet read until the list is
    /// closed showing the entry (<see cref="Chose"/>, at most <see cref="ChangeMaxMs"/>). True once it is, nothing said, and when the row cannot
    /// be tapped (not laid out yet, or showing less than <see cref="Bbs757EfbPress.MinShownPx"/>: the face shows the wish, nothing to choose);
    /// "{name} could not be set to {entry}." when the list stayed open; false, silent, under a question raised meanwhile or with nothing read,
    /// and false, said by the tap, when the row is under a notification or another list.</summary>
    private async Task<bool> CloseOnShownEntryAsync(Bbs757EfbScreenModel page, Bbs757EfbItem dropDown, string name, string entry, CancellationToken ct)
    {
        if (CurrentRowPoint(dropDown) is not { } at) return true;
        if (!await TapAsync(dropDown, new Target(dropDown, page, at), ct, at)) return false;
        var (outcome, _, _) = await WatchAsync(page, dropDown, (_, found) => Chose(found, entry, prompt: false), ct);
        if (outcome is Seen.Unread or Seen.Asked) return false;
        return outcome == Seen.Holds || CouldNotChoose(name, entry);
    }

    /// <summary>
    /// Closes a list <see cref="ChooseAsync"/> opened itself and then could not choose from (the final review's I2: left open, it covered the
    /// page controls under it, and the window had no way to close it), after the choice's one alert: the drop-down's current entry's row is
    /// tapped (<see cref="CurrentRowPoint"/>; measured on the runway list, 2026-11-07: tapping the entry shown closes the list with nothing
    /// changed) and the tablet read until the list is closed (at most <see cref="ChangeMaxMs"/>, so the next press finds it so). Nothing is
    /// said: the choice's failure was. Nothing is tapped when <paramref name="dropDown"/>, the last read's, is no longer open, when its rows
    /// are not laid out, for a prompt drop-down (no current entry), when the current entry's row shows less than
    /// <see cref="Bbs757EfbPress.MinShownPx"/>, and when the row lies under a notification or another list (<see cref="TapAsync"/>, quiet).
    /// </summary>
    private async Task CloseListAsync(Bbs757EfbScreenModel page, Bbs757EfbItem? dropDown, CancellationToken ct)
    {
        if (dropDown is not { Open: true } || CurrentRowPoint(dropDown) is not { } at) return;
        if (!await TapAsync(dropDown, new Target(dropDown, page, at), ct, at, speak: false)) return;
        await WatchAsync(page, dropDown, (_, found) => found?.Open != true, ct);
    }

    /// <summary>Where an open drop-down's current entry's row is tapped (<see cref="Bbs757EfbPress.RowTapPoint"/>); null when its rows are not
    /// laid out, when it shows no entry (a prompt), and when that row shows less than <see cref="Bbs757EfbPress.MinShownPx"/>.</summary>
    private static Point? CurrentRowPoint(Bbs757EfbItem dropDown) =>
        dropDown.Value.Length > 0 && ShowsRows(dropDown) && Bbs757EfbPress.ListViewOf(dropDown) is { } view
            && Bbs757EfbPress.RowOf(dropDown, dropDown.Value) is { } row
            ? Bbs757EfbPress.RowTapPoint(view, row.Box)
            : null;

    /// <summary>Whether a read shows the drop-down open with its list's rows laid out (<see cref="Bbs757EfbPress.RowsLaidOut"/>).</summary>
    private static bool ShowsRows(Bbs757EfbItem? dropDown) => dropDown is { Open: true, List: { } list } && Bbs757EfbPress.RowsLaidOut(list);

    /// <summary>Whether a choice of <paramref name="entry"/> landed, from the drop-down found in the read after its row's tap: its list
    /// closed and its face showing the entry; for a prompt drop-down (<paramref name="prompt"/>: Systems' SELECT, whose face shows no entry
    /// whatever was chosen) its list closed, or the drop-down gone with the page it was on. A prompt's choice changes the page, but choosing
    /// the page shown already changes nothing and took all the same (the coordinator's ruling of the fix round), so the page is no judge.</summary>
    private static bool Chose(Bbs757EfbItem? found, string entry, bool prompt) =>
        prompt
            ? found?.Open != true
            : found is { Open: false } && string.Equals(found.Value, entry, StringComparison.Ordinal);

    /// <summary>Says that the drop-down <paramref name="name"/> was not set to <paramref name="entry"/>; false, for the caller to return.</summary>
    private bool CouldNotChoose(string name, string entry)
    {
        _io.Alert(Bbs757EfbPress.CouldNotSetText(name, entry));
        return false;
    }

    private enum Seen { Holds, Differs, Unread, Asked }

    /// <summary>The tablet read again after a tap until <paramref name="holds"/> says so of the read and the control found in it again
    /// (<see cref="Bbs757EfbPress.Relocate"/> from <paramref name="before"/>), at most <see cref="ChangeMaxMs"/>; Asked as soon as a read
    /// shows a question over the page (no next step: B757-18), Unread when a read failed. The last read, and the control in it, come
    /// back with the verdict.</summary>
    private async Task<(Seen Seen, Bbs757EfbScreenModel Page, Bbs757EfbItem? Item)> WatchAsync(Bbs757EfbScreenModel before, Bbs757EfbItem item,
        Func<Bbs757EfbScreenModel, Bbs757EfbItem?, bool> holds, CancellationToken ct)
    {
        long until = _io.NowMs() + ChangeMaxMs;
        var page = before;
        while (true)
        {
            await _io.Delay(ChangePollMs, ct);
            if (await _io.Read(ct) is not { } now) return (Seen.Unread, page, null);
            page = now;
            if (now.Question != null) return (Seen.Asked, now, null);
            var found = Bbs757EfbPress.Relocate(before, now, item);
            if (holds(now, found)) return (Seen.Holds, now, found);
            if (_io.NowMs() >= until) return (Seen.Differs, now, found);
        }
    }

    // ------------------------------------------------------------------ typing into a field (plan 22, Task 3)

    /// <summary>After a field's tap, the longest wait for BlueBird's keyboard to read up and settled typing into it. Measured 2026-11-07
    /// ([B757-23]): it settled 1.1 to more than 1.9 s after the tap, and a key tapped before lands on the page
    /// beneath (the box loses focus and the keyboard slides away).</summary>
    public const int KeyboardReadyMaxMs = 5000;

    /// <summary>BlueBird's key that puts the keyboard down; it keeps the text on every box measured (Enter differs from box to box).</summary>
    private const string KeyboardDownKey = "btn_keyboard_down";

    /// <summary>
    /// A field (one of BlueBird's text boxes, on the page or a dialog's row) set to <paramref name="text"/>, typed on BlueBird's own
    /// keyboard or numeric keypad as a sighted pilot types it (plan 22; measured 2026-11-07, [B757-29]), through the
    /// runner's discipline (B757-18: a fresh read, the field relocated and brought into view, a tap or nothing):
    /// <list type="number">
    /// <item>The first fresh read, before any drag or tap (<see cref="TypingWanted"/>): a field with no caret read
    /// (<see cref="Bbs757EfbItem.TextState"/> null: a value box, whose tap would open BlueBird's dialog, or a torn read) is never typed,
    /// nothing tapped, "{name} not typed."; the field holding the text exactly (ordinal), nothing tapped, nothing said; a character of the
    /// text on no key of what the field opens, keyboard or keypad (<see cref="MissingKey"/>), no tap of any kind and "{name}: the tablet's
    /// keyboard has no {character}." (plan 22's Global Constraints).</item>
    /// <item>The field is tapped (<see cref="TapAsync"/>, as any control), and the tablet read until its keyboard is up, settled and typing
    /// into it (<see cref="KeyboardUpFor"/>, at most <see cref="KeyboardReadyMaxMs"/>; up for another box counts as not): never a key before
    /// (B757-23). Not in time: a keyboard that the last read shows up and settled for another box is put down (its down key, or the keypad's
    /// OK when that read says the keypad shows), so it never stays over the page, then "{name}: the tablet's keyboard did not come up." and
    /// nothing more tapped; one still sliding is not tapped (a key's place then is the page's). A question that came up over the page
    /// meanwhile: nothing more tapped or said (the read that found it said it).</item>
    /// <item>The taps are planned from that read (<see cref="Bbs757EfbTyping"/>): a box that opens the keypad
    /// (<see cref="Bbs757EfbTextState.Keypad"/>) on the keypad where that read draws it (it moves with its box); any other on the keyboard,
    /// from its layer and the caret the tap put, clearing the old text around it. A character no key carries, which the read before the tap
    /// could not tell (the keyboard's keys not read then): the keyboard put down again, nothing typed, "{name}: the tablet's keyboard has no
    /// {character}."; nothing to plan with (the box's state, the keys or the keypad not read): put down, "{name} not typed."</item>
    /// <item>The taps go in runs of at most <see cref="Bbs757EfbTyping.MaxTapsPerRun"/>, <see cref="Bbs757EfbTyping.TapGapMs"/> apart, one
    /// agent run each (<see cref="Bbs757EfbPressIo.TapRun"/>). A run that does not go out stops the typing: the keyboard put down when it is
    /// still up, and "{name} not typed." (what the field reads is not said; the window shows it). Before each run after the first the
    /// tablet is read again (B757-18): a keyboard no longer up for the field (the pilot's own mouse put it down) takes no more keys, since
    /// a key's place is then the page's: "{name} not typed.", nothing to close.</item>
    /// <item>The keyboard put down with its down key (it keeps the text), a keypad box finished with the keypad's OK (it commits a box with no
    /// dialog, and in the fuel and weight dialog closes only the keypad: INSERT stays the pilot's answer), each only while the read before it
    /// shows the keyboard up for the field (with it down, the key's place is the page's).</item>
    /// <item>The tablet read until the field's text changes from what it held, or reads the text (at most <see cref="ChangeMaxMs"/>), and
    /// said once (CORE-7: the screen reader said the pilot's keys in the window's edit box, never what the tablet took): "{name}: {value}"
    /// when it reads the text, case aside (PERF calc's Airport box upper-cases it); else "{name} reads {value}." (BlueBird's boxes filter
    /// what they take, and a full box drops keys); a field that reads empty (emptied as asked, or every key dropped): "{name} is empty.";
    /// a page field gone after a close that went out, the same from the line its row shows in its place (<see cref="LineFor"/>: the SimBrief
    /// row hides its box once saved); with neither: "{name} is no longer on the tablet."</item>
    /// </list>
    /// The text goes as typed: no trimming, no case change, no number parsing (BlueBird validates it). The text and the field's value are
    /// never logged, nor anything that gives away their length (the taps, the runs, whether it read back as typed): a log line names the
    /// field alone, with a time at most (D2).
    /// </summary>
    public async Task TypeAsync(Bbs757EfbPressed pressed, string text, CancellationToken ct)
    {
        if (pressed.Item.Kind != Bbs757EfbKind.Field) return;
        var target = await LocateAsync(pressed, ct, (page, found) => TypingWanted(page, found, text));
        if (target == null) return;
        var field = target.Item;
        string name = Bbs757EfbPress.SpokenName(target.Page, field);
        if (string.Equals(field.Value, text, StringComparison.Ordinal)) return;   // it holds the text already (read again after a drag)

        if (!await TapAsync(field, target, ct)) return;
        long tappedAt = _io.NowMs();
        Bbs757EfbScreenModel up;
        while (true)
        {
            await _io.Delay(ChangePollMs, ct);
            if (await _io.Read(ct) is not { } now) return;   // nothing read: the window says why
            // A question that came up over the page takes no key (B757-18); the read that found it said it.
            if (now.Question != null && !Bbs757EfbPress.SameQuestion(target.Page.Question, now.Question)) return;
            if (KeyboardUpFor(now, field))
            {
                up = now;
                break;
            }
            if (_io.NowMs() - tappedAt < KeyboardReadyMaxMs) continue;
            Log.Debug("BBS757", $"EFB typing: {field.Name}: the keyboard did not come up in {KeyboardReadyMaxMs.ToString(CultureInfo.InvariantCulture)} ms");
            // A keyboard the tap brought up for another box never stays over the page: put down as that read draws it.
            if (now.KeyboardState is { Shown: true, Ready: true } other && ClosePoint(now, other.Keypad) is { } close && !await _io.Tap(close, ct))
                Log.Debug("BBS757", $"EFB typing: {field.Name}: the keyboard's close tap did not go out");
            _io.Alert(Bbs757EfbPress.NoKeyboardText(name));
            return;
        }

        // The box as the read that shows the keyboard up has it: the caret the tap put, whether it opens the keypad, the text it holds.
        var box = FieldIn(target.Page, up, field);
        var state = box?.TextState;
        bool keypad = state?.Keypad ?? up.KeyboardState!.Keypad;
        char? missing = null;
        Bbs757EfbTypingPlan? plan = null;
        if (box != null && state != null)
        {
            if (state.Keypad) plan = Bbs757EfbTyping.PlanKeypad(up.KeyboardState!.KeypadBox, text, out missing);
            else if (up.Keyboard is { } keyboard) plan = Bbs757EfbTyping.Plan(keyboard, up.KeyboardState!, text, state.Caret, box.Value.Length, out missing);
        }
        if (plan == null)
        {
            Log.Debug("BBS757", $"EFB typing: {field.Name}: {(missing != null ? "a character on no key" : "nothing to plan with")}; nothing typed");
            await CloseKeyboardAsync(field, keypad, ct);
            _io.Alert(missing is char ch ? Bbs757EfbPress.NoKeyText(name, ch) : Bbs757EfbPress.NotTypedText(name));
            return;
        }

        var runs = Bbs757EfbTyping.Runs(plan);
        Log.Debug("BBS757", $"EFB typing: {field.Name}: keyboard up {(_io.NowMs() - tappedAt).ToString(CultureInfo.InvariantCulture)} ms after the tap");
        for (int r = 0; r < runs.Count; r++)
        {
            if (r > 0)
            {
                // A fresh read before each run after the first (B757-18): a keyboard put down meanwhile takes no more keys.
                if (await _io.Read(ct) is not { } now) return;   // nothing read: the window says why
                if (!KeyboardUpFor(now, field))
                {
                    Log.Debug("BBS757", $"EFB typing: {field.Name}: the keyboard is no longer up for it; stopped");
                    _io.Alert(Bbs757EfbPress.NotTypedText(name));
                    return;
                }
            }
            if (await _io.TapRun(runs[r], Bbs757EfbTyping.TapGapMs, ct)) continue;
            if (ct.IsCancellationRequested) return;   // the window hid or the bridge stopped: nothing said
            Log.Debug("BBS757", $"EFB typing: {field.Name}: a run of taps did not go out; stopped");
            await CloseKeyboardAsync(field, keypad, ct);
            _io.Alert(Bbs757EfbPress.NotTypedText(name));
            return;
        }
        bool closed = await CloseKeyboardAsync(field, keypad, ct);
        await ReadBackAsync(up, box!, text, name, closed, ct);
    }

    /// <summary>Whether a read shows BlueBird's keyboard up, settled and typing into <paramref name="field"/> (its node name the keyboard's
    /// target). The keyboard's fields are read one at a time, so shown, ready and the target are needed together.</summary>
    private static bool KeyboardUpFor(Bbs757EfbScreenModel m, Bbs757EfbItem field) =>
        m.KeyboardState is { Shown: true, Ready: true } k && k.Target.Length > 0 && string.Equals(k.Target, field.Name, StringComparison.Ordinal);

    /// <summary>
    /// Puts BlueBird's keyboard down after a typing, the text kept: the tablet read again, and while it shows the keyboard up for
    /// <paramref name="field"/>, one tap on its down key, or on the keypad's OK for a keypad box (where that read draws the keypad). Up for
    /// another box, going down, or not read: nothing tapped, since with the keyboard down a key's place is the page's. True when the close
    /// tap went out.
    /// </summary>
    private async Task<bool> CloseKeyboardAsync(Bbs757EfbItem field, bool keypad, CancellationToken ct)
    {
        if (await _io.Read(ct) is not { } now || !KeyboardUpFor(now, field) || ClosePoint(now, keypad) is not { } at) return false;
        if (await _io.Tap(at, ct)) return true;
        Log.Debug("BBS757", $"EFB typing: {field.Name}: the keyboard's close tap did not go out");
        return false;
    }

    /// <summary>Where the read <paramref name="m"/> puts the keyboard's close: the keypad's OK (<paramref name="keypad"/>, where that read
    /// draws the keypad) or the keyboard's down key; null when that read has neither.</summary>
    private static Point? ClosePoint(Bbs757EfbScreenModel m, bool keypad) =>
        keypad
            ? m.KeyboardState is { } k ? Bbs757EfbTyping.KeypadOk(k.KeypadBox) : null
            : m.Keyboard?.Keys.FirstOrDefault(key => key.Name == KeyboardDownKey)?.Hit;

    /// <summary>
    /// Whether a typing goes on from the first fresh read of the field (<paramref name="field"/> on <paramref name="page"/>), before any
    /// drag or tap: not when the read has no caret for it (a value box, whose tap opens BlueBird's dialog: "{name} not typed."; Task 3's
    /// review), not when the field holds the text already (nothing said), and not when a character of the text is on no key of what the
    /// field opens (<see cref="MissingKey"/>): "{name}: the tablet's keyboard has no {character}." with no tap of any kind (plan 22's Global
    /// Constraints).
    /// </summary>
    private bool TypingWanted(Bbs757EfbScreenModel page, Bbs757EfbItem field, string text)
    {
        if (field.TextState is null)
        {
            Log.Debug("BBS757", $"EFB typing: {field.Name}: no caret read, no text box to type into; nothing tapped");
            _io.Alert(Bbs757EfbPress.NotTypedText(Bbs757EfbPress.SpokenName(page, field)));
            return false;
        }
        if (string.Equals(field.Value, text, StringComparison.Ordinal)) return false;
        if (MissingKey(page, field, text) is not char missing) return true;
        Log.Debug("BBS757", $"EFB typing: {field.Name}: a character on no key; nothing tapped");
        _io.Alert(Bbs757EfbPress.NoKeyText(Bbs757EfbPress.SpokenName(page, field), missing));
        return false;
    }

    /// <summary>The first character of <paramref name="text"/> no key carries, by the planner itself, from a read before the field's tap: on
    /// the numeric keypad for a box that opens it (<see cref="Bbs757EfbTextState.Keypad"/>), else on the keyboard's keys, any layer (its keys
    /// are where they are whether it shows or not). Null when every character has a key, and when the read cannot tell (the box's state or
    /// the keys not read): the check on the read that shows the keyboard up then decides.</summary>
    private static char? MissingKey(Bbs757EfbScreenModel page, Bbs757EfbItem field, string text)
    {
        char? missing = null;
        if (field.TextState is not { } state) return null;
        if (state.Keypad) Bbs757EfbTyping.PlanKeypad(AnyKeypad, text, out missing);
        else if (page.Keyboard is { } keyboard) Bbs757EfbTyping.Plan(keyboard, RestingKeyboard, text, 0, 0, out missing);
        return missing;
    }

    /// <summary>A keypad's rectangle and a keyboard at rest, for <see cref="MissingKey"/>: which characters have a key does not depend on
    /// where the keypad is drawn or which layer shows.</summary>
    private static readonly Rectangle AnyKeypad = new(0, 0, 360, 430);

    private static readonly Bbs757EfbKeyboardState RestingKeyboard = new(false, false, false, "", false, false, false, Rectangle.Empty);

    /// <summary>
    /// The field read back after its typing (<paramref name="box"/>, as the read <paramref name="up"/> showed it before the keys): read
    /// until its text changed or reads <paramref name="text"/>, at most <see cref="ChangeMaxMs"/>, and said once. A page field gone from a
    /// read after a typing whose close went out (<paramref name="closed"/>) is read back from the line its row shows in that read
    /// (<see cref="LineFor"/>; the final review's I3: the empty SimBrief row saves on the keypad's OK, which hides its box, and the row then
    /// shows the saved ID's line), as soon as that line reads other words than the row showed before the typing, or the text; "{name} is no
    /// longer on the tablet." only when no read in the wait showed the field or such a line.
    /// </summary>
    private async Task ReadBackAsync(Bbs757EfbScreenModel up, Bbs757EfbItem box, string text, string name, bool closed, CancellationToken ct)
    {
        long start = _io.NowMs();
        // What the row showed beside its box before the typing (Hoppie's and SayIntentions' value lines), else the box's own text: a line
        // still reading it is not yet the read-back.
        string before = LineFor(up, up, box)?.Value ?? box.Value;
        Bbs757EfbItem? shown, line = null;
        while (true)
        {
            await _io.Delay(ChangePollMs, ct);
            if (await _io.Read(ct) is not { } now) return;   // nothing read: the window says why
            shown = FieldIn(up, now, box);
            if (shown != null && (!string.Equals(shown.Value, box.Value, StringComparison.Ordinal) || ReadsAsTyped(shown, text))) break;
            if (shown == null && closed && LineFor(up, now, box) is { } found)
            {
                line = found;
                if (!string.Equals(found.Value, before, StringComparison.Ordinal) || ReadsAsTyped(found, text)) break;
            }
            if (_io.NowMs() - start >= ChangeMaxMs) break;
        }
        if ((shown ?? line) is not { } said)
        {
            _io.Alert(Bbs757EfbPress.GoneText(name));
            return;
        }
        bool asTyped = ReadsAsTyped(said, text);
        Log.Debug("BBS757",
            $"EFB typing: {box.Name}: read back {(_io.NowMs() - start).ToString(CultureInfo.InvariantCulture)} ms after the keyboard's close"
            + (shown == null ? ", from its row's line" : ""));
        // An empty field says so ("Airport: " would end on nothing): emptied as asked, or every key dropped.
        _io.Alert(said.Value.Length == 0 ? Bbs757EfbPress.EmptyText(name)
            : asTyped ? Bbs757EfbPress.TypedText(name, said.Value) : Bbs757EfbPress.TypedOtherText(name, said.Value));
    }

    /// <summary>
    /// The line a page field's row shows in <paramref name="now"/> (the field <paramref name="box"/> as the read <paramref name="up"/> has
    /// it): the one value line (<see cref="Bbs757EfbKind.Row"/>) named exactly as the field is, by the structural name the reader gave it
    /// (B757-22: its row's label, or its own name), on the same page; never by its place or a word guessed. Null for a dialog's box, on
    /// another page, and when no such line, or more than one, shows.
    /// </summary>
    private static Bbs757EfbItem? LineFor(Bbs757EfbScreenModel up, Bbs757EfbScreenModel now, Bbs757EfbItem box)
    {
        if (box.Text.Length == 0 || up.Question?.Rows.Contains(box) == true || !string.Equals(up.PageName, now.PageName, StringComparison.Ordinal))
            return null;
        var lines = now.Items.Where(i => i.Kind == Bbs757EfbKind.Row && string.Equals(i.Text, box.Text, StringComparison.Ordinal)).Take(2).ToList();
        return lines.Count == 1 ? lines[0] : null;
    }

    /// <summary>Whether a field reads the typed text, case aside: PERF calc's Airport box upper-cases what it takes, the Charts box keeps
    /// case (measured).</summary>
    private static bool ReadsAsTyped(Bbs757EfbItem field, string text) => string.Equals(field.Value, text, StringComparison.OrdinalIgnoreCase);

    /// <summary>A field of <paramref name="before"/> found in <paramref name="now"/>: a dialog's row under the same question
    /// (<see cref="RowIn"/>), a page field by <see cref="Bbs757EfbPress.Relocate"/>; null when it is not there, or no longer a field.</summary>
    private static Bbs757EfbItem? FieldIn(Bbs757EfbScreenModel before, Bbs757EfbScreenModel now, Bbs757EfbItem field) =>
        before.Question?.Rows.Contains(field) == true
            ? RowIn(before, now, field)
            : Bbs757EfbPress.Relocate(before, now, field) is { Kind: Bbs757EfbKind.Field } found ? found : null;

    /// <summary>A dialog's row of <paramref name="before"/> in <paramref name="now"/>: the row with its key (its box's node name) under the
    /// same question; null when the question went or is another.</summary>
    private static Bbs757EfbItem? RowIn(Bbs757EfbScreenModel before, Bbs757EfbScreenModel now, Bbs757EfbItem row) =>
        Bbs757EfbPress.SameQuestion(before.Question, now.Question)
            ? now.Question!.Rows.FirstOrDefault(r => string.Equals(r.Key, row.Key, StringComparison.Ordinal))
            : null;

    // ------------------------------------------------------------------ where, and the tap

    /// <summary>A control where it is to be tapped: the control and the page it is on in the fresh read.</summary>
    private sealed record Target(Bbs757EfbItem Item, Bbs757EfbScreenModel Page, Point At);

    /// <summary>
    /// The pressed control where it is to be tapped: the tablet is read again from memory and the control found in it. Null, with
    /// nothing tapped, when it is gone or no longer a control ("{label} is no longer on the tablet."), when the question an answer was
    /// pressed on was answered meanwhile, when a question came up over the page, when the pressed navigation is done already (the
    /// tablet shows the page a side-menu entry or a home-screen app asks for, or Home for the window's Home: nothing said), and when
    /// nothing could be read (the window says why). With <paramref name="proceed"/>, also when it says no of the control as the first
    /// fresh read finds it, before any drag (<see cref="TypeAsync"/>: the text there already, or a character on no key); it says what is
    /// said then. A drag whose press point BlueBird's keyboard covers (<see cref="Bbs757EfbPress.UnderKeyboard"/>) puts the keyboard down
    /// first, once (<see cref="PutKeyboardDownAsync"/>), and is planned again from the read after it (the final review's I2); still up and
    /// settled over that point then, no drag: "{name} is under the tablet's keyboard, not pressed." (the re-review's N3).
    /// </summary>
    private async Task<Target?> LocateAsync(Bbs757EfbPressed pressed, CancellationToken ct,
        Func<Bbs757EfbScreenModel, Bbs757EfbItem, bool>? proceed = null)
    {
        if (await _io.Read(ct) is not { } now) return null;
        var found = Find(pressed, now, out bool gone);
        if (found != null && proceed != null && !proceed(now, found)) return null;   // settled by the first read: no drag, no tap
        bool keyboardPutDown = false;
        for (int steps = 0; found != null && Bbs757EfbPress.TapPoint(now, found) == null; steps++)
        {
            // Outside its clip (or showing under MinShownPx): dragged toward the viewport's middle, read again, found again where it is
            // drawn now. Only a control of the scrollbox that scrolls has a drag (DragFor); any other is not brought into view.
            if (steps >= MaxScrollSteps || Bbs757EfbPress.DragFor(now, found) is not { } drag) break;
            // A drag presses where it starts: on BlueBird's notification panel that press would land on the notification, and on an
            // open list on one of its rows (the review's Minor 2).
            if (Bbs757EfbPress.UnderNotice(now, null, new Point(drag.X, drag.Y0)))
            {
                _io.Alert(Bbs757EfbPress.UnderNoticeText(Bbs757EfbPress.SpokenName(pressed.Page, pressed.Item)));
                return null;
            }
            if (Bbs757EfbPress.CoveringList(now, null, new Point(drag.X, drag.Y0)) is { } covering)
            {
                _io.Alert(Bbs757EfbPress.CoveredText(Bbs757EfbPress.SpokenName(pressed.Page, pressed.Item), Bbs757EfbPress.SpokenName(now, covering)));
                return null;
            }
            // On BlueBird's keyboard it would be a key (the final review's I2): the keyboard put down first, once, and the drag planned
            // again from the read after it; still up and settled over the point after that, no drag (the re-review's N3).
            if (Bbs757EfbPress.UnderKeyboard(now, new Point(drag.X, drag.Y0)))
            {
                if (!keyboardPutDown)
                {
                    keyboardPutDown = true;
                    if (await PutKeyboardDownAsync(now, ct) is not { } down) return null;   // nothing read: the window says why
                    now = down;
                    found = Find(pressed, now, out gone);
                    continue;
                }
                if (now.KeyboardState is { Ready: true })
                {
                    _io.Alert(Bbs757EfbPress.UnderKeyboardText(Bbs757EfbPress.SpokenName(pressed.Page, pressed.Item)));
                    return null;
                }
            }
            if (!await _io.Drag(drag.X, drag.Y0, drag.Y1, ct))
            {
                if (!ct.IsCancellationRequested) _io.Alert(Bbs757EfbPress.UnreachableText);
                return null;
            }
            if (await _io.Read(ct) is not { } next) return null;
            var moved = Find(pressed, next, out gone);
            bool stayed = moved != null && moved.Box == found.Box;   // the drag left it where it was: the page's end
            now = next;
            found = moved;
            if (stayed) break;
        }
        if (found != null)
        {
            if (Bbs757EfbPress.TapPoint(now, found) is { } at) return new Target(found, now, at);
            _io.Alert(Bbs757EfbPress.NotInViewText(Bbs757EfbPress.SpokenName(pressed.Page, pressed.Item)));
            return null;
        }
        if (gone) _io.Alert(Bbs757EfbPress.GoneText(Bbs757EfbPress.SpokenName(pressed.Page, pressed.Item)));
        return null;
    }

    /// <summary>The pressed control on the page read again, or null; <paramref name="gone"/> says when that is said.</summary>
    private static Bbs757EfbItem? Find(Bbs757EfbPressed pressed, Bbs757EfbScreenModel now, out bool gone)
    {
        gone = false;
        var item = pressed.Item;
        if (item.Key == HomeKey)
            return now.App == "HOME" || now.Question != null ? null : item;

        // A side-menu entry or a home-screen app whose page the tablet shows already (pressed twice): its tap is done.
        if (NavigationDone(pressed, now)) return null;

        // Every question's answers are keyed alike (q:YES, q:NO): an answer is the one pressed only under the same question.
        if (pressed.Page.Question?.Buttons.Contains(item) == true)
        {
            if (now.Question != null && !Bbs757EfbPress.SameQuestion(pressed.Page.Question, now.Question)) return null;
            var answer = now.Question != null ? Bbs757EfbPress.Relocate(pressed.Page, now, item) : null;
            gone = answer == null || now.Question?.Buttons.Contains(answer) != true;
            return gone ? null : answer;
        }

        // A dialog's text box (the fuel input dialog's) is its row, found again as an answer is: only under the same question.
        if (pressed.Page.Question?.Rows.Contains(item) == true)
        {
            if (now.Question != null && !Bbs757EfbPress.SameQuestion(pressed.Page.Question, now.Question)) return null;
            var row = RowIn(pressed.Page, now, item);
            gone = row == null;
            return row;
        }

        var found = Bbs757EfbPress.Relocate(pressed.Page, now, item);
        if (found == null || found.Kind is not (Bbs757EfbKind.Button or Bbs757EfbKind.Tab or Bbs757EfbKind.Menu or Bbs757EfbKind.Toggle
                or Bbs757EfbKind.Checkbox or Bbs757EfbKind.Slider or Bbs757EfbKind.Choice or Bbs757EfbKind.Field))
        {
            gone = true;
            return null;
        }
        // Under a question the tablet takes no tap on its page: only the top bar presses (the answers were found above).
        if (now.Question != null && !now.TopBar.Contains(found)) return null;
        return found;
    }

    /// <summary>Whether the tablet shows the page a side-menu entry or a home-screen app was pressed for.</summary>
    private static bool NavigationDone(Bbs757EfbPressed pressed, Bbs757EfbScreenModel now)
    {
        var item = pressed.Item;
        if (item.Kind != Bbs757EfbKind.Menu) return false;
        if (item.Key.StartsWith("app:", StringComparison.Ordinal))
            return string.Equals(now.App, item.Key.Substring("app:".Length), StringComparison.OrdinalIgnoreCase);
        return item.Key.StartsWith("menu:", StringComparison.Ordinal)
            && string.Equals(now.App, pressed.Page.App, StringComparison.OrdinalIgnoreCase) && PageIs(now, item.Text);
    }

    /// <summary>
    /// The tap at <paramref name="item"/>'s point (<paramref name="at"/>, else the target's), and, once it went out, the press's hook; false,
    /// said, when nothing was tapped. A page point BlueBird's notification pop-up or list covers on the page <paramref name="on"/> (the
    /// target's when not given) is not tapped: the tap would land on the notification, which the fresh read cannot hide the control under
    /// (plan 21, the final review's I1), so "{name} is under a BlueBird notification, not pressed." is said. A point another drop-down's open
    /// list covers is not tapped either: the tap would land on one of its rows and choose that entry (plan 21, Task 7), so "{name} is under
    /// the open {list} list, not pressed." is said. The notification is drawn over the page and its lists, so it is checked first. Both are
    /// checked here, at the point the tap goes to, so a slider's choice or percent place is checked as a control's middle is (the review's
    /// Minor 2). A nav data press reads the cycle installed BEFORE its tap and hands it over with its hook. With <paramref name="speak"/>
    /// false (the runner's own tap that closes a list it opened, after the choice's alert) a tap not made says nothing.
    /// <para>A point BlueBird's keyboard or keypad covers while it shows (<see cref="Bbs757EfbPress.UnderKeyboard"/>; the final review's I2)
    /// would be a key typed into the box the keyboard serves, wherever that is, and the window cannot show the keyboard (B757-23): it is put
    /// down first, once (<see cref="PutKeyboardDownAsync"/>), the control found again in the read after it (<see cref="Find"/>, as the press
    /// found it) and tapped where that read draws it, the checks above made again on that read. Gone then: "{name} is no longer on the
    /// tablet."; out of its clip: "{name} could not be brought into view, not pressed."; done already or under a question come up: nothing
    /// said. Still up and settled over the point after it: not tapped, "{name} is under the tablet's keyboard, not pressed." (the
    /// re-review's N3, a precision stop as under a notification); one that never settled is tapped as before (a tap then lands on the page
    /// beneath: measured). This holds for every tap of a press: a control's, a slider's, a drop-down's face and row, and a typing's own
    /// field tap.</para>
    /// </summary>
    private async Task<bool> TapAsync(Bbs757EfbItem item, Target target, CancellationToken ct, Point? at = null, Bbs757EfbScreenModel? on = null,
        bool speak = true)
    {
        var page = on ?? target.Page;
        var point = at ?? target.At;
        bool keyboardPutDown = false;
        while (true)
        {
            if (Bbs757EfbPress.UnderNotice(page, item, point))
            {
                if (speak) _io.Alert(Bbs757EfbPress.UnderNoticeText(Bbs757EfbPress.SpokenName(page, item)));
                return false;
            }
            if (Bbs757EfbPress.CoveringList(page, item, point) is { } covering)
            {
                if (speak) _io.Alert(Bbs757EfbPress.CoveredText(Bbs757EfbPress.SpokenName(page, item), Bbs757EfbPress.SpokenName(page, covering)));
                return false;
            }
            if (!Bbs757EfbPress.UnderKeyboard(page, point)) break;
            if (keyboardPutDown)
            {
                // Put down once and still up and settled over the point: the tap would be a key (the re-review's N3). One that never
                // settled is tapped as before: a tap before it settles lands on the page beneath (measured).
                if (page.KeyboardState is not { Ready: true }) break;
                if (speak) _io.Alert(Bbs757EfbPress.UnderKeyboardText(Bbs757EfbPress.SpokenName(page, item)));
                return false;
            }
            keyboardPutDown = true;
            if (await PutKeyboardDownAsync(page, ct) is not { } now) return false;   // nothing read: the window says why
            var found = Find(new Bbs757EfbPressed(item, page), now, out bool gone);
            if (found == null)
            {
                if (gone && speak) _io.Alert(Bbs757EfbPress.GoneText(Bbs757EfbPress.SpokenName(page, item)));
                return false;
            }
            // Where the read after draws it: a given point (a slider's place, a list row's) moves with the control's box.
            var shown = Bbs757EfbPress.TapPoint(now, found);
            if (shown == null)
            {
                if (speak) _io.Alert(Bbs757EfbPress.NotInViewText(Bbs757EfbPress.SpokenName(page, item)));
                return false;
            }
            point = at is { } given ? new Point(given.X + found.Box.X - item.Box.X, given.Y + found.Box.Y - item.Box.Y) : shown.Value;
            page = now;
            item = found;
        }
        var hook = Bbs757EfbExtras.HookFor(target.Page.PageName, item);
        int? cycleBefore = hook is Bbs757EfbHook.NavDataUpdate or Bbs757EfbHook.NavDataRemove ? _io.ReadInstalledCycle() : null;
        if (!await _io.Tap(point, ct))
        {
            if (speak) _io.Alert(Bbs757EfbPress.UnreachableText);
            return false;
        }
        if (hook != Bbs757EfbHook.None) _io.Hooked(hook, cycleBefore);
        return true;
    }

    /// <summary>
    /// Puts BlueBird's keyboard (or the numeric keypad) down before a press that it covers (<see cref="Bbs757EfbPress.UnderKeyboard"/> on
    /// <paramref name="m"/>, the read the press was decided on; the final review's I2): one tap on its down key (<c>btn_keyboard_down</c>,
    /// which keeps the text on every box measured) or on the keypad's OK, where the read before the tap draws it (<see cref="ClosePoint"/>),
    /// then the tablet read until it reads down (at most <see cref="ChangeMaxMs"/>). One shown but still sliding up is waited for until it
    /// reads settled, within <see cref="ChangeMaxMs"/>, and never tapped before (a key tapped then lands on the page beneath: measured); one
    /// that never settles in that wait, or whose read has no close key, is not tapped. Nothing is said: the press's own outcome is. The
    /// last read comes back, the keyboard down or not; null when a read failed (the window says why).
    /// </summary>
    private async Task<Bbs757EfbScreenModel?> PutKeyboardDownAsync(Bbs757EfbScreenModel m, CancellationToken ct)
    {
        var now = m;
        long until = _io.NowMs() + ChangeMaxMs;
        while (now.KeyboardState is { Shown: true, Ready: false } && _io.NowMs() < until)
        {
            await _io.Delay(ChangePollMs, ct);
            if (await _io.Read(ct) is not { } next) return null;
            now = next;
        }
        if (now.KeyboardState is not { Shown: true } k) return now;   // it went down meanwhile: nothing to put down
        if (!k.Ready || ClosePoint(now, k.Keypad) is not { } close)
        {
            Log.Debug("BBS757", "EFB reader: BlueBird's keyboard over a press was not put down (not settled, or no close key read)");
            return now;
        }
        if (!await _io.Tap(close, ct))
        {
            Log.Debug("BBS757", "EFB reader: the close tap of BlueBird's keyboard over a press did not go out");
            return now;
        }
        Log.Debug("BBS757", "EFB reader: BlueBird's keyboard was up over a press; put down first");
        until = _io.NowMs() + ChangeMaxMs;
        while (true)
        {
            await _io.Delay(ChangePollMs, ct);
            if (await _io.Read(ct) is not { } next) return null;
            now = next;
            if (now.KeyboardState is not { Shown: true } || _io.NowMs() >= until) return now;
        }
    }

    /// <summary>
    /// After a tap, reads the tablet again until the page itself changes from <paramref name="before"/> (not the top
    /// bar or notifications, which come and go), at most <see cref="ChangeMaxMs"/>; returns the read the change was seen in, the last
    /// read when none changed it, or null when a read failed. Each read pushes the page to the window.
    /// </summary>
    private async Task<Bbs757EfbScreenModel?> WaitForChangeAsync(Bbs757EfbScreenModel before, CancellationToken ct)
    {
        long start = _io.NowMs();
        var now = before;
        while (_io.NowMs() - start < ChangeMaxMs)
        {
            await _io.Delay(ChangePollMs, ct);
            if (await _io.Read(ct) is not { } m) return now;
            now = m;
            if (!Bbs757EfbPress.PageUnchanged(before, m))
            {
                Log.Debug("BBS757", $"EFB reader: page changed {_io.NowMs() - start} ms after the tap");
                return m;
            }
        }
        Log.Debug("BBS757", $"EFB reader: no page change in {ChangeMaxMs} ms after the tap");
        return now;
    }
}

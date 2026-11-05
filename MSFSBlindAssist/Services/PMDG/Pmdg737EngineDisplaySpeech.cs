using System.Globalization;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>One line the engine display reader speaks by itself, with the Ctrl+M row that mutes it.</summary>
public sealed record EngineDisplayAnnouncement(EngineDisplayMuteRow Row, string Text);

/// <summary>
/// Composes what the engine display reader says: what changed between two readings, and its status lines.
/// Alerts appearing ("Upper DU alert: engine 1 START VALVE OPEN") and going ("Upper DU alert gone: …"),
/// gauge colours ("Upper DU: engine 1 EGT amber", back to "normal") and the thrust mode ("Upper DU: thrust
/// mode CLB"), in that order, each on its own line. Items of one form share a line, in the display's order,
/// with the prefix once; a change of prefix starts a new line. The prefix names the display unit whose page
/// draws the item now (the upper one when both do), else the one that normally draws it. Boeing's printed
/// text throughout, as the FMA. Nothing is said unless both readings have the displays lit
/// (<see cref="Pmdg737EngineDisplayPages.DisplaysLit"/>): before PMDG's aircraft initialises its objects hold
/// constructor values, and with the displays dark no one sees what PMDG's logic sets. Pure; numbers only as
/// engine and tank numbers, invariant.
/// </summary>
public static class Pmdg737EngineDisplaySpeech
{
    public const string Starting = "Engine displays starting";
    public const string Resumed = "Engine display announcements resumed";

    public static string Unavailable(string reason) => $"Engine display announcements unavailable: {reason}";
    public static string Stopped(string reason) => $"Engine display announcements stopped: {reason}";

    /// <summary>What both lists say while the reader is unavailable. Output Alt+E said it too until 2026-10-31; it now goes
    /// straight to the AI read (WASM-29).</summary>
    public static string ListUnavailable(string reason) => $"Engine displays unavailable: {reason}";

    /// <summary>What both lists say during a short stop, never stale values.</summary>
    public static string ListStopped(string reason) => $"Engine displays stopped: {reason}, trying again";

    private const string Upper = "Upper DU", Lower = "Lower DU";

    /// <summary>An alert or gauge message: <see cref="Key"/> identifies it (a reverser keeps its key when its colour changes), <see cref="Where"/> the row group that draws it.</summary>
    private sealed record Item(string Key, string Text, PageItem Where);

    private static readonly (Gauge Gauge, string Label, PageItem Where)[] ColouredGauges =
    {
        (Gauge.N1, "N1", PageItem.N1), (Gauge.Egt, "EGT", PageItem.Egt), (Gauge.N2, "N2", PageItem.N2),
        (Gauge.OilPressure, "OIL PRESS", PageItem.OilPressure), (Gauge.OilTemperature, "OIL TEMP", PageItem.OilTemperature),
        (Gauge.Vibration, "VIB", PageItem.Vibration),
    };

    /// <summary>The lines for everything that changed from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static IReadOnlyList<EngineDisplayAnnouncement> Changes(EngineDisplaySample before, EngineDisplaySample after, Pmdg737EngineDisplayLayout l)
    {
        var lines = new List<EngineDisplayAnnouncement>();
        if (!Pmdg737EngineDisplayPages.DisplaysLit(before, l) || !Pmdg737EngineDisplayPages.DisplaysLit(after, l)) return lines;

        var was = AlertItems(before, l);
        var now = AlertItems(after, l);
        var appearing = now.Where(i => was.FirstOrDefault(w => w.Key == i.Key) is not { } old || old.Text != i.Text).ToList();
        var going = was.Where(w => now.All(i => i.Key != w.Key)).ToList();
        AddLines(lines, EngineDisplayMuteRow.Alerts, appearing, after, l, prefix => prefix + " alert: ");
        AddLines(lines, EngineDisplayMuteRow.Alerts, going, after, l, prefix => prefix + " alert gone: ");

        var colours = new List<Item>();
        foreach (var (gauge, label, where) in ColouredGauges)
        {
            for (int e = 0; e < after.Engines.Count && e < before.Engines.Count; e++)
            {
                var colour = Pmdg737EngineDisplayColors.ColorOf(gauge, after, e);
                if (colour == Pmdg737EngineDisplayColors.ColorOf(gauge, before, e)) continue;
                colours.Add(new Item(label, $"{Engine(e)} {label} {Pmdg737EngineDisplayColors.Word(colour)}", where));
            }
        }
        AddLines(lines, EngineDisplayMuteRow.Colors, colours, after, l, prefix => prefix + ": ");

        string thrust = Pmdg737EngineDisplayPages.ThrustModeText(after, l);
        if (thrust.Length > 0 && thrust != Pmdg737EngineDisplayPages.ThrustModeText(before, l))
            lines.Add(new EngineDisplayAnnouncement(EngineDisplayMuteRow.ThrustMode, $"{Prefix(PageItem.ThrustMode, after, l)}: thrust mode {thrust}"));
        return lines;
    }

    /// <summary>
    /// Every alert and gauge message shown, in the display's order: each engine's crew alerts (while not
    /// off; flashing or steady is the same alert), ENG FAIL, REV with its colour, X-BLD START, OIL QTY LO
    /// (under PMDG's LO option) and the amber TAI (the green one is a state, in the list); then the tanks'
    /// warnings; then the autoland advisory, A/T LIM and the ATC and COMM memos.
    /// </summary>
    private static List<Item> AlertItems(EngineDisplaySample s, Pmdg737EngineDisplayLayout l)
    {
        var items = new List<Item>();
        for (int i = 0; i < s.Engines.Count; i++)
        {
            var e = s.Engines[i];
            string engine = Engine(i);
            void Add(bool shown, string text, PageItem where, string? key = null)
            {
                if (shown) items.Add(new Item(key ?? $"{engine} {text}", $"{engine} {text}", where));
            }
            Add(e.StartValve != 0, "START VALVE OPEN", PageItem.Alerts);
            Add(e.OilFilterBypass != 0, "OIL FILTER BYPASS", PageItem.Alerts);
            Add(e.LowOilPressure != 0, "LOW OIL PRESSURE", PageItem.Alerts);
            Add(e.EngineFail, "ENG FAIL", PageItem.Egt);
            Add(e.ReverserShown, e.ReverserSecond ? "REV green" : "REV amber", PageItem.N1, key: engine + " REV");
            Add(e.CrossBleedStart, "X-BLD START", PageItem.N2);
            Add(s.OilLowIndication && e.OilQuantity < s.OilQuantityLow, "OIL QTY LO", PageItem.OilQuantity);
            Add(e.TaiAmber, "TAI amber", PageItem.N1);
        }

        var a = l.Alerts;
        string? Warning(int value) =>
            value == a.FuelConfigValue ? "CONFIG" : value == a.FuelLowValue ? "LOW" : value == a.FuelImbalanceValue ? "IMBAL" : null;
        foreach (int tank in new[] { 0, 1, 2 })
        {
            if (tank >= s.FuelWarning.Count || Warning(s.FuelWarning[tank]) is not { } word) continue;
            string text = tank == 0 ? "fuel " + word : $"tank {tank.ToString(CultureInfo.InvariantCulture)} fuel {word}";
            items.Add(new Item(text, text, PageItem.Fuel));
        }

        if (s.AutolandAdvisory == a.NoLand3Value) items.Add(new Item("NO LAND 3", "NO LAND 3", PageItem.Alerts));
        else if (s.AutolandAdvisory == a.NoAutolandValue) items.Add(new Item("NO AUTOLAND", "NO AUTOLAND", PageItem.Alerts));
        if (s.AutothrottleLimit) items.Add(new Item("A/T LIM", "A/T LIM", PageItem.ThrustMode));
        if (s.AtcMemo) items.Add(new Item("ATC", "ATC", PageItem.Alerts));
        if (s.CommMemo) items.Add(new Item("COMM", "COMM", PageItem.Alerts));
        return items;
    }

    /// <summary>Consecutive items with one prefix make one line; a new prefix starts a new line.</summary>
    private static void AddLines(List<EngineDisplayAnnouncement> lines, EngineDisplayMuteRow row, IReadOnlyList<Item> items,
        EngineDisplaySample s, Pmdg737EngineDisplayLayout l, Func<string, string> start)
    {
        string? prefix = null;
        var texts = new List<string>();
        void Flush()
        {
            if (prefix is not null && texts.Count > 0) lines.Add(new EngineDisplayAnnouncement(row, start(prefix) + string.Join(", ", texts)));
            texts.Clear();
        }
        foreach (var item in items)
        {
            string p = Prefix(item.Where, s, l);
            if (p != prefix)
            {
                Flush();
                prefix = p;
            }
            texts.Add(item.Text);
        }
        Flush();
    }

    /// <summary>The display drawing <paramref name="item"/> now (upper first), else the one that normally does
    /// (the primary engine page's items are the upper display's).</summary>
    private static string Prefix(PageItem item, EngineDisplaySample s, Pmdg737EngineDisplayLayout l)
    {
        bool Draws(int pageValue) =>
            l.PageItems.TryGetValue(Pmdg737EngineDisplayPages.PageOf(pageValue, l), out var items) && items.Contains(item);
        if (Draws(s.UpperPage)) return Upper;
        if (Draws(s.LowerPage)) return Lower;
        return l.PageItems.TryGetValue(DisplayPage.EnginePrimary, out var primary) && primary.Contains(item) ? Upper : Lower;
    }

    private static string Engine(int index) => "engine " + (index + 1).ToString(CultureInfo.InvariantCulture);
}

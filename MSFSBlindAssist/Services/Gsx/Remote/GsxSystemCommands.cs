namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// One always-available GSX system entry. <see cref="Command"/> is the Remote
/// API <c>command.run</c> verb, or null for the entry AccessGSXForm handles
/// itself (Settings, which opens an MSFSBA window rather than asking GSX for
/// anything).
/// </summary>
public sealed record GsxSystemCommand(string Shortcut, string Label, string? Command);

/// <summary>
/// GSX's system entries — the A-E block the in-sim GSX menu always carries
/// below the numbered options, and which AccessGSX has exposed on those same
/// letters since long before the Remote API.
///
/// Under the OLD SimConnect transport these were menu CHOICE INDICES 10-14
/// written into an L:var, so <c>GsxService</c> synthesised five extra
/// MenuOptions and the letters "picked" them. The Remote API has no such
/// indices — <c>menu.pick</c> takes a real 0-based index into the CURRENT
/// entries array, where 10-14 are ordinary menu rows — so the letters have to
/// map to the API's own <c>command.run</c> verbs instead. They were not
/// migrated at all in the transport swap, and "Restart GSX" is the standard
/// recovery when Couatl wedges.
///
/// Deliberately NOT added to <c>GsxService.MenuOptions</c>/<c>Menu</c>: those
/// stay exactly what GSX published, because <c>GsxGateSelector</c> walks them
/// and picks by index. Synthetic rows would collide with real entries 10-14 on
/// a long parking list.
/// </summary>
public static class GsxSystemCommands
{
    /// <summary>Every system entry, in the keyboard order a pilot hears them read out.</summary>
    public static readonly IReadOnlyList<GsxSystemCommand> All = new[]
    {
        new GsxSystemCommand("A", "Customize Airport positions...", "CUSTOMIZE_AIRPORT_POSITION"),
        new GsxSystemCommand("B", "Customize Airplane...",          "CUSTOMIZE_AIRPLANE"),
        new GsxSystemCommand("C", "GSX Settings...",                null),
        new GsxSystemCommand("D", "Restart GSX",                    "RESTART_COUATL"),
        new GsxSystemCommand("E", "Reload SimBrief",                "RELOAD_SIMBRIEF"),
    };

    /// <summary>The entry bound to <paramref name="shortcut"/>, or null when the key is not one of ours.</summary>
    public static GsxSystemCommand? ByShortcut(string shortcut) =>
        All.FirstOrDefault(c => string.Equals(c.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));
}

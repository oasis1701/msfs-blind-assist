using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// What a Fenix window needs to open itself: the definition (to open a tracked window that closes on
/// an aircraft switch, <see cref="FenixA320Definition.ShowFenixWindow{T}"/>), the Fenix data service,
/// SimConnect, the announcer, and the main form as owner.
/// </summary>
public sealed record FenixWindowContext(
    FenixA320Definition Definition,
    FenixDataService Data,
    MSFSBlindAssist.SimConnect.SimConnectManager SimConnect,
    ScreenReaderAnnouncer Announcer,
    Form Owner);

/// <summary>
/// One row of the "Fenix windows" list (Ctrl+Shift+F in input mode). The list speaks
/// "<paramref name="Name"/>. <paramref name="Description"/>"; <paramref name="Open"/> opens the window.
/// </summary>
public sealed record FenixWindowEntry(string Name, string Description, Action<FenixWindowContext> Open);

/// <summary>
/// The windows the "Fenix windows" list offers. Each kind of window adds itself from its own
/// partial file by implementing one of the optional seams below, so no window's file edits another's, and a
/// seam nobody implements compiles to nothing (the list is then empty).
/// <list type="bullet">
/// <item>Engines: <c>FenixWindowRegistry.Engines.cs</c>, <see cref="AddEngineWindows"/></item>
/// <item>Cabin: <c>FenixWindowRegistry.Cabin.cs</c>, <see cref="AddCabinWindows"/></item>
/// <item>Failures: <c>FenixWindowRegistry.Failures.cs</c>, <see cref="AddFailureWindows"/></item>
/// <item>Documents: <c>FenixWindowRegistry.Documents.cs</c>, <see cref="AddDocumentWindows"/></item>
/// <item>Options: <c>FenixWindowRegistry.Options.cs</c>, <see cref="AddOptionsWindows"/></item>
/// </list>
/// A window opens through <see cref="FenixA320Definition.ShowFenixWindow{T}"/>, so it closes with the
/// aircraft. Every <see cref="FenixWindowEntry.Name"/> must be unique (FenixWindowRegistryTests).
/// </summary>
public static partial class FenixWindowRegistry
{
    static partial void AddEngineWindows(List<FenixWindowEntry> entries);
    static partial void AddCabinWindows(List<FenixWindowEntry> entries);
    static partial void AddFailureWindows(List<FenixWindowEntry> entries);
    static partial void AddDocumentWindows(List<FenixWindowEntry> entries);
    static partial void AddOptionsWindows(List<FenixWindowEntry> entries);

    /// <summary>The entries in list order: engines, cabin, failures, documents, options.</summary>
    public static IReadOnlyList<FenixWindowEntry> Build()
    {
        var entries = new List<FenixWindowEntry>();
        AddEngineWindows(entries);
        AddCabinWindows(entries);
        AddFailureWindows(entries);
        AddDocumentWindows(entries);
        AddOptionsWindows(entries);
        return entries;
    }
}

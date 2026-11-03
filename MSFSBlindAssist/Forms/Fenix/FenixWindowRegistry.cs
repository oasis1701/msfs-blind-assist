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
/// partial file by implementing one of the optional seams below (the display channel's six are declared in
/// <c>FenixWindowRegistry.Channel.cs</c>), so no window's file edits another's, and a
/// seam nobody implements compiles to nothing (the list is then empty).
/// <list type="bullet">
/// <item>Engines: <c>FenixWindowRegistry.Engines.cs</c>, <see cref="AddEngineWindows"/></item>
/// <item>Cabin: <c>FenixWindowRegistry.Cabin.cs</c>, <see cref="AddCabinWindows"/></item>
/// <item>Failures: <c>FenixWindowRegistry.Failures.cs</c>, <see cref="AddFailureWindows"/></item>
/// <item>Documents: <c>FenixWindowRegistry.Documents.cs</c>, <see cref="AddDocumentWindows"/></item>
/// <item>Options: <c>FenixWindowRegistry.Options.cs</c>, <see cref="AddOptionsWindows"/></item>
/// <item>PFD: <c>FenixWindowRegistry.ChannelPfd.cs</c>, <see cref="AddChannelPfdWindows"/></item>
/// <item>ND: <c>FenixWindowRegistry.ChannelNd.cs</c>, <see cref="AddChannelNdWindows"/></item>
/// <item>E/WD: <c>FenixWindowRegistry.ChannelEwd.cs</c>, <see cref="AddChannelEwdWindows"/></item>
/// <item>SD: <c>FenixWindowRegistry.ChannelSd.cs</c>, <see cref="AddChannelSdWindows"/></item>
/// <item>ISIS: <c>FenixWindowRegistry.ChannelIsis.cs</c>, <see cref="AddChannelIsisWindows"/></item>
/// <item>DCDU: <c>FenixWindowRegistry.ChannelDcdu.cs</c>, <see cref="AddChannelDcduWindows"/></item>
/// </list>
/// The last six are the Fenix display channel's windows, one for each display, named by the display's short name
/// (the way "Engines" and "Cabin" are named). Their seams are declared in <c>FenixWindowRegistry.Channel.cs</c>, and
/// each display's partial has "Channel" in its file name, which is what the channel's rule glob loads the channel
/// rules on. Each opens through
/// <c>FenixA320Definition.ShowChannelWindow</c>, which shows its window whatever the display's verdict, since the
/// window itself shows a status row while the display is not trusted.
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

    /// <summary>
    /// The entries in list order: engines, cabin, failures, documents, options, then the display channel's windows:
    /// PFD, ND, E/WD, SD, ISIS, DCDU.
    /// </summary>
    public static IReadOnlyList<FenixWindowEntry> Build()
    {
        var entries = new List<FenixWindowEntry>();
        AddEngineWindows(entries);
        AddCabinWindows(entries);
        AddFailureWindows(entries);
        AddDocumentWindows(entries);
        AddOptionsWindows(entries);
        AddChannelPfdWindows(entries);
        AddChannelNdWindows(entries);
        AddChannelEwdWindows(entries);
        AddChannelSdWindows(entries);
        AddChannelIsisWindows(entries);
        AddChannelDcduWindows(entries);
        return entries;
    }
}

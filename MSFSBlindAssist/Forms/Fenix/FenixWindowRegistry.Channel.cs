namespace MSFSBlindAssist.Forms.Fenix;

// The Fenix display channel's window seams in the "Fenix windows" list: one for each display, declared here and
// implemented in a partial file of its own (FenixWindowRegistry.ChannelPfd.cs and the others). Build, in
// FenixWindowRegistry.cs, calls them in the list's order. The registry's other partials are named for their window;
// these have "Channel" in the name, which is what the channel's rule glob (FenixWindowRegistry.Channel*.cs, in
// .claude/rules/fenix-channel.md) loads the channel rules on.
public static partial class FenixWindowRegistry
{
    /// <summary>The PFD's entry, "PFD".</summary>
    static partial void AddChannelPfdWindows(List<FenixWindowEntry> entries);
    /// <summary>The ND's entry, "ND".</summary>
    static partial void AddChannelNdWindows(List<FenixWindowEntry> entries);
    /// <summary>The E/WD's entry, "E/WD".</summary>
    static partial void AddChannelEwdWindows(List<FenixWindowEntry> entries);
    /// <summary>The SD's entry, "SD".</summary>
    static partial void AddChannelSdWindows(List<FenixWindowEntry> entries);
    /// <summary>The ISIS's entry, "ISIS".</summary>
    static partial void AddChannelIsisWindows(List<FenixWindowEntry> entries);
    /// <summary>The DCDU's entry, "DCDU".</summary>
    static partial void AddChannelDcduWindows(List<FenixWindowEntry> entries);
}

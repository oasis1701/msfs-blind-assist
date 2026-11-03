namespace MSFSBlindAssist.Forms.Fenix;

// The Fenix display channel's window seams in the "Fenix windows" list: one for each display, declared here and
// implemented by the plan of that display in a partial file of its own (FenixWindowRegistry.ChannelPfd.cs and the
// others). Build, in FenixWindowRegistry.cs, calls them in the list's order. The registry's other partials are named
// for their window; these have "Channel" in the name, which is what the channel's rule glob
// (FenixWindowRegistry.Channel*.cs, in .claude/rules/fenix-channel.md) loads the channel rules on.
public static partial class FenixWindowRegistry
{
    // The display channel's windows, one seam each. Every seam has no implementation until the plan of its display
    // adds one, and an unimplemented seam adds no entry, so the list is as it was.
    /// <summary>The PFD's entry, "PFD". Plan 02 implements it.</summary>
    static partial void AddChannelPfdWindows(List<FenixWindowEntry> entries);
    /// <summary>The ND's entry, "ND". Plan 03 implements it.</summary>
    static partial void AddChannelNdWindows(List<FenixWindowEntry> entries);
    /// <summary>The E/WD's entry, "E/WD". Plan 04 implements it.</summary>
    static partial void AddChannelEwdWindows(List<FenixWindowEntry> entries);
    /// <summary>The SD's entry, "SD". Plan 05 implements it.</summary>
    static partial void AddChannelSdWindows(List<FenixWindowEntry> entries);
    /// <summary>The ISIS's entry, "ISIS". Plan 07 implements it.</summary>
    static partial void AddChannelIsisWindows(List<FenixWindowEntry> entries);
    /// <summary>The DCDU's entry, "DCDU". Plan 11 implements it.</summary>
    static partial void AddChannelDcduWindows(List<FenixWindowEntry> entries);
}

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>What the PMDG 737's four display readers (FMA, engine displays, ND, PFD) say as one, not each in its own
/// words. Pure.</summary>
public static class Pmdg737DisplaySpeech
{
    /// <summary>Said once per module session when PMDG's display files give a reader no layout, in place of each reader's
    /// own "unavailable" line (WASM-29, <see cref="Pmdg737DisplayFilesNotice"/>); the owner's text, 2026-10-31.</summary>
    public const string FilesNotLoaded = "PMDG display files could not be loaded; the display keys fall back to AI reading.";
}

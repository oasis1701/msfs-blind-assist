namespace MSFSBlindAssist.Settings;

/// <summary>Which GitHub releases the update check will offer.</summary>
public enum UpdateChannel
{
    /// <summary>Full releases only — the default.</summary>
    Release,

    /// <summary>
    /// The rolling pre-release published on every merge to main, AND full releases.
    /// Deliberately a superset: immediately after a release is cut the preview has been
    /// retired, and taking the highest of both simply offers the release instead of
    /// finding nothing. A preview user can never miss a release.
    /// </summary>
    Preview
}

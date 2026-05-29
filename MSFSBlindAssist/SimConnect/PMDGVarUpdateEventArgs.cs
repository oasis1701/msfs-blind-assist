namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Carries a single PMDG variable change notification (any PMDG aircraft).
/// </summary>
public class PMDGVarUpdateEventArgs : EventArgs
{
    public string FieldName { get; set; } = string.Empty;
    public double Value     { get; set; }

    /// <summary>
    /// True when this event is fired from the initial baseline snapshot
    /// (i.e., the very first time PMDG delivers data after subscription).
    /// Consumers should populate caches and refresh UI controls but SKIP
    /// announcing the value as if the user just changed it — these are
    /// "current state when app loaded", not user-triggered transitions.
    /// </summary>
    public bool IsInitialSnapshot { get; set; }
}

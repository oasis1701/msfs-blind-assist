namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Carries a single PMDG variable change notification (any PMDG aircraft).
/// </summary>
public class PMDGVarUpdateEventArgs : EventArgs
{
    public string FieldName { get; set; } = string.Empty;
    public double Value     { get; set; }
}

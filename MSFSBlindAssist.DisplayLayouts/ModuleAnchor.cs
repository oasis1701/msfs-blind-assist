namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>A data run the generator found exactly once in its file, by which the module's memory is located.
/// Equality compares the bytes, so the same anchor derived from two files is searched for once.</summary>
public readonly record struct ModuleAnchor(uint Address, byte[] Bytes)
{
    public bool Equals(ModuleAnchor other) => Address == other.Address && Bytes.AsSpan().SequenceEqual(other.Bytes);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Address);
        h.AddBytes(Bytes);
        return h.ToHashCode();
    }
}

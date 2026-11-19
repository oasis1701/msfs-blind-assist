namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>A run of a WASM module's data the generator read: its wasm memory address and its length in bytes.</summary>
public readonly record struct DataRange(uint Address, int Length);

/// <summary>A search the generator made of the whole data section: the byte pattern and every address it was found at.
/// Equality compares the pattern and the addresses by content.</summary>
public sealed record DataSearch(byte[] Pattern, IReadOnlyList<uint> Hits)
{
    public bool Equals(DataSearch? other) =>
        other is not null && Pattern.AsSpan().SequenceEqual(other.Pattern) && Hits.SequenceEqual(other.Hits);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.AddBytes(Pattern);
        foreach (var hit in Hits) h.Add(hit);
        return h.ToHashCode();
    }
}

/// <summary>
/// One PMDG build's layout file for one module: the generator version that wrote it, the fingerprint (the hash of
/// every section but the data, the data ranges the generator read and their hash, the searches it made) and each
/// reader's result, a layout or the reason there is none. The text is <see cref="LayoutFileFormat"/>'s; the layouts
/// carry an empty source file until the store fills it in. Equality compares the lists by reference.
/// </summary>
public sealed record LayoutFile(int GeneratorVersion, string CodeHash, IReadOnlyList<DataRange> DataRanges, string DataHash,
    IReadOnlyList<DataSearch> Searches, IReadOnlyList<ReaderResult> Readers)
{
    /// <summary>The code hash and the data hash's first 16 characters: two builds with the same code but different
    /// bytes where the generator read get a file each.</summary>
    public string FileName => $"{CodeHash}-{DataHash[..16]}.json";
}

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>Whether a saved layout still fits a .wasm file: the code is the same, the data the generator read is the
/// same, and the searches it made find the same addresses. Bytes the generator never read may differ.</summary>
public static class LayoutFit
{
    /// <summary>SHA-256, lowercase hex, of the ranges' bytes concatenated in list order; null when a range is
    /// unreadable.</summary>
    public static string? DataHash(WasmFileImage image, IReadOnlyList<DataRange> ranges)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var r in ranges)
        {
            if (!image.TryReadData(r.Address, r.Length, out var bytes)) return null;
            hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>True when the layout file fits; else <paramref name="why"/> names the first mismatch.</summary>
    public static bool Fits(WasmFileImage image, LayoutFile file, [NotNullWhen(false)] out string? why)
    {
        if (image.CodeHash != file.CodeHash)
        {
            why = "code differs";
            return false;
        }
        foreach (var r in file.DataRanges)
        {
            if (!image.TryReadData(r.Address, r.Length, out _))
            {
                why = $"data range 0x{r.Address:x} ({r.Length} bytes) is not readable";
                return false;
            }
        }
        if (DataHash(image, file.DataRanges) != file.DataHash)
        {
            why = "data differs";
            return false;
        }
        foreach (var search in file.Searches)
        {
            var found = image.FindData(search.Pattern);
            if (!found.SequenceEqual(search.Hits))
            {
                why = $"search 0x{Convert.ToHexStringLower(search.Pattern)} found [{Hits(found)}], not [{Hits(search.Hits)}]";
                return false;
            }
        }
        why = null;
        return true;
    }

    private static string Hits(IEnumerable<uint> hits) => string.Join(", ", hits.Select(h => $"0x{h:x}"));
}

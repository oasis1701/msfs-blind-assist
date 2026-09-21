using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Model GUID → author's model name, from the `<ModelInfo … guid="{…}" … name="…">` XML
/// fragments MSFS embeds beside each model in a modelLib/objects BGL (verified on three packages).
/// Attribute order varies by developer, so each tag is scanned for both attributes independently.
/// </summary>
public static class ModelLibNameReader
{
    // Tightened so a longer attribute name's tail is never mistaken for the one we want —
    // `displayname="…"` must not be read as `name`, nor a namespaced `foo:guid="…"` as `guid`.
    private static readonly Regex GuidAttr = new(@"(?<![\w:])guid=""\{?([0-9a-fA-F-]{36})\}?""", RegexOptions.CultureInvariant);
    private static readonly Regex NameAttr = new(@"(?<![\w:])name=""([^""]+)""", RegexOptions.CultureInvariant);
    private static readonly byte[] Needle = "<ModelInfo"u8.ToArray();
    private const int DefaultChunkBytes = 1 << 20, MaxTagBytes = 1024;

    /// <summary>Tests only. Production code reads a file through the <see cref="Stream"/>
    /// overload so a multi-hundred-megabyte model library is never pulled fully into memory.</summary>
    public static Dictionary<Guid, string> Read(ReadOnlySpan<byte> bgl) => Read(new MemoryStream(bgl.ToArray()), DefaultChunkBytes);

    public static Dictionary<Guid, string> Read(Stream bgl) => Read(bgl, DefaultChunkBytes);

    /// <summary>
    /// Streams the file looking for the ASCII bytes "&lt;ModelInfo" and decodes ONLY each tag (≤ 1 KB).
    /// The old whole-file Latin-1 string cost ~3× the file size (1.6 GB for a 540 MB library) to
    /// extract ~0.02 % of it, and forced a 600 MB skip that dropped the model library — every name —
    /// of ten real airport packages.
    /// </summary>
    internal static Dictionary<Guid, string> Read(Stream bgl, int chunkBytes)
    {
        var map = new Dictionary<Guid, string>();
        byte[] buf = new byte[chunkBytes + MaxTagBytes];
        int have = 0;
        while (true)
        {
            int n = bgl.Read(buf, have, buf.Length - have);
            bool eof = n <= 0;
            if (!eof) have += n;
            var span = buf.AsSpan(0, have);
            int pos = 0, keepFrom = have;
            while (pos < have)
            {
                int hit = span.Slice(pos).IndexOf(Needle);
                if (hit < 0) { keepFrom = eof ? have : Math.Max(pos, have - (Needle.Length - 1)); break; }   // a needle may straddle the edge
                hit += pos;
                int close = span.Slice(hit, Math.Min(MaxTagBytes, have - hit)).IndexOf((byte)'>');
                if (close < 0)
                {
                    if (!eof && have - hit < MaxTagBytes) { keepFrom = hit; break; }   // tag cut by the chunk edge: re-scan it next round
                    pos = hit + Needle.Length; keepFrom = have; continue;             // over-long or unterminated: not a tag
                }
                AddTag(Encoding.Latin1.GetString(span.Slice(hit, close + 1)), map);
                pos = hit + close + 1; keepFrom = have;
            }
            if (eof) break;
            int carry = have - keepFrom;
            if (carry > 0) span.Slice(keepFrom, carry).CopyTo(buf);
            have = carry;
        }
        return map;
    }

    private static void AddTag(string tag, Dictionary<Guid, string> map)
    {
        var g = GuidAttr.Match(tag); var nm = NameAttr.Match(tag);
        if (g.Success && nm.Success && Guid.TryParse(g.Groups[1].Value, out var guid)) map[guid] = nm.Groups[1].Value;
    }
}

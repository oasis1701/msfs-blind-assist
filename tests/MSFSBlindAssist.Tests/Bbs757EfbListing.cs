using System.Globalization;
using System.Text;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A built tablet window as plain text, one line per element (<c>role  words</c>), for the golden listings in
/// <c>Fixtures/bbs757-efb-memory/*.listing.txt</c>: a change to how a page reads shows as a readable diff.
/// </summary>
internal static class Bbs757EfbListing
{
    internal static string Of(Bbs757EfbElements.Built built)
    {
        var data = built.Data;
        var sb = new StringBuilder();
        sb.Append("title: ").Append(data.GetValueOrDefault("page", "")).Append('\n');
        for (int i = 0; i < built.Count; i++)
        {
            string Get(string field) => data.GetValueOrDefault($"items.{i}.{field}", "");
            string text = Get("text"), type = Get("type"), kind = Get("kind"), value = Get("value");
            int.TryParse(Get("level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level);
            if (type == "pre")
            {
                sb.Append("pre\n");
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                    sb.Append("    ").Append(line).Append('\n');
                continue;
            }
            string role;
            if (kind == "alert") role = "alert";
            else if (level == 2 || level == 3) role = "heading " + level.ToString(CultureInfo.InvariantCulture);
            else if (type == "checkbox") role = value == "true" ? "checkbox checked" : "checkbox not checked";
            else if (type == "select") role = $"combo [{value}] {{{string.Join("|", Get("options").Split((char)0x1f))}}}";
            else if (type == "range") role = "slider " + value;
            else if (type == "text") role = $"edit [{value}]";
            else if (kind == "button" || Get("clickable") == "true") role = "button";
            else role = "text";
            sb.Append(role).Append("  ").Append(text).Append('\n');
        }
        return sb.ToString();
    }
}

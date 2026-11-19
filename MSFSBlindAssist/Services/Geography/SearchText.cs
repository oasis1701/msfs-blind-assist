using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>
/// The form a typed name and a stored name are both compared in: the EFB's airport search
/// (<c>AirportNameSearch</c>) folds airport idents, names and cities this way, and
/// <see cref="OfflineLocationService.FindTowns"/> folds GeoNames town names the same way, so
/// "Göteborg" and "goteborg", or "O'Hare" and "ohare", are one name to both.
/// </summary>
public static class SearchText
{
    /// <summary>
    /// Lower case without accents, with every run of punctuation or spaces as one space:
    /// "Göteborg" → "goteborg", "Charles-De-Gaulle" → "charles de gaulle". Apostrophes are dropped,
    /// not spaced, so "Chicago-O'Hare" → "chicago ohare" and "St John's" → "st johns". Letters
    /// Unicode does not decompose are spelled out (ø → o, æ → ae, ß → ss) so the Nordic and German
    /// names match their plain spellings.
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var sb = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || IsApostrophe(c))
                continue;
            if (!char.IsLetterOrDigit(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c switch
            {
                'ø' => "o",
                'æ' => "ae",
                'œ' => "oe",
                'ß' => "ss",
                'đ' => "d",
                'ð' => "d",
                'ł' => "l",
                'ı' => "i",
                'þ' => "th",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }

    // The typewriter apostrophe, the typographic ones, the modifier letter (a letter to Unicode,
    // so it would otherwise stay inside the word) and the backtick some sources type instead.
    private static bool IsApostrophe(char c) => c is '\'' or '’' or '‘' or 'ʼ' or '`';
}

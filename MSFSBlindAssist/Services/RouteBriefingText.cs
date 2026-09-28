using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Cleans the AI's route briefing before the pilot reads it. The prompt asks the owner's real-world taxi question
/// (<see cref="GeminiService.RealWorldTaxiQuestion"/>) and tells the AI never to write it out, but a model does not
/// follow every instruction: a live KMEM→KATL briefing (Gemini, 2026-09-26) opened both "Real-world practice" parts by
/// repeating the question with its blanks filled in. This removes that echo — the whole question, or either of its two
/// sentences, quoted, labelled, on its own line or after the heading — and nothing else: every line it does not touch
/// comes back byte-identical, and a line it empties is deleted with its line break. The echo must START its line
/// (after indentation, a list marker, or a heading that ends in a colon or a spaced dash) — unanchored, the patterns
/// deleted the AI's OWN sentences whenever they used the question's words ("Ground will provide the step-by-step
/// taxi route after you vacate…" became "Ground will "). Both AI providers call it on their route briefing.
/// </summary>
public static class RouteBriefingText
{
    /// <summary>The question's first words, before its first blank. A test pins that the question starts with them.</summary>
    public const string QuestionOpening = "Provide the step-by-step taxi route";

    /// <summary>The first words of the question's second sentence.</summary>
    public const string SecondSentenceOpening = "Please include the expected taxiways";

    /// <summary>The question's last words.</summary>
    public const string QuestionEnding = "any specific restrictions";

    // IgnoreCase always with CultureInvariant: under tr-TR the pattern's "i" would otherwise stop matching an "I".
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // What may stand before an echo on its line, and is KEPT: indentation, list markers ("-", "*", "•", ">", "1."),
    // and a heading ending in a colon or a spaced dash ("Real-world practice:", "**Real-world practice:**"). The echo
    // must START there. Unanchored, the patterns deleted the AI's own sentences whenever they used the question's words:
    // "Ground will provide the step-by-step taxi route after you vacate…" became "Ground will ". "Question:" and
    // "Prompt:" are never a heading — they are the echo's own label, removed with it.
    private const string Lead =
        @"(?<lead>^[ \t]*(?:(?:[-*•>]+|\d+[.)])[ \t]*)*" +
        @"(?:(?!(?:Question|Prompt)\s*:)[^\r\n]{1,80}?(?::|[ \t][–—-])(?:\*\*|__)?[ \t]*)?)";
    private const string Label = @"(?:(?:Question|Prompt)\s*:\s*)?";
    private const string Quote = "[\"“”'‘’]?";

    // 1. The whole question. It must contain the second sentence's own opening words, so an answer that mentions
    //    "any specific restrictions" after a first-sentence echo is never swallowed with it.
    private static readonly Regex WholeQuestion = new(
        Lead + Label + Quote + Regex.Escape(QuestionOpening) + @"[^\r\n]*?" + Regex.Escape(SecondSentenceOpening) +
        @"[^\r\n]*?" + Regex.Escape(QuestionEnding) + @"\.?" + Quote + @"[ \t]*", Options);

    // 2. Its first sentence alone: to the first period followed by whitespace or the end of the line, else to the end.
    private static readonly Regex FirstSentence = new(
        Lead + Label + Quote + Regex.Escape(QuestionOpening) + @"[^\r\n]*?(?:\." + Quote + @"(?=\s|$)|$)[ \t]*", Options);

    // 3. Its second sentence alone.
    private static readonly Regex SecondSentence = new(
        Lead + Quote + Regex.Escape(SecondSentenceOpening) + @"[^\r\n]*?" + Regex.Escape(QuestionEnding) +
        @"\.?" + Quote + @"[ \t]*", Options);

    /// <summary>The briefing with every echo of the question removed; the same instance when there is none.</summary>
    public static string RemoveEchoedTaxiQuestion(string text)
    {
        if (string.IsNullOrEmpty(text) ||
            (text.IndexOf(QuestionOpening, StringComparison.OrdinalIgnoreCase) < 0 &&
             text.IndexOf(SecondSentenceOpening, StringComparison.OrdinalIgnoreCase) < 0))
            return text;

        var lines = text.Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            bool carriageReturn = line.EndsWith('\r');
            string content = carriageReturn ? line[..^1] : line;
            string cleaned = SecondSentence.Replace(
                FirstSentence.Replace(WholeQuestion.Replace(content, "${lead}"), "${lead}"), "${lead}");
            if (string.Equals(cleaned, content, StringComparison.Ordinal))
            {
                kept.Add(line);
                continue;
            }
            // Emptied by the removal — only a list marker or quote left: drop the line and its break. A surviving
            // marker ("-", "*", "•", ">", or a numbered "1."/"2)") is never a letter, only a heading's own words are,
            // so IsLetter (not IsLetterOrDigit) is what tells a real heading apart from a bare numbered marker.
            if (!cleaned.Any(char.IsLetter)) continue;
            kept.Add(carriageReturn ? cleaned + "\r" : cleaned);
        }
        return string.Join('\n', kept);
    }
}

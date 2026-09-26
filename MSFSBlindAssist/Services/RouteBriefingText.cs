using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Cleans the AI's route briefing before the pilot reads it. The prompt asks the owner's real-world taxi question
/// (<see cref="GeminiService.RealWorldTaxiQuestion"/>) and tells the AI never to write it out, but a model does not
/// follow every instruction: a live KMEM→KATL briefing (Gemini, 2026-09-26) opened both "Real-world practice" parts by
/// repeating the question with its blanks filled in. This removes that echo — the whole question, or either of its two
/// sentences, quoted, labelled, on its own line or after the heading — and nothing else: every line it does not touch
/// comes back byte-identical, and a line it empties is deleted with its line break. Both AI providers call it on their
/// route briefing.
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
    private const string Label = @"(?:(?:Question|Prompt)\s*:\s*)?";
    private const string Quote = "[\"“”]?";

    // 1. The whole question, wherever it is on the line, with the spaces after it.
    private static readonly Regex WholeQuestion = new(
        Label + Quote + Regex.Escape(QuestionOpening) + @"[^\r\n]*?" + Regex.Escape(QuestionEnding) +
        @"\.?" + Quote + @"[ \t]*", Options);

    // 2. Its first sentence alone: to the first period followed by whitespace or the end of the line, else to the end.
    private static readonly Regex FirstSentence = new(
        Label + Quote + Regex.Escape(QuestionOpening) + @"[^\r\n]*?(?:\." + Quote + @"(?=\s|$)|$)[ \t]*", Options);

    // 3. Its second sentence alone.
    private static readonly Regex SecondSentence = new(
        Quote + Regex.Escape(SecondSentenceOpening) + @"[^\r\n]*?" + Regex.Escape(QuestionEnding) +
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
            string cleaned = SecondSentence.Replace(FirstSentence.Replace(WholeQuestion.Replace(content, ""), ""), "");
            if (string.Equals(cleaned, content, StringComparison.Ordinal))
            {
                kept.Add(line);
                continue;
            }
            if (cleaned.Trim().Length == 0) continue;   // emptied by the removal: drop the line and its break
            kept.Add(carriageReturn ? cleaned + "\r" : cleaned);
        }
        return string.Join('\n', kept);
    }
}

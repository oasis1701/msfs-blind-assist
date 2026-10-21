namespace GuideBuilder;

/// <summary>
/// The advisory "User guide" check on pull requests (CORE-19), run by .github/workflows/guide.yml. When
/// <see cref="UserGuideImpact"/> says a PR may change what a pilot meets, it passes if the PR updates guide/ or its
/// description answers with a "User guide:" line, and fails otherwise. It is not a required check.
/// </summary>
public static class PullRequestCheck
{
    /// <summary>Whether the PR passes, and the message that says why.</summary>
    public static (bool Pass, string Message) Evaluate(string description, IEnumerable<string> addedPaths,
        IEnumerable<string> changedPaths)
    {
        List<string> changed = changedPaths.ToList();
        IReadOnlyList<string> reasons = UserGuideImpact.Reasons(addedPaths, changed);
        IReadOnlyList<string> guideChanges = UserGuideImpact.GuideChanges(changed);
        UserGuideLineResult line = UserGuideLine.Parse(description);

        if (guideChanges.Count > 0)
        {
            return (true, $"This PR updates the user guide: {string.Join(", ", guideChanges)}.");
        }

        if (reasons.Count == 0)
        {
            string note = line.Answer switch
            {
                UserGuideAnswer.Missing => "",
                UserGuideAnswer.Invalid => " Worth fixing anyway: " + line.Error,
                _ => $" The description says \"User guide: {line.Value}\".",
            };
            return (true, "Not asked: this PR adds no feature, aircraft or improvement fragment and changes no hotkey " +
                "guide. CORE-19 still applies whenever a pilot would notice the change." + note);
        }

        string whatItDoes = string.Join(", ", reasons);

        if (line.Answered)
        {
            return (true, $"User guide: {line.Value}. Accepted. Asked because this PR {whatItDoes}.");
        }

        string problem = line.Answer == UserGuideAnswer.Invalid ? line.Error + "\n" : "";
        return (false, problem +
            $"This PR {whatItDoes}, so it either updates the user guide in guide/ or says why not (CORE-19). " +
            "Update the guide in this PR, or write one of these in the description, on a line of its own:\n" +
            "  User guide: needed: <chapter and section>   (the guide still has to be updated there)\n" +
            "  User guide: none, because <why the guide is still right>\n" +
            "Editing the description re-runs this check. It is advisory: it does not block merging.");
    }
}

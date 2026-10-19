namespace ChangelogBuilder;

/// <summary>
/// The "guide-check" mode run by .github/workflows/user-guide.yml (CORE-19):
///
///   ChangelogBuilder guide-check --body &lt;file&gt; --added &lt;list&gt; --changed &lt;list&gt;
///
/// --body is the PR description; --added and --changed are newline-delimited path lists from git diff (added
/// files, and every changed file). Writes one message to stdout and exits 0 when the PR passes, 1 when it needs
/// a User guide line it lacks, 2 on a usage error.
/// </summary>
public static class UserGuideCheck
{
    public static int Run(string[] args)
    {
        string? bodyPath = null, addedPath = null, changedPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--body" when i + 1 < args.Length:
                    bodyPath = args[++i];
                    break;
                case "--added" when i + 1 < args.Length:
                    addedPath = args[++i];
                    break;
                case "--changed" when i + 1 < args.Length:
                    changedPath = args[++i];
                    break;
                case "--body" or "--added" or "--changed":
                    Console.Error.WriteLine($"Missing value for option: {args[i]}");
                    return 2;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        foreach ((string option, string? path) in new[] { ("--body", bodyPath), ("--added", addedPath), ("--changed", changedPath) })
        {
            if (path is null)
            {
                Console.Error.WriteLine($"Missing required option: {option} <file>");
                return 2;
            }

            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"{option} not found: {path}");
                return 2;
            }
        }

        (bool pass, string message) = Evaluate(
            File.ReadAllText(bodyPath!), File.ReadAllLines(addedPath!), File.ReadAllLines(changedPath!));
        Console.WriteLine(message);
        return pass ? 0 : 1;
    }

    /// <summary>Pure core of <see cref="Run"/>: whether the PR passes, and the message that says why.</summary>
    public static (bool Pass, string Message) Evaluate(string description, IEnumerable<string> addedPaths,
        IEnumerable<string> changedPaths)
    {
        IReadOnlyList<string> reasons = UserGuideImpact.Reasons(addedPaths, changedPaths);
        UserGuideLineResult line = UserGuideLine.Parse(description);

        if (reasons.Count == 0)
        {
            string note = line.Answer switch
            {
                UserGuideAnswer.Missing => "",
                UserGuideAnswer.Invalid => " Worth fixing anyway: " + line.Error,
                _ => $" The description says \"User guide: {line.Value}\".",
            };
            return (true, "Not required: this PR adds no feature, aircraft or improvement fragment and changes no hotkey " +
                "guide. CORE-19 still asks for the line whenever a pilot would notice the change." + note);
        }

        string whatItDoes = string.Join(", ", reasons);

        if (line.Answered)
        {
            return (true, $"User guide: {line.Value}. Accepted. Required because this PR {whatItDoes}.");
        }

        string problem = line.Answer == UserGuideAnswer.Invalid ? line.Error + "\n" : "";
        return (false, problem +
            $"This PR {whatItDoes}, so its description needs a filled-in User guide line (CORE-19). Write one of these on a line of its own:\n" +
            $"  User guide: https://github.com/{UserGuideLine.SiteRepository}/pull/<n>   (the site PR that updates the guide)\n" +
            "  User guide: needed: <chapter and section>   (the guide still has to be updated there)\n" +
            "  User guide: none, because <why the guide is still right>\n" +
            "Editing the description re-runs this check. It is advisory: it does not block merging.");
    }
}

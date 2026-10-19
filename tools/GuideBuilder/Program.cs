using GuideBuilder;

// The user guide's build and checks. docs/user-guide.md describes where each runs.
//
//   GuideBuilder check      --guide <dir>
//       Checks guide/ (GuideChecker): contents, heading ids, links. Exit 1 on any problem.
//   GuideBuilder one-page   --guide <dir> --out <file> [--edition local|release|preview] [--version <v>]
//       Writes the whole guide as one self-contained HTML page (OnePageBuilder).
//   GuideBuilder check-site --root <built site> [--under guide]
//       Checks the links in a built copy of the website (SiteLinkChecker).
//   GuideBuilder check-pr   --body <file> --added <list> --changed <list>
//       The advisory "User guide" PR check (PullRequestCheck). --added and --changed are git diff --name-only lists.
//
// Exit codes: 0 pass, 1 problems found, 2 usage error.

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: GuideBuilder check|one-page|check-site|check-pr [options]; see Program.cs.");
    return args.Length == 0 ? 2 : 0;
}

Dictionary<string, string>? options = ParseOptions(args[1..]);
if (options is null) return 2;

string? Need(string name)
{
    if (options.TryGetValue(name, out string? value)) return value;
    Console.Error.WriteLine($"Missing required option: {name}");
    return null;
}

switch (args[0])
{
    case "check":
    {
        if (Need("--guide") is not { } guide) return 2;
        return Report(GuideChecker.Check(GuideSource.FromFolder(guide)), "The guide passes its checks.");
    }

    case "one-page":
    {
        if (Need("--guide") is not { } guide || Need("--out") is not { } output) return 2;
        string editionName = options.GetValueOrDefault("--edition", "local");
        if (!Enum.TryParse(editionName, ignoreCase: true, out GuideEdition edition))
        {
            Console.Error.WriteLine($"Unknown --edition: {editionName} (local, release or preview).");
            return 2;
        }

        OnePageResult result = OnePageBuilder.Build(GuideSource.FromFolder(guide), edition, options.GetValueOrDefault("--version"));
        if (!result.Ok) return Report(result.Problems, "");
        string? folder = Path.GetDirectoryName(Path.GetFullPath(output));
        if (folder is not null) Directory.CreateDirectory(folder);
        File.WriteAllText(output, result.Html);
        Console.WriteLine($"Wrote {output} ({result.Html!.Length / 1024} KB, {edition} edition).");
        return 0;
    }

    case "check-site":
    {
        if (Need("--root") is not { } root) return 2;
        return Report(SiteLinkChecker.Check(root, options.GetValueOrDefault("--under", "guide")), "Every link in the built guide resolves.");
    }

    case "check-pr":
    {
        if (Need("--body") is not { } body || Need("--added") is not { } added || Need("--changed") is not { } changed) return 2;
        foreach (string path in new[] { body, added, changed })
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Not found: {path}");
                return 2;
            }
        }

        (bool pass, string message) = PullRequestCheck.Evaluate(File.ReadAllText(body), File.ReadAllLines(added), File.ReadAllLines(changed));
        Console.WriteLine(message);
        return pass ? 0 : 1;
    }

    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        return 2;
}

static Dictionary<string, string>? ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Unexpected argument: {args[i]}");
            return null;
        }

        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Missing value for option: {args[i]}");
            return null;
        }

        options[args[i]] = args[++i];
    }

    return options;
}

static int Report(IReadOnlyList<string> problems, string allClear)
{
    if (problems.Count == 0)
    {
        if (allClear.Length > 0) Console.WriteLine(allClear);
        return 0;
    }

    Console.WriteLine($"{problems.Count} problem(s):");
    foreach (string problem in problems) Console.WriteLine($"  {problem}");
    return 1;
}

using ChangelogBuilder;

// Usage:
//   ChangelogBuilder --out <file> [--from-file <list>] [--contributors <map>] [<fragment path> ...]
//
// --from-file takes a newline-delimited list of paths, which is what `git diff
// --name-only` produces; that avoids shell quoting entirely in the workflows. Positional
// paths are for local use and the dry run. Both may be combined.
//
// --contributors takes the <pr>=<login>,<login> map written by
// tools/changelog-contributors.sh; entries gain " — @login" attribution. Omitting the
// flag renders unattributed (local/dry-run use).
//
// An empty input is NOT an error: a release with no fragments publishes with GitHub's
// generated notes alone.

var outPath = (string?)null;
var listPath = (string?)null;
var contributorsPath = (string?)null;
var paths = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            outPath = args[++i];
            break;
        case "--from-file" when i + 1 < args.Length:
            listPath = args[++i];
            break;
        case "--contributors" when i + 1 < args.Length:
            contributorsPath = args[++i];
            break;
        case "--out" or "--from-file" or "--contributors":
            // The guarded cases above only match when a value follows; reaching here means
            // the flag is real but was given no value (e.g. it was the last argument) — a
            // different fault than an unrecognized flag, so it needs its own message or a
            // CI log reader is sent looking for a typo that isn't there.
            Console.Error.WriteLine($"Missing value for option: {args[i]}");
            return 2;
        case "--help" or "-h":
            Console.WriteLine("Usage: ChangelogBuilder --out <file> [--from-file <list>] [--contributors <map>] [<path> ...]");
            return 0;
        default:
            if (args[i].StartsWith('-'))
            {
                Console.Error.WriteLine($"Unknown option: {args[i]}");
                return 2;
            }

            paths.Add(args[i]);
            break;
    }
}

if (outPath is null)
{
    Console.Error.WriteLine("Missing required option: --out <file>");
    return 2;
}

if (listPath is not null)
{
    if (!File.Exists(listPath))
    {
        Console.Error.WriteLine($"--from-file not found: {listPath}");
        return 2;
    }

    paths.AddRange(File.ReadAllLines(listPath)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0));
}

var fragments = new List<ChangelogFragment>();
var errors = new List<string>();

foreach (var path in paths)
{
    if (!File.Exists(path))
    {
        errors.Add($"{path}: file not found.");
        continue;
    }

    var result = ChangelogFragment.Parse(path, File.ReadAllText(path));

    if (result.Ok)
    {
        fragments.Add(result.Fragment!);
    }
    else
    {
        errors.Add(result.Error!);
    }
}

// Report EVERY error, not just the first — one bad file must not hide three others.
if (errors.Count > 0)
{
    Console.Error.WriteLine($"{errors.Count} invalid changelog fragment(s):");
    foreach (var error in errors)
    {
        Console.Error.WriteLine($"  {error}");
    }

    return 1;
}

// The contributor map is validated as strictly as the fragments: it is machine-written,
// so a malformed line means the generating script broke, and rendering anyway would
// silently misattribute. The graceful path (an unresolvable PR) is the script OMITTING
// that line, which renders the entry unattributed.
IReadOnlyDictionary<int, IReadOnlyList<string>> contributors = new Dictionary<int, IReadOnlyList<string>>();
if (contributorsPath is not null)
{
    if (!File.Exists(contributorsPath))
    {
        Console.Error.WriteLine($"--contributors not found: {contributorsPath}");
        return 2;
    }

    var mapResult = ContributorMap.Parse(File.ReadAllText(contributorsPath));
    if (!mapResult.Ok)
    {
        Console.Error.WriteLine($"{mapResult.Errors.Count} invalid contributor-map line(s) in {contributorsPath}:");
        foreach (var error in mapResult.Errors)
        {
            Console.Error.WriteLine($"  {error}");
        }

        return 1;
    }

    contributors = mapResult.Map;
}

var released = fragments.Count(f => f.Category != ChangelogCategory.Internal);
File.WriteAllText(outPath, ChangelogRenderer.Render(fragments, contributors));

Console.WriteLine(
    $"Wrote {outPath}: {released} entr{(released == 1 ? "y" : "ies")} " +
    $"from {fragments.Count} fragment(s).");

return 0;

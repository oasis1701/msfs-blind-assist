using ChangelogBuilder;

// Usage:
//   ChangelogBuilder --out <file> [--from-file <list>] [<fragment path> ...]
//
// --from-file takes a newline-delimited list of paths, which is what `git diff
// --name-only` produces; that avoids shell quoting entirely in the workflows. Positional
// paths are for local use and the dry run. Both may be combined.
//
// An empty input is NOT an error: a release with no fragments publishes with GitHub's
// generated notes alone.

var outPath = (string?)null;
var listPath = (string?)null;
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
        case "--help" or "-h":
            Console.WriteLine("Usage: ChangelogBuilder --out <file> [--from-file <list>] [<path> ...]");
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

var released = fragments.Count(f => f.Category != ChangelogCategory.Internal);
File.WriteAllText(outPath, ChangelogRenderer.Render(fragments));

Console.WriteLine(
    $"Wrote {outPath}: {released} entr{(released == 1 ? "y" : "ies")} " +
    $"from {fragments.Count} fragment(s).");

return 0;

using System.Text.RegularExpressions;
using MSFSBlindAssist.DisplayLayouts;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>
/// Finds every installed copy of a WASM module's file under a packages root, in Community or an Official folder
/// (OneStore one level deeper). Read-only: it only lists folders.
/// </summary>
public static class ModuleFiles
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false,
    };

    /// <summary>
    /// For each folder directly under <paramref name="packagesRoot"/> whose name starts with Community or
    /// Official: the packages at depth 1 or 2 whose name matches the module's pattern, then the folders of each
    /// of the module's PathsInPackage and its file. Sorted ordinal, each file once; a missing root gives an empty
    /// list, and a folder that cannot be listed is skipped.
    /// </summary>
    public static IReadOnlyList<string> FindInstalled(string packagesRoot, ModuleDescription module)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(packagesRoot) || !Directory.Exists(packagesRoot)) return found;

        var packageName = new Regex(module.PackageNamePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var top in Directories(packagesRoot))
        {
            string name = Path.GetFileName(top);
            if (!name.StartsWith("Community", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Official", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var child in Directories(top))
            {
                if (packageName.IsMatch(Path.GetFileName(child))) { AddFiles(child, module, found); continue; }
                foreach (var grandchild in Directories(child))
                    if (packageName.IsMatch(Path.GetFileName(grandchild))) AddFiles(grandchild, module, found);
            }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
    }

    private static void AddFiles(string package, ModuleDescription module, List<string> found)
    {
        foreach (var path in module.PathsInPackage)
        {
            var folders = new List<string> { package };
            foreach (string segment in path)
            {
                var next = new List<string>();
                foreach (var folder in folders)
                {
                    if (segment.Contains('*')) next.AddRange(Directories(folder, segment));
                    else next.Add(Path.Combine(folder, segment));
                }
                folders = next;
            }

            foreach (var folder in folders)
            {
                string wasm = Path.Combine(folder, module.WasmFileName);
                if (File.Exists(wasm)) found.Add(wasm); // File.Exists never throws: an unreadable path is false
            }
        }
    }

    /// <summary>The subfolders of <paramref name="dir"/>; empty when it is missing or cannot be listed.</summary>
    private static List<string> Directories(string dir, string pattern = "*")
    {
        try
        {
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.EnumerateDirectories(dir, pattern, Options).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new List<string>();
        }
    }
}

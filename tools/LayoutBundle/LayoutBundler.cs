using System.Text.RegularExpressions;
using MSFSBlindAssist.DisplayLayouts;

namespace LayoutBundle;

public sealed record BundleResult(bool Ok, IReadOnlyList<string> Lines);

/// <summary>Copies the layout files of a layouts store into a build's output after checking that each loads in this
/// build and that every module the build knows has at least one. Nothing is copied when anything fails.</summary>
public static class LayoutBundler
{
    private static readonly Regex LayoutName = new(@"^[0-9a-f]{64}-[0-9a-f]{16}\.json$", RegexOptions.CultureInvariant);

    public static BundleResult Bundle(string storeFolder, string outputFolder, IReadOnlyList<ModuleDescription> modules)
    {
        var lines = new List<string>();
        if (!Directory.Exists(storeFolder))
        {
            lines.Add($"{storeFolder}: the store folder does not exist");
            return new BundleResult(false, lines);
        }

        bool ok = true;
        var copies = new List<(string Source, string Module, string Name)>();

        foreach (string folder in Directory.GetDirectories(storeFolder).OrderBy(f => f, StringComparer.Ordinal))
        {
            string id = Path.GetFileName(folder);
            if (id.StartsWith('.')) continue;
            if (!modules.Any(m => m.Id == id))
                lines.Add($"{id}: skipped, not a module this app knows");
        }

        foreach (var module in modules)
        {
            string folder = Path.Combine(storeFolder, module.Id);
            var names = Directory.Exists(folder)
                ? Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().Where(n => LayoutName.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal).ToList()
                : new List<string>();
            if (names.Count == 0)
            {
                lines.Add($"{module.Id}: no layout file");
                ok = false;
                continue;
            }

            int good = 0;
            foreach (string name in names)
            {
                string source = Path.Combine(folder, name);
                if (!LayoutFileFormat.TryRead(module, File.ReadAllText(source), out var file, out var failure))
                {
                    lines.Add($"{module.Id}/{name}: {failure}");
                    ok = false;
                }
                else if (file.FileName != name)
                {
                    lines.Add($"{module.Id}/{name}: the name does not match the contents (expected {file.FileName})");
                    ok = false;
                }
                else
                {
                    // How many readers have a layout, so a release log shows a file whose readers all failed.
                    int withLayout = file.Readers.Count(r => r.Layout is not null);
                    lines.Add($"{module.Id}/{name}: generator version {file.GeneratorVersion}, {withLayout} of {file.Readers.Count} readers have a layout");
                    copies.Add((source, module.Id, name));
                    good++;
                }
            }
            lines.Add($"{good} layout files for {module.Id}");
        }

        if (ok)
        {
            foreach (var (source, module, name) in copies)
            {
                string dir = Path.Combine(outputFolder, module);
                Directory.CreateDirectory(dir);
                File.Copy(source, Path.Combine(dir, name), true);
            }
        }
        return new BundleResult(ok, lines);
    }
}

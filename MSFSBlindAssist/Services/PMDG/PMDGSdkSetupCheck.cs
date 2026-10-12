using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>One options file still missing the SDK lines: the row the setup dialog lists.</summary>
public sealed record PMDGSdkFileProblem(string Path, string Name, string Problem)
{
    /// <summary>The row as the pilot hears it: "737-800: no [SDK] section".</summary>
    public override string ToString() => $"{Name}: {Problem}";
}

/// <summary>What a check of one family found: how many options files exist, and which need the lines.</summary>
public sealed record PMDGSdkSetupResult(PMDGSdkFamily Family, int FilesFound, IReadOnlyList<PMDGSdkFileProblem> Problems)
{
    /// <summary>True when no options file exists yet or any needs the lines: the dialog is shown.</summary>
    public bool NeedsAttention => FilesFound == 0 || Problems.Count > 0;
}

/// <summary>
/// The check behind the PMDG SDK setup dialog, run when the pilot picks the PMDG 737 or 777 from the
/// Aircraft menu and at startup for a restored PMDG profile ([PSDK-2]): READS every installed variant's
/// options file (never writes one, [PSDK-1]) and says which still lack the <c>[SDK]</c> lines the CDU
/// window and the panel readouts need.
/// </summary>
public static class PMDGSdkSetupCheck
{
    /// <summary>Checks the four storage roots on this machine. Never throws for one bad file.</summary>
    public static PMDGSdkSetupResult Run(PMDGSdkFamily family) => Run(family, PMDGOptionsIniLocator.StorageRoots());

    /// <summary>
    /// Checks the given storage roots. A file that cannot be read is listed with the reason rather
    /// than skipped, so the pilot is told; rows name the simulator too once files were found under
    /// both, since the same variant then appears twice.
    /// </summary>
    public static PMDGSdkSetupResult Run(PMDGSdkFamily family, IEnumerable<PMDGStorageRoot> roots)
    {
        var files = PMDGOptionsIniLocator.FindOptionsFiles(roots, family);
        bool nameSimulator = files.Select(f => f.Simulator).Distinct(StringComparer.Ordinal).Count() > 1;

        var problems = new List<PMDGSdkFileProblem>();
        foreach (var file in files)
        {
            string? problem;
            try
            {
                problem = PMDGOptionsIniFormat.DescribeProblem(ReadAllLinesShared(file.Path), family.RequiresCenterCdu);
            }
            catch (Exception ex)
            {
                problem = $"could not be read ({ex.Message})";
            }
            Log.Debug("PMDG", $"SDK setup check: {file.Path}: {problem ?? "SDK lines present"}");
            if (problem == null) continue;

            string name = PMDGSdkFamily.VariantName(file.PackageFolder);
            if (nameSimulator) name += ", " + file.Simulator;
            problems.Add(new PMDGSdkFileProblem(file.Path, name, problem));
        }

        Log.Info("PMDG", $"SDK setup check for the {family.DisplayName}: {files.Count} options file(s), {problems.Count} missing the SDK lines.");
        return new PMDGSdkSetupResult(family, files.Count, problems);
    }

    /// <summary>
    /// The dialog's opening text. With files to fix: why the lines matter, that MSFSBA does not edit
    /// PMDG's files, and the steps, ending with how many files are listed. With no file yet: that PMDG
    /// creates it the first time the aircraft is loaded, and what to do once it exists.
    /// </summary>
    public static string Instructions(PMDGSdkSetupResult result)
    {
        var family = result.Family;
        if (result.FilesFound == 0)
        {
            return
                $"MSFS Blind Assist could not find the {family.DisplayName} options file, {family.OptionsFileName}. " +
                $"PMDG creates it the first time the aircraft is loaded in the simulator, so this is expected " +
                $"if you have not loaded a {family.DisplayName} yet.\r\n\r\n" +
                $"The {family.DisplayName} needs the lines below in that file, or the CDU window and the cockpit " +
                "readouts stay empty. Once the file exists, close Microsoft Flight Simulator, add the lines at the " +
                "end of the file, save it and start the simulator again. Picking the " +
                $"{family.DisplayName} from the Aircraft menu again, or restarting MSFS Blind Assist, checks the " +
                "file and lists where it is.";
        }

        int count = result.Problems.Count;
        return
            $"PMDG's SDK data broadcast is not switched on for the {family.DisplayName}, so the CDU window and " +
            "the cockpit readouts cannot work. MSFS Blind Assist does not change PMDG's files; to switch it on:\r\n\r\n" +
            "1. Close Microsoft Flight Simulator. PMDG rewrites this file when the simulator exits and can drop " +
            "lines added while it runs.\r\n" +
            "2. For each file listed below, select it and press Open folder, then press Enter on the selected file to open it.\r\n" +
            "3. If the file has an [SDK] section, replace it with the lines below; otherwise add them at the end. " +
            "Keep only one [SDK] section.\r\n" +
            "4. Save the file and start the simulator.\r\n\r\n" +
            $"{count} {(count == 1 ? "file" : "files")} to fix, listed below.";
    }

    /// <summary>Reads the file without ever blocking PMDG's own write: shared for write and delete.</summary>
    private static List<string> ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null) lines.Add(line);
        return lines;
    }
}

// The test assembly's settings: what keeps every test of the process away from the real settings file, and the scratch
// folder a test takes on top of that when it needs a first start of its own.
//
// SettingsManager's paths are private static readonly fields, made from %APPDATA%, and its Load runs one-time seeds, each
// of which saves whenever its flag is false: that is every settings file on a developer's machine, until they run an app
// that has the flag. Any test that reaches SettingsManager.Current runs that Load (SpeechCapture is a
// ScreenReaderAnnouncer, whose constructor reads Current, and dozens of test files build one or call Save). So the first
// "dotnet test" after pulling a new seed would rewrite the real settings of whoever runs it, in the shape of the test
// build, which loses every key that build does not know.
//
// So before any test runs, whatever order they run in, a module initializer (FenixTestHost is the precedent, and an
// assembly may have several) points the three paths at a scratch folder for the whole test process. The folder is this
// process's own, because two worktrees can test at the same time. The legacy path goes with the other two, or a Load
// with no settings file would copy the machine's old FBWBA file into the folder and read it back. The current settings
// are not touched: nothing has loaded yet, and the first Current loads from the folder as a first start does. So every
// test on every machine reads the defaults, as it always has on CI, which has no real file. The folder is deleted when
// the process exits, if it can be; a leftover one is harmless.

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

internal static class FenixChannelTestSettings
{
    [ModuleInitializer]
    internal static void KeepEveryTestAwayFromTheRealSettingsFile()
    {
        string folder = SettingsRedirect.NewScratchFolder();
        SettingsRedirect.PointSettingsManagerAt(folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SettingsRedirect.DeleteScratchFolder(folder);
    }
}

public class FenixChannelTestSettingsTests
{
    [Fact]
    public void No_test_can_reach_a_real_settings_file()
    {
        // The module initializer is all that stands between this process and the settings file of whoever runs the
        // tests, and without this test nothing would miss it: every other test still passes, CI has no real file, and a
        // developer's is rewritten in silence. A redirect another test holds at this moment is under the temp folder as
        // well, so this holds whatever runs beside it.
        string temp = Path.GetTempPath();
        string appData = Path.TrimEndingDirectorySeparator(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)) + Path.DirectorySeparatorChar;

        string[] paths =
        [
            SettingsManager.GetSettingsFilePath(),
            (string)SettingsRedirect.SettingsManagerField("SettingsDirectory").GetValue(null)!,
            (string)SettingsRedirect.SettingsManagerField("LegacySettingsFilePath").GetValue(null)!,
        ];

        foreach (string path in paths)
        {
            Assert.StartsWith(temp, path, StringComparison.OrdinalIgnoreCase);
            Assert.False(path.StartsWith(appData, StringComparison.OrdinalIgnoreCase), $"{path} is under %APPDATA%");
        }
    }
}

/// <summary>
/// Points SettingsManager at a scratch folder of its own for as long as it lives, on top of the folder the whole test
/// process already uses, and starts it with no current settings, so the first Current loads from the scratch folder as
/// a first start does. Dispose puts back what it found and deletes the folder. The class that holds one is in the
/// "SettingsManagerGlobalState" collection: the redirect is process-wide, so no other test that reaches SettingsManager
/// may run beside it.
/// </summary>
/// <remarks>
/// The redirect SettingsSeedTests makes, by the same means: the paths are <c>static readonly</c>, which reflection will
/// not set once the type has run its initializer, so a tiny dynamic method stores them (<c>stsfld</c>); that class's
/// own helper is private to it, so the pattern is repeated here. One path more is redirected: the legacy FBWBA file. A
/// Load with no settings file would otherwise copy whatever file is at that path into the scratch folder and read it
/// back as a fresh install.
/// </remarks>
internal sealed class SettingsRedirect : IDisposable
{
    private static readonly FieldInfo DirectoryField = SettingsManagerField("SettingsDirectory");
    private static readonly FieldInfo FilePathField = SettingsManagerField("SettingsFilePath");
    private static readonly FieldInfo LegacyFilePathField = SettingsManagerField("LegacySettingsFilePath");
    private static readonly FieldInfo CurrentSettingsField = SettingsManagerField("_currentSettings");

    private readonly object? _originalDirectory = DirectoryField.GetValue(null);
    private readonly object? _originalFilePath = FilePathField.GetValue(null);
    private readonly object? _originalLegacyFilePath = LegacyFilePathField.GetValue(null);
    private readonly object? _originalCurrentSettings = CurrentSettingsField.GetValue(null);

    /// <summary>The scratch folder. It is created by the first save into it.</summary>
    public string Folder { get; }

    /// <summary>The settings file inside it, the one SettingsManager now reads and writes.</summary>
    public string SettingsFile { get; }

    public SettingsRedirect()
    {
        Folder = NewScratchFolder();
        SettingsFile = PointSettingsManagerAt(Folder);
        CurrentSettingsField.SetValue(null, null);   // not initonly, plain SetValue is fine
    }

    public void Dispose()
    {
        SetStaticField(DirectoryField, _originalDirectory);
        SetStaticField(FilePathField, _originalFilePath);
        SetStaticField(LegacyFilePathField, _originalLegacyFilePath);
        CurrentSettingsField.SetValue(null, _originalCurrentSettings);

        DeleteScratchFolder(Folder);
    }

    /// <summary>A folder name of its own under the temp folder. Nothing creates it yet.</summary>
    internal static string NewScratchFolder()
        => Path.Combine(Path.GetTempPath(), "MSFSBlindAssistTests_" + Guid.NewGuid().ToString("N"));

    /// <summary>Points SettingsManager's three paths into <paramref name="folder"/>; gives the settings file.</summary>
    internal static string PointSettingsManagerAt(string folder)
    {
        string settingsFile = Path.Combine(folder, "settings.json");
        SetStaticField(DirectoryField, folder);
        SetStaticField(FilePathField, settingsFile);
        SetStaticField(LegacyFilePathField, Path.Combine(folder, "legacy", "settings.json"));
        return settingsFile;
    }

    internal static void DeleteScratchFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leftover scratch folder is harmless.
        }
    }

    internal static FieldInfo SettingsManagerField(string name)
        => typeof(SettingsManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    // FieldInfo.SetValue refuses a static readonly field once the declaring type has run its initializer; a
    // hand-emitted stsfld against the same field is accepted by the runtime, which is how SettingsSeedTests redirects
    // these too.
    private static void SetStaticField(FieldInfo field, object? value)
    {
        var method = new DynamicMethod(
            "ForceSetStatic_" + field.Name,
            typeof(void),
            new[] { typeof(object) },
            typeof(SettingsRedirect).Module,
            skipVisibility: true);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, field.FieldType);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);

        var setter = (Action<object?>)method.CreateDelegate(typeof(Action<object?>));
        setter(value);
    }
}

// Characterization tests for the one-time seed/migration statics in
// MSFSBlindAssist.Settings.SettingsManager: SeedTakeoffAssistToneConvention and
// SeedFenixMonitorDefaults. Pins the InvertPanning->SteerTowardTone mapping, the
// 0->1 degree threshold bump, the fresh-install/upgrade split, the already-seeded
// no-op path, and idempotence (running a seed twice == running it once).
//
// SeedFenixMonitorDefaults calls SettingsManager.Save() internally the first time
// it actually seeds (see SettingsManager.cs ~152-169), which would otherwise write
// to the real %APPDATA%\MSFSBlindAssist\settings.json and publish
// SettingsManager.Current. To honor "never touch the real settings file", this
// suite redirects the private SettingsDirectory/SettingsFilePath fields to a
// scratch temp folder via reflection for the lifetime of each test and restores
// them (plus _currentSettings) in Dispose. The shared, DisableParallelization'd
// collection (see SettingsManagerGlobalStateCollection) keeps that redirect from
// racing any other test that might touch SettingsManager.

using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
public class SettingsSeedTests : IDisposable
{
    private static readonly FieldInfo DirectoryField = typeof(SettingsManager)
        .GetField("SettingsDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo FilePathField = typeof(SettingsManager)
        .GetField("SettingsFilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo CurrentSettingsField = typeof(SettingsManager)
        .GetField("_currentSettings", BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly object? _originalDirectory;
    private readonly object? _originalFilePath;
    private readonly object? _originalCurrentSettings;
    private readonly string _tempDirectory;

    public SettingsSeedTests()
    {
        _originalDirectory = DirectoryField.GetValue(null);
        _originalFilePath = FilePathField.GetValue(null);
        _originalCurrentSettings = CurrentSettingsField.GetValue(null);

        _tempDirectory = Path.Combine(Path.GetTempPath(), "MSFSBlindAssistTests_" + Guid.NewGuid().ToString("N"));
        SetStaticField(DirectoryField, _tempDirectory);
        SetStaticField(FilePathField, Path.Combine(_tempDirectory, "settings.json"));
    }

    public void Dispose()
    {
        SetStaticField(DirectoryField, _originalDirectory);
        SetStaticField(FilePathField, _originalFilePath);
        CurrentSettingsField.SetValue(null, _originalCurrentSettings); // not initonly, plain SetValue is fine

        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leftover scratch temp dir is harmless.
        }
    }

    /// <summary>
    /// FieldInfo.SetValue refuses to write a `static readonly` field once the
    /// declaring type has run its type initializer (FieldAccessException:
    /// "Cannot set initonly static field"). SettingsDirectory/SettingsFilePath ARE
    /// `static readonly` (by design — they're not meant to change at runtime), so
    /// this suite emits a tiny DynamicMethod that does a raw `stsfld` instead: the
    /// InitOnly restriction is a metadata convention enforced only by the
    /// reflection API surface (and by peverify/the C# compiler), not by the JIT
    /// when executing IL that wasn't produced by csc — so a hand-emitted `stsfld`
    /// against the same FieldInfo is accepted and writes the field like any other
    /// static. This is the only way to redirect SettingsManager's disk-write
    /// target away from the real %APPDATA% path without touching product code.
    /// </summary>
    private static void SetStaticField(FieldInfo field, object? value)
    {
        var method = new DynamicMethod(
            "ForceSetStatic_" + field.Name,
            typeof(void),
            new[] { typeof(object) },
            typeof(SettingsSeedTests).Module,
            skipVisibility: true);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, field.FieldType);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);

        var setter = (Action<object?>)method.CreateDelegate(typeof(Action<object?>));
        setter(value);
    }

    // --- SeedTakeoffAssistToneConvention ------------------------------------

    [Fact]
    public void TakeoffAssist_FreshInstall_LeavesMappingAtClassDefaults()
    {
        var settings = new UserSettings();
        // Sanity: class defaults before seeding.
        Assert.True(settings.TakeoffAssistSteerTowardTone);
        Assert.Equal(1, settings.TakeoffAssistHeadingToneThreshold);
        Assert.False(settings.TakeoffAssistToneConventionMigrated);

        SettingsManager.SeedTakeoffAssistToneConvention(settings, freshInstall: true);

        Assert.True(settings.TakeoffAssistToneConventionMigrated);
        // Fresh installs keep the class defaults untouched by the mapping/bump.
        Assert.True(settings.TakeoffAssistSteerTowardTone);
        Assert.Equal(1, settings.TakeoffAssistHeadingToneThreshold);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TakeoffAssist_Upgrade_MapsSteerTowardToneFromInvertPanning(bool invertPanning, bool expectedSteerTowardTone)
    {
        var settings = new UserSettings
        {
            TakeoffAssistInvertPanning = invertPanning,
            TakeoffAssistToneConventionMigrated = false,
        };

        SettingsManager.SeedTakeoffAssistToneConvention(settings, freshInstall: false);

        Assert.Equal(expectedSteerTowardTone, settings.TakeoffAssistSteerTowardTone);
        Assert.True(settings.TakeoffAssistToneConventionMigrated);
    }

    [Fact]
    public void TakeoffAssist_Upgrade_BumpsStoredZeroThresholdToOne()
    {
        var settings = new UserSettings
        {
            TakeoffAssistHeadingToneThreshold = 0,
            TakeoffAssistToneConventionMigrated = false,
        };

        SettingsManager.SeedTakeoffAssistToneConvention(settings, freshInstall: false);

        Assert.Equal(1, settings.TakeoffAssistHeadingToneThreshold);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void TakeoffAssist_Upgrade_KeepsNonZeroThreshold(int storedThreshold)
    {
        var settings = new UserSettings
        {
            TakeoffAssistHeadingToneThreshold = storedThreshold,
            TakeoffAssistToneConventionMigrated = false,
        };

        SettingsManager.SeedTakeoffAssistToneConvention(settings, freshInstall: false);

        Assert.Equal(storedThreshold, settings.TakeoffAssistHeadingToneThreshold);
    }

    [Fact]
    public void TakeoffAssist_AlreadyMigrated_NoChanges()
    {
        var settings = new UserSettings
        {
            TakeoffAssistToneConventionMigrated = true,
            TakeoffAssistInvertPanning = true,
            TakeoffAssistSteerTowardTone = false, // deliberately diverges from the mapping
            TakeoffAssistHeadingToneThreshold = 0, // deliberately diverges from the bump
        };

        SettingsManager.SeedTakeoffAssistToneConvention(settings, freshInstall: false);

        Assert.False(settings.TakeoffAssistSteerTowardTone);
        Assert.Equal(0, settings.TakeoffAssistHeadingToneThreshold);
    }

    [Fact]
    public void TakeoffAssist_Idempotent_SecondCallMatchesFirst()
    {
        var settings = new UserSettings
        {
            TakeoffAssistInvertPanning = true,
            TakeoffAssistHeadingToneThreshold = 0,
            TakeoffAssistToneConventionMigrated = false,
        };

        SettingsManager.SeedTakeoffAssistToneConvention(settings, freshInstall: false);
        var afterFirst = (
            settings.TakeoffAssistSteerTowardTone,
            settings.TakeoffAssistHeadingToneThreshold,
            settings.TakeoffAssistToneConventionMigrated
        );

        SettingsManager.SeedTakeoffAssistToneConvention(settings, freshInstall: false);
        var afterSecond = (
            settings.TakeoffAssistSteerTowardTone,
            settings.TakeoffAssistHeadingToneThreshold,
            settings.TakeoffAssistToneConventionMigrated
        );

        Assert.Equal(afterFirst, afterSecond);
    }

    // --- SeedFenixMonitorDefaults --------------------------------------------

    private static readonly string[] ExpectedDefaultSilentVars =
    {
        "N_MIP_CLOCK_CHRONO", "N_MIP_CLOCK_ELAPSED", "N_MIP_CLOCK_UTC",
        "FNX2PLD_clockChr", "FNX2PLD_clockEt",
        "S_SEAT_HEIGHT_CAPT", "S_SEAT_DISTANCE_CAPT",
        "S_SEAT_HEIGHT_FO", "S_SEAT_DISTANCE_FO",
    };

    [Fact]
    public void Fenix_NotYetSeeded_AddsDefaultSilentVarsAndSetsFlag()
    {
        var settings = new UserSettings
        {
            FenixMonitorDefaultsSeeded = false,
        };

        SettingsManager.SeedFenixMonitorDefaults(settings);

        Assert.True(settings.FenixMonitorDefaultsSeeded);
        foreach (var key in ExpectedDefaultSilentVars)
        {
            Assert.Contains(key, settings.FenixDisabledMonitorVariables);
        }
        Assert.Equal(ExpectedDefaultSilentVars.Length, settings.FenixDisabledMonitorVariables.Count);
    }

    [Fact]
    public void Fenix_NotYetSeeded_DoesNotDuplicateAlreadyPresentVar()
    {
        var settings = new UserSettings
        {
            FenixMonitorDefaultsSeeded = false,
        };
        settings.FenixDisabledMonitorVariables.Add("N_MIP_CLOCK_CHRONO"); // user already disabled this one

        SettingsManager.SeedFenixMonitorDefaults(settings);

        Assert.Equal(1, settings.FenixDisabledMonitorVariables.Count(v => v == "N_MIP_CLOCK_CHRONO"));
        Assert.Equal(ExpectedDefaultSilentVars.Length, settings.FenixDisabledMonitorVariables.Count);
    }

    [Fact]
    public void Fenix_AlreadySeeded_NoChanges()
    {
        var settings = new UserSettings
        {
            FenixMonitorDefaultsSeeded = true,
        };
        // FenixDisabledMonitorVariables is deliberately left empty — proves the
        // early return skips re-adding the defaults.

        SettingsManager.SeedFenixMonitorDefaults(settings);

        Assert.True(settings.FenixMonitorDefaultsSeeded);
        Assert.Empty(settings.FenixDisabledMonitorVariables);
    }

    [Fact]
    public void Fenix_Idempotent_SecondCallMatchesFirst()
    {
        var settings = new UserSettings
        {
            FenixMonitorDefaultsSeeded = false,
        };

        SettingsManager.SeedFenixMonitorDefaults(settings);
        bool seededAfterFirst = settings.FenixMonitorDefaultsSeeded;
        var varsAfterFirst = new List<string>(settings.FenixDisabledMonitorVariables);

        SettingsManager.SeedFenixMonitorDefaults(settings);
        bool seededAfterSecond = settings.FenixMonitorDefaultsSeeded;
        var varsAfterSecond = new List<string>(settings.FenixDisabledMonitorVariables);

        Assert.Equal(seededAfterFirst, seededAfterSecond);
        Assert.Equal(varsAfterFirst, varsAfterSecond);
    }
}

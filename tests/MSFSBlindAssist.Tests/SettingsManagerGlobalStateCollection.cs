using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// SettingsManager holds process-global mutable static state: `_currentSettings`
/// (published by Save()) and the private `SettingsDirectory`/`SettingsFilePath`
/// fields that point Save()'s disk write at the real %APPDATA%\MSFSBlindAssist
/// folder. SettingsSeedTests redirects those path fields via reflection to a
/// throwaway temp directory (so the seed migration's internal Save() call in
/// SeedFenixMonitorDefaults never touches the real settings file) and restores
/// everything afterward. That reflection-based redirect is itself global,
/// process-wide state for the duration of a test, so — same as
/// DistanceUnitGlobalStateCollection — this shared collection name opts every
/// test class touching it out of xUnit's default per-class parallelism.
/// </summary>
[CollectionDefinition("SettingsManagerGlobalState", DisableParallelization = true)]
public class SettingsManagerGlobalStateCollection
{
}

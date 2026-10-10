using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The CONF 3 landing selection caps the FBW-family auto-flap extension at CONF 3 (FOA-6). Its
/// L:var differs per airframe: FlyByWire #10890 (2026-09-11) renamed the A32NX's
/// A32NX_SPEEDS_LANDING_CONF3 to A32NX_FM_LANDING_CONF3, the A380X writes A380X_FM_LANDING_CONF3,
/// and Headwind's A339X still writes the old name. A name nobody writes reads NaN, which the cap
/// takes as FULL — the A32NX and A380 bug found 2026-10-09. Each airframe registers its own name
/// OnRequest and its evaluator polls it.
/// </summary>
public class Conf3RegistrationTests
{
    private const string A32nxConf3 = "A32NX_FM_LANDING_CONF3";
    private const string A330Conf3 = "A32NX_SPEEDS_LANDING_CONF3";
    private const string A380Conf3 = "A380X_FM_LANDING_CONF3";

    [Fact]
    public void A32nx_reads_fbws_renamed_conf3()
    {
        Assert.Equal(A32nxConf3, FbwA320StateEvaluator.LandingConf3Field);
        var polled = new FbwA320StateEvaluator().OnRequestPollFields;
        Assert.Contains(A32nxConf3, polled);
        Assert.DoesNotContain(A330Conf3, polled);
    }

    [Fact]
    public void A380_reads_its_own_conf3()
    {
        Assert.Equal(A380Conf3, FbwA380StateEvaluator.LandingConf3Field);
        var polled = new FbwA380StateEvaluator().OnRequestPollFields;
        Assert.Contains(A380Conf3, polled);
        Assert.DoesNotContain(A330Conf3, polled);
    }

    [Fact]
    public void A330_keeps_headwinds_name()
    {
        var polled = new HwA330StateEvaluator().OnRequestPollFields;
        Assert.Contains(A330Conf3, polled);
        Assert.DoesNotContain(A32nxConf3, polled);
    }

    [Fact]
    public void Each_airframe_registers_only_its_own_conf3()
    {
        var a32nx = new FlyByWireA320Definition().GetVariables();
        Assert.True(a32nx.ContainsKey(A32nxConf3));
        Assert.False(a32nx.ContainsKey(A330Conf3));

        var a330 = new HeadwindA330Definition().GetVariables();
        Assert.True(a330.ContainsKey(A330Conf3));
        Assert.False(a330.ContainsKey(A32nxConf3));

        var a380 = new FlyByWireA380Definition().GetVariables();
        Assert.True(a380.ContainsKey(A380Conf3));
        Assert.False(a380.ContainsKey(A330Conf3));
    }

    // ------------------------------------------------------------------
    // Delivery contract
    // ------------------------------------------------------------------

    /// <summary>
    /// Registering the key is not the same as being DELIVERED it. The app runs exactly two
    /// machineries that push an L:var into the SimConnect value cache the FO evaluator reads
    /// (LVarStateEvaluator.GetValue -> GetCachedVariableValue): CONTINUOUS (the batch, or the
    /// per-var PERIOD.SECOND subscription of an ExcludeFromBatch var — all three gates also need
    /// IsAnnounced) and ON REQUEST (FirstOfficerForm's 1 s poll over OnRequestPollFields). So
    /// Continuous WITHOUT IsAnnounced is a declaration nothing honours (FOA-6).
    /// </summary>
    private static void AssertConf3ReachableByARealDeliveryPath(
        IReadOnlyDictionary<string, SimVarDefinition> vars,
        IReadOnlyList<string> pollFields,
        string key,
        string aircraft)
    {
        Assert.True(vars.TryGetValue(key, out var def),
            $"{aircraft}: {key} is unregistered, so nothing can deliver it.");

        bool continuousDelivered = def!.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced;
        bool pollDelivered = def.UpdateFrequency == UpdateFrequency.OnRequest && pollFields.Contains(key);

        Assert.True(continuousDelivered || pollDelivered,
            $"{aircraft}: {key} is registered UpdateFrequency.{def.UpdateFrequency} " +
            $"(IsAnnounced={def.IsAnnounced}, in poll list={pollFields.Contains(key)}), which no " +
            "delivery path honours. Register it OnRequest and poll it from the evaluator (FOA-6).");
    }

    [Fact]
    public void A32nx_conf3_registration_matches_a_real_delivery_path()
        => AssertConf3ReachableByARealDeliveryPath(new FlyByWireA320Definition().GetVariables(),
            new FbwA320StateEvaluator().OnRequestPollFields, A32nxConf3, "A32NX");

    [Fact]
    public void A330_conf3_registration_matches_a_real_delivery_path()
        => AssertConf3ReachableByARealDeliveryPath(new HeadwindA330Definition().GetVariables(),
            new HwA330StateEvaluator().OnRequestPollFields, A330Conf3, "A339X");

    [Fact]
    public void A380_conf3_registration_matches_a_real_delivery_path()
        => AssertConf3ReachableByARealDeliveryPath(new FlyByWireA380Definition().GetVariables(),
            new FbwA380StateEvaluator().OnRequestPollFields, A380Conf3, "A380X");

    // ------------------------------------------------------------------
    // The auto managers read their own airframe's name
    // ------------------------------------------------------------------

    /// <summary>
    /// The definitions register, and the evaluators poll, the right name (above), but the cap itself
    /// is read by each auto manager, and a manager reading another airframe's name reads NaN, which
    /// the cap takes as FULL: the original bug. The managers cannot be constructed in a test
    /// (ScreenReaderAnnouncer must never be instantiated twice), so, as FbwA320VfeNextTests pins their
    /// VFE-next read, the CONF 3 read is pinned on the source text. The A32NX and A380 managers read
    /// their evaluator's LandingConf3Field (its value is pinned above) and name no CONF 3 variable of
    /// their own; the A330 manager reads Headwind's name as a literal (FOA-6).
    /// </summary>
    [Theory]
    [InlineData("FBWA320", "FbwA320FOAutoManager.cs",
        "_state.GetValue(FbwA320StateEvaluator.LandingConf3Field)", "")]
    [InlineData("FBWA380", "FbwA380FOAutoManager.cs",
        "_state.GetValue(FbwA380StateEvaluator.LandingConf3Field)", "")]
    [InlineData("HWA330", "HwA330FOAutoManager.cs",
        "_state.GetValue(\"A32NX_SPEEDS_LANDING_CONF3\")", "A32NX_SPEEDS_LANDING_CONF3")]
    public void Each_auto_manager_reads_its_own_airframes_conf3(
        string folder, string fileName, string expectedRead, string expectedLiteral)
    {
        string source = File.ReadAllText(FirstOfficerSourcePath(Path.Combine(folder, fileName)));

        Assert.Contains(expectedRead, source);

        string[] literals = Regex.Matches(source, "\"[A-Za-z0-9_]*LANDING_CONF3\"")
            .Select(m => m.Value.Trim('"')).Distinct().ToArray();
        string[] expected = expectedLiteral.Length == 0 ? System.Array.Empty<string>() : new[] { expectedLiteral };
        Assert.True(expected.SequenceEqual(literals),
            $"{fileName} names the CONF 3 variable(s) [{string.Join(", ", literals)}], expected " +
            $"[{string.Join(", ", expected)}]. A manager on another airframe's name reads NaN, " +
            "which the cap takes as FULL (FOA-6).");
    }

    private static string FirstOfficerSourcePath(string relativePath,
        [CallerFilePath] string thisTestFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisTestFilePath)!,
            "..", "..", "..", "MSFSBlindAssist", "FirstOfficer", relativePath));
}

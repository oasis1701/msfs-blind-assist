using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins CoherentPmdgEfbClient's push signature. FbwEfbForm is re-rendered only when it changes, so a
/// field left out of it is a change the pilot never sees. Review round 2 of PR 189 (B5/B6): the
/// agent's reconcile key and a dropdown's option list were missing from it.
/// </summary>
public class CoherentPmdgEfbClientSignatureTests
{
    private static CoherentPmdgEfbClient.ScrapeElement Runway(string? key, params string[] options) => new()
    {
        idx = 3,
        kind = "",
        text = "Runway",
        value = "RW06L",
        controlType = "select",
        key = key,
        options = options.Length > 0 ? new List<string>(options) : null,
    };

    private static string Sig(CoherentPmdgEfbClient.ScrapeElement e) =>
        CoherentPmdgEfbClient.ElementsSignature("Perf", new[] { e });

    [Fact]
    public void TheSameElements_GiveTheSameSignature() =>
        Assert.Equal(Sig(Runway("step-value:Runway", "RW06L", "RW06R")), Sig(Runway("step-value:Runway", "RW06L", "RW06R")));

    [Fact]
    public void AnOptionListThatChangesUnderAnUnchangedValue_ChangesTheSignature() =>
        Assert.NotEqual(Sig(Runway(null, "RW06L", "RW06R")), Sig(Runway(null, "RW06L", "RW24L")));

    [Fact]
    public void AChangedKey_ChangesTheSignature() =>
        Assert.NotEqual(Sig(Runway("step-next:Runway")), Sig(Runway("step-next:Arrival Runway")));

    [Fact]
    public void AKeyAppearing_ChangesTheSignature() =>
        Assert.NotEqual(Sig(Runway(null)), Sig(Runway("step-value:Runway")));
}

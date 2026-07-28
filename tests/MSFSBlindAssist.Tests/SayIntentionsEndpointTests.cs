// Characterization tests for SayIntentions SAPI URL construction.
//
// SECURITY: the hostname comes from %LOCALAPPDATA%\SayIntentionsAI\flight.json —
// a file this app does not own. PR #86 appended the user's API key to whatever
// that file said, so a tampered or corrupt flight.json could exfiltrate the
// credential to an arbitrary host, in cleartext if the scheme was http.
// Anything not https on a sayintentions.ai host falls back to the default.
//
// The key stays a query parameter because that is how SAPI documents its auth;
// the allowlist is the mitigation that does not risk breaking the integration.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsEndpointTests
{
    [Theory]
    [InlineData("https://apipri.sayintentions.ai", true)]
    [InlineData("https://api.sayintentions.ai/sapi", true)]
    [InlineData("https://sayintentions.ai", true)]
    [InlineData("http://apipri.sayintentions.ai", false)]      // cleartext
    [InlineData("https://evil.example.com", false)]            // off-allowlist
    [InlineData("https://sayintentions.ai.evil.com", false)]   // suffix spoof
    [InlineData("https://notsayintentions.ai", false)]         // substring spoof
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HostAllowlist(string? hostname, bool allowed)
    {
        Assert.Equal(allowed, SayIntentionsEndpoint.IsAllowedHost(hostname));
    }

    [Fact]
    public void DisallowedHostFallsBackToTheDefault()
    {
        string url = SayIntentionsEndpoint.Build("http://evil.example.com", "getParking", "KEY123");
        Assert.StartsWith("https://apipri.sayintentions.ai/sapi/getParking", url);
    }

    [Fact]
    public void SapiSegmentIsAddedOnceOnly()
    {
        Assert.Equal(
            "https://api.sayintentions.ai/sapi/getCommsHistory?api_key=KEY123",
            SayIntentionsEndpoint.Build("https://api.sayintentions.ai/sapi/", "getCommsHistory", "KEY123"));
    }

    [Fact]
    public void ApiKeyIsUrlEncoded()
    {
        Assert.Contains("api_key=a%2Bb%26c", SayIntentionsEndpoint.Build(null, "getParking", "a+b&c"));
    }

    [Fact]
    public void RedactionStripsTheKeyForLogging()
    {
        string url = SayIntentionsEndpoint.Build(null, "getParking", "SECRET");
        Assert.DoesNotContain("SECRET", SayIntentionsEndpoint.Redact(url));
    }
}

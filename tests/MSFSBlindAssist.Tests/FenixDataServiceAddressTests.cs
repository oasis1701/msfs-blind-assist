// Pins [A320-40]: the Fenix MCDU service addresses the Fenix data service as 127.0.0.1. The
// service listens on IPv4 only; by name, Windows tries ::1 first and spends about 2 s refusing it
// on every NEW connection, which was the MCDU's stall on the first key press after a minute idle
// (measured 2005 ms against 1 ms, 2026-10-11). A source scan, because the stall only shows against
// the live service.

using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class FenixDataServiceAddressTests
{
    [Fact]
    public void Fenix_mcdu_service_never_addresses_the_data_service_by_name()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "FenixMCDUService.cs"));

        Assert.DoesNotMatch(new Regex(@"(?i)(ws|http)s?://localhost"), source);
        Assert.Contains("\"ws://127.0.0.1:8083/graphql\"", source);
        Assert.Contains("\"http://127.0.0.1:8083/graphql\"", source);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}

using System;
using System.IO;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Every DA40 window that reads through the definition must close with it: the Alt+S engine
/// window kept refreshing against an aircraft that had been switched away.
/// </summary>
public class CowsDA40TeardownTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException("MSFSBlindAssist.sln not found");
    }

    [Fact]
    public void TheEngineGlanceWindowClosesWhenTheAircraftIsSwitchedAway()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(),
            "MSFSBlindAssist", "Aircraft", "DA40", "CowsDA40Definition.CasMonitor.cs"));
        int stop = src.IndexOf("public void StopCasMonitor()", StringComparison.Ordinal);
        Assert.True(stop >= 0);
        int end = src.IndexOf("if (_casClient == null) return;", stop, StringComparison.Ordinal);
        Assert.Contains("CloseEngineGlance();", src.Substring(stop, end - stop));
    }
}

// Source-scan tests for the Fenix documents window: the window cannot be driven without the
// sim, so these pin what its source must and must never contain (CORE-12, CORE-18, VAT-13, the inert reader,
// GETs only on the UI thread) and that the "Fenix windows" list offers it once.

namespace MSFSBlindAssist.Tests;

public class FenixDocumentsFormSourceTests
{
    static string Root => System.IO.Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist");
    static string FormSource => File.ReadAllText(System.IO.Path.Combine(Root, "Forms", "Fenix", "FenixDocumentsForm.cs"));
    static IEnumerable<string> Owned() => Directory.GetFiles(System.IO.Path.Combine(Root, "Services"), "FenixDocument*.cs")
        .Concat(Directory.GetFiles(System.IO.Path.Combine(Root, "Forms", "Fenix"), "FenixDocuments*.cs"))
        .Append(System.IO.Path.Combine(Root, "Forms", "Fenix", "FenixWindowRegistry.Documents.cs"));

    [Fact] public void Tree_is_native_accessible_and_status_is_never_a_label()
    {
        Assert.Contains("new NativeAccessibleTreeView", FormSource);
        Assert.DoesNotMatch(@"new\s+(System\.Windows\.Forms\.)?(TreeView|Label)\b", FormSource);
        Assert.DoesNotContain("CheckBoxes = true", FormSource);
        Assert.Contains("BeforeExpand", FormSource);
    }

    [Fact] public void Reader_runs_without_scripts_and_cancels_navigation()
    {
        foreach (var s in new[] { "IsScriptEnabled = false", "AreBrowserAcceleratorKeysEnabled = false", "IsWebMessageEnabled = false",
                                  "NavigationStarting", "NewWindowRequested", "ProcessFailed", "NavigateToString(FenixDocumentPage.Build(" })
            Assert.Contains(s, FormSource);
        foreach (var s in new[] { "ExecuteScriptAsync", "AddScriptToExecuteOnDocumentCreated", ".Navigate(" })
            Assert.DoesNotContain(s, FormSource);
    }

    [Fact] public void Never_calls_what_acts_on_the_PC_and_never_leaves_the_UI_thread()
    {
        foreach (var file in Owned())
        {
            string src = File.ReadAllText(file), name = System.IO.Path.GetFileName(file);
            Assert.DoesNotMatch("\"[^\"\\r\\n]*(OpenDocumentsFolder|/documents/outline|\\?page=|\\?matchIndex=)[^\"\\r\\n]*\"", src);   // no such URL in a string
            Assert.DoesNotMatch(@"(?i)(ws|http)s?://localhost", src);
            Assert.False(src.Contains("PostRestAsync(", StringComparison.Ordinal), $"{name} posts");
            Assert.False(src.Contains("ConfigureAwait(false)", StringComparison.Ordinal), $"{name} leaves the UI thread");
        }
    }

    [Fact] public void Registry_lists_Fenix_documents_once()
        => Assert.Single(MSFSBlindAssist.Forms.Fenix.FenixWindowRegistry.Build(), e => e.Name == "Fenix documents");
}

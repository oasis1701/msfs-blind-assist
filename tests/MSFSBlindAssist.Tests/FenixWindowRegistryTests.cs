// The "Fenix windows" list on Ctrl+Shift+F in input mode. Each kind of window registers into
// FenixWindowRegistry from its own partial file (engines, cabin, failures, documents, options), so none
// of them edits another's. With no partial present the registry is empty, and these facts pass
// trivially; they keep the windows honest.

using MSFSBlindAssist.Forms.Fenix;

namespace MSFSBlindAssist.Tests;

public class FenixWindowRegistryTests
{
    [Fact]
    public void Build_returns_unique_names()
    {
        // The list speaks "Name. Description": two entries with one name cannot be told apart by ear.
        var names = FenixWindowRegistry.Build().Select(entry => entry.Name).ToList();
        var duplicates = names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.True(duplicates.Count == 0, "Fenix windows registered more than once: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Build_is_stable()
    {
        // The list is built on every press; a pilot learns the order, so it must not move between presses.
        var first = FenixWindowRegistry.Build().Select(entry => entry.Name).ToList();
        var second = FenixWindowRegistry.Build().Select(entry => entry.Name).ToList();
        Assert.Equal(first, second);
    }

    [Fact]
    public void Every_entry_has_a_name_a_description_and_something_to_open()
    {
        // Name and description are both spoken; an empty one is a silent or half-empty list row.
        foreach (var entry in FenixWindowRegistry.Build())
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name), "A Fenix window has no name.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), $"The Fenix window '{entry.Name}' has no description.");
            Assert.NotNull(entry.Open);
        }
    }
}

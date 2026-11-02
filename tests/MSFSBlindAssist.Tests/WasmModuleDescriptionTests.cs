// A module description says where a WASM module's file lives and which readers it has.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Tests;

public class WasmModuleDescriptionTests
{
    [Fact]
    public void Pmdg737_readers_are_in_anchor_search_order()
    {
        Assert.Equal(new[] { "fma", "engineDisplays", "navDisplay", "pfd" },
            Pmdg737Module.Description.Readers.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void Catalog_finds_the_737_by_file_name_in_any_case()
    {
        Assert.Same(Pmdg737Module.Description, ModuleCatalog.ForFileName(@"C:\x\737ng3.WASM"));
        Assert.Null(ModuleCatalog.ForFileName("PMDG_777_MSFS.wasm"));
    }

    [Fact]
    public void Pmdg737_part_failures_name_the_nd_and_pfd_parts()
    {
        var layout = NdTestMemory.Layout with { Approach = null, ApproachFailure = "a", Pointers = null, PointersFailure = "p" };

        var failures = Pmdg737Module.Description.Reader(Pmdg737Module.NavDisplay).PartFailures(layout);

        Assert.Equal(new[] { ("APP/VOR", "a"), ("pointers", "p") }, failures.ToArray());
    }

    [Fact]
    public void With_source_file_changes_only_the_source_file()
    {
        var reader = Pmdg737Module.Description.Reader(Pmdg737Module.NavDisplay);

        var result = (Pmdg737NavDisplayLayout)reader.WithSourceFile(NdTestMemory.Layout, "x");

        Assert.Equal(NdTestMemory.Layout, result);
        Assert.Equal("x", result.SourceFile);
    }

    [Fact]
    public void An_unknown_reader_id_throws()
    {
        Assert.Throws<KeyNotFoundException>(() => Pmdg737Module.Description.Reader("nope"));
    }
}

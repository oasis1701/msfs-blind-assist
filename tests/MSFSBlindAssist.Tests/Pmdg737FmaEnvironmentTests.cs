// The app's side of the FMA monitor, in the part that needs no simulator: the saved layout as the
// pmdg737-fma log names it, in any culture. The file analysis and the memory search moved to the shared
// module session (WasmModuleSessionTests), with the FMA's view of their results.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737FmaEnvironmentTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_logged_layout_names_everything_read_in_any_culture(string culture)
    {
        var layout = GoldenLayouts.Fma with { SourceFile = @"C:\p\737NG3.wasm" };
        var invariant = Under(CultureInfo.InvariantCulture, () => Pmdg737LayoutDescriptions.Describe(layout));
        var local = Under(new CultureInfo(culture), () => Pmdg737LayoutDescriptions.Describe(layout));
        Assert.Equal(invariant, local);

        Assert.Equal(5, local.Count);
        Assert.Equal(
            @"layout from C:\p\737NG3.wasm: anchor 0x366ac (96 bytes); instance global 0xed2e4, object 19768 bytes, "
            + "display global 0x26d4a0; offsets FMA_AP 520, FMA_AT 2688, FMAPitch 2216, FMAPitchArmed 2220, "
            + "FMARoll 2456, FMARollArmed 2460, FD1 4, FD2 5, MCPDialAlt 2280, MCPDialHdg_C 2488", local[0]);
        Assert.Equal("  A/T words from value 1: THR HLD | ARM | MCP SPD | FMC SPD | N1 | GA | RETARD", local[1]);
        Assert.Equal("  Roll words from value 3: HDG SEL | VOR/LOC | LNAV | B/CRS | FAC | ROLLOUT; value 1: CWS R", local[2]);
        Assert.StartsWith("  Pitch words from value 2: TO/GA | V/S | MCP SPD", local[3], StringComparison.Ordinal);
        Assert.EndsWith("VNAV ALT; value 1: CWS P", local[3], StringComparison.Ordinal);
        Assert.Equal("  AFDS words from value 2: FD | CMD | SINGLE CH | LAND 2 | LAND 3 | NO AUTOLAND", local[4]);
    }

    private static T Under<T>(CultureInfo culture, Func<T> f)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            return f();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

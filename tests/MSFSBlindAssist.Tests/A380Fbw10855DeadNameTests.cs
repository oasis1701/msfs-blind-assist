// FBW #10855 ("add FG part to PRIM", a380x 1bbd304, 2026-08-18) killed five A380 controls without
// either of their names disappearing, which is why the post-#10855 sweep — a diff of A32NX_/A380X_
// tokens — could not see them, and why they went unnoticed until a pilot found the flight directors
// would not come on (2026-09-25):
//
//   - STOCK names never disappear: AUTOPILOT FLIGHT DIRECTOR ACTIVE:n and AUTOPILOT MANAGED SPEED IN
//     MACH lost every writer, and TOGGLE_FLIGHT_DIRECTOR / AP_MANAGED_SPEED_IN_MACH_ON/OFF are now
//     MASKED by the WASM and re-meant (one press of the single FD button; the FMS's own speed/Mach
//     crossover).
//   - L:vars can lose their READER and still take writes: XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT,
//     XMLVAR_Baro_Selector_HPA_{1,2} and A32NX_METRIC_ALT_TOGGLE hold whatever is written to them —
//     the stickiness trap — while the FCU reads other inputs.
//
//   - and a DELETED name can still appear in the FBW tree: L:A32NX_FCDC_{1,2}_FG_DISCRETE_WORD_4 (the
//     approach capability, now FCDC FG word 1 bits 24-26) has no writer, but FBW's OIT maintenance
//     page still reads it.
//
// A name that comes back into the A380's CODE (comments are fine, and are where the history lives)
// brings a dead control back with it, with no error anywhere. The replacements: A380FlightDirector,
// A32NX.FCU_SPD_MACH_TOGGLE_PUSH, A32NX_FCU_ALT_INCREMENT_1000, A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG,
// A380MetricAltitude, A380ApproachCapability.

using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class A380Fbw10855DeadNameTests
{
    public static TheoryData<string> DeadNames() => new()
    {
        "AUTOPILOT FLIGHT DIRECTOR ACTIVE",
        "TOGGLE_FLIGHT_DIRECTOR",
        "AUTOPILOT MANAGED SPEED IN MACH",
        "AP_MANAGED_SPEED_IN_MACH",
        "XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT",
        "XMLVAR_Baro_Selector_HPA",
        "A32NX_METRIC_ALT_TOGGLE",
        // Deleted outright, but FBW's own OIT maintenance page still READS it, so a text search of
        // the FBW tree keeps finding it: the approach capability is FCDC FG word 1 now.
        "A32NX_FCDC_1_FG_DISCRETE_WORD_4",
        "A32NX_FCDC_2_FG_DISCRETE_WORD_4",
    };

    [Theory]
    [MemberData(nameof(DeadNames))]
    public void No_a380_code_uses_a_name_fbw_10855_killed(string name)
    {
        var hits = new List<string>();
        foreach (string file in A380SourceFiles())
        {
            int lineNo = 0;
            foreach (string code in CodeOnly(File.ReadAllLines(file)))
            {
                lineNo++;
                if (code.Contains(name, StringComparison.Ordinal))
                    hits.Add($"{Path.GetFileName(file)}:{lineNo}");
            }
        }

        Assert.True(hits.Count == 0,
            $"'{name}' is back in A380 code, and FBW #10855 left nothing on the other end of it: "
            + string.Join(", ", hits));
    }

    [Fact]
    public void The_scan_sees_the_a380_source()
    {
        // A scan of nothing passes every theory above.
        Assert.Contains(A380SourceFiles(), f => Path.GetFileName(f) == "FlyByWireA380Definition.cs");
        Assert.Contains(A380SourceFiles(), f => Path.GetFileName(f) == "FBWA380AutopilotWindow.cs");
    }

    /// <summary>The A380 definition, its helpers and its windows — and the First Officer's A380 flows
    /// wherever that feature exists: its branch still wrote XMLVAR_Baro_Selector_HPA_{1,2} when this
    /// was written, and a merge would otherwise bring the dead write in unnoticed. (When porting it,
    /// mind the polarity: the old XMLVAR was 1 = hPa, A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG is 1 = inHg.)</summary>
    private static IEnumerable<string> A380SourceFiles()
    {
        string app = Path.Combine(RepoRoot(), "MSFSBlindAssist");
        var files = Directory.EnumerateFiles(Path.Combine(app, "Aircraft"), "FlyByWireA380Definition*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(app, "Aircraft"), "A380*.cs"))
            .Concat(Directory.EnumerateFiles(Path.Combine(app, "Forms", "FBWA380"), "*.cs"));
        string firstOfficer = Path.Combine(app, "FirstOfficer", "FBWA380");
        return Directory.Exists(firstOfficer)
            ? files.Concat(Directory.EnumerateFiles(firstOfficer, "*.cs", SearchOption.AllDirectories))
            : files;
    }

    private static readonly Regex LineComment = new(@"//.*$");

    /// <summary>Each line with its comments removed (line comments and /* */ blocks), in order, so a
    /// line number still points at the source line. Good enough for these files, none of which puts
    /// "//" inside a string literal on a line that also names an actuator.</summary>
    private static IEnumerable<string> CodeOnly(IEnumerable<string> lines)
    {
        bool inBlock = false;
        foreach (string raw in lines)
        {
            string line = raw;
            if (inBlock)
            {
                int end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) { yield return ""; continue; }
                line = line[(end + 2)..];
                inBlock = false;
            }
            int start = line.IndexOf("/*", StringComparison.Ordinal);
            if (start >= 0 && !line[..start].Contains("//", StringComparison.Ordinal))
            {
                int end = line.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0) { inBlock = true; line = line[..start]; }
                else line = line[..start] + line[(end + 2)..];
            }
            yield return LineComment.Replace(line, "");
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}

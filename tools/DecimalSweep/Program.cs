// DecimalSweep: finds numbers the shipped code formats in the PC's culture with NO format specifier.
//
// CORE-21 wants a number that reaches the pilot or another program formatted with the invariant culture:
// "118,300" is what a German Windows makes of $"{value:F3}". DecimalFormatSourceTests scans the source text
// for a decimal FORMAT with no invariant call around it, but a text scan cannot tell that {value} is a
// double. This sweep can: it compiles the source in memory and asks the compiler for each expression's type.
//
// It lists every double, float or decimal that becomes text with no format, outside a log call and outside
// an invariant call: an interpolation hole ($"{value}"), value.ToString(), "text" + value, and a value
// handed to string.Concat, Join, Format, StringBuilder.Append or a TextWriter. A hole that can only hold a
// whole number (Math.Round(x), a whole constant) is left out: no culture gives it a separator.
//
// A clean tree prints "0 to review" and exits 0; anything listed exits 1 (2 when the sweep could not run).
// It is run by hand, never by CI: it needs the app built, and a person decides each line. See README.md.

using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

return Sweep.Run(args);

internal static class Sweep
{
    /// <summary>The shipped projects, the same four DecimalFormatSourceTests scans.</summary>
    private static readonly string[] ShippedProjects =
    {
        "MSFSBlindAssist", "MSFSBlindAssist.DisplayLayouts", "MSFSBlindAssistUpdater", "plugins/MSFSBlindAssist.VPilotPlugin",
    };

    /// <summary>A logger's type: Log, LogChannel, PluginLog, StartupLog, a Logger, and the framework's own.
    /// Case matters: "Dialog" is no logger.</summary>
    private static readonly Regex LoggerType = new(@"^Log([A-Z]|$)|[a-z]Log$|Logger|^Startup$|^Debug$|^Trace$|^Console$");

    /// <summary>A delegate that logs, by the name it is held under: log, _log, logLine, diag, onTrace.</summary>
    private static readonly Regex LoggerDelegate = new(@"^(this\.)?_?(log|diag|trace)|Log(\?|\.|$)", RegexOptions.IgnoreCase);

    private static readonly HashSet<string> LogHelpers = new() { "RolloutDiag", "DiagLog", "Log", "LogLine" };
    private static readonly HashSet<string> TextSinkTypes = new() { "String", "StringBuilder", "TextWriter", "StreamWriter", "StringWriter", "Convert" };
    private static readonly HashSet<string> TextSinkMethods = new()
    {
        "Concat", "Join", "Format", "Append", "AppendLine", "AppendFormat", "AppendJoin", "Insert", "Write", "WriteLine", "ToString",
    };

    public static int Run(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("DecimalSweep [--repo <folder>] [--all] [--rpn]");
            Console.WriteLine("  --repo  the repository root (default: the folder above this one that holds MSFSBlindAssist.sln)");
            Console.WriteLine("  --all   also list what is in a log call, in an invariant call, or whole");
            Console.WriteLine("  --rpn   also list whole numbers written into calculator code in the PC's culture");
            return 0;
        }
        bool all = args.Contains("--all"), rpn = args.Contains("--rpn");
        int repoAt = Array.IndexOf(args, "--repo");
        string? repo = repoAt >= 0 && repoAt + 1 < args.Length ? Path.GetFullPath(args[repoAt + 1]) : FindRepoRoot();
        if (repo == null || !File.Exists(Path.Combine(repo, "MSFSBlindAssist.sln")))
        {
            Console.Error.WriteLine("MSFSBlindAssist.sln was not found. Run from inside the repository, or pass --repo <folder>.");
            return 2;
        }
        List<string>? referenceFolders = ReferenceFolders(repo, out string? problem);
        if (referenceFolders == null)
        {
            Console.Error.WriteLine(problem);
            return 2;
        }

        var totals = new Dictionary<string, int>();
        foreach (string project in ShippedProjects)
            SweepProject(repo, project, referenceFolders, all, rpn, totals);

        int Count(string key) => totals.GetValueOrDefault(key);
        Console.WriteLine();
        Console.WriteLine($"{Count("review")} to review; left out: {Count("log")} in a log call, {Count("invariant")} in an invariant call, {Count("whole")} whole."
            + (rpn ? $" Calculator code: {Count("rpn")} whole numbers in the PC's culture." : ""));
        if (Count("review") > 0)
            Console.WriteLine("Wrap each one that can carry a fraction in FormattableString.Invariant (CORE-21, docs/invariants/core.md).");
        return Count("review") > 0 ? 1 : 0;
    }

    private static string? FindRepoRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        return null;
    }

    /// <summary>
    /// Where the compiler finds what the source refers to: the .NET and Windows Desktop reference assemblies
    /// of the runtime this tool runs on, and the app's own dependencies, read from its build output.
    /// </summary>
    private static List<string>? ReferenceFolders(string repo, out string? problem)
    {
        problem = null;
        string dotnetRoot = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        int major = Environment.Version.Major;
        var folders = new List<string>();
        foreach (string framework in new[] { "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App" })
        {
            string? folder = NewestVersion(Path.Combine(dotnetRoot, "packs", framework + ".Ref"), major) is { } pack
                ? Path.Combine(pack, "ref", $"net{major}.0")
                : NewestVersion(Path.Combine(dotnetRoot, "shared", framework), major);
            if (folder == null || !Directory.Exists(folder))
            {
                problem = $"{framework} {major}.x was not found under {dotnetRoot} (looked in packs and shared). Install the .NET {major} SDK.";
                return null;
            }
            folders.Add(folder);
        }

        string output = Path.Combine(repo, "MSFSBlindAssist", "bin", "x64");
        string? appOutput = !Directory.Exists(output) ? null : Directory.EnumerateDirectories(output)
            .SelectMany(config => Directory.EnumerateDirectories(config, "net*-windows"))
            .Where(folder => File.Exists(Path.Combine(folder, "MSFSBlindAssist.dll")))
            .OrderByDescending(folder => File.GetLastWriteTimeUtc(Path.Combine(folder, "MSFSBlindAssist.dll")))
            .FirstOrDefault();
        if (appOutput == null)
        {
            problem = "The app has not been built here. Build it first (dotnet build MSFSBlindAssist.sln -c Debug): "
                + @"the sweep reads the app's dependencies from MSFSBlindAssist\bin\x64\<configuration>\net10.0-windows.";
            return null;
        }
        folders.Add(appOutput);
        return folders;
    }

    private static string? NewestVersion(string folder, int major) => !Directory.Exists(folder) ? null : Directory.EnumerateDirectories(folder)
        .Select(path => (path, version: Version.TryParse(Path.GetFileName(path).Split('-')[0], out var v) ? v : null))
        .Where(x => x.version != null && x.version.Major == major)
        .OrderByDescending(x => x.version)
        .Select(x => x.path)
        .FirstOrDefault();

    private static void SweepProject(string repo, string project, List<string> referenceFolders, bool all, bool rpn, Dictionary<string, int> totals)
    {
        string folder = Path.Combine(repo, project);
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"{project}: not found, skipped.");
            return;
        }
        // What ships is the Release build: no DEBUG.
        var symbols = new List<string> { "TRACE", "WINDOWS", "NET" };
        for (int v = 5; v <= Environment.Version.Major; v++) symbols.Add($"NET{v}_0_OR_GREATER");
        var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: symbols);

        var trees = new List<SyntaxTree>();
        foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal)) continue;
            // Of the build's own files only the implicit usings: without them half the names resolve to nothing.
            if (relative.StartsWith("obj/", StringComparison.Ordinal) && !relative.EndsWith(".GlobalUsings.g.cs", StringComparison.Ordinal)) continue;
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), parse, file));
        }

        // The project's own assembly stays out: its source is what is being compiled.
        string ownAssembly = Path.GetFileName(project) + ".dll";
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dll in referenceFolders.SelectMany(f => Directory.EnumerateFiles(f, "*.dll")))
        {
            string name = Path.GetFileName(dll);
            if (name.Equals(ownAssembly, StringComparison.OrdinalIgnoreCase) || !seen.Add(name) || !IsManaged(dll)) continue;
            references.Add(MetadataReference.CreateFromFile(dll));
        }

        var compilation = CSharpCompilation.Create("DecimalSweep." + Path.GetFileName(project), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        int errors = compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        Console.WriteLine($"== {project}: {trees.Count} files, {errors} compile errors"
            + (errors > 50 ? " (many: build the solution first, or the types below are unreliable)" : ""));

        foreach (SyntaxTree tree in trees)
        {
            string path = Path.GetRelativePath(repo, tree.FilePath).Replace('\\', '/');
            if (path.Contains("/obj/")) continue;
            var file = new FileSweep(compilation.GetSemanticModel(tree), tree, path, all, rpn, totals);
            file.Walk();
        }
    }

    private static bool IsManaged(string dll)
    {
        try
        {
            using var stream = File.OpenRead(dll);
            using var pe = new PEReader(stream);
            return pe.HasMetadata;
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ITypeSymbol? Unwrap(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    private static bool IsFraction(ITypeSymbol? type) =>
        Unwrap(type)?.SpecialType is SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal;

    /// <summary>A double[] or a List/IEnumerable of doubles: what string.Join and string.Concat write one by one.</summary>
    private static bool IsFractionSequence(ITypeSymbol? type) => type switch
    {
        IArrayTypeSymbol array => IsFraction(array.ElementType),
        INamedTypeSymbol { IsGenericType: true } named => named.TypeArguments.Any(IsFraction),
        _ => false,
    };

    private static bool IsSignedWhole(ITypeSymbol? type) =>
        Unwrap(type)?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16 or SpecialType.System_SByte;

    private static bool IsCalculatorCode(string text) => text.Contains("(>") || text.Contains("(L:") || text.Contains("(A:");

    /// <summary>One source file's walk.</summary>
    private sealed class FileSweep(SemanticModel model, SyntaxTree tree, string path, bool all, bool rpn, Dictionary<string, int> totals)
    {
        public void Walk()
        {
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case InterpolationSyntax hole when hole.FormatClause == null:
                    {
                        ITypeSymbol? type = model.GetTypeInfo(hole.Expression).Type;
                        if (IsFraction(type)) Report("hole", hole, hole.Expression);
                        else if (rpn && IsSignedWhole(type) && IsCalculatorCode(hole.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>()?.ToString() ?? ""))
                            ReportCalculatorWhole(hole);
                        break;
                    }
                    case InvocationExpressionSyntax call:
                        Invocation(call);
                        break;
                    case BinaryExpressionSyntax add when add.IsKind(SyntaxKind.AddExpression):
                        if (model.GetTypeInfo(add).Type?.SpecialType != SpecialType.System_String) break;
                        if (IsFraction(model.GetTypeInfo(add.Left).Type)) Report("concat", add, add.Left);
                        else if (IsFraction(model.GetTypeInfo(add.Right).Type)) Report("concat", add, add.Right);
                        break;
                    case AssignmentExpressionSyntax append when append.IsKind(SyntaxKind.AddAssignmentExpression):
                        if (model.GetTypeInfo(append.Left).Type?.SpecialType == SpecialType.System_String && IsFraction(model.GetTypeInfo(append.Right).Type))
                            Report("concat", append, append.Right);
                        break;
                }
            }
        }

        private void Invocation(InvocationExpressionSyntax call)
        {
            // value.ToString() and value?.ToString(), with no format and no culture.
            if (call.ArgumentList.Arguments.Count == 0)
            {
                ExpressionSyntax? receiver = call.Expression switch
                {
                    MemberAccessExpressionSyntax { Name.Identifier.Text: "ToString" } access => access.Expression,
                    MemberBindingExpressionSyntax { Name.Identifier.Text: "ToString" } => call.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression,
                    _ => null,
                };
                if (receiver != null && IsFraction(model.GetTypeInfo(receiver).Type)) Report("ToString()", call, receiver);
                return;
            }
            if (Method(call) is not { } method || !TextSinkTypes.Contains(method.ContainingType?.Name ?? "") || !TextSinkMethods.Contains(method.Name)) return;
            if (method.Parameters.Any(p => p.Type.Name is "IFormatProvider" or "CultureInfo")) return;
            foreach (ArgumentSyntax argument in call.ArgumentList.Arguments)
            {
                ITypeSymbol? type = model.GetTypeInfo(argument.Expression).Type;
                if (!IsFraction(type) && !(method.Name is "Join" or "Concat" or "AppendJoin" && IsFractionSequence(type))) continue;
                Report($"{method.ContainingType!.Name}.{method.Name}", call, argument.Expression);
                return;
            }
        }

        private IMethodSymbol? Method(InvocationExpressionSyntax call)
        {
            SymbolInfo info = model.GetSymbolInfo(call);
            return (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) as IMethodSymbol;
        }

        /// <summary>A whole number however it is written: Math.Round(x) with no digits, or a whole constant.</summary>
        private bool IsWhole(ExpressionSyntax value)
        {
            if (value is InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } call
                && Method(call) is { ContainingType.Name: "Math" or "MathF", Name: "Round" or "Floor" or "Ceiling" or "Truncate" })
                return true;
            Optional<object?> constant = model.GetConstantValue(value);
            return constant is { HasValue: true, Value: IConvertible number } && Convert.ToDouble(number) % 1 == 0;
        }

        /// <summary>What surrounds a candidate: a log call, an invariant call, speech, calculator code.</summary>
        private SortedSet<string> Context(SyntaxNode candidate)
        {
            var context = new SortedSet<string>(StringComparer.Ordinal);
            foreach (SyntaxNode ancestor in candidate.AncestorsAndSelf())
            {
                switch (ancestor)
                {
                    case InvocationExpressionSyntax call when !ReferenceEquals(call, candidate):
                    {
                        IMethodSymbol? method = Method(call);
                        string name = method?.Name ?? (call.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text ?? call.Expression.ToString();
                        if (name is "Invariant" or "Inv" || (method?.Parameters.Any(p => p.Type.Name is "IFormatProvider" or "CultureInfo") ?? false))
                            context.Add("invariant");
                        if (LogHelpers.Contains(name) || LoggerType.IsMatch(method?.ContainingType?.Name ?? "")
                            || (method?.MethodKind == MethodKind.DelegateInvoke && LoggerDelegate.IsMatch(call.Expression.ToString())))
                            context.Add("log");
                        if (name.StartsWith("Announce", StringComparison.Ordinal)) context.Add("speech");
                        if (name.StartsWith("ExecuteCalculatorCode", StringComparison.Ordinal) || name == "SendMFCommand") context.Add("calculator code");
                        break;
                    }
                    case ObjectCreationExpressionSyntax creation when creation.Type.ToString().EndsWith("Exception", StringComparison.Ordinal):
                        context.Add("exception message");
                        break;
                    case InterpolatedStringExpressionSyntax text:
                    {
                        // Handed to FormattableString.Invariant and the like, or to a handler that formats by itself.
                        string? convertedTo = model.GetTypeInfo(text).ConvertedType?.Name;
                        if (convertedTo is "FormattableString" or "IFormattable") context.Add("invariant");
                        else if (convertedTo is not (null or "String" or "Object")) context.Add("handler " + convertedTo);
                        if (IsCalculatorCode(text.ToString())) context.Add("calculator code");
                        break;
                    }
                    case MemberDeclarationSyntax:
                        return context;
                }
            }
            return context;
        }

        private void Report(string kind, SyntaxNode candidate, ExpressionSyntax value)
        {
            SortedSet<string> context = Context(candidate);
            string? leftOut = context.Contains("invariant") ? "invariant" : context.Contains("log") ? "log" : IsWhole(value) ? "whole" : null;
            totals[leftOut ?? "review"] = totals.GetValueOrDefault(leftOut ?? "review") + 1;
            if (leftOut != null && !all) return;
            if (leftOut == "whole") context.Add("whole");
            Print(kind, candidate, context);
        }

        private void ReportCalculatorWhole(InterpolationSyntax hole)
        {
            SortedSet<string> context = Context(hole);
            if (context.Contains("invariant")) return;
            totals["rpn"] = totals.GetValueOrDefault("rpn") + 1;
            Print("whole number in calculator code", hole, context);
        }

        private void Print(string kind, SyntaxNode candidate, SortedSet<string> context)
        {
            int line = candidate.GetLocation().GetLineSpan().StartLinePosition.Line;
            string source = tree.GetText().Lines[line].ToString().Trim();
            if (source.Length > 180) source = source[..180] + "...";
            string tags = context.Count == 0 ? "" : " (" + string.Join(", ", context) + ")";
            Console.WriteLine($"{path}:{line + 1}: {kind}{tags}");
            Console.WriteLine($"    {source}");
        }
    }
}

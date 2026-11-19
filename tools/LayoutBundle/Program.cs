using LayoutBundle;
using MSFSBlindAssist.DisplayLayouts;

// LayoutBundle <store folder> <output layouts folder>
// Exit 0: every file loads and every known module has one, files copied. 1: a check failed, nothing copied. 2: wrong arguments.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: LayoutBundle <store folder> <output layouts folder>");
    return 2;
}

var result = LayoutBundler.Bundle(args[0], args[1], ModuleCatalog.All);
foreach (string line in result.Lines) Console.WriteLine(line);
return result.Ok ? 0 : 1;

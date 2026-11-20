using System;
using System.IO;
using System.Linq;
using System.Text;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Writes the aircraft's whole variable surface to a file so a live probe can read every
/// one against the running sim.
///
/// ⚠️ ENUMERATED FROM THE BUILT ASSEMBLY, NEVER FROM SOURCE TEXT. A KEY is not a NAME, both
/// sides build names dynamically, and a grep over the definitions reports concatenation
/// fragments and comments as variables - the A380 sweep that did it that way produced 137
/// "unresolved" tokens of which ZERO were real. GetVariables() is the only honest answer.
///
/// Skipped unless MSFSBA_DUMP_VARS is set, so it costs a normal test run nothing.
/// </summary>
public class CowsDA40VariableDumpTests
{
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void DumpVariableSurface(DA40Variant variant)
    {
        string? dir = Environment.GetEnvironmentVariable("MSFSBA_DUMP_VARS");
        if (string.IsNullOrWhiteSpace(dir)) return;

        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        var panelOf = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var (panel, keys) in def.GetPanelControls())
            foreach (var k in keys) panelOf[k] = panel + " (control)";
        foreach (var (panel, keys) in def.GetPanelDisplayVariables())
            foreach (var k in keys) if (!panelOf.ContainsKey(k)) panelOf[k] = panel + " (display)";

        var sb = new StringBuilder();
        sb.AppendLine("key\ttype\tname\tunits\tfreq\tannounced\tbutton\tstates\tpanel\tdisplayName");
        foreach (var kv in vars.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var d = kv.Value;
            sb.Append(kv.Key).Append('\t')
              .Append(d.Type).Append('\t')
              .Append(d.Name).Append('\t')
              .Append(d.Units ?? "").Append('\t')
              .Append(d.UpdateFrequency).Append('\t')
              .Append(d.IsAnnounced ? "1" : "0").Append('\t')
              .Append(d.RenderAsButton ? "1" : "0").Append('\t')
              .Append(d.ValueDescriptions.Count).Append('\t')
              .Append(panelOf.TryGetValue(kv.Key, out var p) ? p : "").Append('\t')
              .Append(d.DisplayName ?? "")
              .AppendLine();
        }

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"da40-{variant}-vars.tsv"), sb.ToString());
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One variable a lamp rule reads: an aircraft L:var, or a stock A: var read in its unit.</summary>
/// <param name="Name">The variable as MSFSBA registers it: <c>INI_STARTER1_OPEN</c>, <c>GEAR POSITION:1</c>.</param>
public sealed record A300LampInput(string Name, bool IsStock, string Units)
{
    /// <summary>One id per variable whatever unit an L:var is written with: <c>L:INI_X</c>,
    /// <c>A:GEAR POSITION:1|Percent</c>.</summary>
    public string Id => IsStock ? $"A:{Name}|{Units}" : $"L:{Name}";
}

/// <summary>
/// A cockpit lamp's state rule, in the cockpit's own RPN (copied from its emissive code by
/// <c>tools/a300-gen</c>): variables, numbers, and the only operators the A300's 496 lamps use,
/// <c>! and or == != &gt; &lt; /</c>. Lit is a value of 0.5 or more, as the cockpit's own brightness
/// reaches full. Pure.
/// </summary>
public sealed class A300LampRule
{
    private static readonly Regex Token = new(@"\((?<k>[LA]):(?<n>[^,)]+?)(?:,\s*(?<u>[^)]+?))?\)|(?<op>\S+)", RegexOptions.CultureInvariant);

    private readonly List<object> _program;

    private A300LampRule(List<object> program, IReadOnlyList<A300LampInput> inputs)
    {
        _program = program;
        Inputs = inputs;
    }

    /// <summary>Every variable the rule reads, once each, in the order it first reads them.</summary>
    public IReadOnlyList<A300LampInput> Inputs { get; }

    /// <summary>Parses a rule; a form the cockpit does not use throws <see cref="FormatException"/>.</summary>
    public static A300LampRule Parse(string state)
    {
        var program = new List<object>();
        var inputs = new List<A300LampInput>();
        int depth = 0;
        foreach (Match m in Token.Matches(state))
        {
            if (m.Groups["k"].Success)
            {
                bool stock = m.Groups["k"].Value == "A";
                var input = new A300LampInput(m.Groups["n"].Value.Trim(), stock,
                    stock ? m.Groups["u"].Value.Trim() : "number");
                var known = inputs.Find(i => i.Id == input.Id);
                if (known == null)
                    inputs.Add(known = input);
                program.Add(known);
                depth++;
                continue;
            }
            string op = m.Groups["op"].Value;
            if (double.TryParse(op, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                program.Add(number);
                depth++;
                continue;
            }
            int needs = op switch
            {
                "!" => 1,
                "and" or "or" or "==" or "!=" or ">" or "<" or "/" => 2,
                _ => throw new FormatException($"A300 lamp rule: '{op}' is not a form the cockpit uses ({state})"),
            };
            if (depth < needs)
                throw new FormatException($"A300 lamp rule: '{op}' has too few values ({state})");
            depth -= needs - 1;
            program.Add(op);
        }
        if (depth != 1)
            throw new FormatException($"A300 lamp rule: leaves {depth} values ({state})");
        return new A300LampRule(program, inputs);
    }

    /// <summary>The rule's value, or null while an input it reads is unread.</summary>
    public double? Evaluate(Func<A300LampInput, double?> read)
    {
        var stack = new Stack<double>();
        foreach (var step in _program)
        {
            switch (step)
            {
                case A300LampInput input:
                    if (read(input) is not double value)
                        return null;
                    stack.Push(value);
                    break;
                case double number:
                    stack.Push(number);
                    break;
                case "!":
                    stack.Push(stack.Pop() == 0 ? 1 : 0);
                    break;
                default:
                    double b = stack.Pop(), a = stack.Pop();
                    stack.Push((string)step switch
                    {
                        "and" => a != 0 && b != 0 ? 1 : 0,
                        "or" => a != 0 || b != 0 ? 1 : 0,
                        "==" => Math.Abs(a - b) < 1e-6 ? 1 : 0,
                        "!=" => Math.Abs(a - b) < 1e-6 ? 0 : 1,
                        ">" => a > b ? 1 : 0,
                        "<" => a < b ? 1 : 0,
                        _ => b == 0 ? 0 : a / b,
                    });
                    break;
            }
        }
        return stack.Pop();
    }

    /// <summary>Whether the state lights the lamp (power aside), or null while an input is unread.</summary>
    public bool? IsLit(Func<A300LampInput, double?> read) => Evaluate(read) is double v ? v >= 0.5 : null;
}

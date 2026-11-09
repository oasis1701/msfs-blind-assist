using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Evaluates a TFDi tooltip readout (<see cref="Md11Control.Readout"/>) the way the cockpit's
/// tooltip does, so a row speaks TFDi's own words and scale: the rudder trim's
/// <c>%((L:MD11_PED_RUD_TRIM_IND) 25 - abs 0.1 &lt;)%{if}Neutral%{else}…%!1.1f! degrees Nose Left…</c>
/// reads "Neutral" or "1.2 degrees Nose Left" from the live L:var, and nothing here knows that 25 is
/// neutral or that 2.5 units make a degree. Pure: the caller supplies the L:var reads.
///
/// The markup, as TFDi's tooltips use it: literal text; <c>%%</c> for a percent sign;
/// <c>%(rpn)</c> followed by <c>%{if}A%{else}B%{end}</c> (the else half optional) or by a format
/// <c>%!w.pf!</c>. The RPN is postfix: numbers, <c>(L:NAME)</c> reads, <c>+ - * /</c>, <c>abs</c>,
/// <c>neg</c>, the six comparisons, <c>and</c>, <c>or</c>, <c>not</c> and <c>rng</c>
/// (<c>lo hi x rng</c>: lo ≤ x ≤ hi).
///
/// Anything else is REFUSED (null), never guessed: an unknown operator, a <c>%{case}</c>, the
/// integer format <c>%!d!</c> (whether the sim rounds or truncates it is unmeasured, and the trims do
/// not use it), a var the cache has not delivered, a malformed expression. A refused readout leaves
/// the row as it was before readouts existed.
/// </summary>
public static class Md11TooltipReadout
{
    /// <summary>The readout's text for the current values, or null when it cannot be evaluated (see the class remarks).</summary>
    public static string? Evaluate(string? readout, Func<string, double?> readLVar)
    {
        if (string.IsNullOrEmpty(readout)) return null;
        var parser = new Parser(readout, readLVar);
        try
        {
            var text = parser.Sequence(stopAtBranch: false);
            if (text == null || parser.Position != readout.Length) return null;
            return text.Trim();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="readout"/> is markup this evaluator understands, whatever the values:
    /// every L:var reads as 0 and only the SHAPE is judged. A row is given a readout only when this
    /// holds, so a shape this class refuses never replaces a working row.
    /// </summary>
    public static bool CanEvaluate(string? readout) => Evaluate(readout, _ => 0) != null;

    /// <summary>Every L:var the readout reads, in order, repeats removed — what a row must read and watch.</summary>
    public static IReadOnlyList<string> Vars(string? readout)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(readout)) return names;
        int i = 0;
        while ((i = readout.IndexOf("(L:", i, StringComparison.Ordinal)) >= 0)
        {
            int start = i + 3, end = start;
            while (end < readout.Length && (char.IsLetterOrDigit(readout[end]) || readout[end] == '_')) end++;
            var name = readout[start..end];
            if (name.Length > 0 && !names.Contains(name)) names.Add(name);
            i = end;
        }
        return names;
    }

    private sealed class Parser
    {
        private readonly string _s;
        private readonly Func<string, double?> _read;
        public int Position;

        public Parser(string s, Func<string, double?> read) { _s = s; _read = read; }

        private bool At(string token) => string.CompareOrdinal(_s, Position, token, 0, token.Length) == 0;

        /// <summary>
        /// Text up to the end of the input or, inside a branch, up to the <c>%{else}</c> or
        /// <c>%{end}</c> that closes it (left unconsumed). Null when a value it needs is missing.
        /// </summary>
        public string? Sequence(bool stopAtBranch)
        {
            var sb = new StringBuilder();
            while (Position < _s.Length)
            {
                if (At("%%")) { sb.Append('%'); Position += 2; continue; }
                if (At("%{else}") || At("%{end}"))
                {
                    if (stopAtBranch) return sb.ToString();
                    throw new FormatException("branch end outside a branch");
                }
                if (At("%("))
                {
                    Position += 2;
                    var value = Rpn(ReadRpnBody());
                    if (At("%{if}"))
                    {
                        Position += "%{if}".Length;
                        var whenTrue = Sequence(stopAtBranch: true);
                        string? whenFalse = "";
                        if (At("%{else}"))
                        {
                            Position += "%{else}".Length;
                            whenFalse = Sequence(stopAtBranch: true);
                        }
                        if (!At("%{end}")) throw new FormatException("unterminated %{if}");
                        Position += "%{end}".Length;
                        if (value == null) return null;
                        var chosen = value.Value != 0 ? whenTrue : whenFalse;
                        if (chosen == null) return null;
                        sb.Append(chosen);
                        continue;
                    }
                    if (At("%!"))
                    {
                        int close = _s.IndexOf('!', Position + 2);
                        if (close < 0) throw new FormatException("unterminated format");
                        var spec = _s.Substring(Position + 2, close - Position - 2);
                        Position = close + 1;
                        if (value == null) return null;
                        sb.Append(Format(value.Value, spec));
                        continue;
                    }
                    throw new FormatException("a value with neither %{if} nor a format");
                }
                if (_s[Position] == '%') throw new FormatException($"unsupported directive at {Position}");
                sb.Append(_s[Position]);
                Position++;
            }
            if (stopAtBranch) throw new FormatException("unterminated branch");
            return sb.ToString();
        }

        /// <summary>The RPN between <c>%(</c> and its matching <c>)</c>; the reads' own parentheses nest inside.</summary>
        private string ReadRpnBody()
        {
            int depth = 1, start = Position;
            while (Position < _s.Length)
            {
                char ch = _s[Position++];
                if (ch == '(') depth++;
                else if (ch == ')' && --depth == 0) return _s.Substring(start, Position - start - 1);
            }
            throw new FormatException("unterminated %(");
        }

        /// <summary>The RPN's value; null when an L:var it reads has no value yet.</summary>
        private double? Rpn(string rpn)
        {
            var stack = new Stack<double>();
            bool missing = false;
            int i = 0;
            while (i < rpn.Length)
            {
                if (char.IsWhiteSpace(rpn[i])) { i++; continue; }
                if (rpn[i] == '(')
                {
                    int close = rpn.IndexOf(')', i);
                    if (close < 0) throw new FormatException("unterminated read");
                    var inner = rpn.Substring(i + 1, close - i - 1).Trim();
                    if (!inner.StartsWith("L:", StringComparison.Ordinal)) throw new FormatException($"unsupported read ({inner})");
                    var name = inner[2..].Split(',')[0].Trim();
                    var v = _read(name);
                    if (v == null) missing = true;
                    stack.Push(v ?? 0);
                    i = close + 1;
                    continue;
                }
                int end = i;
                while (end < rpn.Length && !char.IsWhiteSpace(rpn[end])) end++;
                var token = rpn[i..end];
                i = end;
                if (double.TryParse(token, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var number))
                {
                    stack.Push(number);
                    continue;
                }
                Apply(token, stack);
            }
            if (stack.Count != 1) throw new FormatException("the RPN did not leave one value");
            return missing ? null : stack.Pop();
        }

        private static void Apply(string op, Stack<double> stack)
        {
            double Pop() => stack.Count > 0 ? stack.Pop() : throw new FormatException($"'{op}' on an empty stack");
            static double B(bool b) => b ? 1 : 0;
            switch (op)
            {
                case "abs": stack.Push(Math.Abs(Pop())); return;
                case "neg": stack.Push(-Pop()); return;
                case "not": stack.Push(B(Pop() == 0)); return;
                case "rng":
                {
                    double x = Pop(), hi = Pop(), lo = Pop();
                    stack.Push(B(x >= lo && x <= hi));
                    return;
                }
            }
            double b = Pop(), a = Pop();
            stack.Push(op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0 ? throw new FormatException("division by zero") : a / b,
                "<" => B(a < b),
                ">" => B(a > b),
                "<=" => B(a <= b),
                ">=" => B(a >= b),
                "==" => B(a == b),
                "!=" => B(a != b),
                "and" or "&&" => B(a != 0 && b != 0),
                "or" or "||" => B(a != 0 || b != 0),
                _ => throw new FormatException($"unsupported operator '{op}'"),
            });
        }

        /// <summary>
        /// A <c>w.pf</c> format: <c>p</c> decimals, invariant culture — a spoken number never takes the
        /// PC's comma. The width is a display concern and is ignored. Every other spec is refused.
        /// </summary>
        private static string Format(double value, string spec)
        {
            int dot = spec.IndexOf('.');
            if (dot < 0 || !spec.EndsWith('f')
                || !int.TryParse(spec.AsSpan(dot + 1, spec.Length - dot - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var decimals)
                || decimals > 6)
                throw new FormatException($"unsupported format '{spec}'");
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }
}

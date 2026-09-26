using System.Globalization;
using System.Runtime.CompilerServices;

namespace MSFSBlindAssist.Utils.Logging;

/// <summary>
/// An interpolated log line formatted with the invariant culture: "12.5", never the "12,5" a German or
/// Turkish Windows renders, so a line reads the same on every machine and every number in it parses.
///
/// <para>Offer it as a <c>ref InvariantLogLine</c> overload beside the <c>string</c> one: C# binds a
/// non-constant interpolated string - and a <c>+</c> chain of them - to the handler, and a plain literal
/// to the string. A conditional between two interpolated strings is typed <c>string</c> before the call
/// sees it and so is NOT covered: write it as two calls.</para>
/// </summary>
[InterpolatedStringHandler]
public ref struct InvariantLogLine
{
    private DefaultInterpolatedStringHandler _inner;

    public InvariantLogLine(int literalLength, int formattedCount)
        => _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount, CultureInfo.InvariantCulture);

    public void AppendLiteral(string value) => _inner.AppendLiteral(value);
    public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);
    public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);
    public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);
    public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);
    public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);
    public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

    /// <summary>The formatted line; the handler is spent afterwards.</summary>
    public string ToStringAndClear() => _inner.ToStringAndClear();

    /// <summary>Formats an interpolated string invariantly: <c>InvariantLogLine.Format($"{x:F1}")</c>.</summary>
    public static string Format(ref InvariantLogLine line) => line.ToStringAndClear();
}

using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The printf subset PMDG's display formats use ("FL%03d%c", "% 5d%c", "%4.2f", "%5.2f IN.", ".%03d", "%d/%d"): flags
/// - + space 0, width, precision; d i u f c s; invariant culture. Shared by the 737's navigation display and primary
/// flight display rows. Pure.
/// </summary>
internal static class PmdgPrintf
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Format(string format, params object[] args)
    {
        var sb = new StringBuilder();
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                sb.Append(format[i]);
                continue;
            }
            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                sb.Append('%');
                i++;
                continue;
            }
            int j = i + 1;
            bool left = false, plus = false, space = false, zero = false;
            for (; j < format.Length && "-+ 0".Contains(format[j]); j++)
            {
                left |= format[j] == '-';
                plus |= format[j] == '+';
                space |= format[j] == ' ';
                zero |= format[j] == '0';
            }
            int width = 0;
            for (; j < format.Length && char.IsAsciiDigit(format[j]); j++) width = width * 10 + (format[j] - '0');
            int precision = -1;
            if (j < format.Length && format[j] == '.')
            {
                precision = 0;
                for (j++; j < format.Length && char.IsAsciiDigit(format[j]); j++) precision = precision * 10 + (format[j] - '0');
            }
            char conversion = j < format.Length ? format[j] : 's';
            i = j;
            object arg = next < args.Length ? args[next++] : "";
            bool numeric = true, negative = false;
            string body;
            switch (conversion)
            {
                case 'd' or 'i' or 'u':
                    long n = Convert.ToInt64(arg, Inv);
                    negative = n < 0;
                    body = (negative ? -n : n).ToString(Inv);
                    break;
                case 'f':
                    double f = Convert.ToDouble(arg, Inv);
                    negative = f < 0;
                    body = Math.Abs(f).ToString("F" + (precision < 0 ? 6 : precision).ToString(Inv), Inv);
                    break;
                case 'c':
                    numeric = false;
                    body = Convert.ToChar(arg, Inv).ToString();
                    break;
                default:
                    numeric = false;
                    body = Convert.ToString(arg, Inv) ?? "";
                    break;
            }
            string sign = numeric ? (negative ? "-" : plus ? "+" : space ? " " : "") : "";
            int pad = width - sign.Length - body.Length;
            sb.Append(pad <= 0 ? sign + body
                : left ? sign + body + new string(' ', pad)
                : zero && numeric ? sign + new string('0', pad) + body
                : new string(' ', pad) + sign + body);
        }
        return sb.ToString();
    }
}

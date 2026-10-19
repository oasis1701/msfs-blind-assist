// Runs a test's code under a named culture and restores the previous one. CI runs under en-US, so a
// comma-decimal culture (de-DE, sv-SE) has to be set by the test, or a culture leak ("29,92") passes
// unnoticed. Older test files still carry private UnderCulture copies of this.

using System.Globalization;

namespace MSFSBlindAssist.Tests;

internal static class TestCulture
{
    public static T Run<T>(string name, Func<T> body)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

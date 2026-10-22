// Set-up for the whole test assembly, run once before any test, whatever order the tests run in.
//
// FenixA320Definition starts its background seams (plan 08's cabin monitor, plan 09's engine data) from
// the first ProcessSimVarUpdate a definition receives. Those subscribe and read over the Fenix data
// service on 127.0.0.1:8083, and no test disposes them: every test that feeds an update to a new
// definition would leave a socket loop retrying for the rest of the run, and on a PC with the Fenix
// loaded it would talk to the pilot's sim. So the seams stay off for every test here; a test of a
// background class drives that class directly, with fakes.

using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

internal static class FenixTestHost
{
    [ModuleInitializer]
    internal static void KeepTheFenixBackgroundSeamsOff() => FenixA320Definition.BackgroundSeamsEnabled = false;
}

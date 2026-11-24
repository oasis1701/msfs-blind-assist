// Set-up for the whole test assembly, run once before any test, whatever order the tests run in.
//
// BBS757Definition starts the cockpit-wide watch (plan 13) when the BBS Nest check settles, and the
// watch starts the definition's tablet client unless a test hands it a factory. That client polls the
// Coherent debugger on 127.0.0.1:19999 and no test disposes it: every test that settles the check
// would leave a socket loop retrying for the rest of the run, and on a PC with the 757 loaded it
// would take the tablet view's one socket from the pilot's running app. So the watch stays off for
// every test here; Bbs757CockpitAnnouncementsTests drives the announcement class directly.

using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

internal static class Bbs757TestHost
{
    [ModuleInitializer]
    internal static void KeepTheCockpitWatchOff() => BBS757Definition.CockpitWatchEnabled = false;
}

using System.Reflection;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins that the control map is memoized through ONE thread-safe cell. The app loads it from the
/// definition's constructor on the UI thread, but this suite loads it from a dozen classes' static
/// initialisers that xUnit runs in parallel, and the check-then-assign static it replaced could
/// deserialize the map twice and hand two callers different instances. A race test cannot pin that
/// deterministically — any earlier class has already warmed the cache — so the FIELD is pinned, and
/// the contract (one instance for every caller) is checked from many threads at once.
/// </summary>
public class Md11ControlMapCacheTests
{
    [Fact]
    public void The_map_is_memoized_through_a_readonly_Lazy_and_no_mutable_static_field()
    {
        var statics = typeof(Md11ControlMap).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(statics, f => f.FieldType == typeof(Md11ControlMap));
        var cache = Assert.Single(statics, f => f.FieldType == typeof(Lazy<Md11ControlMap>));
        Assert.True(cache.IsInitOnly);
    }

    [Fact]
    public void Every_caller_on_every_thread_gets_the_same_map()
    {
        var maps = new Md11ControlMap[16];
        Parallel.For(0, maps.Length, i => maps[i] = Md11ControlMap.Load());

        Assert.All(maps, m => Assert.Same(maps[0], m));
        Assert.NotEmpty(maps[0].Controls);
    }
}

using System.Reflection;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class FlightPlanCopyTests
{
    private static object Sample(Type t, string name) =>
        t == typeof(string) ? "x-" + name
        : t == typeof(int?) ? 7
        : t == typeof(DateTime?) ? new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc)
        : t == typeof(List<WaypointFix>) ? new List<WaypointFix> { new() { Ident = "W-" + name } }
        : throw new InvalidOperationException($"FlightPlan.{name} ({t.Name}) has no sample: teach this test and CopyFrom about it");

    [Fact]
    public void Every_settable_property_is_copied_and_every_list_is_a_copy()
    {
        var props = typeof(FlightPlan).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite).ToList();
        var source = new FlightPlan();
        foreach (var p in props) p.SetValue(source, Sample(p.PropertyType, p.Name));

        var copy = new FlightPlan();
        copy.CopyFrom(source);

        foreach (var p in props)
        {
            object? a = p.GetValue(source), b = p.GetValue(copy);
            if (a is List<WaypointFix> la)
            {
                var lb = Assert.IsType<List<WaypointFix>>(b);
                Assert.NotSame(la, lb);
                Assert.Equal(la, lb);
            }
            else Assert.Equal(a, b);
        }
    }
}

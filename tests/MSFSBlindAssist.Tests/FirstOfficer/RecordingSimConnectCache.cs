using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// A never-connected <see cref="SimConnectManager"/>, as <see cref="SeededSimConnectCache"/> builds one,
/// whose variable cache also RECORDS every key it is asked for. A First Officer evaluator's
/// <c>GetValue</c> reaches the cache through <c>GetCachedVariableValue</c>, so what the recorder holds
/// afterwards is exactly the cache keys the code under test read: the keys a spoken value
/// (<c>ChecklistItem.LiveValue</c>) or a state field depends on. A synthetic field never reaches the
/// cache itself; the keys it is built from do, and those are recorded.
///
/// There is nothing to hook in production: <c>GetCachedVariableValue</c> is not virtual and the cache is
/// a private field. The recorder is the cache's own key comparer. A <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// hashes every key it is asked for with its comparer, found or not, so a comparer whose
/// <c>GetHashCode</c> notes the key sees each lookup. The dictionary is swapped in through
/// <see cref="SeededSimConnectCache.Cache"/>, the one accessor to the private field, so a renamed field
/// still fails loudly there. The seeding writes are not reads and are not recorded.
/// </summary>
internal sealed class RecordingSimConnectCache
{
    private readonly KeyRecorder _recorder;

    private RecordingSimConnectCache(SimConnectManager manager, KeyRecorder recorder)
    {
        Manager = manager;
        _recorder = recorder;
    }

    /// <summary>Give this to the evaluator under test: <c>evaluator.SetSimConnect(cache.Manager)</c>.</summary>
    public SimConnectManager Manager { get; }

    /// <summary>The distinct keys asked of the cache since it was made or last <see cref="Clear"/>ed,
    /// in the order they were first asked.</summary>
    public IReadOnlyList<string> Reads => _recorder.Snapshot();

    public void Clear() => _recorder.Clear();

    /// <summary>A recording cache holding the given values; a key not given reads as unread (NaN).</summary>
    public static RecordingSimConnectCache With(params (string Key, double Value)[] values)
    {
        var recorder = new KeyRecorder();
        var cache = new ConcurrentDictionary<string, double>(recorder);
        foreach (var (key, value) in values)
            cache[key] = value;
        var manager = SeededSimConnectCache.With();
        SeededSimConnectCache.Cache(manager) = cache;
        recorder.Start();
        return new RecordingSimConnectCache(manager, recorder);
    }

    /// <summary>Ordinal, like the cache's own default comparer, so the swapped-in dictionary finds exactly
    /// the keys the original would.</summary>
    private sealed class KeyRecorder : IEqualityComparer<string>
    {
        private readonly object _gate = new();
        private readonly List<string> _keys = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private bool _recording;

        public void Start() { lock (_gate) _recording = true; }

        public void Clear()
        {
            lock (_gate)
            {
                _keys.Clear();
                _seen.Clear();
            }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_gate) return _keys.ToArray();
        }

        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);

        public int GetHashCode(string key)
        {
            lock (_gate)
            {
                if (_recording && _seen.Add(key))
                    _keys.Add(key);
            }
            return StringComparer.Ordinal.GetHashCode(key);
        }
    }
}

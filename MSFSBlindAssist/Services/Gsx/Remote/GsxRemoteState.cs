using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// A flat mirror of GSX's published state. The top-level keys ARE the model.
///
/// CRITICAL: a patch REPLACES one top-level key outright — never deep-merge.
/// GSX's own client does exactly this (store.js), and coarse patches resend the
/// whole key, so merging would resurrect members GSX just removed.
/// </summary>
public sealed class GsxRemoteState
{
    private readonly object _lock = new();
    private Dictionary<string, JsonElement> _state = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Envelope =
        new(StringComparer.Ordinal) { "v", "type", "ts", "id" };

    /// <summary>Key that changed; "*" when a snapshot replaced everything.</summary>
    public event Action<string>? Changed;

    public bool GsxRunning { get; private set; }
    public bool Restarting { get; private set; }
    public bool HasSnapshot { get; private set; }

    public void Apply(GsxFrame frame)
    {
        switch (frame.Type)
        {
            case GsxFrameType.Hello:
                GsxRunning = frame.GsxRunning;
                if (GsxRunning) Restarting = false;
                Changed?.Invoke("*");
                break;

            case GsxFrameType.Snapshot:
            {
                var next = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (frame.Root.ValueKind == JsonValueKind.Object)
                    foreach (var p in frame.Root.EnumerateObject())
                        if (!Envelope.Contains(p.Name))
                            next[p.Name] = p.Value;
                lock (_lock) { _state = next; HasSnapshot = true; }
                Changed?.Invoke("*");
                break;
            }

            case GsxFrameType.Patch:
            {
                if (string.IsNullOrEmpty(frame.Key)) return;
                bool drop = frame.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
                lock (_lock)
                {
                    if (drop) _state.Remove(frame.Key);
                    else _state[frame.Key] = frame.Value;
                }
                Changed?.Invoke(frame.Key);
                break;
            }

            case GsxFrameType.Event:
                if (frame.Topic == "engine")
                {
                    GsxRunning = frame.GsxRunning;
                    if (frame.Restarting) Restarting = true;
                    else if (GsxRunning) Restarting = false;
                    Changed?.Invoke("engine");
                }
                break;
        }
    }

    public bool TryGet(string key, out JsonElement value)
    {
        lock (_lock) return _state.TryGetValue(key, out value);
    }

    /// <summary>Socket lost: state is no longer trustworthy. Keeps the restart latch.</summary>
    public void Clear()
    {
        lock (_lock) { _state = new Dictionary<string, JsonElement>(StringComparer.Ordinal); HasSnapshot = false; }
        GsxRunning = false;
        Changed?.Invoke("*");
    }
}

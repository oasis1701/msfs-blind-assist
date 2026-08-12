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
    private bool _gsxRunning;
    private bool _restarting;
    private bool _hasSnapshot;
    private static readonly HashSet<string> Envelope =
        new(StringComparer.Ordinal) { "v", "type", "ts", "id" };

    /// <summary>Key that changed; "*" when a snapshot replaced everything.</summary>
    public event Action<string>? Changed;

    /// <summary>GSX is currently running. Read under lock to ensure visibility from WebSocket thread.</summary>
    public bool GsxRunning
    {
        get { lock (_lock) return _gsxRunning; }
    }

    /// <summary>GSX is restarting. Read under lock to ensure visibility from WebSocket thread.</summary>
    public bool Restarting
    {
        get { lock (_lock) return _restarting; }
    }

    /// <summary>A snapshot has been received. Read under lock to ensure visibility from WebSocket thread.</summary>
    public bool HasSnapshot
    {
        get { lock (_lock) return _hasSnapshot; }
    }

    public void Apply(GsxFrame frame)
    {
        string? changeKey = null;

        switch (frame.Type)
        {
            case GsxFrameType.Hello:
                lock (_lock)
                {
                    _gsxRunning = frame.GsxRunning;
                    if (_gsxRunning) _restarting = false;
                }
                changeKey = "*";
                break;

            case GsxFrameType.Snapshot:
            {
                var next = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (frame.Root.ValueKind == JsonValueKind.Object)
                    foreach (var p in frame.Root.EnumerateObject())
                        if (!Envelope.Contains(p.Name))
                            next[p.Name] = p.Value;
                lock (_lock)
                {
                    _state = next;
                    _hasSnapshot = true;
                }
                changeKey = "*";
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
                changeKey = frame.Key;
                break;
            }

            case GsxFrameType.Event:
                if (frame.Topic == "engine")
                {
                    lock (_lock)
                    {
                        _gsxRunning = frame.GsxRunning;
                        if (frame.Restarting) _restarting = true;
                        else if (_gsxRunning) _restarting = false;
                    }
                    changeKey = "engine";
                }
                break;
        }

        if (changeKey != null)
            Changed?.Invoke(changeKey);
    }

    public bool TryGet(string key, out JsonElement value)
    {
        lock (_lock) return _state.TryGetValue(key, out value);
    }

    /// <summary>Socket lost: state is no longer trustworthy. Keeps the restart latch.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _state = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            _hasSnapshot = false;
            _gsxRunning = false;
            // Deliberately NOT resetting _restarting - the restart latch survives the socket drop
        }
        Changed?.Invoke("*");
    }
}

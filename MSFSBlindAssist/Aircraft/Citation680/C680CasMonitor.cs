using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Polls the pilot PFD's CAS list once a second over its own Coherent client and speaks what
/// changed: each newly posted message, then each cleared one, as "Caution: FUEL IMBALANCE" /
/// "Caution cleared: …". Baseline-first (the first successful read is never spoken, so a
/// reconnect does not re-read the whole list), queued (never interrupts a callout), gated by
/// the Ctrl+E switch and by the four Ctrl+M severity rows. Created on the UI thread: the
/// client marshals RowsUpdated there.
/// </summary>
public sealed class C680CasMonitor : IDisposable
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Func<string, bool> _isClassMuted;
    private CoherentDisplayClient? _client;
    private List<(string cls, string text)> _current = new();
    private bool _baselined;

    public C680CasMonitor(ScreenReaderAnnouncer announcer, Func<string, bool> isClassMuted)
    {
        _announcer = announcer;
        _isClassMuted = isClassMuted;
    }

    /// <summary>The Ctrl+E switch. On by default.</summary>
    public bool Enabled { get; set; } = true;

    public IReadOnlyList<(string cls, string text)> Current => _current;

    /// <summary>Raised on the UI thread after every change to the list.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (_client != null) return;
        _client = new CoherentDisplayClient("WTG3000_PFD_1", 1000, "coherent-c680-cas-agent.js");
        _client.RowsUpdated += OnRows;
        _client.Start();
    }

    private void OnRows(List<string> rows)
    {
        var next = rows.Select(C680CasDiff.ParseRow).ToList();
        var (posted, cleared) = C680CasDiff.Diff(_current, next);
        if (_baselined && Enabled)
        {
            foreach (var p in posted) if (!_isClassMuted(p.cls)) _announcer.Announce(C680CasDiff.Phrase(p.cls, p.text, posted: true));
            foreach (var c in cleared) if (!_isClassMuted(c.cls)) _announcer.Announce(C680CasDiff.Phrase(c.cls, c.text, posted: false));
        }
        _current = next;
        _baselined = true;
        Changed?.Invoke();
    }

    public List<string> Lines() => C680CasDiff.Lines(_current);

    public void Dispose()
    {
        if (_client != null) { _client.RowsUpdated -= OnRows; _client.Dispose(); _client = null; }
    }
}

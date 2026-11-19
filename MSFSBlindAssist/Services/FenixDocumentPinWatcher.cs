using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Polls the Fenix <c>pinned_documents</c> setting while the documents window is open and raises <see cref="PinsChanged"/>
/// when the list changes. It never speaks. A poll that gets no answer changes nothing.
/// </summary>
public sealed class FenixDocumentPinWatcher : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly Func<CancellationToken, Task<IReadOnlyList<FenixSetting>>> _readSettings;
    private readonly CancellationTokenSource _cts = new();
    private System.Windows.Forms.Timer? _timer;
    private bool _polling;
    private bool _disposed;

    public FenixDocumentPinWatcher(Func<CancellationToken, Task<IReadOnlyList<FenixSetting>>> readSettings)
    {
        _readSettings = readSettings;
    }

    /// <summary>The pins as last answered; null until the first answer.</summary>
    public IReadOnlyList<FenixPinnedDocument>? Current { get; private set; }

    /// <summary>Raised on the thread that ran the poll, when the first answer arrives and whenever the list then differs.</summary>
    public event EventHandler? PinsChanged;

    /// <summary>Starts polling. Call on the UI thread; the timer is created on the first call.</summary>
    public void Start()
    {
        if (_disposed) return;
        if (_timer is null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
            _timer.Tick += async (_, _) => await PollOnceAsync(_cts.Token);
        }
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        if (_disposed || _polling) return;
        _polling = true;
        try
        {
            IReadOnlyList<FenixSetting> settings;
            try { settings = await _readSettings(ct); }
            catch (Exception) { return; }   // no answer: nothing changes (also a cancel on dispose)

            if (_disposed) return;
            var pins = FenixDocumentPins.FromSettings(settings);
            if (pins is null) return;

            if (Current is not null && Current.SequenceEqual(pins)) return;
            Current = pins;
            Log.Debug("FenixDocuments", $"pins: {pins.Count}");
            PinsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _polling = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer?.Stop(); } catch (Exception) { }
        try { _timer?.Dispose(); } catch (Exception) { }
        try { _cts.Cancel(); } catch (Exception) { }
        try { _cts.Dispose(); } catch (Exception) { }
    }
}

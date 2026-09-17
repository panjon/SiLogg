using System.ComponentModel;
using SiLogg.Reader.Models;

namespace SiLogg.Reader;

public sealed class ReadoutCoordinator : IDisposable
{
    private readonly ReaderOptions _options;
    private readonly IRemoteReadoutService _service;
    private readonly JsonReadoutWriter _jsonWriter;
    private readonly ReadoutUploadClient _uploadClient;
    private readonly Dictionary<string, DateTime> _lastReadAt = [];
    private ReadoutType _pendingType = ReadoutType.Auto;
    private string? _lastDetectedStation;

    public BindingList<LogEntry> Log { get; } = [];

    public event Action<string>? StatusChanged;
    public event Action<string?>? CurrentStationChanged;
    public event Action<IReadOnlyList<string>>? AvailableDevicesChanged;

    public ReadoutCoordinator(ReaderOptions options)
    {
        _options = options;
        _service = options.UseMockDevice ? new MockRemoteReadoutService() : new RemoteReadoutService(options);
        _jsonWriter = new JsonReadoutWriter(options.LocalOutputFolder);
        _uploadClient = new ReadoutUploadClient(options);

        _service.StatusChanged += message => StatusChanged?.Invoke(message);
        _service.StationDetected += OnStationDetected;
        _service.ReadCompleted += OnReadCompleted;
        _service.ReadFailed += error => StatusChanged?.Invoke($"Fel: {error}");
        _service.AvailableDevicesChanged += devices => AvailableDevicesChanged?.Invoke(devices);
    }

    public void Start() => _service.Start();

    public void SelectDevice(string? deviceName) => _service.SelectDevice(deviceName);

    public void SetTargetMode(TargetMode mode) => _service.SetTargetMode(mode);

    public TargetMode DefaultTargetMode => Enum.TryParse<TargetMode>(_options.DefaultTargetMode, ignoreCase: true, out var mode)
        ? mode
        : TargetMode.Remote;

    public void ForceReadout()
    {
        _pendingType = ReadoutType.Forced;
        _service.RequestReadout();
    }

    private void OnStationDetected(string stationSerial)
    {
        CurrentStationChanged?.Invoke(stationSerial);
        _lastDetectedStation = stationSerial;

        var dedupeWindow = TimeSpan.FromMinutes(_options.DedupeWindowMinutes);
        if (_lastReadAt.TryGetValue(stationSerial, out var lastRead) && DateTime.UtcNow - lastRead < dedupeWindow)
        {
            // Redan utläst nyligen - vänta på nästa poll-cykel eller en tvingad läsning.
            return;
        }

        _pendingType = ReadoutType.Auto;
        _service.RequestReadout();
    }

    private async void OnReadCompleted(ReadoutDto readout)
    {
        var stationSerial = _lastDetectedStation ?? readout.StationSerial;
        _lastReadAt[stationSerial] = DateTime.UtcNow;

        var entry = new LogEntry
        {
            Time = DateTime.Now,
            StationSerial = stationSerial,
            CodeNumber = readout.Punches.Count > 0 ? readout.Punches[0].CodeNumber : null,
            PunchCount = readout.Punches.Count,
            Type = _pendingType == ReadoutType.Forced ? "Tvingad" : "Auto"
        };
        Log.Insert(0, entry);

        var taggedReadout = readout with { ReadoutType = _pendingType };

        StatusChanged?.Invoke($"Kontroll {stationSerial}: sparar JSON-fil...");
        try
        {
            var path = _jsonWriter.Write(taggedReadout);
            entry.JsonStatus = $"Sparad ({Path.GetFileName(path)})";
        }
        catch (Exception ex)
        {
            entry.JsonStatus = $"Fel: {ex.Message}";
        }
        RefreshEntry(entry);

        StatusChanged?.Invoke($"Kontroll {stationSerial}: laddar upp till servern...");
        var (success, error) = await _uploadClient.UploadAsync(taggedReadout, CancellationToken.None);
        entry.UploadStatus = success ? "Uppladdad" : $"Misslyckades: {error}";
        RefreshEntry(entry);

        StatusChanged?.Invoke($"Kontroll {stationSerial} klar. Väntar på nästa kontroll...");
        CurrentStationChanged?.Invoke(null);
    }

    private void RefreshEntry(LogEntry entry)
    {
        var index = Log.IndexOf(entry);
        if (index >= 0)
        {
            Log.ResetItem(index);
        }
    }

    public void Dispose()
    {
        _service.Dispose();
        _uploadClient.Dispose();
    }
}

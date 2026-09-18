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
    private uint _lastDetectedCodeNumber;
    private string? _alreadyReadStation;
    private bool _errorActive;

    public BindingList<LogEntry> Log { get; } = [];

    public event Action<string>? StatusChanged;
    public event Action<string?, uint?>? CurrentStationChanged;
    public event Action<IReadOnlyList<string>>? AvailableDevicesChanged;
    public event Action? ReadoutCompleted;

    public ReadoutCoordinator(ReaderOptions options)
    {
        _options = options;
        _service = options.UseMockDevice ? new MockRemoteReadoutService() : new RemoteReadoutService(options);
        _jsonWriter = new JsonReadoutWriter(options.LocalOutputFolder);
        _uploadClient = new ReadoutUploadClient(options);

        _service.StatusChanged += OnServiceStatusChanged;
        _service.StationDetected += OnStationDetected;
        _service.ReadCompleted += OnReadCompleted;
        _service.ReadFailed += OnReadFailed;
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

    public async Task RetryUploadAsync(LogEntry entry)
    {
        if (entry.Readout is null)
        {
            return;
        }

        entry.UploadStatus = "Laddar upp igen...";
        RefreshEntry(entry);
        StatusChanged?.Invoke($"Kontroll {entry.StationSerial} ({entry.CodeNumber}): laddar upp igen...");

        var (success, error) = await _uploadClient.UploadAsync(entry.Readout, CancellationToken.None);
        entry.UploadStatus = success ? "Uppladdad" : $"Misslyckades: {error}";
        RefreshEntry(entry);
        StatusChanged?.Invoke(success
            ? $"Kontroll {entry.StationSerial} ({entry.CodeNumber}) uppladdad igen."
            : $"Uppladdning misslyckades för kontroll {entry.StationSerial} ({entry.CodeNumber}): {error}");
    }

    private void OnStationDetected(string stationSerial, uint codeNumber)
    {
        _errorActive = false;
        var isNewStation = !string.Equals(_lastDetectedStation, stationSerial, StringComparison.Ordinal)
            || _lastDetectedCodeNumber != codeNumber;
        _lastDetectedStation = stationSerial;
        _lastDetectedCodeNumber = codeNumber;
        CurrentStationChanged?.Invoke(stationSerial, codeNumber);

        var dedupeWindow = TimeSpan.FromMinutes(_options.DedupeWindowMinutes);
        var stationKey = GetStationKey(stationSerial, codeNumber);
        if (_lastReadAt.TryGetValue(stationKey, out var lastRead) && DateTime.UtcNow - lastRead < dedupeWindow)
        {
            _alreadyReadStation = stationSerial;
            StatusChanged?.Invoke($"Kontroll {stationSerial} ({codeNumber}) klar. Tryck på Tvinga läsning om du vill läsa igen.");
            return;
        }

        _alreadyReadStation = null;
        if (isNewStation)
        {
            StatusChanged?.Invoke($"Ny kontroll upptäckt: {stationSerial} ({codeNumber}).");
        }

        _pendingType = ReadoutType.Auto;
        _service.RequestReadout();
    }

    private void OnServiceStatusChanged(string message)
    {
        if (IsErrorStatus(message))
        {
            _errorActive = true;
        }

        if (_errorActive && IsNeutralConnectionStatus(message))
        {
            return;
        }

        if (_alreadyReadStation is not null
            && IsNeutralConnectionStatus(message))
        {
            return;
        }

        StatusChanged?.Invoke(message);
    }

    private void OnReadFailed(string error)
    {
        _errorActive = true;
        StatusChanged?.Invoke($"Fel: {error}");
    }

    private static bool IsErrorStatus(string message) =>
        message.Contains("fel", StringComparison.OrdinalIgnoreCase)
        || message.Contains("misslyck", StringComparison.OrdinalIgnoreCase)
        || message.Contains("kunde inte", StringComparison.OrdinalIgnoreCase)
        || message.Contains("not open", StringComparison.OrdinalIgnoreCase);

    private static bool IsNeutralConnectionStatus(string message) =>
        message.StartsWith("Ansluten till", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Väntar på kontrollenhet", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Väntar på USB-enhet", StringComparison.OrdinalIgnoreCase);

    private async void OnReadCompleted(ReadoutDto readout)
    {
        ReadoutCompleted?.Invoke();
        var stationSerial = _lastDetectedStation ?? readout.StationSerial;
        var codeNumber = readout.CodeNumber != 0 ? readout.CodeNumber : _lastDetectedCodeNumber;
        _lastReadAt[GetStationKey(stationSerial, codeNumber)] = DateTime.UtcNow;
        var taggedReadout = readout with { ReadoutType = _pendingType };

        var entry = new LogEntry
        {
            Time = DateTime.Now,
            StationSerial = stationSerial,
            CodeNumber = codeNumber,
            PunchCount = readout.Punches.Count,
            Type = _pendingType == ReadoutType.Forced ? "Tvingad" : "Auto",
            Readout = taggedReadout
        };
        Log.Insert(0, entry);

        StatusChanged?.Invoke($"Kontroll {stationSerial} ({_lastDetectedCodeNumber}): sparar JSON-fil...");
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

        StatusChanged?.Invoke($"Kontroll {stationSerial} ({_lastDetectedCodeNumber}): laddar upp till servern...");
        var (success, error) = await _uploadClient.UploadAsync(taggedReadout, CancellationToken.None);
        entry.UploadStatus = success ? "Uppladdad" : $"Misslyckades: {error}";
        RefreshEntry(entry);

        StatusChanged?.Invoke($"Kontroll {stationSerial} ({_lastDetectedCodeNumber}) klar. Söker efter nästa kontroll...");
        CurrentStationChanged?.Invoke(null, null);
    }

    private void RefreshEntry(LogEntry entry)
    {
        var index = Log.IndexOf(entry);
        if (index >= 0)
        {
            Log.ResetItem(index);
        }
    }

    private static string GetStationKey(string stationSerial, uint codeNumber) =>
        $"{stationSerial}:{codeNumber}";

    public void Dispose()
    {
        _service.Dispose();
        _uploadClient.Dispose();
    }
}

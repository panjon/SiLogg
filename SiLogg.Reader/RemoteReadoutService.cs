using SPORTident.Communication;
using SPORTident.Communication.Licensing.Common;
using SPORTident.Communication.UsbDevice;
using SiLogg.Reader.Models;
using SiComm = SPORTident.Communication.Communication;

namespace SiLogg.Reader;

/// <summary>
/// Wraps the SPORTident SDK to read out the backup memory of a remote control station
/// via a USB master station (TargetDevice.Remote).
/// NOTE: exact behavior of TargetDevice.Remote (pairing/addressing) is unverified until
/// tested against real hardware - see comments below for assumptions that need validation.
/// </summary>
public sealed class RemoteReadoutService : IRemoteReadoutService
{
    private readonly ReaderOptions _options;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private SiComm? _comm;
    private bool _licenseConfigured;
    private string? _selectedDeviceName;
    private List<string> _lastKnownDeviceNames = [];
    private TargetDevice _targetMode = TargetDevice.Remote;
    private string? _currentStationSerial;
    private uint? _currentStationCodeNumber;
    private DateTime _lastSystemDataRequestUtc;
    private DateTime _backupStartedUtc;
    private bool _directReadCompleted;
    private List<DeviceInfo> _probeDevices = [];
    private int _probeIndex;
    private DateTime _probeStartedUtc;
    private bool _probingDevice;
    private bool _probeFailed;
    private const int MaxBackupRetries = 3;
    private static readonly TimeSpan BackupReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DeviceProbeTimeout = TimeSpan.FromSeconds(2);
    private int _backupRetryCount;
    private bool _backupReadInProgress;

    public event Action<string>? StatusChanged;
    public event Action<string, uint>? StationDetected;
    public event Action<int>? ReadProgressChanged;
    public event Action<ReadoutDto>? ReadCompleted;
    public event Action<string>? ReadFailed;
    public event Action<IReadOnlyList<string>>? AvailableDevicesChanged;

    public RemoteReadoutService(ReaderOptions options)
    {
        _options = options;
        _pollTimer = new System.Windows.Forms.Timer { Interval = Math.Max(250, options.PollIntervalMs) };
        _pollTimer.Tick += OnPollTick;
    }

    public bool IsDeviceConnected => _comm?.IsOpen == true;

    public void Start()
    {
        ConfigureLicenseOnce();
        _targetMode = Enum.TryParse<TargetDevice>(_options.DefaultTargetMode, ignoreCase: true, out var mode)
            ? mode
            : TargetDevice.Remote;
        _directReadCompleted = false;
        StatusChanged?.Invoke("Väntar på USB-enhet...");
        _pollTimer.Start();
    }

    public void RequestReadout()
    {
        if (_comm is not { IsOpen: true })
        {
            ReadFailed?.Invoke("Ingen ansluten enhet att läsa ut från.");
            return;
        }

        if (_backupReadInProgress)
        {
            return;
        }

        if (_targetMode == TargetDevice.Direct && _directReadCompleted)
        {
            return;
        }

        _backupRetryCount = 0;
        _backupReadInProgress = true;
        _backupStartedUtc = DateTime.UtcNow;
        StatusChanged?.Invoke("Läser ut backupminne...");
        _comm.GetBackupMemory();
    }

    public void SelectDevice(string? deviceName)
    {
        _selectedDeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName;
        _directReadCompleted = false;

        // Enkelt: koppla ur nuvarande anslutning så nästa poll-tick öppnar den valda enheten.
        if (_comm is { IsOpen: true })
        {
            _comm.Close();
        }
        _comm = null;
    }

    public void SetTargetMode(Models.TargetMode mode)
    {
        _targetMode = mode == Models.TargetMode.Direct ? TargetDevice.Direct : TargetDevice.Remote;
        _directReadCompleted = false;
        if (_comm is { IsOpen: true })
        {
            _comm.TargetDevice = _targetMode;
        }
        StatusChanged?.Invoke($"Läsläge satt till {(_targetMode == TargetDevice.Direct ? "Direct" : "Remote")}.");
    }

    private void ConfigureLicenseOnce()
    {
        if (_licenseConfigured)
        {
            return;
        }

        License.Type = Enum.TryParse<LicenseType>(_options.LicenseType, ignoreCase: true, out var licenseType)
            ? licenseType
            : LicenseType.NonCommercial;
        License.Name = _options.LicenseName;
        License.Key = _options.LicenseKey;
        _licenseConfigured = true;
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_comm is null || !_comm.IsOpen)
        {
            _comm = null;
            TryOpenDevice();
            return;
        }

        if (_probingDevice)
        {
            if (_probeFailed || DateTime.UtcNow - _probeStartedUtc > DeviceProbeTimeout)
            {
                MoveToNextDevice();
            }

            return;
        }

        if (_backupReadInProgress)
        {
            if (DateTime.UtcNow - _backupStartedUtc > BackupReadTimeout)
            {
                AbortBackupReadout("Backup-läsningen avbröts eftersom kontrollen inte längre svarar.");
            }
            return;
        }

        if (DateTime.UtcNow - _lastSystemDataRequestUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        // Lightweight call used as a "is a remote station reachable right now?" probe.
        // TODO: validate against real hardware - StationConfigRead/CommunicationFailed
        // are expected to fire as a result of this call.
        try
        {
            _lastSystemDataRequestUtc = DateTime.UtcNow;
            _comm.GetSystemData();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Kommunikationsfel: {ex.Message}");
            if (_comm is { IsOpen: false })
            {
                _comm = null;
            }
        }
    }

    private void TryOpenDevice()
    {
        var devices = GetProbeDevices();
        var deviceNames = devices.Select(GetDeviceName).ToList();
        if (!deviceNames.SequenceEqual(_lastKnownDeviceNames))
        {
            _lastKnownDeviceNames = deviceNames;
            AvailableDevicesChanged?.Invoke(deviceNames);
        }

        if (devices.Count == 0)
        {
            StatusChanged?.Invoke("Väntar på USB-enhet...");
            return;
        }

        _probeDevices = devices;
        _probeIndex = 0;
        OpenNextDevice();
    }

    private List<DeviceInfo> GetProbeDevices()
    {
        var devices = DeviceInfo.GetAvailableDeviceList();
        if (_selectedDeviceName is not null)
        {
            var selected = devices.FirstOrDefault(d => GetDeviceName(d) == _selectedDeviceName);
            return selected is null ? [] : [selected];
        }

        var preferredPort = string.IsNullOrWhiteSpace(_options.DevicePort)
            ? null
            : _options.DevicePort.Trim();
        return devices
            .OrderByDescending(d => preferredPort is not null
                && string.Equals(GetDeviceName(d), preferredPort, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(d => GetComPortNumber(GetDeviceName(d)))
            .ToList();
    }

    private static int GetComPortNumber(string deviceName)
    {
        var prefixLength = deviceName.LastIndexOf("COM", StringComparison.OrdinalIgnoreCase);
        return prefixLength >= 0
            && int.TryParse(deviceName[(prefixLength + 3)..], out var portNumber)
            ? portNumber
            : -1;
    }

    private void OpenNextDevice()
    {
        if (_probeIndex >= _probeDevices.Count)
        {
            _probingDevice = false;
            StatusChanged?.Invoke("Ingen SI-master svarade på någon COM-port.");
            return;
        }

        var device = _probeDevices[_probeIndex];
        try
        {
            var comm = new SiComm
            {
                DeviceConnection = device,
                TargetDevice = TargetDevice.Direct,
                BaudRate = SiComm.BAUDRATE_AUTO_DETECT,
            };
            SubscribeEvents(comm);
            comm.Open();
            if (!comm.IsOpen)
            {
                comm.Close();
                MoveToNextDevice();
                return;
            }

            _comm = comm;
            var isConfiguredDevice = !string.IsNullOrWhiteSpace(_options.DevicePort)
                && string.Equals(GetDeviceName(device), _options.DevicePort.Trim(), StringComparison.OrdinalIgnoreCase);
            _probingDevice = !isConfiguredDevice;
            _probeFailed = false;
            _probeStartedUtc = DateTime.UtcNow;
            StatusChanged?.Invoke($"Ansluten till {GetDeviceName(device)}. Väntar på kontrollenhet...");
            if (isConfiguredDevice)
            {
                comm.TargetDevice = _targetMode;
            }

            comm.GetSystemData();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Kunde inte prova {GetDeviceName(device)}: {ex.Message}");
            MoveToNextDevice();
        }
    }

    private void MoveToNextDevice()
    {
        _probingDevice = false;
        _probeFailed = false;
        if (_comm is { } comm)
        {
            try
            {
                if (comm.IsOpen)
                {
                    comm.Close();
                }
            }
            catch
            {
                // Nästa kandidat provas ändå.
            }
        }

        _comm = null;
        _probeIndex++;
        OpenNextDevice();
    }

    private static string GetDeviceName(DeviceInfo device) =>
        string.IsNullOrWhiteSpace(device.DeviceInterface) ? device.DeviceName : device.DeviceInterface;

    private void SubscribeEvents(SiComm comm)
    {
        comm.StationConfigRead += (_, e) =>
        {
            _currentStationSerial = e.Device.SerialNumber;
            _currentStationCodeNumber = (uint)e.Device.CodeNumber;
            if (_probingDevice)
            {
                _probingDevice = false;
                _probeFailed = false;
                comm.TargetDevice = _targetMode;
                StatusChanged?.Invoke($"Ansluten till {GetDeviceName(_probeDevices[_probeIndex])}. Väntar på kontrollenhet...");
                if (_targetMode == TargetDevice.Direct)
                {
                    StationDetected?.Invoke(_currentStationSerial, _currentStationCodeNumber.Value);
                }

                return;
            }

            StationDetected?.Invoke(_currentStationSerial, _currentStationCodeNumber.Value);
        };

        comm.BackupReadProgressChanged += (_, e) =>
        {
            ReadProgressChanged?.Invoke(e.ProgressPercentage);
        };

        comm.BackupMemoryReadCompleted += (_, e) =>
        {
            var punches = e.PunchData
                .Select(p => new PunchRecordDto(
                    p.Id,
                    p.Siid,
                    p.PunchDateTime,
                    p.DayOfWeek.ToString(),
                    p.SiacRecordNo,
                    p.SiacRecordCount,
                    p.SiacIsLowBattery,
                    p.SiacIsCardFull,
                    p.SiacBatteryVoltage,
                    p.IsMissingOrEmpty))
                .ToList();

            var stationSerial = _currentStationSerial
                ?? (e.PunchData.Length > 0 ? e.PunchData[0].StationSerial.ToString() : "okänd");
            var codeNumber = _currentStationCodeNumber
                ?? (e.PunchData.Length > 0 ? e.PunchData[0].CodeNumber : 0);
            var operatingMode = e.PunchData.Length > 0
                ? e.PunchData[0].OperatingMode.ToString()
                : string.Empty;
            _backupReadInProgress = false;
            _backupRetryCount = 0;
            _backupStartedUtc = default;
            if (_targetMode == TargetDevice.Direct)
            {
                _directReadCompleted = true;
            }
            else
            {
                _lastSystemDataRequestUtc = DateTime.MinValue;
            }
            var dto = new ReadoutDto(stationSerial, codeNumber, operatingMode, e.ReadoutDateTime, Models.ReadoutType.Auto, punches);
            ReadCompleted?.Invoke(dto);
        };

        comm.CommunicationFailed += (_, _) =>
        {
            if (_probingDevice)
            {
                _probeFailed = true;
                return;
            }

            if (!_backupReadInProgress)
            {
                // Fel under den lätta "är någon i räckhåll"-sondningen åtgärdas redan av nästa poll-tick.
                return;
            }

            _backupRetryCount++;
            if (_backupRetryCount <= MaxBackupRetries)
            {
                StatusChanged?.Invoke($"Kommunikationsfel, försöker igen ({_backupRetryCount}/{MaxBackupRetries})...");
                try
                {
                    if (comm.IsOpen)
                    {
                        comm.GetBackupMemory();
                    }
                    else
                    {
                        AbortBackupReadout("Kontrollen försvann under läsningen. Väntar på att den ska hittas igen.");
                    }
                }
                catch (Exception ex)
                {
                    AbortBackupReadout($"Kontrollen försvann under läsningen: {ex.Message}");
                }
                return;
            }

            AbortBackupReadout($"Kontrollen försvann under läsningen efter {MaxBackupRetries} försök. Väntar på att den ska hittas igen.");
        };
    }

    private void AbortBackupReadout(string message)
    {
        _backupReadInProgress = false;
        _backupRetryCount = 0;
        _backupStartedUtc = default;
        if (_comm is { } comm)
        {
            try
            {
                if (comm.IsOpen)
                {
                    comm.Close();
                }
            }
            catch
            {
                // Nästa poll-cykel försöker hitta och öppna enheten igen.
            }

            _comm = null;
        }

        ReadFailed?.Invoke(message);
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        if (_comm?.IsOpen == true)
        {
            _comm.Close();
        }
    }
}

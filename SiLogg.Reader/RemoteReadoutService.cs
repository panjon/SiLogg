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
    private const int MaxBackupRetries = 3;
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

        _backupRetryCount = 0;
        _backupReadInProgress = true;
        StatusChanged?.Invoke("Läser ut backupminne...");
        _comm.GetBackupMemory();
    }

    public void SelectDevice(string? deviceName)
    {
        _selectedDeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName;

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
        if (_comm is null)
        {
            TryOpenDevice();
            return;
        }

        // Lightweight call used as a "is a remote station reachable right now?" probe.
        // TODO: validate against real hardware - StationConfigRead/CommunicationFailed
        // are expected to fire as a result of this call.
        try
        {
            _comm.GetSystemData();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Kommunikationsfel: {ex.Message}");
        }
    }

    private void TryOpenDevice()
    {
        var devices = DeviceInfo.GetAvailableDeviceList();
        var deviceNames = devices.Select(d => d.DeviceName).ToList();
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

        // Auto-väljer första hittade enhet om inget uttryckligt val gjorts i UI:t.
        var device = (_selectedDeviceName is not null
            ? devices.FirstOrDefault(d => d.DeviceName == _selectedDeviceName)
            : null) ?? devices[0];
        try
        {
            var comm = new SiComm
            {
                DeviceConnection = device,
                TargetDevice = _targetMode,
                BaudRate = SiComm.BAUDRATE_AUTO_DETECT,
            };
            SubscribeEvents(comm);
            comm.Open();
            _comm = comm;
            StatusChanged?.Invoke($"Ansluten till {device.DeviceName}. Väntar på kontrollenhet...");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Kunde inte ansluta till {device.DeviceName}: {ex.Message}");
        }
    }

    private void SubscribeEvents(SiComm comm)
    {
        comm.StationConfigRead += (_, e) =>
        {
            StationDetected?.Invoke(e.Device.SerialNumber, (uint)e.Device.CodeNumber);
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
                    p.CodeNumber,
                    p.Siid,
                    p.PunchDateTime,
                    p.DayOfWeek.ToString(),
                    p.OperatingMode.ToString(),
                    p.SiacRecordNo,
                    p.SiacRecordCount,
                    p.SiacIsLowBattery,
                    p.SiacIsCardFull,
                    p.SiacBatteryVoltage,
                    p.IsMissingOrEmpty))
                .ToList();

            var stationSerial = e.PunchData.Length > 0 ? e.PunchData[0].StationSerial.ToString() : "okänd";
            var codeNumber = e.PunchData.Length > 0 ? e.PunchData[0].CodeNumber : 0;
            _backupReadInProgress = false;
            _backupRetryCount = 0;
            var dto = new ReadoutDto(stationSerial, codeNumber, e.ReadoutDateTime, Models.ReadoutType.Auto, punches);
            ReadCompleted?.Invoke(dto);
        };

        comm.CommunicationFailed += (_, _) =>
        {
            if (!_backupReadInProgress)
            {
                // Fel under den lätta "är någon i räckhåll"-sondningen åtgärdas redan av nästa poll-tick.
                return;
            }

            _backupRetryCount++;
            if (_backupRetryCount <= MaxBackupRetries)
            {
                StatusChanged?.Invoke($"Kommunikationsfel, försöker igen ({_backupRetryCount}/{MaxBackupRetries})...");
                comm.GetBackupMemory();
                return;
            }

            _backupReadInProgress = false;
            _backupRetryCount = 0;
            ReadFailed?.Invoke($"Kommunikationen med enheten misslyckades efter {MaxBackupRetries} försök.");
        };
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

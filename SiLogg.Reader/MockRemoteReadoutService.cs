using SiLogg.Reader.Models;

namespace SiLogg.Reader;

/// <summary>Simulates a USB master station + a remote control unit, for trying out the app without hardware.</summary>
public sealed class MockRemoteReadoutService : IRemoteReadoutService
{
    private static readonly string[] MockStationSerials = ["100101", "502108", "79881"];
    private static readonly uint[] MockStationCodeNumbers = [101, 108, 81];
    private static readonly string[] MockDeviceNames = ["COM5 (Mockad USB-master 1)", "COM7 (Mockad USB-master 2)"];

    private readonly System.Windows.Forms.Timer _timer;
    private readonly Random _random = new();
    private int _step;
    private string? _currentStation;
    private uint _currentCodeNumber;
    private string? _selectedDeviceName;

    public event Action<string>? StatusChanged;
    public event Action<string>? StationDetected;
    public event Action<int>? ReadProgressChanged;
    public event Action<ReadoutDto>? ReadCompleted;
    public event Action<string>? ReadFailed;
    public event Action<IReadOnlyList<string>>? AvailableDevicesChanged;

    public MockRemoteReadoutService()
    {
        _timer = new System.Windows.Forms.Timer { Interval = 1500 };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        StatusChanged?.Invoke("[MOCK] Väntar på USB-enhet...");
        AvailableDevicesChanged?.Invoke(MockDeviceNames);
        _timer.Start();
    }

    public void SelectDevice(string? deviceName) => _selectedDeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName;

    public void RequestReadout()
    {
        if (_currentStation is null)
        {
            ReadFailed?.Invoke("[MOCK] Ingen kontrollenhet i räckhåll.");
            return;
        }

        _timer.Stop();
        StatusChanged?.Invoke($"[MOCK] Läser ut backupminne från kontroll {_currentStation}...");
        SimulateProgress(_currentStation, _currentCodeNumber);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _step++;
        switch (_step)
        {
            case 1:
                var deviceLabel = _selectedDeviceName ?? MockDeviceNames[0];
                StatusChanged?.Invoke($"[MOCK] Ansluten till {deviceLabel}. Väntar på kontrollenhet...");
                break;
            case 2:
                var stationIndex = _random.Next(MockStationSerials.Length);
                _currentStation = MockStationSerials[stationIndex];
                _currentCodeNumber = MockStationCodeNumbers[stationIndex];
                StatusChanged?.Invoke($"[MOCK] Kontroll {_currentStation} upptäckt.");
                StationDetected?.Invoke(_currentStation);
                break;
            default:
                // Håll "kontrollen i räckhåll" tills en ny simulerad kontroll väljs slumpmässigt.
                if (_random.Next(10) == 0)
                {
                    _step = 1;
                }
                break;
        }
    }

    private async void SimulateProgress(string stationSerial, uint codeNumber)
    {
        for (var percent = 0; percent <= 100; percent += 20)
        {
            ReadProgressChanged?.Invoke(percent);
            await Task.Delay(150);
        }

        var punchCount = _random.Next(3, 12);
        var startTime = DateTime.Now.AddHours(-2);
        var punches = Enumerable.Range(1, punchCount)
            .Select(i => new PunchRecordDto(
                i,
                codeNumber,
                (100000 + _random.Next(999999)).ToString(),
                startTime.AddMinutes(i * 3),
                startTime.AddMinutes(i * 3).DayOfWeek.ToString(),
                "Clear",
                0,
                1,
                false,
                false,
                null,
                false))
            .ToList();

        var dto = new ReadoutDto(stationSerial, codeNumber, DateTime.Now, ReadoutType.Auto, punches);
        ReadCompleted?.Invoke(dto);
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}

using SiLogg.Reader.Models;

namespace SiLogg.Reader;

/// <summary>Abstraction over the SDK-based remote readout so it can be swapped for a mock during testing.</summary>
public interface IRemoteReadoutService : IDisposable
{
    event Action<string>? StatusChanged;
    event Action<string, uint>? StationDetected;
    event Action<int>? ReadProgressChanged;
    event Action<ReadoutDto>? ReadCompleted;
    event Action<string>? ReadFailed;
    event Action<IReadOnlyList<string>>? AvailableDevicesChanged;

    void Start();
    void RequestReadout();

    /// <summary>Switches between reading the directly connected master station and a remote control station.</summary>
    void SetTargetMode(TargetMode mode);

    /// <summary>Overrides which detected device to use. Pass null/empty to go back to auto-select (first found).</summary>
    void SelectDevice(string? deviceName);
}

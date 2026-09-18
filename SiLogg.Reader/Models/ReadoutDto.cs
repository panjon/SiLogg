namespace SiLogg.Reader.Models;

public enum ReadoutType
{
    Auto,
    Forced
}

/// <summary>Mirrors the SDK's TargetDevice enum without leaking the SDK type outside RemoteReadoutService.</summary>
public enum TargetMode
{
    Remote,
    Direct
}

public sealed record PunchRecordDto(
    int Id,
    string? Siid,
    DateTime PunchDateTime,
    string DayOfWeek,
    int SiacRecordNo,
    int SiacRecordCount,
    bool SiacIsLowBattery,
    bool SiacIsCardFull,
    double? SiacBatteryVoltage,
    bool IsMissingOrEmpty);

public sealed record ReadoutDto(
    string StationSerial,
    uint CodeNumber,
    string OperatingMode,
    DateTime ReadoutDateTime,
    ReadoutType ReadoutType,
    IReadOnlyList<PunchRecordDto> Punches);

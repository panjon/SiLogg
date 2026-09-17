namespace SiLogg.Reader.Models;

public enum ReadoutType
{
    Auto,
    Forced
}

public sealed record PunchRecordDto(
    int Id,
    uint CodeNumber,
    string? Siid,
    DateTime PunchDateTime,
    string DayOfWeek,
    string OperatingMode,
    int SiacRecordNo,
    int SiacRecordCount,
    bool SiacIsLowBattery,
    bool SiacIsCardFull,
    double? SiacBatteryVoltage,
    bool IsMissingOrEmpty);

public sealed record ReadoutDto(
    string StationSerial,
    uint CodeNumber,
    DateTime ReadoutDateTime,
    ReadoutType ReadoutType,
    IReadOnlyList<PunchRecordDto> Punches);

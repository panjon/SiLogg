namespace SiLogg.Reader;

using SiLogg.Reader.Models;

public sealed class LogEntry
{
    public required DateTime Time { get; init; }
    public required string StationSerial { get; init; }
    public uint? CodeNumber { get; set; }
    public int? PunchCount { get; set; }
    public string JsonStatus { get; set; } = "Läser ut...";
    public string UploadStatus { get; set; } = "-";
    public required string Type { get; init; }
    public ReadoutDto? Readout { get; init; }
}

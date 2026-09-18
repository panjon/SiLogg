using System.Security.Cryptography;
using System.Text;
using SiLogg.Core;

namespace SiLogg.Web;

public static class ReadoutApi
{
    public static void MapReadoutApi(this WebApplication app)
    {
        app.MapPost("/api/readouts", async (HttpRequest request, ReadoutRequest payload, IConfiguration configuration, SiLoggService service) =>
        {
            var configuredKey = configuration["SILOGG_READOUT_API_KEY"]
                ?? configuration["ReadoutApiKey"];
            if (string.IsNullOrWhiteSpace(configuredKey)
                || !request.Headers.TryGetValue("X-Api-Key", out var suppliedKey)
                || !KeysMatch(configuredKey, suppliedKey.ToString()))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(payload.StationSerial)
                || payload.CodeNumber == 0
                || payload.Punches is null)
            {
                return Results.BadRequest("StationSerial, CodeNumber och Punches krävs.");
            }

            var sourceFile = $"readout_{payload.CodeNumber}_{payload.StationSerial}_{payload.ReadoutDateTime:yyyyMMdd_HHmmss}.json";
            var result = service.ImportReadout(
                new ReadoutImport(
                    payload.StationSerial,
                    payload.CodeNumber,
                    payload.OperatingMode,
                    payload.ReadoutDateTime,
                    payload.Punches.Select(p => new ReadoutPunch(
                        p.Id,
                        p.Siid,
                        p.PunchDateTime,
                        p.DayOfWeek,
                        p.SiacRecordNo,
                        p.SiacRecordCount,
                        p.SiacIsLowBattery,
                        p.SiacIsCardFull,
                        p.SiacBatteryVoltage,
                        p.IsMissingOrEmpty)).ToList()),
                sourceFile);

            return Results.Ok(new { result.ImportedRows, result.SourceFile });
        }).AllowAnonymous();
    }

    private static bool KeysMatch(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}

public sealed record ReadoutRequest
{
    public required string StationSerial { get; init; }
    public uint CodeNumber { get; init; }
    public string OperatingMode { get; init; } = string.Empty;
    public DateTimeOffset ReadoutDateTime { get; init; }
    public required IReadOnlyList<ReadoutPunchRequest> Punches { get; init; }
}

public sealed record ReadoutPunchRequest
{
    public int Id { get; init; }
    public string? Siid { get; init; }
    public DateTimeOffset PunchDateTime { get; init; }
    public string DayOfWeek { get; init; } = string.Empty;
    public int SiacRecordNo { get; init; }
    public int SiacRecordCount { get; init; }
    public bool SiacIsLowBattery { get; init; }
    public bool SiacIsCardFull { get; init; }
    public double? SiacBatteryVoltage { get; init; }
    public bool IsMissingOrEmpty { get; init; }
}
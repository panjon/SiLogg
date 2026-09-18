using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.Sqlite;

namespace SiLogg.Core;

public sealed class SiLoggService(string databasePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    public void ClearDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = OpenConnection();
        RecreateSchema(connection);
    }

    public void DeleteFile(string sourceFile)
    {
        using var connection = OpenConnection();
        CreateSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM punches WHERE source_file = $source_file;";
        command.Parameters.AddWithValue("$source_file", sourceFile);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ImportedFileResult> ListImportedFiles()
    {
        if (!File.Exists(DatabasePath))
        {
            return [];
        }

        using var connection = OpenConnection();
        CreateSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.source_file, p.code_number, p.raw_json, counts.row_count
            FROM punches p
            JOIN (
                SELECT source_file, COUNT(*) AS row_count, MIN(row_number) AS first_row
                FROM punches
                GROUP BY source_file
            ) counts ON counts.source_file = p.source_file AND counts.first_row = p.row_number
            ORDER BY p.source_file;
            """;

        var files = new List<ImportedFileResult>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var rawData = DeserializeRawData(reader.GetString(2));
            files.Add(new ImportedFileResult(
                reader.GetString(0),
                reader.GetString(1),
                GetReadoutDate(rawData, reader.GetString(0)),
                GetRawValue(rawData, "Operating mode"),
                reader.GetInt32(3)));
        }

        return files
            .OrderByDescending(file => DateTimeOffset.TryParse(
                file.ReadOn,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var readOn)
                ? readOn
                : DateTimeOffset.MinValue)
            .ThenBy(file => file.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ImportResult ImportFolder(string folder)
    {
        var fullFolder = Path.GetFullPath(folder);
        if (!Directory.Exists(fullFolder))
        {
            throw new DirectoryNotFoundException($"Mappen finns inte: {fullFolder}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = OpenConnection();
        CreateSchema(connection);
        using var transaction = connection.BeginTransaction();

        // Re-importing a file replaces just its own rows, so uploads can happen a few files at a time.
        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM punches WHERE source_file = $source_file;";
        var deleteSourceFile = deleteCommand.Parameters.Add("$source_file", SqliteType.Text);

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO punches (source_file, row_number, siid, code_number, punch_datetime, sort_key, raw_json)
            VALUES ($source_file, $row_number, $siid, $code_number, $punch_datetime, $sort_key, $raw_json);
            """;
        var sourceFile = insertCommand.Parameters.Add("$source_file", SqliteType.Text);
        var rowNumber = insertCommand.Parameters.Add("$row_number", SqliteType.Integer);
        var siid = insertCommand.Parameters.Add("$siid", SqliteType.Text);
        var codeNumber = insertCommand.Parameters.Add("$code_number", SqliteType.Text);
        var punchDateTime = insertCommand.Parameters.Add("$punch_datetime", SqliteType.Text);
        var sortKey = insertCommand.Parameters.Add("$sort_key", SqliteType.Text);
        var rawJson = insertCommand.Parameters.Add("$raw_json", SqliteType.Text);

        var importedRows = 0;
        var files = Directory.EnumerateFiles(fullFolder, "*.csv", SearchOption.AllDirectories).ToList();
        foreach (var relativePath in files.Select(filePath => Path.GetRelativePath(fullFolder, filePath)).Distinct())
        {
            deleteSourceFile.Value = relativePath;
            deleteCommand.ExecuteNonQuery();
        }

        foreach (var punch in ReadPunches(fullFolder, files))
        {
            sourceFile.Value = punch.SourceFile;
            rowNumber.Value = punch.RowNumber;
            siid.Value = punch.Siid;
            codeNumber.Value = punch.CodeNumber;
            punchDateTime.Value = punch.PunchDateTime is null ? DBNull.Value : punch.PunchDateTime;
            sortKey.Value = punch.SortKey == DateTime.MaxValue
                ? string.Empty
                : punch.SortKey.ToString("O", CultureInfo.InvariantCulture);
            rawJson.Value = JsonSerializer.Serialize(punch.RawData, JsonOptions);
            insertCommand.ExecuteNonQuery();
            importedRows++;
        }

        transaction.Commit();
        return new ImportResult(importedRows, files.Count, fullFolder, DatabasePath);
    }

    public ReadoutImportResult ImportReadout(ReadoutImport readout, string sourceFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = OpenConnection();
        CreateSchema(connection);
        using var transaction = connection.BeginTransaction();

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM punches WHERE source_file = $source_file;";
        deleteCommand.Parameters.AddWithValue("$source_file", sourceFile);
        deleteCommand.ExecuteNonQuery();

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO punches (source_file, row_number, siid, code_number, punch_datetime, sort_key, raw_json)
            VALUES ($source_file, $row_number, $siid, $code_number, $punch_datetime, $sort_key, $raw_json);
            """;
        var source = insertCommand.Parameters.Add("$source_file", SqliteType.Text);
        var rowNumber = insertCommand.Parameters.Add("$row_number", SqliteType.Integer);
        var siid = insertCommand.Parameters.Add("$siid", SqliteType.Text);
        var codeNumber = insertCommand.Parameters.Add("$code_number", SqliteType.Text);
        var punchDateTime = insertCommand.Parameters.Add("$punch_datetime", SqliteType.Text);
        var sortKey = insertCommand.Parameters.Add("$sort_key", SqliteType.Text);
        var rawJson = insertCommand.Parameters.Add("$raw_json", SqliteType.Text);

        var importedRows = 0;
        foreach (var punch in readout.Punches)
        {
            var punchTime = punch.PunchDateTime.ToString("O", CultureInfo.InvariantCulture);
            var rawData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SIID"] = punch.Siid ?? string.Empty,
                ["Code number"] = readout.CodeNumber.ToString(CultureInfo.InvariantCulture),
                ["Punch DateTime"] = punchTime,
                ["Control time"] = punchTime,
                ["Station serial"] = readout.StationSerial,
                ["Read on"] = readout.ReadoutDateTime.ToString("O", CultureInfo.InvariantCulture),
                ["DayOfWeek"] = punch.DayOfWeek,
                ["Operating mode"] = readout.OperatingMode,
                ["SIAC number"] = punch.SiacRecordNo.ToString(CultureInfo.InvariantCulture),
                ["SIAC Count"] = punch.SiacRecordCount.ToString(CultureInfo.InvariantCulture),
                ["SIAC is battery low"] = punch.SiacIsLowBattery.ToString(),
                ["SIAC is card full"] = punch.SiacIsCardFull.ToString(),
                ["SIAC battery voltage"] = punch.SiacBatteryVoltage?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["Is missing or empty"] = punch.IsMissingOrEmpty.ToString()
            };
            var parsedSortKey = punch.PunchDateTime.DateTime;

            source.Value = sourceFile;
            rowNumber.Value = punch.Id;
            siid.Value = punch.Siid ?? string.Empty;
            codeNumber.Value = readout.CodeNumber.ToString(CultureInfo.InvariantCulture);
            punchDateTime.Value = punchTime;
            sortKey.Value = parsedSortKey.ToString("O", CultureInfo.InvariantCulture);
            rawJson.Value = JsonSerializer.Serialize(rawData, JsonOptions);
            insertCommand.ExecuteNonQuery();
            importedRows++;
        }

        transaction.Commit();
        return new ReadoutImportResult(importedRows, sourceFile, DatabasePath);
    }

    private static string GetReadoutDate(Dictionary<string, string> rawData, string sourceFile)
    {
        var readoutDate = GetRawValue(rawData, "Read on");
        if (!string.IsNullOrWhiteSpace(readoutDate))
        {
            return FormatReadoutDate(readoutDate);
        }

        var name = Path.GetFileNameWithoutExtension(sourceFile);
        var parts = name.Split('_');
        if (parts.Length >= 5
            && DateTime.TryParseExact(
                $"{parts[^2]}_{parts[^1]}",
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static string FormatReadoutDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : value;

    public SearchResult Search(string? siid, string? codeNumber = null)
    {
        var normalizedSiid = siid?.Trim() ?? string.Empty;
        var normalizedCodeNumber = codeNumber?.Trim() ?? string.Empty;
        var punches = new List<PunchResult>();
        if (!File.Exists(DatabasePath)
            || (string.IsNullOrWhiteSpace(normalizedSiid) && string.IsNullOrWhiteSpace(normalizedCodeNumber)))
        {
            return new SearchResult(normalizedSiid, []);
        }

        using var connection = OpenConnection();
        CreateSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT code_number, punch_datetime, source_file, row_number, raw_json
            FROM punches
                        WHERE ($siid = '' OR siid = $siid)
                            AND ($code_number = '' OR code_number = $code_number)
            ORDER BY sort_key, source_file, row_number;
            """;
        command.Parameters.AddWithValue("$siid", normalizedSiid);
        command.Parameters.AddWithValue("$code_number", normalizedCodeNumber);

        var errorLookup = FindErrors().ToDictionary(
            error => (error.SourceFile, error.RowNumber),
            error => error);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var rawData = DeserializeRawData(reader.GetString(4));
            var analysis = AnalyzeTime(GetRawValue(rawData, "Punch DateTime"), GetRawValue(rawData, "Control time"));
            errorLookup.TryGetValue((reader.GetString(2), reader.GetInt32(3)), out var error);
            punches.Add(new PunchResult(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : FormatPunchTime(reader.GetString(1)),
                reader.GetString(2),
                analysis.EffectiveDateTime,
                analysis.Warning,
                error?.ErrorCode,
                error?.PreviousPunch,
                error?.NextPunch,
                rawData));
        }

        var competitions = punches
            .GroupBy(punch => TryParseDateTime(punch.EffectivePunchDateTime, out var dateTime)
                ? dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "Okänt datum")
            .OrderBy(group => group.Key)
            .Select(group => new CompetitionResult(group.Key, group.ToList()))
            .ToList();

        return new SearchResult(normalizedSiid, competitions);
    }

    public IReadOnlyList<PunchErrorResult> FindErrors()
    {
        if (!File.Exists(DatabasePath))
        {
            return [];
        }

        using var connection = OpenConnection();
        CreateSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_file, row_number, siid, code_number, raw_json
            FROM punches
            ORDER BY source_file, row_number;
            """;

        var rows = new List<ImportedPunch>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var rawData = DeserializeRawData(reader.GetString(4));
            rows.Add(new ImportedPunch(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                GetRawValue(rawData, "Control time"),
                rawData));
        }

        var errors = new List<PunchErrorResult>();
        foreach (var fileRows in rows.GroupBy(row => row.SourceFile, StringComparer.OrdinalIgnoreCase))
        {
            var orderedRows = fileRows.OrderBy(row => row.RowNumber).ToList();
            for (var index = 0; index < orderedRows.Count; index++)
            {
                var row = orderedRows[index];
                var errorCode = PunchErrorCodes.Find(row.ControlTime);
                if (errorCode is null)
                {
                    continue;
                }

                var date = GetDatePrefix(row.ControlTime);
                errors.Add(new PunchErrorResult(
                    row.Siid,
                    row.CodeNumber,
                    errorCode,
                    PunchErrorCodes.GetDescription(errorCode),
                    date,
                    FindNeighbor(orderedRows, index, -1, date),
                    FindNeighbor(orderedRows, index, 1, date),
                    row.SourceFile,
                    row.RowNumber,
                    row.RawData));
            }
        }

        return errors
            .OrderBy(error => error.Date)
            .ThenBy(error => error.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(error => error.RowNumber)
            .ToList();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS punches (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_file TEXT NOT NULL,
                row_number INTEGER NOT NULL,
                siid TEXT NOT NULL,
                code_number TEXT NOT NULL,
                punch_datetime TEXT NULL,
                sort_key TEXT NOT NULL,
                raw_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_punches_siid_sort_key ON punches (siid, sort_key);
            CREATE INDEX IF NOT EXISTS idx_punches_code_number_sort_key ON punches (code_number, sort_key);
            """;
        command.ExecuteNonQuery();
    }

    private static void RecreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE IF EXISTS punches;";
        command.ExecuteNonQuery();
        CreateSchema(connection);
    }

    private static IEnumerable<ImportedCsvPunch> ReadPunches(string rootFolder, IEnumerable<string> files)
    {
        foreach (var filePath in files)
        {
            using var textReader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var csv = new CsvReader(textReader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ";",
                BadDataFound = null,
                HeaderValidated = null,
                MissingFieldFound = null,
                TrimOptions = TrimOptions.Trim
            });

            if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is null)
            {
                continue;
            }

            var headers = csv.HeaderRecord.Select(header => header.Trim()).Where(header => header.Length > 0).ToArray();
            var siidHeader = FindHeader(headers, "SIID");
            var controlTimeHeader = FindHeader(headers, "Control time");
            var codeNumberHeader = FindHeader(headers, "Code number");
            var punchDateTimeHeader = FindHeader(headers, "Punch DateTime");
            if (siidHeader is null || punchDateTimeHeader is null)
            {
                continue;
            }

            var rowNumber = 1;
            while (csv.Read())
            {
                rowNumber++;
                var rawData = headers.ToDictionary(
                    header => header,
                    header => csv.TryGetField(header, out string? value) ? value?.Trim() ?? string.Empty : string.Empty,
                    StringComparer.OrdinalIgnoreCase);
                var rawPunchTime = rawData[punchDateTimeHeader];
                var controlTime = controlTimeHeader is null ? string.Empty : rawData[controlTimeHeader];
                var analysis = AnalyzeTime(rawPunchTime, controlTime);
                var errorCode = PunchErrorCodes.Find(controlTime);
                yield return new ImportedCsvPunch(
                    Path.GetRelativePath(rootFolder, filePath),
                    rowNumber,
                    rawData[siidHeader],
                    codeNumberHeader is null ? string.Empty : rawData[codeNumberHeader],
                    errorCode is null ? rawPunchTime : null,
                    analysis.SortKey,
                    rawData);
            }
        }
    }

    private static TimeAnalysis AnalyzeTime(string punchDateTime, string controlTime)
    {
        var normalizedPunch = NormalizeDateTime(punchDateTime);
        var normalizedControl = NormalizeDateTime(controlTime);
        var errorCode = PunchErrorCodes.Find(normalizedControl);
        if (errorCode is not null)
        {
            var date = GetDatePrefix(normalizedControl);
            var errorSortKey = DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                ? parsedDate
                : DateTime.MaxValue;
            return new TimeAnalysis(date, errorSortKey, null);
        }

        var effective = ContainsDate(normalizedControl) ? normalizedControl : normalizedPunch;
        var sortKey = TryParseDateTime(effective, out var parsed) ? parsed : DateTime.MaxValue;
        string? warning = null;
        if (TryParseDateTime(normalizedPunch, out var punch) && TryParseDateTime(normalizedControl, out var control))
        {
            var equal = ContainsDate(normalizedPunch) ? punch == control : punch.TimeOfDay == control.TimeOfDay;
            if (!equal)
            {
                warning = $"AVVIKER: Control time={normalizedControl}, Punch DateTime={normalizedPunch}";
            }
        }

        return new TimeAnalysis(effective, sortKey, warning);
    }

    private static bool TryParseDateTime(string value, out DateTime parsed)
    {
        string[] formats =
        [
            "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd H:mm:ss.FFFFFFF",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd H:mm:ss",
            "HH:mm:ss.FFFFFFF", "H:mm:ss.FFFFFFF", "HH:mm:ss", "H:mm:ss"
        ];
        var normalized = NormalizeDateTime(value);
        return DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            || DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed);
    }

    private static NeighborPunch? FindNeighbor(IReadOnlyList<ImportedPunch> rows, int errorIndex, int direction, string errorDate)
    {
        for (var index = errorIndex + direction; index >= 0 && index < rows.Count; index += direction)
        {
            var row = rows[index];
            if (!GetDatePrefix(row.ControlTime).Equals(errorDate, StringComparison.Ordinal))
            {
                return null;
            }

            if (PunchErrorCodes.Find(row.ControlTime) is null && TryParseDateTime(row.ControlTime, out _))
            {
                return new NeighborPunch(row.Siid, NormalizeDateTime(row.ControlTime), row.RowNumber);
            }
        }

        return null;
    }

    private static Dictionary<string, string> DeserializeRawData(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];

    private static string GetRawValue(IReadOnlyDictionary<string, string> rawData, string key) =>
        rawData.TryGetValue(key, out var value) ? value : string.Empty;

    private static string? FindHeader(IEnumerable<string> headers, string expected) =>
        headers.FirstOrDefault(header => header.Equals(expected, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeDateTime(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string FormatPunchTime(string value) =>
        TryParseDateTime(value, out var parsed)
            ? parsed.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
            : value;

    private static bool ContainsDate(string value) =>
        value.Length >= 10 && value[4] == '-' && value[7] == '-';

    private static string GetDatePrefix(string value)
    {
        var normalized = NormalizeDateTime(value);
        return normalized.Length >= 10 ? normalized[..10] : string.Empty;
    }

    private sealed record ImportedCsvPunch(string SourceFile, int RowNumber, string Siid, string CodeNumber, string? PunchDateTime, DateTime SortKey, Dictionary<string, string> RawData);
    private sealed record ImportedPunch(string SourceFile, int RowNumber, string Siid, string CodeNumber, string ControlTime, Dictionary<string, string> RawData);
    private sealed record TimeAnalysis(string EffectiveDateTime, DateTime SortKey, string? Warning);
}

public sealed record ImportResult(int ImportedRows, int FilesRead, string SourceFolder, string DatabasePath);
public sealed record ReadoutImport(
    string StationSerial,
    uint CodeNumber,
    string OperatingMode,
    DateTimeOffset ReadoutDateTime,
    IReadOnlyList<ReadoutPunch> Punches);
public sealed record ReadoutPunch(
    int Id,
    string? Siid,
    DateTimeOffset PunchDateTime,
    string DayOfWeek,
    int SiacRecordNo,
    int SiacRecordCount,
    bool SiacIsLowBattery,
    bool SiacIsCardFull,
    double? SiacBatteryVoltage,
    bool IsMissingOrEmpty);
public sealed record ReadoutImportResult(int ImportedRows, string SourceFile, string DatabasePath);
public sealed record ImportedFileResult(string SourceFile, string CodeNumber, string ReadOn, string OperatingMode, int RowCount);
public sealed record SearchResult(string Siid, IReadOnlyList<CompetitionResult> Competitions);
public sealed record CompetitionResult(string Date, IReadOnlyList<PunchResult> Punches);
public sealed record PunchResult(
    string CodeNumber,
    string? PunchDateTime,
    string SourceFile,
    string EffectivePunchDateTime,
    string? TimeWarning,
    string? ErrorCode,
    NeighborPunch? PreviousPunch,
    NeighborPunch? NextPunch,
    Dictionary<string, string> RawData);
public sealed record PunchErrorResult(string Siid, string CodeNumber, string ErrorCode, string Description, string Date, NeighborPunch? PreviousPunch, NeighborPunch? NextPunch, string SourceFile, int RowNumber, Dictionary<string, string> RawData);
public sealed record NeighborPunch(string Siid, string ControlTime, int RowNumber);

public static class PunchErrorCodes
{
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Err8"] = "Pekaren är förstörd. Rekonstruktion utfördes automatiskt.",
        ["Err9"] = "SI-pinnen är full och inga fler data kan sparas.",
        ["ErrA"] = "SI-pinnen togs bort för snabbt. Endast pinnens nummer lästes.",
        ["ErrB"] = "Fel när kontrollens kodnummer skickades till SI-pinnen.",
        ["ErrC"] = "Fel när SI-pinnen lästes för att bekräfta att koden sparats.",
        ["ErrD"] = "Fel vid återläsning av SI-pinnen för verifiering.",
        ["ErrE"] = "SI-pinnen var inte tömd.",
        ["ErrF"] = "SI-pinnen stöder inte kontrollens kodnummer."
    };

    public static string? Find(string value) =>
        Descriptions.Keys.FirstOrDefault(code => value.Contains(code, StringComparison.OrdinalIgnoreCase));

    public static string GetDescription(string errorCode) =>
        Descriptions.TryGetValue(errorCode, out var description) ? description : string.Empty;
}
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length < 1)
{
    PrintUsage();
    return 0;
}

var command = args[0].Trim();
if (command.Equals("-list", StringComparison.OrdinalIgnoreCase)
    || command.Equals("--list", StringComparison.OrdinalIgnoreCase)
    || command.Equals("list", StringComparison.OrdinalIgnoreCase)
    || command.Equals("?", StringComparison.OrdinalIgnoreCase)
    || command.Equals("/?", StringComparison.OrdinalIgnoreCase)
    || command.Equals("-h", StringComparison.OrdinalIgnoreCase)
    || command.Equals("-help", StringComparison.OrdinalIgnoreCase)
    || command.Equals("--help", StringComparison.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

var databasePath = Path.Combine(AppContext.BaseDirectory, "si-logg.db");

if (command.Equals("import", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Mapp med CSV-filer saknas.");
        PrintUsage();
        return 1;
    }

    var folder = args[1];
    if (!Directory.Exists(folder))
    {
        Console.Error.WriteLine($"Mappen finns inte: {folder}");
        return 1;
    }

    var importedRows = PunchDatabase.ImportFolder(databasePath, folder);
    Console.WriteLine($"Importerade {importedRows} rader till {databasePath}");
    return 0;
}

if (command.Equals("errors", StringComparison.OrdinalIgnoreCase))
{
    var format = SearchOptions.Parse(args.Skip(1)).Format;
    var errors = PunchDatabase.FindErrors(databasePath);
    Console.OutputEncoding = Encoding.UTF8;

    if (format == OutputFormat.Text)
    {
        PrintErrorTextResult(errors);
    }
    else
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        Console.WriteLine(JsonSerializer.Serialize(new ErrorSearchResult(errors), jsonOptions));
    }

    return 0;
}

if (!command.Equals("search", StringComparison.OrdinalIgnoreCase) || args.Length < 2)
{
    PrintUsage();
    return 1;
}

var siid = args[1].Trim();
if (string.IsNullOrWhiteSpace(siid))
{
    Console.Error.WriteLine("SIID saknas.");
    PrintUsage();
    return 1;
}

var searchOptions = SearchOptions.Parse(args.Skip(2));
var punches = searchOptions.Folder is not null
    ? SearchCsvFolder(searchOptions.Folder, siid)
    : PunchDatabase.Search(databasePath, siid);

Console.OutputEncoding = Encoding.UTF8;
var competitions = GroupByCompetitionDate(punches);
if (searchOptions.Format == OutputFormat.Text)
{
    PrintTextResult(siid, competitions);
}
else
{
    var result = new SearchResult(siid, competitions);
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
}

return 0;

static void PrintUsage()
{
    Console.WriteLine("Användning:");
    Console.WriteLine("  dotnet run -- --help");
    Console.WriteLine("  dotnet run -- import <mapp-med-csv-filer>");
    Console.WriteLine("  dotnet run -- errors [--format json|text]");
    Console.WriteLine("  dotnet run -- search <SIID> [mapp-med-csv-filer] [--format json|text]");
    Console.WriteLine();
    Console.WriteLine("Exempel:");
    Console.WriteLine("  dotnet run -- import ..\\controlpost-dump");
    Console.WriteLine("  dotnet run -- errors --text");
    Console.WriteLine("  dotnet run -- search 1000696");
    Console.WriteLine("  dotnet run -- search 1000696 ..\\controlpost-dump");
    Console.WriteLine("  dotnet run -- search 1000696 --format text");
    Console.WriteLine("  dotnet run -- search 1000696 ..\\controlpost-dump --text");
}

static void PrintErrorTextResult(IEnumerable<PunchErrorResult> errors)
{
    foreach (var error in errors)
    {
        Console.WriteLine($"SIID: {error.SIID}");
        Console.WriteLine($"Kontroll: {error.CodeNumber}");
        Console.WriteLine($"Felkod: {error.ErrorCode} - {error.Description}");
        Console.WriteLine($"Före: {FormatNeighbor(error.PreviousPunch)}");
        Console.WriteLine($"Efter: {FormatNeighbor(error.NextPunch)}");
        Console.WriteLine($"Källfil: {error.SourceFile}, rad {error.RowNumber}");
        Console.WriteLine();
    }
}

static string FormatNeighbor(NeighborPunch? punch)
{
    return punch is null ? "Saknas" : $"{punch.ControlTime} (SIID {punch.SIID})";
}

static void PrintTextResult(string siid, IEnumerable<CompetitionResult> competitions)
{
    Console.WriteLine($"SSID: {siid}");
    foreach (var competition in competitions)
    {
        Console.WriteLine();
        Console.WriteLine($"Datum: {competition.Date}");
        foreach (var punch in competition.Punches)
        {
            var warning = punch.TimeWarning is null ? string.Empty : $" [{punch.TimeWarning}]";
            Console.WriteLine($"{punch.CodeNumber}: {FormatPunchTime(punch.PunchDateTime)}{warning}");
        }
    }
}

static List<CompetitionResult> GroupByCompetitionDate(IEnumerable<PunchResult> punches)
{
    return punches
        .GroupBy(punch => GetCompetitionDate(punch.EffectivePunchDateTime))
        .OrderBy(group => group.Key)
        .Select(group => new CompetitionResult(group.Key, group.ToList()))
        .ToList();
}

static string GetCompetitionDate(string punchDateTime)
{
    return TryParseResultDateTime(punchDateTime, out var parsed)
        ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : "Okänt datum";
}

static string FormatPunchTime(string punchDateTime)
{
    return TryParseResultDateTime(punchDateTime, out var parsed)
        ? parsed.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
        : punchDateTime;
}

static bool TryParseResultDateTime(string value, out DateTime parsed)
{
    var normalized = string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    var formats = new[]
    {
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd H:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd H:mm:ss"
    };

    return DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
        || DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed);
}

static List<PunchResult> SearchCsvFolder(string folder, string siid)
{
    if (!Directory.Exists(folder))
    {
        Console.Error.WriteLine($"Mappen finns inte: {folder}");
        return [];
    }

    return CsvPunchFinder.FindPunches(folder, siid)
        .OrderBy(punch => punch.SortKey)
        .ThenBy(punch => punch.SourceFile, StringComparer.OrdinalIgnoreCase)
        .ThenBy(punch => punch.RowNumber)
        .Select(punch => new PunchResult(
            punch.CodeNumber,
            punch.PunchDateTime,
            punch.SourceFile,
            punch.EffectivePunchDateTime,
            punch.TimeWarning,
            punch.RawData))
        .ToList();
}

internal static class PunchDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int ImportFolder(string databasePath, string folder)
    {
        using var connection = OpenConnection(databasePath);
        CreateSchema(connection);

        using var transaction = connection.BeginTransaction();
        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM punches;";
        deleteCommand.ExecuteNonQuery();

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
			INSERT INTO punches (source_file, row_number, siid, code_number, punch_datetime, sort_key, raw_json)
			VALUES ($source_file, $row_number, $siid, $code_number, $punch_datetime, $sort_key, $raw_json);
			""";
        var sourceFileParameter = insertCommand.Parameters.Add("$source_file", SqliteType.Text);
        var rowNumberParameter = insertCommand.Parameters.Add("$row_number", SqliteType.Integer);
        var siidParameter = insertCommand.Parameters.Add("$siid", SqliteType.Text);
        var codeNumberParameter = insertCommand.Parameters.Add("$code_number", SqliteType.Text);
        var punchDateTimeParameter = insertCommand.Parameters.Add("$punch_datetime", SqliteType.Text);
        var sortKeyParameter = insertCommand.Parameters.Add("$sort_key", SqliteType.Text);
        var rawJsonParameter = insertCommand.Parameters.Add("$raw_json", SqliteType.Text);

        var importedRows = 0;
        foreach (var punch in CsvPunchFinder.ReadPunches(folder))
        {
            sourceFileParameter.Value = punch.SourceFile;
            rowNumberParameter.Value = punch.RowNumber;
            siidParameter.Value = punch.SIID;
            codeNumberParameter.Value = punch.CodeNumber;
            punchDateTimeParameter.Value = punch.PunchDateTime;
            sortKeyParameter.Value = punch.SortKey == DateTime.MaxValue ? string.Empty : punch.SortKey.ToString("O", CultureInfo.InvariantCulture);
            rawJsonParameter.Value = JsonSerializer.Serialize(punch.RawData, JsonOptions);
            insertCommand.ExecuteNonQuery();
            importedRows++;
        }

        transaction.Commit();
        return importedRows;
    }

    public static List<PunchResult> Search(string databasePath, string siid)
    {
        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"Databasen finns inte: {databasePath}");
            Console.Error.WriteLine("Kör import först, eller sök direkt i en mapp: dotnet run -- search <SIID> <mapp-med-csv-filer>");
            return [];
        }

        using var connection = OpenConnection(databasePath);
        CreateSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
			SELECT code_number, punch_datetime, source_file, raw_json
			FROM punches
			WHERE siid = $siid
			ORDER BY sort_key, source_file, row_number;
			""";
        command.Parameters.AddWithValue("$siid", siid);

        var punches = new List<PunchResult>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var rawData = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(3), JsonOptions) ?? [];
            var timeAnalysis = PunchTimeAnalyzer.Analyze(
                GetRawValue(rawData, "Punch DateTime"),
                GetRawValue(rawData, "Control time"));
            var punchDateTime = GetRawValue(rawData, "Punch DateTime");
            punches.Add(new PunchResult(
                reader.GetString(0),
                string.IsNullOrWhiteSpace(punchDateTime) ? reader.GetString(1) : punchDateTime,
                reader.GetString(2),
                timeAnalysis.EffectivePunchDateTime,
                timeAnalysis.Warning,
                rawData));
        }

        return punches;
    }

    public static List<PunchErrorResult> FindErrors(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"Databasen finns inte: {databasePath}");
            Console.Error.WriteLine("Kör import först: dotnet run -- import <mapp-med-csv-filer>");
            return [];
        }

        using var connection = OpenConnection(databasePath);
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
            var rawData = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(4), JsonOptions) ?? [];
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
                var errorCode = PunchErrorCodes.FindIn(row.ControlTime);
                if (errorCode is null)
                {
                    continue;
                }

                var errorDate = GetDatePrefix(row.ControlTime);
                var previous = FindNeighbor(orderedRows, index, -1, errorDate);
                var next = FindNeighbor(orderedRows, index, 1, errorDate);
                errors.Add(new PunchErrorResult(
                    row.SIID,
                    row.CodeNumber,
                    errorCode,
                    PunchErrorCodes.GetDescription(errorCode),
                    errorDate,
                    previous,
                    next,
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

    private static NeighborPunch? FindNeighbor(IReadOnlyList<ImportedPunch> rows, int errorIndex, int direction, string errorDate)
    {
        for (var index = errorIndex + direction; index >= 0 && index < rows.Count; index += direction)
        {
            var row = rows[index];
            if (!GetDatePrefix(row.ControlTime).Equals(errorDate, StringComparison.Ordinal))
            {
                return null;
            }

            if (PunchErrorCodes.FindIn(row.ControlTime) is null
                && PunchTimeAnalyzer.TryParse(row.ControlTime, out _))
            {
                return new NeighborPunch(row.SIID, row.ControlTime, row.RowNumber);
            }
        }

        return null;
    }

    private static string GetDatePrefix(string controlTime)
    {
        var normalized = string.Join(' ', controlTime.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length >= 10 ? normalized[..10] : string.Empty;
    }

    private static string GetRawValue(IReadOnlyDictionary<string, string> rawData, string key)
    {
        return rawData.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static SqliteConnection OpenConnection(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
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
				punch_datetime TEXT NOT NULL,
				sort_key TEXT NOT NULL,
				raw_json TEXT NOT NULL
			);

			CREATE INDEX IF NOT EXISTS idx_punches_siid_sort_key ON punches (siid, sort_key);
			""";
        command.ExecuteNonQuery();
    }
}

internal static class CsvPunchFinder
{
    public static IEnumerable<PunchRow> ReadPunches(string folder)
    {
        foreach (var filePath in Directory.EnumerateFiles(folder, "*.csv", SearchOption.AllDirectories))
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
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

            var headers = csv.HeaderRecord
                .Select(header => header.Trim())
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToArray();
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
                var rawData = CreateRawData(csv, headers);
                var rawPunchDateTime = rawData[punchDateTimeHeader];
                var controlTime = controlTimeHeader is null ? string.Empty : rawData[controlTimeHeader];
                var timeAnalysis = PunchTimeAnalyzer.Analyze(rawPunchDateTime, controlTime);
                var codeNumber = codeNumberHeader is null ? string.Empty : rawData[codeNumberHeader];

                yield return new PunchRow(
                    Path.GetRelativePath(folder, filePath),
                    rowNumber,
                    rawData[siidHeader],
                    codeNumber,
                    rawPunchDateTime,
                    timeAnalysis.EffectivePunchDateTime,
                    timeAnalysis.SortKey,
                    timeAnalysis.Warning,
                    rawData);
            }
        }
    }

    public static IEnumerable<PunchRow> FindPunches(string folder, string siid)
    {
        return ReadPunches(folder)
            .Where(punch => punch.SIID.Equals(siid, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> CreateRawData(CsvReader csv, IEnumerable<string> headers)
    {
        var rawData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            rawData[header] = csv.TryGetField(header, out string? value) ? value?.Trim() ?? string.Empty : string.Empty;
        }

        return rawData;
    }

    private static string? FindHeader(IEnumerable<string> headers, string expectedHeader)
    {
        return headers.FirstOrDefault(header => header.Equals(expectedHeader, StringComparison.OrdinalIgnoreCase));
    }


}

internal static class PunchTimeAnalyzer
{
    public static PunchTimeAnalysis Analyze(string punchDateTime, string controlTime)
    {
        var normalizedPunchDateTime = NormalizeDateTimeText(punchDateTime);
        var normalizedControlTime = NormalizeDateTimeText(controlTime);
        var effectivePunchDateTime = ContainsDate(normalizedControlTime)
            ? normalizedControlTime
            : normalizedPunchDateTime;
        var sortKey = ParseDateTime(effectivePunchDateTime);
        var warning = GetMismatchWarning(normalizedPunchDateTime, normalizedControlTime);

        return new PunchTimeAnalysis(effectivePunchDateTime, sortKey, warning);
    }

    private static DateTime ParseDateTime(string value)
    {
        var normalized = NormalizeDateTimeText(value);
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd H:mm:ss.FFFFFFF",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd H:mm:ss",
            "HH:mm:ss.FFFFFFF",
            "H:mm:ss.FFFFFFF",
            "HH:mm:ss",
            "H:mm:ss"
        };

        if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
        {
            return parsed;
        }

        return DateTime.MaxValue;
    }

    public static bool TryParse(string value, out DateTime parsed)
    {
        parsed = ParseDateTime(value);
        return parsed != DateTime.MaxValue;
    }

    private static string? GetMismatchWarning(string punchDateTime, string controlTime)
    {
        if (string.IsNullOrWhiteSpace(punchDateTime) || string.IsNullOrWhiteSpace(controlTime))
        {
            return null;
        }

        var parsedPunchDateTime = ParseDateTime(punchDateTime);
        var parsedControlTime = ParseDateTime(controlTime);
        if (parsedPunchDateTime == DateTime.MaxValue || parsedControlTime == DateTime.MaxValue)
        {
            return null;
        }

        var isSameTime = ContainsDate(punchDateTime)
            ? parsedPunchDateTime == parsedControlTime
            : parsedPunchDateTime.TimeOfDay == parsedControlTime.TimeOfDay;

        return isSameTime
            ? null
            : $"AVVIKER: Control time={controlTime}, Punch DateTime={punchDateTime}";
    }

    private static string NormalizeDateTimeText(string value)
    {
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool ContainsDate(string value)
    {
        return value.Length >= 10 && value[4] == '-' && value[7] == '-';
    }
}

internal sealed record PunchTimeAnalysis(string EffectivePunchDateTime, DateTime SortKey, string? Warning);

internal sealed record PunchRow(
    string SourceFile,
    int RowNumber,
    string SIID,
    string CodeNumber,
    string PunchDateTime,
    string EffectivePunchDateTime,
    DateTime SortKey,
    string? TimeWarning,
    Dictionary<string, string> RawData);

internal sealed record SearchResult(string SIID, IReadOnlyList<CompetitionResult> Competitions);

internal sealed record CompetitionResult(string Date, IReadOnlyList<PunchResult> Punches);

internal sealed record ErrorSearchResult(IReadOnlyList<PunchErrorResult> Errors);

internal sealed record PunchErrorResult(
    string SIID,
    [property: JsonPropertyName("Code number")] string CodeNumber,
    [property: JsonPropertyName("Error code")] string ErrorCode,
    string Description,
    string Date,
    [property: JsonPropertyName("Previous punch")] NeighborPunch? PreviousPunch,
    [property: JsonPropertyName("Next punch")] NeighborPunch? NextPunch,
    [property: JsonPropertyName("Source file")] string SourceFile,
    [property: JsonPropertyName("Row number")] int RowNumber,
    [property: JsonPropertyName("raw-data")] Dictionary<string, string> RawData);

internal sealed record NeighborPunch(
    string SIID,
    [property: JsonPropertyName("Control time")] string ControlTime,
    [property: JsonPropertyName("Row number")] int RowNumber);

internal sealed record ImportedPunch(
    string SourceFile,
    int RowNumber,
    string SIID,
    string CodeNumber,
    string ControlTime,
    Dictionary<string, string> RawData);

internal sealed record PunchResult(
    [property: JsonPropertyName("Code number")]
    string CodeNumber,

    [property: JsonPropertyName("Punch DateTime")]
    string PunchDateTime,

    [property: JsonPropertyName("Source file")]
    string SourceFile,

    [property: JsonIgnore]
    string EffectivePunchDateTime,

    [property: JsonPropertyName("Time warning")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TimeWarning,

    [property: JsonPropertyName("raw-data")]
    Dictionary<string, string> RawData);

internal sealed record SearchOptions(string? Folder, OutputFormat Format)
{
    public static SearchOptions Parse(IEnumerable<string> arguments)
    {
        string? folder = null;
        var format = OutputFormat.Json;
        var pendingFormatValue = false;

        foreach (var argument in arguments)
        {
            if (pendingFormatValue)
            {
                format = ParseFormat(argument);
                pendingFormatValue = false;
                continue;
            }

            if (argument.Equals("--text", StringComparison.OrdinalIgnoreCase))
            {
                format = OutputFormat.Text;
            }
            else if (argument.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                format = OutputFormat.Json;
            }
            else if (argument.Equals("--format", StringComparison.OrdinalIgnoreCase) || argument.Equals("-f", StringComparison.OrdinalIgnoreCase))
            {
                pendingFormatValue = true;
            }
            else if (argument.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
            {
                format = ParseFormat(argument["--format=".Length..]);
            }
            else
            {
                folder ??= argument;
            }
        }

        return new SearchOptions(folder, format);
    }

    private static OutputFormat ParseFormat(string value)
    {
        return value.Equals("text", StringComparison.OrdinalIgnoreCase)
            ? OutputFormat.Text
            : OutputFormat.Json;
    }
}

internal enum OutputFormat
{
    Json,
    Text
}

internal static class PunchErrorCodes
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Err8"] = "The pointer is destroyed. The reconstruction was done automatically.",
        ["Err9"] = "The SI-Card used is already full and no further data will be saved.",
        ["ErrA"] = "The SI-Card was removed from the station too fast. Only the card number was read.",
        ["ErrB"] = "Error during sending the station’s code number to the SI-Card.",
        ["ErrC"] = "Error during reading the SI-Card to confirm that the code has been stored.",
        ["ErrD"] = "Error during re-reading the SI-Card for verification.",
        ["ErrE"] = "The SI-Card was not cleared.",
        ["ErrF"] = "The SI-Card does not support the code number of the station."
    };

    public static string? FindIn(string value)
    {
        return Descriptions.Keys.FirstOrDefault(code => value.Contains(code, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetDescription(string errorCode)
    {
        return Descriptions.TryGetValue(errorCode, out var description) ? description : string.Empty;
    }
}

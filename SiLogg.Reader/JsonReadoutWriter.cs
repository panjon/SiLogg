using System.Text.Encodings.Web;
using System.Text.Json;
using SiLogg.Reader.Models;

namespace SiLogg.Reader;

public sealed class JsonReadoutWriter(string outputFolder)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string Write(ReadoutDto readout)
    {
        Directory.CreateDirectory(outputFolder);
        var fileName = $"{readout.CodeNumber}_readout_{readout.StationSerial}_{readout.ReadoutDateTime:yyyyMMdd_HHmmss}.json";
        var path = Path.Combine(outputFolder, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(readout, JsonOptions));
        return path;
    }
}

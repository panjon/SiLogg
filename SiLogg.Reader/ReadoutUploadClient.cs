using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SiLogg.Reader.Models;

namespace SiLogg.Reader;

public sealed class ReadoutUploadClient(ReaderOptions options) : IDisposable
{
    private readonly HttpClient _httpClient = new();

    public async Task<(bool Success, string? Error)> UploadAsync(ReadoutDto readout, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.UploadApiUrl))
        {
            return (false, "Ingen API-URL konfigurerad.");
        }

        try
        {
            var json = JsonSerializer.Serialize(readout);
            using var request = new HttpRequestMessage(HttpMethod.Post, options.UploadApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Api-Key", options.UploadApiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

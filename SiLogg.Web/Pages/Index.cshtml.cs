using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SiLogg.Core;

namespace SiLogg.Web.Pages;

public class IndexModel(SiLoggService service) : PageModel
{
    public static string FormatRawValue(string key, string value)
    {
        if (!value.Contains('T')
            && !key.Equals("Read on", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
            : value;
    }

    [BindProperty(SupportsGet = true)]
    public string? Siid { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CodeNumber { get; set; }

    public SearchResult? Result { get; private set; }
    public bool SearchPerformed { get; private set; }

    public void OnGet()
    {
        if (!string.IsNullOrWhiteSpace(Siid) || !string.IsNullOrWhiteSpace(CodeNumber))
        {
            Result = service.Search(Siid, CodeNumber);
            SearchPerformed = true;
        }
    }

}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SiLogg.Core;

namespace SiLogg.Web.Pages;

public class IndexModel(SiLoggService service) : PageModel
{
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

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SiLogg.Core;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace SiLogg.Web.Pages;

public class ErrorsModel(SiLoggService service) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? ErrorCode { get; set; }

    [BindProperty(SupportsGet = true)]
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}", ApplyFormatInEditMode = true)]
    public DateOnly? Date { get; set; }

    [BindProperty(SupportsGet = true)] public string? CodeNumber { get; set; }
    [BindProperty(SupportsGet = true)] public string? Siid { get; set; }
    public IReadOnlyList<PunchErrorResult> Errors { get; private set; } = [];
    public IReadOnlyCollection<string> ErrorCodes => PunchErrorCodes.Descriptions.Keys.Order().ToArray();

    public void OnGet()
    {
        IEnumerable<PunchErrorResult> query = service.FindErrors();
        if (!string.IsNullOrWhiteSpace(ErrorCode)) query = query.Where(e => e.ErrorCode.Equals(ErrorCode, StringComparison.OrdinalIgnoreCase));
        if (Date is not null) query = query.Where(e => e.Date.Equals(Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(CodeNumber)) query = query.Where(e => e.CodeNumber.Equals(CodeNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(Siid)) query = query.Where(e => e.Siid.Contains(Siid.Trim(), StringComparison.OrdinalIgnoreCase));
        Errors = query.ToList();
    }
}